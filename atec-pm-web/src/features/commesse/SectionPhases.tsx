import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { Plus, Trash2, UserPlus } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  deletePhaseAssignment,
  deleteProjectPhase,
  updateAssignmentHours,
} from "@/lib/api/phases"
import type { PhaseAssignmentDto, PhaseListItem } from "@/lib/api/types"

import {
  AddTechDialog,
  AssignmentRow,
  ImportPhasesDialog,
  NewLocalPhaseDialog,
} from "./ProjectPhaseAssignments"

/**
 * Pannello "FASI ASSEGNATE" di UNA sezione di costo — mostrato a destra delle
 * risorse pianificate, come nel WPF (le fasi stanno DENTRO la sezione).
 */
export function SectionPhases({
  projectId,
  sectionTemplateId,
  phases,
  existingTemplateIds,
  onChanged,
}: {
  projectId: number
  sectionTemplateId: number | null
  phases: PhaseListItem[]
  existingTemplateIds: Set<number>
  onChanged: () => void
}) {
  const confirm = useConfirm()
  const [addTechPhase, setAddTechPhase] = React.useState<PhaseListItem | null>(
    null
  )
  const [newLocalOpen, setNewLocalOpen] = React.useState(false)
  const [importOpen, setImportOpen] = React.useState(false)

  const hoursMutation = useMutation({
    mutationFn: ({ id, hours }: { id: number; hours: number }) =>
      updateAssignmentHours(id, hours),
    onSuccess: () => onChanged(),
    onError: (err: Error) => notifyError(err),
  })
  const removeMutation = useMutation({
    mutationFn: (id: number) => deletePhaseAssignment(id),
    onSuccess: () => onChanged(),
    onError: (err: Error) => notifyError(err),
  })
  const deletePhaseMutation = useMutation({
    mutationFn: (id: number) => deleteProjectPhase(id),
    onSuccess: () => onChanged(),
    onError: (err: Error) => notifyError(err),
  })

  async function handleRemove(a: PhaseAssignmentDto) {
    const ok = await confirm({
      title: "Rimuovi tecnico",
      description: `Rimuovere "${a.employeeName}" dalla fase?`,
      confirmLabel: "Rimuovi",
    })
    if (ok) removeMutation.mutate(a.id)
  }
  async function handleDeletePhase(phase: PhaseListItem) {
    const ok = await confirm({
      title: "Elimina fase",
      description: `Eliminare la fase "${phase.customName || phase.name}"? Possibile solo se non ha ore registrate.`,
      confirmLabel: "Elimina",
    })
    if (ok) deletePhaseMutation.mutate(phase.id)
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
          Fasi assegnate
        </p>
        <div className="flex gap-1.5">
          <Button
            variant="outline"
            size="sm"
            className="h-7 text-xs"
            onClick={() => setImportOpen(true)}
          >
            <Plus className="size-3.5" />
            Importa fase
          </Button>
          <Button
            variant="outline"
            size="sm"
            className="h-7 text-xs"
            onClick={() => setNewLocalOpen(true)}
          >
            <Plus className="size-3.5" />
            Fase locale
          </Button>
        </div>
      </div>

      {phases.length === 0 ? (
        <p className="text-xs text-muted-foreground">
          Nessuna fase in questa sezione.
        </p>
      ) : (
        phases.map((phase) => {
          const pct =
            phase.budgetHours > 0
              ? Math.round((phase.hoursWorked / phase.budgetHours) * 100)
              : 0
          return (
            <div key={phase.id} className="rounded-md border">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-muted/20 px-3 py-1.5">
                <div className="flex items-center gap-2">
                  <Badge
                    variant="outline"
                    className={
                      phase.isLocal
                        ? "border-amber-500/40 text-amber-600"
                        : "border-blue-500/40 text-blue-600"
                    }
                  >
                    {phase.isLocal ? "LOCALE" : "FASE"}
                  </Badge>
                  <span className="text-sm font-medium">
                    {phase.customName || phase.name}
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <span className="text-xs text-muted-foreground tabular-nums">
                    {phase.budgetHours.toFixed(1)} h prev. ·{" "}
                    {phase.hoursWorked.toFixed(1)} h lav. · {pct}%
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
                        onClick: () => void handleDeletePhase(phase),
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
                      onRemove={() => void handleRemove(a)}
                    />
                  ))
                )}
                <button
                  type="button"
                  className="flex w-full items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-primary hover:bg-accent"
                  onClick={() => setAddTechPhase(phase)}
                >
                  <UserPlus className="size-3.5" />
                  Aggiungi tecnico
                </button>
              </div>
            </div>
          )
        })
      )}

      <AddTechDialog
        phase={addTechPhase}
        onClose={() => setAddTechPhase(null)}
        onAdded={() => {
          setAddTechPhase(null)
          onChanged()
        }}
      />
      <NewLocalPhaseDialog
        projectId={projectId}
        open={newLocalOpen}
        presetSectionId={sectionTemplateId}
        onClose={() => setNewLocalOpen(false)}
        onCreated={() => {
          setNewLocalOpen(false)
          onChanged()
        }}
      />
      <ImportPhasesDialog
        projectId={projectId}
        open={importOpen}
        existingTemplateIds={existingTemplateIds}
        onClose={() => setImportOpen(false)}
        onImported={() => {
          setImportOpen(false)
          onChanged()
        }}
      />
    </div>
  )
}
