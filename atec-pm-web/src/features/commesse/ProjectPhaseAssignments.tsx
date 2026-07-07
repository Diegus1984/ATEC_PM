import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus, RefreshCw, Trash2, UserPlus } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
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
      className="h-8 w-20 text-right tabular-nums"
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
            <Select
              value={employeeId != null ? String(employeeId) : ""}
              onValueChange={(value) => setEmployeeId(Number(value))}
            >
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Seleziona tecnico…" />
              </SelectTrigger>
              <SelectContent>
                {employees.map((emp) => (
                  <SelectItem key={emp.id} value={String(emp.id)}>
                    {emp.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
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

export function ImportPhasesDialog({
  projectId,
  open,
  existingTemplateIds,
  onClose,
  onImported,
}: {
  projectId: number
  open: boolean
  existingTemplateIds: Set<number>
  onClose: () => void
  onImported: () => void
}) {
  const [selected, setSelected] = React.useState<Set<number>>(new Set())
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

  const available = (templatesQuery.data ?? []).filter(
    (t) => !existingTemplateIds.has(t.id)
  )

  const saveMutation = useMutation({
    mutationFn: () =>
      bulkCreatePhases({ projectId, templateIds: [...selected] }),
    onSuccess: () => onImported(),
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Importa fasi da template</DialogTitle>
          <DialogDescription>
            Seleziona i template di fase da aggiungere alla commessa.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-2">
          {available.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              Nessun template disponibile (tutti già presenti in commessa).
            </p>
          ) : (
            available.map((t) => (
              <label
                key={t.id}
                className="flex items-center gap-2 rounded-md border px-3 py-2 text-sm"
              >
                <Checkbox
                  checked={selected.has(t.id)}
                  onCheckedChange={(value) =>
                    setSelected((prev) => {
                      const next = new Set(prev)
                      if (value) next.add(t.id)
                      else next.delete(t.id)
                      return next
                    })
                  }
                />
                <span className="flex-1">{t.name}</span>
                {t.costSectionName ? (
                  <span className="text-xs text-muted-foreground">
                    {t.costSectionName}
                  </span>
                ) : null}
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

  const sections = sectionsQuery.data ?? []

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
            <Select value={sectionId} onValueChange={setSectionId}>
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_SECTION}>(nessuna)</SelectItem>
                {sections.map((s) => (
                  <SelectItem key={s.id} value={String(s.id)}>
                    {s.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
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
      <span className="w-24 text-right text-xs text-muted-foreground tabular-nums">
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
  const existingTemplateIds = new Set(
    phases.filter((p) => !p.isLocal).map((p) => p.phaseTemplateId)
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
          <Button
            variant="outline"
            size="sm"
            onClick={() => setImportOpen(true)}
          >
            <Plus />
            Importa fasi
          </Button>
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
            Nessuna fase in commessa. Usa «Importa fasi» o «Fase locale».
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
                      <span className="text-xs text-muted-foreground tabular-nums">
                        {phase.budgetHours.toFixed(1)} h prev. ·{" "}
                        {phase.hoursWorked.toFixed(1)} h lav.
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
      <ImportPhasesDialog
        projectId={projectId}
        open={importOpen}
        existingTemplateIds={existingTemplateIds}
        onClose={() => setImportOpen(false)}
        onImported={() => {
          setImportOpen(false)
          invalidate()
        }}
      />
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
