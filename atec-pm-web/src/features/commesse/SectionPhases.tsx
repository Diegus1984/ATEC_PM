import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import {
  CheckCircle2,
  Plus,
  Power,
  PowerOff,
  RefreshCw,
  RotateCcw,
  Trash2,
  UserPlus,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { canWriteFeature } from "@/lib/auth/permissions"
import { notifyError, notifySuccess } from "@/lib/toast"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  bulkCreatePhases,
  deletePhaseAssignment,
  deleteProjectPhase,
  fetchPhaseTemplates,
  setProjectPhaseOff,
  setProjectPhaseStatus,
  updateAssignmentHours,
} from "@/lib/api/phases"
import type { PhaseAssignmentDto, PhaseListItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { bvaPhaseHoursClass } from "./bva-shared"
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
  existingPhaseKeys,
  onChanged,
}: {
  projectId: number
  sectionTemplateId: number | null
  phases: PhaseListItem[]
  existingPhaseKeys: Set<string>
  onChanged: () => void
}) {
  const confirm = useConfirm()
  const [addTechPhase, setAddTechPhase] = React.useState<PhaseListItem | null>(
    null
  )
  const [newLocalOpen, setNewLocalOpen] = React.useState(false)
  const [importOpen, setImportOpen] = React.useState(false)

  // «Importa fase» resta ristretta (segnalazione #51, 07/08/2026): con le fasi
  // assegnate dalla Configurazione sezioni di costo la funzione non serve più, ma non
  // la si cancella — è la via di scampo se una commessa nasce senza le fasi giuste.
  // A dire chi ce l'ha è ora la chiave `action.import_project_phases` sulla persona.
  // «Fase locale» invece resta a tutti: è il modo previsto per la fase speciale.
  const canImportPhases = canWriteFeature("action.import_project_phases")

  // «Allinea dall'anagrafica» (segnalazione #57, 10/08/2026) — è un'altra cosa da «Importa
  // fase», ed è per questo che esiste anche dove quella resta nascosta: non fa scegliere
  // niente e non può portare dentro fasi di altri mestieri. Prende ESATTAMENTE le fasi che
  // la Configurazione Sezioni di Costo aggancia a QUESTA sezione e che la commessa non ha
  // ancora, e le aggiunge.
  // Serve perché la commessa è uno snapshot: le fasi entrano alla sua nascita, e ciò che si
  // aggancia dopo in anagrafica non la raggiunge più. Paolo l'ha visto così — aggancia «Capo
  // Cantiere / Coordinatore Attività» alla sezione del PM, torna nel Timesheet e non trova
  // niente di nuovo. A dire chi può farlo è ora la chiave `action.sync_project_phases` sulla
  // persona: aggiunge fasi alla commessa, quindi è una scrittura (`canWriteFeature`).
  const canSyncFromCatalog = canWriteFeature("action.sync_project_phases")

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
  const offMutation = useMutation({
    mutationFn: ({ id, isOff }: { id: number; isOff: boolean }) =>
      setProjectPhaseOff(id, isOff),
    onSuccess: () => onChanged(),
    onError: (err: Error) => notifyError(err),
  })
  // #106: chiusura della fase. È quello che alimenta «Avanzamento Commessa»:
  // prima nessuna schermata scriveva `COMPLETED` e la percentuale restava a 0%
  // anche su una commessa quasi finita.
  const statusMutation = useMutation({
    mutationFn: ({ id, done }: { id: number; done: boolean }) =>
      setProjectPhaseStatus(id, done ? "COMPLETED" : "IN_PROGRESS"),
    onSuccess: (_r, vars) => {
      notifySuccess(vars.done ? "Fase completata" : "Fase riaperta")
      onChanged()
    },
    onError: (err: Error) => notifyError(err),
  })

  // Anagrafica delle fasi: stessa chiave di query di «Importa fase», quindi una sola lettura
  // anche con venti sezioni aperte.
  const templatesQuery = useQuery({
    queryKey: ["phase-templates"],
    queryFn: fetchPhaseTemplates,
    enabled: canSyncFromCatalog && sectionTemplateId != null,
  })

  /** Fasi che l'anagrafica aggancia a questa sezione e che la commessa non ha ancora. */
  const missingFromCatalog = React.useMemo(() => {
    if (sectionTemplateId == null) return []
    return (templatesQuery.data ?? [])
      .filter(
        (template) =>
          template.sections.some((link) => link.sectionId === sectionTemplateId) &&
          !existingPhaseKeys.has(`${template.id}:${sectionTemplateId}`)
      )
      .map((template) => ({ templateId: template.id, name: template.name }))
      .sort((a, b) => a.name.localeCompare(b.name, "it"))
  }, [templatesQuery.data, existingPhaseKeys, sectionTemplateId])

  const syncMutation = useMutation({
    mutationFn: () =>
      bulkCreatePhases({
        projectId,
        templateIds: [],
        items: missingFromCatalog.map((row) => ({
          templateId: row.templateId,
          sectionId: sectionTemplateId,
        })),
      }),
    onSuccess: () => {
      notifySuccess(
        missingFromCatalog.length === 1
          ? "1 fase aggiunta dall'anagrafica."
          : `${missingFromCatalog.length} fasi aggiunte dall'anagrafica.`
      )
      onChanged()
    },
    onError: (err: Error) => notifyError(err),
  })

  async function handleSyncFromCatalog() {
    if (missingFromCatalog.length === 0) return
    const elenco = missingFromCatalog.map((row) => `• ${row.name}`).join("\n")
    const ok = await confirm({
      title: "Allinea dall'anagrafica",
      description:
        `In Configurazione Sezioni di Costo questa sezione ha ${missingFromCatalog.length} ` +
        `fase/i che la commessa non ha ancora:\n${elenco}\n\n` +
        "Vengono aggiunte alla commessa (senza tecnici né ore preventivate) e da subito " +
        "compaiono nella tendina del Timesheet di chi è del reparto. Nessun numero si muove.",
      confirmLabel: "Aggiungi",
      destructive: false,
    })
    if (ok) syncMutation.mutate()
  }

  // Le fasi spente restano fuori dall'elenco: è tutto il senso della funzione. Il pulsante
  // «spente (N)» le rimette a video per poterle riaccendere — senza, spegnere sarebbe una
  // strada a senso unico.
  const [showOff, setShowOff] = React.useState(false)
  const offCount = phases.filter((p) => p.isOff).length
  const visiblePhases = showOff ? phases : phases.filter((p) => !p.isOff)

  async function handleRemove(a: PhaseAssignmentDto) {
    const ok = await confirm({
      title: "Rimuovi tecnico",
      description: `Rimuovere "${a.employeeName}" dalla fase?`,
      confirmLabel: "Rimuovi",
    })
    if (ok) removeMutation.mutate(a.id)
  }
  async function handleToggleOff(phase: PhaseListItem) {
    const nome = phase.customName || phase.name
    if (phase.isOff) {
      offMutation.mutate({ id: phase.id, isOff: false })
      return
    }
    // Due messaggi diversi, perché sono due situazioni diverse (deciso l'08/08/2026):
    // fase vuota → sparisce e non lascia niente; fase con ore → sparisce dalla vista ma
    // le ore restano nei costi. Chi spegne deve saperlo PRIMA, non scoprirlo dopo.
    const conOre = phase.hoursWorked > 0
    const ok = await confirm({
      title: "Spegni fase",
      description: conOre
        ? `«${nome}» ha ${phase.hoursWorked.toFixed(1)} ore già imputate. Spegnendola sparisce da questo elenco e non accetterà ore nuove dal Timesheet, ma quelle ore e il loro costo CONTINUANO a contare nel Bilancio.`
        : `«${nome}» non ha ore imputate: sparisce da questo elenco e non accetterà ore dal Timesheet. Non lascia niente nei conti.`,
      confirmLabel: "Spegni",
    })
    if (ok) offMutation.mutate({ id: phase.id, isOff: true })
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
          {offCount > 0 ? (
            <Button
              variant="ghost"
              size="sm"
              className="h-7 text-xs text-muted-foreground"
              onClick={() => setShowOff((v) => !v)}
            >
              <PowerOff className="size-3.5" />
              {showOff ? "Nascondi spente" : `Spente (${offCount})`}
            </Button>
          ) : null}
          {canSyncFromCatalog && missingFromCatalog.length > 0 ? (
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs"
              disabled={syncMutation.isPending}
              title="Aggiunge alla commessa le fasi che la Configurazione Sezioni di Costo aggancia a questa sezione"
              onClick={() => void handleSyncFromCatalog()}
            >
              <RefreshCw className="size-3.5" />
              Allinea dall'anagrafica ({missingFromCatalog.length})
            </Button>
          ) : null}
          {canImportPhases ? (
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs"
              onClick={() => setImportOpen(true)}
            >
              <Plus className="size-3.5" />
              Importa fase
            </Button>
          ) : null}
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

      {visiblePhases.length === 0 ? (
        <p className="text-xs text-muted-foreground">
          {phases.length === 0
            ? "Nessuna fase in questa sezione."
            : `Nessuna fase attiva in questa sezione (${offCount} spenta/e).`}
        </p>
      ) : (
        visiblePhases.map((phase) => {
          const pct =
            phase.budgetHours > 0
              ? Math.round((phase.hoursWorked / phase.budgetHours) * 100)
              : 0
          return (
            <div
              key={phase.id}
              className={phase.isOff ? "rounded-md border opacity-60" : "rounded-md border"}
            >
              <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-muted/20 px-3 py-1.5">
                <div className="flex min-w-0 items-center gap-2">
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
                  <span className="truncate text-sm font-medium" title={phase.customName || phase.name}>
                    {phase.customName || phase.name}
                  </span>
                  {phase.isOff ? (
                    <Badge
                      variant="outline"
                      className="border-muted-foreground/40 text-muted-foreground"
                      title={
                        phase.hoursWorked > 0
                          ? "Spenta: fuori dall'elenco e dal Timesheet, ma le sue ore contano ancora nei costi"
                          : "Spenta: fuori dall'elenco e dal Timesheet"
                      }
                    >
                      SPENTA
                    </Badge>
                  ) : null}
                </div>

                {/* #110: Comando fase completata al centro — carattere nero, fondo rosso pastello
                    gradiente. `order-last basis-full`: la card della fase è stretta (~390 px) e
                    le tre parti dell'intestazione vanno a capo comunque; senza questo il comando
                    finiva in fondo alla prima riga, cioè a destra e non al centro (misurato:
                    112 px fuori asse). Così si prende una riga sua e sta al centro davvero,
                    a qualunque larghezza. */}
                <div className="order-last flex basis-full items-center justify-center">
                  <button
                    type="button"
                    disabled={statusMutation.isPending || phase.isOff}
                    onClick={() =>
                      statusMutation.mutate({
                        id: phase.id,
                        done: phase.status !== "COMPLETED",
                      })
                    }
                    className={cn(
                      "inline-flex items-center gap-1.5 rounded-md border px-2.5 py-0.5 text-xs font-semibold shadow-xs transition-all",
                      phase.isOff
                        ? "cursor-not-allowed opacity-50 bg-muted text-muted-foreground border-transparent"
                        : phase.status === "COMPLETED"
                          ? "bg-gradient-to-r from-emerald-100 via-green-50 to-emerald-100 border-emerald-300 text-black hover:from-emerald-200 hover:to-green-100 dark:from-emerald-950/70 dark:via-green-900/50 dark:to-emerald-950/70 dark:border-emerald-800 dark:text-neutral-100 cursor-pointer"
                          : "bg-gradient-to-r from-red-200 via-rose-100 to-red-200 border-red-300 text-black hover:from-red-300 hover:via-rose-200 hover:to-red-300 dark:from-red-950/70 dark:via-rose-900/50 dark:to-red-950/70 dark:border-red-800 dark:text-neutral-100 cursor-pointer"
                    )}
                    title={
                      phase.isOff
                        ? "Fase spenta"
                        : phase.status === "COMPLETED"
                          ? "Fase completata: clicca per riaprirla"
                          : "Fase in corso: clicca per segnarla come completata"
                    }
                  >
                    {phase.status === "COMPLETED" ? (
                      <>
                        <CheckCircle2 className="size-3.5 text-emerald-700 dark:text-emerald-400" />
                        <span>Fase completata</span>
                        <RotateCcw className="size-3 text-neutral-600 opacity-60 ml-0.5" />
                      </>
                    ) : (
                      <>
                        <CheckCircle2 className="size-3.5 text-red-700 dark:text-red-400" />
                        <span>Fase completata</span>
                      </>
                    )}
                  </button>
                </div>

                <div className="flex items-center gap-2">
                  <span className={cn("text-xs", bvaPhaseHoursClass)}>
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
                        label: phase.isOff ? "Riaccendi fase" : "Spegni fase",
                        icon: phase.isOff ? Power : PowerOff,
                        separatorBefore: true,
                        onClick: () => void handleToggleOff(phase),
                      },
                      {
                        label: "Elimina fase",
                        icon: Trash2,
                        destructive: true,
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
      {canImportPhases ? (
        <ImportPhasesDialog
          projectId={projectId}
          open={importOpen}
          existingPhaseKeys={existingPhaseKeys}
          sectionFilterId={sectionTemplateId}
          onClose={() => setImportOpen(false)}
          onImported={() => {
            setImportOpen(false)
            onChanged()
          }}
        />
      ) : null}
    </div>
  )
}
