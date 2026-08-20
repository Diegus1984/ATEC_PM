import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Settings2 } from "lucide-react"

import { MoneyInput } from "@/components/shared/money-input"
import { DateField } from "@/components/shared/date-field"
import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import {
  createProject,
  fetchCustomerLookup,
  fetchPmLookup,
  fetchProject,
  fetchProjectNextCode,
  promoteProjectToCommessa,
  updateProject,
} from "@/lib/api/projects"
import { fetchActiveActivityCatalog } from "@/lib/api/activity-catalog"
import { seedMilestonesFromCatalog } from "@/lib/api/milestones"
import { canWriteFeature } from "@/lib/auth/permissions"
import { dateToIso, isoToDate, toDateOnly } from "@/lib/date-iso"
import { parseDecimal } from "@/lib/format"
import { ActivityCatalogDialog } from "@/features/admin/activity-catalog/ActivityCatalogDialog"
import {
  allowedProjectStatuses,
  PROJECT_STATUS_META,
} from "@/features/commesse/project-status"
import { cn } from "@/lib/utils"

const STATUS_OPTIONS = PROJECT_STATUS_META

const PRIORITY_OPTIONS = [
  { value: "LOW", label: "Bassa" },
  { value: "MEDIUM", label: "Media" },
  { value: "HIGH", label: "Alta" },
  { value: "CRITICAL", label: "Critica" },
] as const

const EMPTY = {
  code: "",
  title: "",
  customerId: null as number | null,
  pmId: null as number | null,
  startDate: null as string | null,
  endDatePlanned: null as string | null,
  revenue: "0",
  budgetTotal: "0",
  budgetHoursTotal: "0",
  status: "DRAFT",
  priority: "MEDIUM",
  description: "",
  serverPath: "",
  notes: "",
  createDefaultPhases: true,
}

type FormState = typeof EMPTY

