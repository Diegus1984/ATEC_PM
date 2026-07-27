import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import { DateField } from "@/components/shared/date-field"
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
  updateProject,
} from "@/lib/api/projects"
import { fetchActiveActivityCatalog } from "@/lib/api/activity-catalog"
import { seedMilestonesFromCatalog } from "@/lib/api/milestones"
import { dateToIso, isoToDate, toDateOnly } from "@/lib/date-iso"
import { parseDecimal } from "@/lib/format"

const NO_SELECTION = "__none__"

const STATUS_OPTIONS = [
  { value: "DRAFT", label: "Bozza" },
  { value: "ACTIVE", label: "Attiva" },
  { value: "ON_HOLD", label: "In pausa" },
  { value: "COMPLETED", label: "Completata" },
  { value: "CANCELLED", label: "Annullata" },
] as const

const PRIORITY_OPTIONS = [
  { value: "LOW", label: "Bassa" },
  { value: "MEDIUM", label: "Media" },
  { value: "HIGH", label: "Alta" },
  { value: "CRITICAL", label: "Critica" },
] as const

/** Transizioni di stato consentite a partire dallo stato corrente (come WPF). */
const STATUS_TRANSITIONS: Record<string, string[]> = {
  DRAFT: ["DRAFT", "ACTIVE", "CANCELLED"],
  ACTIVE: ["ACTIVE", "ON_HOLD", "COMPLETED", "CANCELLED"],
  ON_HOLD: ["ON_HOLD", "ACTIVE", "CANCELLED"],
  COMPLETED: ["COMPLETED"],
  CANCELLED: ["CANCELLED"],
}

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
  onClose,
  onSaved,
}: {
  open: boolean
  projectId: number | "new" | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const isNew = projectId === "new"
  const editId = typeof projectId === "number" ? projectId : null

  const [form, setForm] = React.useState<FormState>(EMPTY)
  const [error, setError] = React.useState<string | null>(null)
  const [originalStatus, setOriginalStatus] = React.useState<string | null>(null)
  const [linkedQuoteId, setLinkedQuoteId] = React.useState(0)
  // Precarico attività standard: voci del catalogo da materializzare come milestone alla creazione.
  const [preloadIds, setPreloadIds] = React.useState<Set<number>>(new Set())

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
  }, [open, isNew, editId])

  // All'apertura in creazione, pre-seleziona TUTTE le voci attive del catalogo (come nel prototipo).
  React.useEffect(() => {
    if (open && isNew && catalogQuery.data) {
      setPreloadIds(new Set(catalogQuery.data.map((v) => v.id)))
    }
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
      if (editId != null) {
        await updateProject(editId, payload)
      } else {
        const newId = await createProject(payload)
        // Precarico milestone standard dal catalogo (copia snapshot). Best-effort: non blocca la creazione.
        if (preloadIds.size > 0) {
          try {
            await seedMilestonesFromCatalog(newId, [...preloadIds])
          } catch {
            /* la commessa è comunque creata */
          }
        }
      }
    },
    onSuccess: async () => {
      await onSaved()
    },
    onError: (err: Error) => setError(err.message),
  })

  const quoteLocked = linkedQuoteId > 0
  const allowedStatuses =
    originalStatus != null
      ? (STATUS_TRANSITIONS[originalStatus] ?? STATUS_OPTIONS.map((s) => s.value))
      : STATUS_OPTIONS.map((s) => s.value)

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
            {isNew ? "Nuova commessa" : "Modifica commessa"}
          </DialogTitle>
          <DialogDescription>
            Anagrafica commessa: cliente, responsabile, tempi ed economics.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid grid-cols-[1fr_2fr] gap-4">
            <div className="grid gap-2">
              <Label>Codice</Label>
              <Input
                value={form.code}
                autoFocus
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
              <Select
                value={
                  form.customerId != null ? String(form.customerId) : NO_SELECTION
                }
                onValueChange={(value) =>
                  set("customerId", value === NO_SELECTION ? null : Number(value))
                }
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Seleziona cliente…" />
                </SelectTrigger>
                <SelectContent>
                  {customers.map((customer) => (
                    <SelectItem key={customer.id} value={String(customer.id)}>
                      {customer.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="grid gap-2">
              <Label>Project Manager</Label>
              <Select
                value={form.pmId != null ? String(form.pmId) : NO_SELECTION}
                onValueChange={(value) =>
                  set("pmId", value === NO_SELECTION ? null : Number(value))
                }
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Seleziona PM…" />
                </SelectTrigger>
                <SelectContent>
                  {pms.map((pm) => (
                    <SelectItem key={pm.id} value={String(pm.id)}>
                      {pm.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
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
              <Input
                inputMode="decimal"
                value={form.revenue}
                disabled={quoteLocked}
                onChange={(event) => set("revenue", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Budget (€)</Label>
              <Input
                inputMode="decimal"
                value={form.budgetTotal}
                disabled={quoteLocked}
                onChange={(event) => set("budgetTotal", event.target.value)}
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
                  {STATUS_OPTIONS.map((option) => (
                    <SelectItem
                      key={option.value}
                      value={option.value}
                      disabled={!allowedStatuses.includes(option.value)}
                    >
                      {option.label}
                    </SelectItem>
                  ))}
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
                <div className="flex gap-3 text-xs">
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
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
