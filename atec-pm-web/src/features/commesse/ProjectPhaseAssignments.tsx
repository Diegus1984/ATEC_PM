import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus, RefreshCw, Trash2, UserPlus } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { canWriteFeature } from "@/lib/auth/permissions"
import { notifyError } from "@/lib/toast"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
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
import { Skeleton } from "@/components/ui/skeleton"
import { fetchCostSectionTemplates } from "@/lib/api/cost-sections"
import {
  addPhaseAssignment,
  bulkCreatePhases,
  createLocalPhase,
  deletePhaseAssignment,
  deleteProjectPhase,
  fetchEmployeesByPhase,
  fetchPhaseTemplates,
  fetchProjectPhases,
  updateAssignmentHours,
} from "@/lib/api/phases"
import type { PhaseAssignmentDto, PhaseListItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { bvaPhaseHoursClass } from "./bva/bva-shared"

const NO_SECTION = "__none__"
const SENZA_SEZIONE = "Senza sezione di costo"

function num(value: string): number {
  const n = Number(value.replace(",", "."))
  return Number.isFinite(n) ? n : 0
}

/** Input ore con salvataggio su blur/Enter; riparte dal valore della prop. */
export function HoursInput({
  value,
  onSave,
  disabled,
}: {
  value: number
  onSave: (value: number) => void
  disabled?: boolean
}) {
  const [text, setText] = React.useState(String(value))
  React.useEffect(() => {
    setText(String(value))
  }, [value])

  function commit() {
    const parsed = num(text)
    if (parsed !== value) {
      onSave(parsed)
    }
  }

  return (
    <Input
      inputMode="decimal"
      value={text}
      disabled={disabled}
      className={cn(
        // Campo piatto a riposo: il bordo compare al passaggio/focus (pattern foglio SAL).
        "h-8 w-20 text-right border-transparent bg-transparent shadow-none hover:border-input focus:border-input focus:bg-background",
        bvaPhaseHoursClass
      )}
      onChange={(event) => setText(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.currentTarget.blur()
        }
      }}
    />
  )
}