export function ProjectDialog({
  open,
  projectId,
  promoteFromId = null,
  onClose,
  onSaved,
  onPromoted,
}: {
  open: boolean
  projectId: number | "new" | null
  /**
   * #89: promozione di un'Altra Attività a commessa. Il dialog si apre precompilato
   * dall'attività (con `projectId` null), l'utente rivede i dati e conferma: il server
   * genera il codice definitivo e conserva quello vecchio nelle note.
   */
  promoteFromId?: number | null
  onClose: () => void
  /** `newProjectId` valorizzato solo in creazione: serve al chiamante per portarcisi. */
  onSaved: (newProjectId?: number) => Promise<void>
  /** Solo promozione: riceve il codice commessa assegnato dal server. */
  onPromoted?: (newCode: string) => void
}) {
  const isPromote = typeof promoteFromId === "number"
  const isNew = projectId === "new"
  const editId = typeof projectId === "number" ? projectId : null

  const [form, setForm] = React.useState<FormState>(EMPTY)
  const [error, setError] = React.useState<string | null>(null)
  const [originalStatus, setOriginalStatus] = React.useState<string | null>(null)
  // Codice dell'attività prima della promozione: serve al testo del dialog.
  const [promoteOldCode, setPromoteOldCode] = React.useState("")
  const [linkedQuoteId, setLinkedQuoteId] = React.useState(0)
  // Precarico attività standard: voci del catalogo da materializzare come milestone alla creazione.
  const [preloadIds, setPreloadIds] = React.useState<Set<number>>(new Set())
  const [catalogOpen, setCatalogOpen] = React.useState(false)
  // `canWriteFeature`: da qui si CREANO voci nell'anagrafica attività, quindi la sola lettura
  // non basta — altrimenti il pulsante resta attivo e a dire di no è solo l'API.
  const canManageCatalog = canWriteFeature("nav.anagrafica_attivita")

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  // Data inizio: allinea la fine se rimane precedente all'inizio (regola date range).
  function handleStartChange(value: string | null) {
    setForm((prev) => {
      let end = prev.endDatePlanned
      if (value && end) {
        const start = isoToDate(value)
        const endDate = isoToDate(end)
        if (start && endDate && endDate < start) {
          end = value
        }
      }
      return { ...prev, startDate: value, endDatePlanned: end }
    })
  }

  const customersQuery = useQuery({
    queryKey: ["lookup-customers"],
    queryFn: fetchCustomerLookup,
    enabled: open,
  })
  const pmQuery = useQuery({
    queryKey: ["lookup-pm"],
    queryFn: fetchPmLookup,
    enabled: open,
  })
  // Catalogo attività (solo alla creazione): alimenta la checklist «Attività da precaricare».
  const catalogQuery = useQuery({
    queryKey: ["activity-catalog", "active"],
    queryFn: fetchActiveActivityCatalog,
    enabled: open && isNew,
  })

  React.useEffect(() => {
    if (!open) {
      return
    }
    setError(null)
    setPromoteOldCode("")

    if (isPromote) {
      // Promozione: anagrafica dell'attività + anteprima del codice di oggi. Il codice
      // definitivo lo genera comunque il server al salvataggio (progressivo in transazione).
      // Azzeramento SINCRONO prima del fetch: senza, il form terrebbe i dati dell'attività
      // promossa un attimo prima e — a fetch fallito o lento — si potrebbero salvare
      // quei dati sull'attività sbagliata (canSave resta spento finché EMPTY non si riempie).
      setForm(EMPTY)
      setOriginalStatus(null)
      setLinkedQuoteId(0)
      let cancelled = false
      void Promise.all([
        fetchProject(promoteFromId),
        fetchProjectNextCode().catch(() => ""),
      ])
        .then(([data, nextCode]) => {
          if (cancelled) return
          setForm({
            code: nextCode || "(assegnato al salvataggio)",
            title: data.title,
            customerId: data.customerId || null,
            pmId: data.pmId || null,
            startDate: toDateOnly(data.startDate),
            endDatePlanned: toDateOnly(data.endDatePlanned),
            revenue: String(data.revenue ?? 0),
            budgetTotal: String(data.budgetTotal ?? 0),
            budgetHoursTotal: String(data.budgetHoursTotal ?? 0),
            status: data.status || "DRAFT",
            priority: data.priority || "MEDIUM",
            description: data.description,
            serverPath: data.serverPath,
            notes: data.notes,
            // L'attività esiste già: fasi/milestone eventuali sono agganciate per id,
            // ricrearle qui le duplicherebbe.
            createDefaultPhases: false,
          })
          setPromoteOldCode(data.code)
          setOriginalStatus(data.status || "DRAFT")
          setLinkedQuoteId(data.linkedQuoteId || 0)
        })
        .catch((err: Error) => {
          if (!cancelled) setError(err.message)
        })
      return () => {
        cancelled = true
      }
    }

    if (isNew || editId == null) {
      setForm({ ...EMPTY, startDate: dateToIso(new Date()) })
      setOriginalStatus(null)
      setLinkedQuoteId(0)

      let cancelled = false
      void fetchProjectNextCode()
        .then((code) => {
          if (!cancelled) setForm((prev) => ({ ...prev, code }))
        })
        .catch(() => {
          // Codice non recuperato: l'utente lo può digitare a mano.
        })
      return () => {
        cancelled = true
      }
    }

    let cancelled = false
    void fetchProject(editId)
      .then((data) => {
        if (cancelled) return
        setForm({
          code: data.code,
          title: data.title,
          customerId: data.customerId || null,
          pmId: data.pmId || null,
          startDate: toDateOnly(data.startDate),
          endDatePlanned: toDateOnly(data.endDatePlanned),
          revenue: String(data.revenue ?? 0),
          budgetTotal: String(data.budgetTotal ?? 0),
          budgetHoursTotal: String(data.budgetHoursTotal ?? 0),
          status: data.status || "DRAFT",
          priority: data.priority || "MEDIUM",
          description: data.description,
          serverPath: data.serverPath,
          notes: data.notes,
          createDefaultPhases: false,
        })
        setOriginalStatus(data.status || "DRAFT")
        setLinkedQuoteId(data.linkedQuoteId || 0)
      })
      .catch((err: Error) => {
        if (!cancelled) setError(err.message)
      })
    return () => {
      cancelled = true
    }
  }, [open, isNew, editId, isPromote, promoteFromId])

  // Voci del catalogo già viste: distingue «voce nuova» da «voce che l'utente ha deselezionato».
  const knownCatalogIds = React.useRef<Set<number>>(new Set())

  React.useEffect(() => {
    if (!open) {
      knownCatalogIds.current = new Set()
      setPreloadIds(new Set())
    }
  }, [open])

  // Prima apertura: TUTTE le voci attive pre-selezionate (come nel prototipo — il catalogo
  // è ancora sconosciuto, quindi ogni voce è «nuova»). Al ritorno dall'anagrafica attività
  // la stessa regola fa il round-trip: la scelta fatta finora resta, le voci aggiunte lì
  // arrivano già spuntate e quelle eliminate o disattivate escono dalla selezione da sole.
  React.useEffect(() => {
    if (!open || !isNew || !catalogQuery.data) return
    const items = catalogQuery.data
    const known = knownCatalogIds.current
    setPreloadIds((prev) => {
      const next = new Set<number>()
      for (const item of items) {
        if (prev.has(item.id) || !known.has(item.id)) next.add(item.id)
      }
      return next
    })
    knownCatalogIds.current = new Set(items.map((item) => item.id))
  }, [open, isNew, catalogQuery.data])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!form.code.trim() || !form.title.trim()) {
        throw new Error("Codice e titolo sono obbligatori.")
      }
      if (form.customerId == null || form.pmId == null) {
        throw new Error("Seleziona cliente e Project Manager.")
      }
      if (form.startDate && form.endDatePlanned) {
        const start = isoToDate(form.startDate)
        const end = isoToDate(form.endDatePlanned)
        if (start && end && end < start) {
          throw new Error("La data fine non può precedere la data inizio.")
        }
      }
      const payload = {
        id: editId ?? 0,
        code: form.code.trim(),
        title: form.title.trim(),
        customerId: form.customerId,
        pmId: form.pmId,
        description: form.description.trim(),
        startDate: form.startDate,
        endDatePlanned: form.endDatePlanned,
        budgetTotal: parseDecimal(form.budgetTotal),
        budgetHoursTotal: parseDecimal(form.budgetHoursTotal),
        revenue: parseDecimal(form.revenue),
        status: form.status || "DRAFT",
        priority: form.priority || "MEDIUM",
        serverPath: form.serverPath.trim(),
        notes: form.notes.trim(),
        createDefaultPhases: form.createDefaultPhases,
        linkedQuoteId,
      }
      if (isPromote) {
        // La promozione è un UPDATE sulla stessa riga: il codice lo genera il server,
        // qui viaggia solo l'anagrafica rivista dall'utente.
        const promotedCode = await promoteProjectToCommessa(promoteFromId, payload)
        return { promotedCode }
      }
      if (editId != null) {
        await updateProject(editId, payload)
        return {}
      }
      const newId = await createProject(payload)
      // Precarico milestone standard dal catalogo (copia snapshot). Best-effort: non blocca la creazione.
      if (preloadIds.size > 0) {
        try {
          await seedMilestonesFromCatalog(newId, [...preloadIds])
        } catch {
          /* la commessa è comunque creata */
        }
      }
      return { newId }
    },
    onSuccess: async (result: { newId?: number; promotedCode?: string }) => {
      if (result.promotedCode) {
        onPromoted?.(result.promotedCode)
      }
      await onSaved(result.newId)
    },
    onError: (err: Error) => setError(err.message),
  })

  const quoteLocked = linkedQuoteId > 0
  // #89: chi «Opera su commesse sospese o chiuse» cambia stato liberamente dal dialog,
  // riaprire una Completata compreso (stessa regola della colonna Stato in Dashboard).
  const canOverrideStatus = canWriteFeature("action.project_locked_write")
  const allowedStatuses = allowedProjectStatuses(originalStatus, canOverrideStatus)
  // «Annullata» non è uno stato da tendina: è il soft delete, che ha il suo percorso
  // (Elimina, con conferma). Resta visibile solo se la commessa è GIÀ annullata.
  const statusOptions = STATUS_OPTIONS.filter(
    (option) => option.value !== "CANCELLED" || form.status === "CANCELLED"
  )

  const customers = customersQuery.data ?? []
  const pms = pmQuery.data ?? []
  const startDate = isoToDate(form.startDate)

  const canSave =
    !!form.code.trim() &&
    !!form.title.trim() &&
    form.customerId != null &&
    form.pmId != null &&
    !saveMutation.isPending

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>
            {isPromote
              ? "Promuovi a commessa"
              : isNew
                ? "Nuova commessa"
                : "Modifica commessa"}
          </DialogTitle>
          <DialogDescription>
            {isPromote
              ? `«${promoteOldCode || "…"}» diventa una commessa a tutti gli effetti: ` +
                "controlla i dati e conferma. Il nome attuale resta scritto nelle note; " +
                "il codice definitivo lo assegna il server al salvataggio."
              : "Anagrafica commessa: cliente, responsabile, tempi ed economics."}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid grid-cols-[1fr_2fr] gap-4">
            <div className="grid gap-2">
              <Label>Codice</Label>
              <Input
                value={form.code}
                autoFocus={!isPromote}
                maxLength={isPromote ? undefined : 20}
                disabled={isPromote}
                title={
                  isPromote
                    ? "Anteprima: il codice definitivo lo genera il server al salvataggio"
                    : undefined
                }
                onChange={(event) => set("code", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Titolo</Label>
              <Input
                value={form.title}
                onChange={(event) => set("title", event.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Cliente</Label>
              {/* Anagrafica lunga: combo con ricerca, non una Select da scorrere. */}
              <LookupCombobox
                options={customers}
                value={form.customerId}
                onValueChange={(id) => set("customerId", id)}
                placeholder="Seleziona cliente…"
                searchPlaceholder="Cerca cliente…"
                emptyText="Nessun cliente trovato"
                loading={customersQuery.isLoading}
              />
            </div>
            <div className="grid gap-2">
              <Label>Project Manager</Label>
              <LookupCombobox
                options={pms}
                value={form.pmId}
                onValueChange={(id) => set("pmId", id)}
                placeholder="Seleziona PM…"
                searchPlaceholder="Cerca PM…"
                emptyText="Nessun PM trovato"
                loading={pmQuery.isLoading}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Data inizio</Label>
              <DateField value={form.startDate} onChange={handleStartChange} />
            </div>
            <div className="grid gap-2">
              <Label>Data fine prevista</Label>
              <DateField
                value={form.endDatePlanned}
                onChange={(value) => set("endDatePlanned", value)}
                disabled={!form.startDate}
                disableBefore={startDate}
              />
            </div>
          </div>

          {quoteLocked ? (
            <p className="rounded-md bg-muted px-3 py-2 text-sm text-muted-foreground">
              🔒 Valori economici ereditati dal Preventivo (sola lettura).
            </p>
          ) : null}

          <div className="grid grid-cols-3 gap-4">
            <div className="grid gap-2">
              <Label>Ricavo (€)</Label>
              <MoneyInput
                value={form.revenue}
                disabled={quoteLocked}
                onChange={(value) => set("revenue", value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Budget (€)</Label>
              <MoneyInput
                value={form.budgetTotal}
                disabled={quoteLocked}
                onChange={(value) => set("budgetTotal", value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Ore previste</Label>
              <Input
                inputMode="decimal"
                value={form.budgetHoursTotal}
                disabled={quoteLocked}
                onChange={(event) => set("budgetHoursTotal", event.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Stato</Label>
              <Select
                value={form.status}
                onValueChange={(value) => set("status", value)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {statusOptions.map((option) => {
                    const StatusIcon = option.icon
                    return (
                      <SelectItem
                        key={option.value}
                        value={option.value}
                        disabled={!allowedStatuses.includes(option.value)}
                      >
                        <StatusIcon className={cn("size-4", option.className)} />
                        {option.label}
                      </SelectItem>
                    )
                  })}
                </SelectContent>
              </Select>
            </div>
            <div className="grid gap-2">
              <Label>Priorità</Label>
              <Select
                value={form.priority}
                onValueChange={(value) => set("priority", value)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {PRIORITY_OPTIONS.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="grid gap-2">
            <Label>Descrizione</Label>
            <Textarea
              value={form.description}
              rows={2}
              onChange={(event) => set("description", event.target.value)}
            />
          </div>

          <div className="grid gap-2">
            <Label>Percorso server</Label>
            <Input
              value={form.serverPath}
              placeholder="Cartella documenti sul server (opzionale)"
              disabled={isPromote}
              title={
                isPromote
                  ? "Il percorso resta quello attuale: si cambia dopo, da «Modifica commessa»"
                  : undefined
              }
              onChange={(event) => set("serverPath", event.target.value)}
            />
          </div>

          <div className="grid gap-2">
            <Label>Note</Label>
            <Textarea
              value={form.notes}
              rows={2}
              onChange={(event) => set("notes", event.target.value)}
            />
          </div>

          {isNew ? (
            <label className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={form.createDefaultPhases}
                onCheckedChange={(value) =>
                  set("createDefaultPhases", !!value)
                }
              />
              Crea fasi di default dal template
            </label>
          ) : null}

          {isNew ? (
            <div className="grid gap-2 rounded-md border p-3">
              <div className="flex items-center justify-between">
                <Label className="text-sm">
                  Attività da precaricare{" "}
                  <span className="font-normal text-muted-foreground">
                    ({preloadIds.size} selezionate)
                  </span>
                </Label>
                <div className="flex items-center gap-3 text-xs">
                  <button
                    type="button"
                    className="font-medium text-primary hover:underline"
                    onClick={() =>
                      setPreloadIds(
                        new Set((catalogQuery.data ?? []).map((v) => v.id))
                      )
                    }
                  >
                    Tutte
                  </button>
                  <button
                    type="button"
                    className="font-medium text-primary hover:underline"
                    onClick={() => setPreloadIds(new Set())}
                  >
                    Nessuna
                  </button>
                  {canManageCatalog ? (
                    <>
                      <span className="h-3 w-px bg-border" />
                      {/* Manca una voce? Si aggiunge qui senza abbandonare la commessa
                          che si sta creando: al rientro la selezione si riallinea. */}
                      <button
                        type="button"
                        className="inline-flex items-center gap-1 font-medium text-primary hover:underline"
                        onClick={() => setCatalogOpen(true)}
                      >
                        <Settings2 className="size-3.5" />
                        Gestisci anagrafica attività
                      </button>
                    </>
                  ) : null}
                </div>
              </div>
              <div className="max-h-48 space-y-1 overflow-y-auto pr-1">
                {(catalogQuery.data ?? []).length === 0 ? (
                  <p className="text-sm text-muted-foreground">
                    Nessuna voce in anagrafica attività.
                  </p>
                ) : (
                  (catalogQuery.data ?? []).map((v) => (
                    <label
                      key={v.id}
                      className="flex items-center gap-2 text-sm"
                    >
                      <Checkbox
                        checked={preloadIds.has(v.id)}
                        onCheckedChange={(value) =>
                          setPreloadIds((prev) => {
                            const next = new Set(prev)
                            if (value === true) next.add(v.id)
                            else next.delete(v.id)
                            return next
                          })
                        }
                      />
                      {v.label}
                    </label>
                  ))
                )}
              </div>
              <p className="text-xs text-muted-foreground">
                Le voci scelte diventano milestone della commessa (copia
                indipendente dal catalogo).
              </p>
            </div>
          ) : null}

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button onClick={() => saveMutation.mutate()} disabled={!canSave}>
            {saveMutation.isPending
              ? "Salvataggio…"
              : isPromote
                ? "Promuovi"
                : "Salva"}
          </Button>
        </DialogFooter>

        {/* Round-trip: alla chiusura `catalogQuery` si è già invalidata da sola, e
            l'effetto sul catalogo riallinea la selezione. */}
        <ActivityCatalogDialog
          open={catalogOpen}
          onClose={() => setCatalogOpen(false)}
        />
      </DialogContent>
    </Dialog>
  )
}