export function AddTechDialog({
  phase,
  onClose,
  onAdded,
}: {
  phase: PhaseListItem | null
  onClose: () => void
  onAdded: () => void
}) {
  const open = phase !== null
  const [employeeId, setEmployeeId] = React.useState<number | null>(null)
  const [hours, setHours] = React.useState("0")
  const [error, setError] = React.useState<string | null>(null)

  const employeesQuery = useQuery({
    queryKey: ["phase-employees", phase?.id],
    queryFn: () => fetchEmployeesByPhase(phase!.id),
    enabled: open,
  })

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (employeeId == null) {
        throw new Error("Seleziona un tecnico.")
      }
      await addPhaseAssignment(phase!.id, {
        employeeId,
        assignRole: "MEMBER",
        plannedHours: num(hours),
      })
    },
    onSuccess: () => onAdded(),
    onError: (err: Error) => setError(err.message),
  })

  React.useEffect(() => {
    if (open) {
      setEmployeeId(null)
      setHours("0")
      setError(null)
      saveMutation.reset()
    }
    // saveMutation.reset è stabile; non va in deps per evitare loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, phase?.id])

  const employees = employeesQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Aggiungi tecnico</DialogTitle>
          <DialogDescription>
            Fase: {phase?.customName || phase?.name}
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-4">
          <div className="grid gap-2">
            <Label>Tecnico</Label>
            <LookupCombobox
              options={employees.map((emp) => ({ id: emp.id, name: emp.name }))}
              value={employeeId}
              onValueChange={setEmployeeId}
              placeholder="Seleziona tecnico…"
              searchPlaceholder="Cerca tecnico…"
              emptyText="Nessun tecnico trovato"
            />
          </div>
          <div className="grid gap-2">
            <Label>Ore pianificate</Label>
            <Input
              inputMode="decimal"
              value={hours}
              onChange={(event) => setHours(event.target.value)}
            />
          </div>
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={employeeId == null || saveMutation.isPending}
          >
            {saveMutation.isPending ? "Salvataggio…" : "Aggiungi"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/**
 * Importa fasi dall'anagrafica. Si sceglie una **coppia fase + sezione**: la stessa fase
 * compare una volta per ogni sezione a cui è agganciata, e importarla in due sezioni crea due
 * righe distinte — è così che le ore di «Call Cliente» in PM restano separate da quelle in
 * Progettazione. Vedi PIANO-FASI-MULTISEZIONE.md.
 */
export function ImportPhasesDialog({
  projectId,
  open,
  existingPhaseKeys,
  sectionFilterId,
  onClose,
  onImported,
}: {
  projectId: number
  open: boolean
  /** Chiavi `templateId:sectionId` già presenti in commessa. */
  existingPhaseKeys: Set<string>
  /** Aperto da dentro una sezione: mostra solo le fasi di quella sezione. */
  sectionFilterId?: number | null
  onClose: () => void
  onImported: () => void
}) {
  const [selected, setSelected] = React.useState<Set<string>>(new Set())
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (open) {
      setSelected(new Set())
      setError(null)
    }
  }, [open])

  const templatesQuery = useQuery({
    queryKey: ["phase-templates"],
    queryFn: fetchPhaseTemplates,
    enabled: open,
  })

  const available = React.useMemo(() => {
    const rows: {
      key: string
      templateId: number
      sectionId: number | null
      name: string
      sectionLabel: string
    }[] = []
    for (const template of templatesQuery.data ?? []) {
      // Una fase senza sezioni non è importabile: le sue ore non si saprebbero attribuire.
      for (const link of template.sections) {
        if (sectionFilterId != null && link.sectionId !== sectionFilterId) {
          continue
        }
        rows.push({
          key: `${template.id}:${link.sectionId}`,
          templateId: template.id,
          sectionId: link.sectionId,
          name: template.name,
          sectionLabel: link.groupName
            ? `${link.groupName} · ${link.sectionName}`
            : link.sectionName,
        })
      }
    }
    return rows
      .filter((row) => !existingPhaseKeys.has(row.key))
      .sort(
        (a, b) =>
          a.sectionLabel.localeCompare(b.sectionLabel, "it") ||
          a.name.localeCompare(b.name, "it")
      )
  }, [templatesQuery.data, existingPhaseKeys, sectionFilterId])

  const saveMutation = useMutation({
    mutationFn: () =>
      bulkCreatePhases({
        projectId,
        templateIds: [],
        items: available
          .filter((row) => selected.has(row.key))
          .map((row) => ({ templateId: row.templateId, sectionId: row.sectionId })),
      }),
    onSuccess: () => onImported(),
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Importa fasi dettaglio commessa</DialogTitle>
          <DialogDescription>
            Ogni riga è una fase in una sezione di costo: la stessa fase si può importare in
            più sezioni, e ogni sezione tiene le sue ore.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-2">
          {available.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              Nessuna fase disponibile (tutte già presenti in commessa).
            </p>
          ) : (
            available.map((row) => (
              <label
                key={row.key}
                className="flex items-center gap-2 rounded-md border px-3 py-2 text-sm"
              >
                <Checkbox
                  checked={selected.has(row.key)}
                  onCheckedChange={(value) =>
                    setSelected((prev) => {
                      const next = new Set(prev)
                      if (value) next.add(row.key)
                      else next.delete(row.key)
                      return next
                    })
                  }
                />
                <span className="flex-1">{row.name}</span>
                <span className="text-xs text-muted-foreground">
                  {row.sectionLabel}
                </span>
              </label>
            ))
          )}
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={selected.size === 0 || saveMutation.isPending}
          >
            {saveMutation.isPending
              ? "Importazione…"
              : `Importa (${selected.size})`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function NewLocalPhaseDialog({
  projectId,
  open,
  presetSectionId,
  onClose,
  onCreated,
}: {
  projectId: number
  open: boolean
  /** Sezione di costo preselezionata (quando si crea la fase da dentro una sezione). */
  presetSectionId?: number | null
  onClose: () => void
  onCreated: () => void
}) {
  const [name, setName] = React.useState("")
  const [sectionId, setSectionId] = React.useState<string>(NO_SECTION)
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (open) {
      setName("")
      setSectionId(presetSectionId != null ? String(presetSectionId) : NO_SECTION)
      setError(null)
    }
  }, [open, presetSectionId])

  const sectionsQuery = useQuery({
    queryKey: ["cost-section-templates"],
    queryFn: fetchCostSectionTemplates,
    enabled: open,
  })

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!name.trim()) {
        throw new Error("Il nome della fase è obbligatorio.")
      }
      await createLocalPhase({
        projectId,
        name: name.trim(),
        costSectionTemplateId:
          sectionId === NO_SECTION ? null : Number(sectionId),
        departmentId: null,
      })
    },
    onSuccess: () => onCreated(),
    onError: (err: Error) => setError(err.message),
  })

  /**
   * Segnalazione #42: l'elenco deve essere quello dell'anagrafica, non un altro.
   * Le sezioni disattivate (es. «Ore Viaggio») spariscono — resta visibile solo
   * quella eventualmente preselezionata, altrimenti la si perderebbe dal dialogo.
   * L'etichetta porta il gruppo e il marchio cliente: sono le stesse voci, con lo
   * stesso nome, delle tabelle di Impegno Risorse.
   */
  const sections = React.useMemo(() => {
    const all = sectionsQuery.data ?? []
    return all.filter((s) => s.isActive || s.id === presetSectionId)
  }, [sectionsQuery.data, presetSectionId])

  const sectionOptions = React.useMemo(
    () =>
      sections.map((s) => ({
        id: String(s.id),
        name: `${s.groupName ? `${s.groupName} · ` : ""}${s.name}${
          s.sectionType === "DA_CLIENTE" ? " (cliente)" : ""
        }`,
      })),
    [sections]
  )

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Nuova fase locale</DialogTitle>
          <DialogDescription>
            Fase creata solo per questa commessa (non diventa un template).
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-4">
          <div className="grid gap-2">
            <Label>Nome fase</Label>
            <Input
              value={name}
              autoFocus
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="grid gap-2">
            <Label>Sezione di costo</Label>
            <LookupCombobox
              options={sectionOptions}
              value={sectionId === NO_SECTION ? null : sectionId}
              onValueChange={(id) => setSectionId(id ?? NO_SECTION)}
              placeholder="(nessuna)"
              noneLabel="(nessuna)"
              searchPlaceholder="Cerca sezione…"
              emptyText="Nessuna sezione trovata"
            />
          </div>
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!name.trim() || saveMutation.isPending}
          >
            {saveMutation.isPending ? "Creazione…" : "Crea"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function AssignmentRow({
  assignment,
  onSaveHours,
  onRemove,
}: {
  assignment: PhaseAssignmentDto
  onSaveHours: (hours: number) => void
  onRemove: () => void
}) {
  const progress =
    assignment.plannedHours > 0
      ? Math.round((assignment.hoursWorked / assignment.plannedHours) * 100)
      : 0
  return (
    <div className="flex items-center gap-3 px-3 py-1.5 text-sm">
      <span className="flex-1 truncate">{assignment.employeeName}</span>
      <HoursInput value={assignment.plannedHours} onSave={onSaveHours} />
      <span className={cn("w-24 text-right text-xs", bvaPhaseHoursClass)}>
        {assignment.hoursWorked.toFixed(1)} h lav. ({progress}%)
      </span>
      <Button
        variant="ghost"
        size="icon-sm"
        onClick={onRemove}
        aria-label="Rimuovi tecnico"
      >
        <Trash2 />
      </Button>
    </div>
  )
}

export function ProjectPhaseAssignments({
  projectId,
  onChanged,
}: {
  projectId: number
  onChanged: () => void
}) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [addTechPhase, setAddTechPhase] = React.useState<PhaseListItem | null>(
    null
  )
  const [importOpen, setImportOpen] = React.useState(false)
  const [newLocalOpen, setNewLocalOpen] = React.useState(false)

  // Vedi il commento gemello in SectionPhases.tsx: «Importa fasi» è ristretta dalla
  // segnalazione #51 e ora la governa la chiave `action.import_project_phases` sulla
  // persona. Nascosta, non cancellata: serve ancora come rete di scampo.
  const canImportPhases = canWriteFeature("action.import_project_phases")

  const phasesQuery = useQuery({
    queryKey: ["project-phases", projectId],
    queryFn: () => fetchProjectPhases(projectId),
    enabled: projectId > 0,
  })

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({
      queryKey: ["project-phases", projectId],
    })
    onChanged()
  }, [queryClient, projectId, onChanged])

  const hoursMutation = useMutation({
    mutationFn: ({ id, hours }: { id: number; hours: number }) =>
      updateAssignmentHours(id, hours),
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const removeAssignmentMutation = useMutation({
    mutationFn: (id: number) => deletePhaseAssignment(id),
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  const deletePhaseMutation = useMutation({
    mutationFn: (id: number) => deleteProjectPhase(id),
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  async function handleRemoveAssignment(a: PhaseAssignmentDto) {
    const ok = await confirm({
      title: "Rimuovi tecnico",
      description: `Rimuovere "${a.employeeName}" dalla fase?`,
      confirmLabel: "Rimuovi",
    })
    if (ok) removeAssignmentMutation.mutate(a.id)
  }

  async function handleDeletePhase(phase: PhaseListItem) {
    const ok = await confirm({
      title: "Elimina fase",
      description: `Eliminare la fase "${phase.customName || phase.name}"? Possibile solo se non ha ore registrate.`,
      confirmLabel: "Elimina",
    })
    if (ok) deletePhaseMutation.mutate(phase.id)
  }

  const phases = React.useMemo(
    () => phasesQuery.data ?? [],
    [phasesQuery.data]
  )
  // Chiavi (fase, sezione) già in commessa. **Non** i soli id di fase: la stessa fase può stare
  // in più sezioni, e ragionando per id una volta importata «Call Cliente» in Program Manager
  // non la si sarebbe più potuta aggiungere a Progettazione.
  const existingPhaseKeys = new Set(
    phases
      .filter((p) => !p.isLocal)
      .map((p) => `${p.phaseTemplateId}:${p.costSectionTemplateId ?? ""}`)
  )

  // Raggruppa per sezione di costo.
  const grouped = React.useMemo(() => {
    const map = new Map<string, PhaseListItem[]>()
    for (const phase of phases) {
      const key = phase.costSectionName || SENZA_SEZIONE
      const list = map.get(key) ?? []
      list.push(phase)
      map.set(key, list)
    }
    return [...map.entries()]
  }, [phases])

  return (
    <div className="rounded-lg border">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-muted/40 px-3 py-2">
        <h3 className="text-sm font-semibold">Fasi e assegnazioni</h3>
        <div className="flex gap-2">
          {canImportPhases ? (
            <Button
              variant="outline"
              size="sm"
              onClick={() => setImportOpen(true)}
            >
              <Plus />
              Importa fasi
            </Button>
          ) : null}
          <Button
            variant="outline"
            size="sm"
            onClick={() => setNewLocalOpen(true)}
          >
            <Plus />
            Fase locale
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => phasesQuery.refetch()}
            disabled={phasesQuery.isFetching}
          >
            <RefreshCw className={phasesQuery.isFetching ? "animate-spin" : ""} />
          </Button>
        </div>
      </div>

      <div className="space-y-4 p-3">
        {phasesQuery.isLoading ? (
          <Skeleton className="h-32 w-full" />
        ) : phases.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            {canImportPhases
              ? "Nessuna fase in commessa. Usa «Importa fasi» o «Fase locale»."
              : "Nessuna fase in commessa. Usa «Fase locale»."}
          </p>
        ) : (
          grouped.map(([sectionName, sectionPhases]) => (
            <div key={sectionName} className="space-y-2">
              <p className="text-xs font-semibold text-muted-foreground uppercase">
                {sectionName}
              </p>
              {sectionPhases.map((phase) => (
                <div key={phase.id} className="rounded-md border">
                  <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-muted/20 px-3 py-1.5">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-medium">
                        {phase.customName || phase.name}
                      </span>
                      {phase.isLocal ? (
                        <Badge
                          variant="outline"
                          className="border-amber-500/40 text-amber-600"
                        >
                          LOCALE
                        </Badge>
                      ) : null}
                    </div>
                    <div className="flex items-center gap-3">
                      <span className={cn("text-xs", bvaPhaseHoursClass)}>
                        {phase.budgetHours.toFixed(1)} h prev. ·{" "}
                        {phase.hoursWorked.toFixed(1)} h lav.
                        {phase.budgetHours > 0
                          ? ` · ${Math.round((phase.hoursWorked / phase.budgetHours) * 100)}%`
                          : " · 0%"}
                      </span>
                      <RowActionsMenu
                        size="icon-sm"
                        actions={[
                          {
                            label: "Aggiungi tecnico",
                            icon: UserPlus,
                            onClick: () => setAddTechPhase(phase),
                          },
                          {
                            label: "Elimina fase",
                            icon: Trash2,
                            destructive: true,
                            separatorBefore: true,
                            onClick: () => {
                              void handleDeletePhase(phase)
                            },
                          },
                        ]}
                      />
                    </div>
                  </div>
                  <div className="divide-y">
                    {phase.assignments.length === 0 ? (
                      <p className="px-3 py-2 text-xs text-muted-foreground">
                        Nessun tecnico assegnato.
                      </p>
                    ) : (
                      phase.assignments.map((a) => (
                        <AssignmentRow
                          key={a.id}
                          assignment={a}
                          onSaveHours={(hours) =>
                            hoursMutation.mutate({ id: a.id, hours })
                          }
                          onRemove={() => {
                            void handleRemoveAssignment(a)
                          }}
                        />
                      ))
                    )}
                  </div>
                </div>
              ))}
            </div>
          ))
        )}
      </div>

      <AddTechDialog
        phase={addTechPhase}
        onClose={() => setAddTechPhase(null)}
        onAdded={() => {
          setAddTechPhase(null)
          invalidate()
        }}
      />
      {canImportPhases ? (
        <ImportPhasesDialog
          projectId={projectId}
          open={importOpen}
          existingPhaseKeys={existingPhaseKeys}
          onClose={() => setImportOpen(false)}
          onImported={() => {
            setImportOpen(false)
            invalidate()
          }}
        />
      ) : null}
      <NewLocalPhaseDialog
        projectId={projectId}
        open={newLocalOpen}
        onClose={() => setNewLocalOpen(false)}
        onCreated={() => {
          setNewLocalOpen(false)
          invalidate()
        }}
      />
    </div>
  )
}
