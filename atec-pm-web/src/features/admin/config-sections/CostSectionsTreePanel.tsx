import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import {
  ChevronRight,
  GripVertical,
  Pencil,
  Plus,
  Star,
  Trash2,
  Unlink,
  X,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Collapsible } from "@/components/ui/collapsible"
import {
  Dialog,
  DialogContent,
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import {
  createCostSectionGroup,
  createCostSectionTemplate,
  deleteCostSectionGroup,
  deleteCostSectionTemplate,
  updateSectionDepartments,
} from "@/lib/api/cost-sections"
import {
  addPhaseToSection,
  createPhaseTemplate,
  deletePhaseTemplate,
  removePhaseFromSection,
  reorderSectionPhases,
  updatePhaseSectionLink,
} from "@/lib/api/phases"
import type {
  CostSectionGroupDto,
  CostSectionTemplateDto,
  DepartmentDto,
  PhaseTemplateDto,
  PhaseTemplateSectionLink,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"

import {
  EditGroupDialog,
  EditPhaseDialog,
  EditSectionDialog,
  PromptDialog,
} from "./config-sections-dialogs"

const DRAG_DEPT = "application/x-atec-dept"
const DRAG_PHASE = "application/x-atec-phase"
const DRAG_PHASE_REORDER = "application/x-atec-phase-reorder"

/** Il legame di una fase con una sezione: `undefined` se la fase non è in quella sezione. */
function linkOf(
  phase: PhaseTemplateDto,
  sectionId: number
): PhaseTemplateSectionLink | undefined {
  return phase.sections.find((link) => link.sectionId === sectionId)
}

/**
 * Le fasi di una sezione, **nell'ordine del legame**. L'ordine è per sezione: la stessa fase
 * può stare terza in Program Manager e prima in Progettazione.
 */
function sectionPhasesOf(
  phases: PhaseTemplateDto[],
  sectionId: number
): PhaseTemplateDto[] {
  return phases
    .filter((phase) => linkOf(phase, sectionId) != null)
    .slice()
    .sort(
      (a, b) =>
        (linkOf(a, sectionId)?.sortOrder ?? 0) - (linkOf(b, sectionId)?.sortOrder ?? 0) ||
        a.name.localeCompare(b.name, "it")
    )
}

interface CostSectionsTreePanelProps {
  groups: CostSectionGroupDto[]
  templates: CostSectionTemplateDto[]
  departments: DepartmentDto[]
  phases: PhaseTemplateDto[]
  onRefresh: () => Promise<void>
}

export function CostSectionsTreePanel({
  groups,
  templates,
  departments,
  phases,
  onRefresh,
}: CostSectionsTreePanelProps) {
  const confirm = useConfirm()
  const [expandedGroupId, setExpandedGroupId] = React.useState<number | null>(
    groups[0]?.id ?? null
  )
  const [dropHighlight, setDropHighlight] = React.useState<string | null>(null)

  const [editGroup, setEditGroup] = React.useState<CostSectionGroupDto | null>(
    null
  )
  const [editSection, setEditSection] =
    React.useState<CostSectionTemplateDto | null>(null)
  const [editPhase, setEditPhase] = React.useState<PhaseTemplateDto | null>(null)
  const [addGroupOpen, setAddGroupOpen] = React.useState(false)
  const [addSectionGroup, setAddSectionGroup] =
    React.useState<CostSectionGroupDto | null>(null)
  const [addPhaseSection, setAddPhaseSection] =
    React.useState<CostSectionTemplateDto | null>(null)
  const [addPhaseDockOpen, setAddPhaseDockOpen] = React.useState(false)

  const refreshMutation = useMutation({ mutationFn: onRefresh })

  const sortedGroups = groups.slice().sort((a, b) => a.sortOrder - b.sortOrder)

  /**
   * Fasi agganciate a NESSUNA sezione. Nel modello multi-sezione vuol dire una cosa precisa:
   * **non entreranno in nessuna commessa**, né da sole né dal picker. E se qualcuno ci imputa
   * ore da una fase locale omonima, quelle ore non si sanno attribuire né a «in sede» né a
   * «da cliente», quindi restano fuori dalla ripartizione del Bilancio.
   */
  const orphanPhases = React.useMemo(
    () =>
      phases
        .filter((phase) => phase.sections.length === 0)
        .slice()
        .sort((a, b) => a.name.localeCompare(b.name, "it")),
    [phases]
  )

  const linkedPhaseCount = phases.length - orphanPhases.length

  async function handleAddDepartmentToSection(
    section: CostSectionTemplateDto,
    departmentId: number
  ) {
    if (section.departmentIds.includes(departmentId)) {
      return
    }
    try {
      await updateSectionDepartments(section.id, [
        ...section.departmentIds,
        departmentId,
      ])
      await refreshMutation.mutateAsync()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  async function handleRemoveDepartment(
    section: CostSectionTemplateDto,
    departmentId: number
  ) {
    const dept = departments.find((item) => item.id === departmentId)
    const ok = await confirm({
      title: "Rimuovi reparto",
      description: `Rimuovere il reparto "${dept?.code ?? departmentId}" dalla sezione "${section.name}"?`,
      confirmLabel: "Rimuovi",
    })
    if (!ok) return
    try {
      await updateSectionDepartments(
        section.id,
        section.departmentIds.filter((id) => id !== departmentId)
      )
      await refreshMutation.mutateAsync()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  /**
   * Riordina DENTRO una sezione: l'ordine è del legame, quindi spostare «Call Cliente» in
   * Program Manager non tocca la sua posizione in Progettazione.
   */
  async function handleReorderPhase(
    movedPhaseId: number,
    targetPhase: PhaseTemplateDto,
    sectionId: number
  ) {
    const movedPhase = phases.find((phase) => phase.id === movedPhaseId)
    if (!movedPhase || movedPhase.id === targetPhase.id) {
      return
    }

    const ordered = sectionPhasesOf(phases, sectionId)
    const withoutMoved = ordered.filter((phase) => phase.id !== movedPhase.id)
    const targetIndex = withoutMoved.findIndex((phase) => phase.id === targetPhase.id)
    withoutMoved.splice(targetIndex < 0 ? 0 : targetIndex, 0, movedPhase)

    try {
      await reorderSectionPhases(
        sectionId,
        withoutMoved.map((phase) => phase.id)
      )
      await refreshMutation.mutateAsync()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  /** Drop dal dock: **aggiunge** la fase alla sezione, non la toglie dalle altre. */
  async function handleLinkPhaseById(
    phaseId: number,
    section: CostSectionTemplateDto
  ) {
    try {
      await addPhaseToSection(phaseId, section.id)
      await refreshMutation.mutateAsync()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  async function handleToggleSectionDefault(
    phase: PhaseTemplateDto,
    sectionId: number,
    isDefault: boolean
  ) {
    try {
      await updatePhaseSectionLink(phase.id, sectionId, { isDefault })
      await refreshMutation.mutateAsync()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  async function handleDeleteGroup(group: CostSectionGroupDto) {
    const sectionCount = templates.filter(
      (template) => template.groupId === group.id
    ).length
    if (sectionCount > 0) {
      notifyError(
        `Impossibile eliminare «${group.name}»: contiene ancora ${sectionCount} sezion${
          sectionCount === 1 ? "e" : "i"
        }. Eliminale prima.`
      )
      return
    }
    const ok = await confirm({
      title: "Elimina gruppo",
      description: `Eliminare il gruppo "${group.name}"? (deve essere vuoto)`,
      confirmLabel: "Elimina",
    })
    if (!ok) {
      return
    }
    try {
      await deleteCostSectionGroup(group.id)
      await refreshMutation.mutateAsync()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  async function handleDeleteSection(section: CostSectionTemplateDto) {
    // Con fasi dentro il server rifiuta: dirlo prima, invece di far confermare un'azione che
    // non può riuscire (è la stessa scelta fatta per il gruppo non vuoto).
    const phaseCount = sectionPhasesOf(phases, section.id).length
    if (phaseCount > 0) {
      notifyError(
        `Impossibile eliminare «${section.name}»: ha ancora ${phaseCount} fas${
          phaseCount === 1 ? "e" : "i"
        }. Toglile dalla sezione, oppure disattiva la sezione invece di cancellarla.`
      )
      return
    }
    const ok = await confirm({
      title: "Elimina sezione di costo",
      description: `Eliminare la sezione "${section.name}"? Se è usata in commesse o preventivi, l'operazione verrà rifiutata.`,
      confirmLabel: "Elimina",
    })
    if (!ok) {
      return
    }
    try {
      await deleteCostSectionTemplate(section.id)
      await refreshMutation.mutateAsync()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  async function handleDeletePhase(phase: PhaseTemplateDto) {
    const ok = await confirm({
      title: "Elimina fase dettaglio commessa",
      description:
        phase.sections.length > 1
          ? `Eliminare «${phase.name}» dall'anagrafica? Sparisce da tutte e ${phase.sections.length} le sezioni in cui è. Per toglierla da una sola, usa «Togli da questa sezione». Sulle commesse già create resta come fase locale.`
          : `Eliminare la fase "${phase.name}"? Sulle commesse già create la fase resta come locale (senza template).`,
      confirmLabel: "Elimina",
    })
    if (!ok) {
      return
    }
    try {
      await deletePhaseTemplate(phase.id)
      await refreshMutation.mutateAsync()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  /**
   * Toglie la fase da UNA sezione: resta viva in anagrafica e nelle altre sezioni, e le
   * commesse già create non cambiano (la sezione è scritta sulla riga della fase di commessa).
   */
  async function handleRemoveFromSection(
    phase: PhaseTemplateDto,
    section: CostSectionTemplateDto
  ) {
    const stillIn = phase.sections.length - 1
    const ok = await confirm({
      title: "Togli dalla sezione",
      description:
        stillIn > 0
          ? `Togliere «${phase.name}» da «${section.name}»? Resta nelle altre ${stillIn} sezion${stillIn === 1 ? "e" : "i"}.`
          : `Togliere «${phase.name}» da «${section.name}»? Resta in anagrafica ma senza sezioni: non entrerà in nessuna commessa nuova.`,
      confirmLabel: "Togli",
    })
    if (!ok) {
      return
    }
    try {
      await removePhaseFromSection(phase.id, section.id)
      await refreshMutation.mutateAsync()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  return (
    <>
    {/* Altezza ereditata dalla card: il dock fasi sta fermo, scorre solo l'albero. */}
    <div className="grid h-full min-h-0 gap-4 lg:grid-cols-[minmax(260px,320px)_minmax(0,1fr)]">
      <PhasesDockPanel
        phases={phases}
        onEditPhase={setEditPhase}
        onDeletePhase={handleDeletePhase}
        onAddPhase={() => setAddPhaseDockOpen(true)}
      />

      <div className="flex min-h-0 min-w-0 flex-col gap-3">
      <div className="flex shrink-0 items-center justify-between gap-2">
        <p className="text-xs text-muted-foreground">
          {groups.length} gruppi (a) — {templates.length} sezioni (b) —{" "}
          <span className={cn(orphanPhases.length > 0 && "font-medium text-amber-700 dark:text-amber-400")}>
            {linkedPhaseCount} di {phases.length} fasi (d) collegate
          </span>
        </p>
        <Button size="sm" variant="outline" onClick={() => setAddGroupOpen(true)}>
          <Plus />
          Nuovo gruppo
        </Button>
      </div>

      {/* L'unico scroller della pagina: i gruppi scorrono qui dentro, i dock restano fermi.
          `pr-1` lascia respiro fra la barra di scorrimento e le righe. */}
      <div className="min-h-0 flex-1 space-y-2 overflow-y-auto pr-1">
        {sortedGroups.map((group) => {
          const groupSections = templates
            .filter((template) => template.groupId === group.id)
            .sort((a, b) => a.sortOrder - b.sortOrder)
          const isExpanded = expandedGroupId === group.id
          const bgColor = group.bgColor || "#6B7280"
          const textColor = group.textColor || "#FFFFFF"

          return (
            <div key={group.id} className="rounded-lg border">
              <div
                className="flex items-center gap-2 px-3 py-2"
                style={{ backgroundColor: bgColor, color: textColor }}
              >
                <button
                  type="button"
                  className="flex flex-1 items-center gap-2 text-left"
                  onClick={() =>
                    setExpandedGroupId(isExpanded ? null : group.id)
                  }
                >
                  <ChevronRight
                    className={cn(
                      "size-4 shrink-0 transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
                      isExpanded && "rotate-90"
                    )}
                  />
                  <span className="text-sm font-semibold">{group.name}</span>
                  <span className="text-xs opacity-80">
                    ({groupSections.length} sezioni)
                  </span>
                </button>
                <RowActionsMenu
                  size="icon-sm"
                  triggerClassName="text-inherit hover:bg-white/20"
                  actions={[
                    {
                      label: "Modifica gruppo",
                      icon: Pencil,
                      onClick: () => setEditGroup(group),
                    },
                    {
                      label: "Nuova sezione di costo",
                      icon: Plus,
                      onClick: () => setAddSectionGroup(group),
                    },
                    {
                      label: "Elimina gruppo",
                      icon: Trash2,
                      destructive: true,
                      separatorBefore: true,
                      onClick: () => handleDeleteGroup(group),
                    },
                  ]}
                />
              </div>

              <Collapsible open={isExpanded}>
                <div className="space-y-1 p-2">
                  {groupSections.length === 0 ? (
                    <p className="px-2 py-4 text-center text-sm text-muted-foreground">
                      Nessuna sezione. Usa + sul gruppo per aggiungerne una.
                    </p>
                  ) : (
                    groupSections.map((section) => (
                      <SectionNode
                        key={section.id}
                        section={section}
                        departments={departments}
                        phases={sectionPhasesOf(phases, section.id)}
                        dropHighlight={dropHighlight}
                        onDropHighlight={setDropHighlight}
                        onAddDepartment={handleAddDepartmentToSection}
                        onRemoveDepartment={handleRemoveDepartment}
                        onLinkPhaseById={handleLinkPhaseById}
                        onReorderPhase={handleReorderPhase}
                        onEditSection={() => setEditSection(section)}
                        onAddPhase={() => setAddPhaseSection(section)}
                        onDeleteSection={() => handleDeleteSection(section)}
                        onEditPhase={setEditPhase}
                        onDeletePhase={handleDeletePhase}
                        onRemoveFromSection={handleRemoveFromSection}
                        onToggleSectionDefault={handleToggleSectionDefault}
                      />
                    ))
                  )}
                </div>
              </Collapsible>
            </div>
          )
        })}
      </div>
      </div>
    </div>

      <EditGroupDialog
        open={editGroup !== null}
        group={editGroup}
        onClose={() => setEditGroup(null)}
        onSaved={async () => {
          setEditGroup(null)
          await refreshMutation.mutateAsync()
        }}
      />

      <EditSectionDialog
        open={editSection !== null}
        section={editSection}
        onClose={() => setEditSection(null)}
        onSaved={async () => {
          setEditSection(null)
          await refreshMutation.mutateAsync()
        }}
      />

      <EditPhaseDialog
        open={editPhase !== null}
        phase={editPhase}
        onClose={() => setEditPhase(null)}
        onSaved={async () => {
          setEditPhase(null)
          await refreshMutation.mutateAsync()
        }}
      />

      <PromptDialog
        open={addGroupOpen}
        title="Nuovo gruppo"
        label="Nome gruppo"
        onClose={() => setAddGroupOpen(false)}
        onConfirm={async (name) => {
          const maxSort = groups.length
            ? Math.max(...groups.map((group) => group.sortOrder)) + 1
            : 1
          await createCostSectionGroup({
            id: 0,
            name,
            sortOrder: maxSort,
            isActive: true,
          })
          setAddGroupOpen(false)
          await refreshMutation.mutateAsync()
        }}
      />

      <AddSectionPrompt
        open={addSectionGroup !== null}
        group={addSectionGroup}
        templates={templates}
        departments={departments}
        onClose={() => setAddSectionGroup(null)}
        onSaved={async (groupId) => {
          setExpandedGroupId(groupId)
          setAddSectionGroup(null)
          await refreshMutation.mutateAsync()
        }}
      />

      <PromptDialog
        open={addPhaseSection !== null}
        title={
          addPhaseSection
            ? `Nuova fase per "${addPhaseSection.name}"`
            : "Nuova fase dettaglio commessa"
        }
        label="Nome fase dettaglio commessa"
        description="La stessa fase può poi essere agganciata ad altre sezioni trascinandola dal dock: non serve ricrearla con un altro nome."
        onClose={() => setAddPhaseSection(null)}
        onConfirm={async (name) => {
          if (!addPhaseSection) {
            return
          }
          if (addPhaseSection.departmentIds.length === 0) {
            notifyError(
              "Aggiungi prima almeno un reparto alla sezione trascinandolo dal pannello sinistro."
            )
            return
          }
          const maxSort = phases.length
            ? Math.max(...phases.map((phase) => phase.sortOrder)) + 1
            : 1
          // Nasce dentro una sezione → nasce anche sulle commesse nuove. Si spegne dalla riga.
          await createPhaseTemplate({
            name,
            costSectionTemplateId: addPhaseSection.id,
            sortOrder: maxSort,
            isDefault: true,
          })
          setAddPhaseSection(null)
          await refreshMutation.mutateAsync()
        }}
      />

      <PromptDialog
        open={addPhaseDockOpen}
        title="Nuova fase dettaglio commessa"
        label="Nome fase"
        description="La fase compare nel dock senza sezione (b). Trascinala su tutte le sezioni in cui serve: è sempre la stessa fase."
        onClose={() => setAddPhaseDockOpen(false)}
        onConfirm={async (name) => {
          const maxSort = phases.length
            ? Math.max(...phases.map((phase) => phase.sortOrder)) + 1
            : 1
          await createPhaseTemplate({
            name,
            costSectionTemplateId: null,
            sortOrder: maxSort,
            isDefault: false,
          })
          setAddPhaseDockOpen(false)
          await refreshMutation.mutateAsync()
        }}
      />
    </>
  )
}

function SectionNode({
  section,
  departments,
  phases,
  dropHighlight,
  onDropHighlight,
  onAddDepartment,
  onRemoveDepartment,
  onLinkPhaseById,
  onReorderPhase,
  onEditSection,
  onAddPhase,
  onDeleteSection,
  onEditPhase,
  onDeletePhase,
  onRemoveFromSection,
  onToggleSectionDefault,
}: {
  section: CostSectionTemplateDto
  /** Già filtrate e ordinate per questa sezione (`sectionPhasesOf`). */
  phases: PhaseTemplateDto[]
  departments: DepartmentDto[]
  dropHighlight: string | null
  onDropHighlight: (key: string | null) => void
  onAddDepartment: (
    section: CostSectionTemplateDto,
    departmentId: number
  ) => Promise<void>
  onRemoveDepartment: (
    section: CostSectionTemplateDto,
    departmentId: number
  ) => Promise<void>
  onLinkPhaseById: (phaseId: number, section: CostSectionTemplateDto) => Promise<void>
  onReorderPhase: (
    movedPhaseId: number,
    target: PhaseTemplateDto,
    sectionId: number
  ) => Promise<void>
  onEditSection: () => void
  onAddPhase: () => void
  onDeleteSection: () => void
  onEditPhase: (phase: PhaseTemplateDto) => void
  onDeletePhase: (phase: PhaseTemplateDto) => void
  onRemoveFromSection: (
    phase: PhaseTemplateDto,
    section: CostSectionTemplateDto
  ) => void
  onToggleSectionDefault: (
    phase: PhaseTemplateDto,
    sectionId: number,
    isDefault: boolean
  ) => void
}) {
  const sectionKey = `section-${section.id}`
  const typeColor = section.sectionType === "DA_CLIENTE" ? "#D97706" : "#059669"
  const typeLabel =
    section.sectionType === "DA_CLIENTE" ? "DA CLIENTE" : "IN SEDE"

  const sectionDepartments = section.departmentIds
    .map((id) => departments.find((dept) => dept.id === id))
    .filter((dept): dept is DepartmentDto => dept != null)

  function handleSectionDragOver(event: React.DragEvent) {
    if (
      event.dataTransfer.types.includes(DRAG_DEPT) ||
      event.dataTransfer.types.includes(DRAG_PHASE)
    ) {
      event.preventDefault()
      onDropHighlight(sectionKey)
    }
  }

  async function handleSectionDrop(event: React.DragEvent) {
    event.preventDefault()
    onDropHighlight(null)

    const deptId = event.dataTransfer.getData(DRAG_DEPT)
    if (deptId) {
      await onAddDepartment(section, Number(deptId))
      return
    }

    const phaseId = event.dataTransfer.getData(DRAG_PHASE)
    if (phaseId) {
      await onLinkPhaseById(Number(phaseId), section)
    }
  }

  return (
    <div
      className={cn(
        "rounded-md border bg-card p-2",
        !section.isActive && "opacity-55",
        dropHighlight === sectionKey && "ring-2 ring-primary/40"
      )}
      onDragOver={handleSectionDragOver}
      onDragLeave={() => onDropHighlight(null)}
      onDrop={handleSectionDrop}
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-semibold">{section.name}</span>
        <Badge
          variant="outline"
          className="text-[10px]"
          style={{ color: typeColor, borderColor: typeColor }}
        >
          {typeLabel}
        </Badge>
        {!section.isActive ? (
          <Badge variant="secondary" className="text-[10px]">
            Disattiva
          </Badge>
        ) : null}
        <div className="ml-auto">
          <RowActionsMenu
            size="icon-sm"
            actions={[
              { label: "Modifica sezione di costo", icon: Pencil, onClick: onEditSection },
              { label: "Nuova fase dettaglio commessa", icon: Plus, onClick: onAddPhase },
              {
                label: "Elimina sezione di costo",
                icon: Trash2,
                destructive: true,
                separatorBefore: true,
                onClick: onDeleteSection,
              },
            ]}
          />
        </div>
      </div>

      {sectionDepartments.length > 0 ? (
        <div className="mt-2 space-y-1">
          <p className="text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
            Reparti interessati
          </p>
          <div className="flex flex-wrap gap-1">
            {sectionDepartments.map((dept) => (
              <span
                key={dept.id}
                className="inline-flex items-center gap-1 rounded bg-muted px-2 py-0.5 text-xs"
              >
                <span className="font-semibold">{dept.code}</span>
                <span className="text-muted-foreground">{dept.name}</span>
                <button
                  type="button"
                  className="rounded p-0.5 hover:bg-background"
                  onClick={() => onRemoveDepartment(section, dept.id)}
                >
                  <X className="size-3" />
                </button>
              </span>
            ))}
          </div>
        </div>
      ) : (
        <p className="mt-2 text-xs text-muted-foreground">
          Trascina un reparto dal pannello sinistro per collegarlo.
        </p>
      )}

      {phases.length > 0 ? (
        <div className="mt-2 space-y-1 rounded-md bg-amber-50/50 p-2 dark:bg-amber-950/20">
          <p className="text-[10px] font-medium uppercase tracking-wide text-amber-700 dark:text-amber-400">
            Fasi dettaglio commessa ({phases.length})
          </p>
          {phases.map((phase) => (
            <PhaseRow
              key={phase.id}
              phase={phase}
              section={section}
              dropHighlight={dropHighlight}
              onDropHighlight={onDropHighlight}
              onReorder={onReorderPhase}
              onEdit={() => onEditPhase(phase)}
              onDelete={() => onDeletePhase(phase)}
              onRemoveFromSection={() => onRemoveFromSection(phase, section)}
              onToggleDefault={(next) =>
                onToggleSectionDefault(phase, section.id, next)
              }
            />
          ))}
        </div>
      ) : null}
    </div>
  )
}

/**
 * Dock (d): la libreria delle fasi, sempre in elenco. Trascina una riga su una sezione (b)
 * per **aggiungerla** lì — la fase resta anche nelle sezioni in cui è già, non si sposta.
 * È il punto di tutto: «Call Cliente» è una fase sola che vive sotto PM e sotto Progettazione.
 */
function PhasesDockPanel({
  phases,
  onEditPhase,
  onDeletePhase,
  onAddPhase,
}: {
  phases: PhaseTemplateDto[]
  onEditPhase: (phase: PhaseTemplateDto) => void
  onDeletePhase: (phase: PhaseTemplateDto) => Promise<void>
  onAddPhase: () => void
}) {
  const [filter, setFilter] = React.useState("")

  const rows = React.useMemo(() => {
    const q = filter.trim().toLowerCase()
    // Ordine dell'ANAGRAFICA (`sortOrder`), non alfabetico: la lista è stata importata in un
    // ordine ragionato — grosso modo il flusso del lavoro, dalla gestione commessa al
    // post-vendita — e cercare una fase è più veloce se sta dove uno se l'aspetta. Per il nome
    // c'è il filtro qui sopra. Le fasi senza sezione si riconoscono dal badge ambra, non
    // dalla posizione: portarle in cima spezzerebbe di nuovo l'ordine.
    const sorted = phases
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, "it"))
    if (!q) return sorted
    return sorted.filter((phase) => {
      const hay = [
        phase.name,
        ...phase.sections.map((link) => `${link.groupName} ${link.sectionName}`),
      ]
        .join(" ")
        .toLowerCase()
      return hay.includes(q)
    })
  }, [phases, filter])

  const orphanCount = phases.filter((phase) => phase.sections.length === 0).length

  // Alto quanto la card (che è alta quanto lo schermo): il dock è l'elenco su cui si lavora,
  // e ogni riga in meno è una fase da cercare col filtro. Non si muove mai — a scorrere è
  // l'albero al centro — quindi il punto da cui si trascina una fase è sempre dov'era.
  return (
    <aside className="flex h-full min-h-0 flex-col rounded-lg border bg-card shadow-xs">
      <div className="shrink-0 space-y-2 border-b p-3">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="text-sm font-semibold">Fasi dettaglio (d)</p>
            <p className="text-xs text-muted-foreground">
              {filter.trim()
                ? `${rows.length} di ${phases.length} visibili`
                : `${phases.length} fasi · sempre in elenco`}
              {orphanCount > 0 ? (
                <span className="font-medium text-amber-700 dark:text-amber-400">
                  {" "}
                  · {orphanCount} senza sezione (b)
                </span>
              ) : null}
            </p>
          </div>
          <Button
            type="button"
            size="sm"
            variant="outline"
            className="shrink-0"
            onClick={onAddPhase}
          >
            <Plus />
            Nuova
          </Button>
        </div>
        <Input
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          placeholder="Cerca fase…"
          className="h-8"
          aria-label="Filtra fasi"
        />
        <p className="text-[11px] leading-snug text-muted-foreground">
          Trascina la stessa fase su tutte le sezioni (b) in cui serve: si aggiunge, non
          si sposta. Ferma il cursore su una riga per vedere dov'è già.
        </p>
      </div>
      {/*
        Tooltip a 10 secondi, e ogni riga se li fa tutti (`skipDelayDuration={0}`, altrimenti
        dopo il primo Radix apre gli altri all'istante). Qui dentro il cursore ci passa sopra
        di continuo per trascinare: comparendo subito copriva le righe vicine proprio mentre
        si mira. Così esce solo se lo si lascia fermo apposta.
      */}
      <TooltipProvider delayDuration={10000} skipDelayDuration={0}>
      <div className="min-h-0 flex-1 overflow-auto">
        {rows.length === 0 ? (
          <p className="p-3 text-xs text-muted-foreground">
            {filter.trim() ? "Nessuna fase corrisponde alla ricerca." : "Nessuna fase."}
          </p>
        ) : (
          <Table className="text-xs">
            <TableHeader className="sticky top-0 z-10 bg-background">
              <TableRow>
                <TableHead className="h-8 w-6 px-1" />
                <TableHead className="h-8">Fase</TableHead>
                <TableHead className="h-8 w-8 px-1" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((phase) => {
                const assignmentLabel =
                  phase.sections.length > 0
                    ? `In ${phase.sections.length} sezion${
                        phase.sections.length === 1 ? "e" : "i"
                      }:\n${phase.sections
                        .map(
                          (link) =>
                            `• ${link.groupName ? `${link.groupName} · ` : ""}${link.sectionName}${
                              link.isDefault ? " (default)" : ""
                            }`
                        )
                        .join("\n")}`
                    : "In nessuna sezione (b): non entrerà in nessuna commessa"
                return (
                  <TableRow
                    key={phase.id}
                    draggable
                    onDragStart={(event) => {
                      event.dataTransfer.setData(DRAG_PHASE, String(phase.id))
                      event.dataTransfer.effectAllowed = "copy"
                    }}
                    onDoubleClick={() => onEditPhase(phase)}
                    className="cursor-grab bg-background hover:bg-muted/40 active:cursor-grabbing"
                  >
                    <TableCell className="bg-background px-1">
                      <GripVertical className="size-3.5 text-muted-foreground" />
                    </TableCell>
                    <TableCell className="bg-background whitespace-normal">
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <div className="min-w-0">
                            <div className="font-medium leading-snug">{phase.name}</div>
                            <div className="mt-0.5 flex flex-wrap items-center gap-1">
                              {phase.sections.length === 0 ? (
                                <Badge
                                  variant="outline"
                                  className="border-amber-500 text-[9px] text-amber-700 dark:text-amber-400"
                                >
                                  senza sezione
                                </Badge>
                              ) : (
                                <Badge variant="secondary" className="text-[9px]">
                                  {phase.sections.length} sezion
                                  {phase.sections.length === 1 ? "e" : "i"}
                                </Badge>
                              )}
                              {phase.isDefault ? (
                                <Badge variant="outline" className="text-[9px]">
                                  Default
                                </Badge>
                              ) : null}
                            </div>
                          </div>
                        </TooltipTrigger>
                        <TooltipContent className="max-w-xs whitespace-pre-line">
                          {assignmentLabel}
                        </TooltipContent>
                      </Tooltip>
                    </TableCell>
                    <TableCell className="bg-background px-1">
                      <RowActionsMenu
                        size="icon-sm"
                        actions={[
                          {
                            label: "Rinomina fase",
                            icon: Pencil,
                            onClick: () => onEditPhase(phase),
                          },
                          {
                            label: "Elimina dall'anagrafica",
                            icon: Trash2,
                            destructive: true,
                            separatorBefore: true,
                            onClick: () => void onDeletePhase(phase),
                          },
                        ]}
                      />
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        )}
      </div>
      </TooltipProvider>
    </aside>
  )
}

function PhaseRow({
  phase,
  section,
  dropHighlight,
  onDropHighlight,
  onReorder,
  onEdit,
  onDelete,
  onRemoveFromSection,
  onToggleDefault,
}: {
  phase: PhaseTemplateDto
  section: CostSectionTemplateDto
  dropHighlight: string | null
  onDropHighlight: (key: string | null) => void
  onReorder: (
    movedPhaseId: number,
    target: PhaseTemplateDto,
    sectionId: number
  ) => Promise<void>
  onEdit: () => void
  onDelete: () => void
  onRemoveFromSection: () => void
  onToggleDefault: (isDefault: boolean) => void
}) {
  const phaseKey = `phase-${phase.id}`
  const link = linkOf(phase, section.id)
  const isDefaultHere = link?.isDefault ?? false
  // Le ALTRE sezioni in cui vive la stessa fase: è l'informazione che dice «questa non è una
  // copia, è la stessa fase» — senza, tre righe uguali in tre sezioni sembrano un doppione.
  const otherSections = phase.sections.filter(
    (other) => other.sectionId !== section.id
  )

  function handleDragStart(event: React.DragEvent) {
    event.dataTransfer.setData(
      DRAG_PHASE_REORDER,
      JSON.stringify({ phaseId: phase.id, sectionId: section.id })
    )
    event.dataTransfer.effectAllowed = "move"
  }

  function handleDragOver(event: React.DragEvent) {
    if (event.dataTransfer.types.includes(DRAG_PHASE_REORDER)) {
      event.preventDefault()
      onDropHighlight(phaseKey)
    }
  }

  async function handleDrop(event: React.DragEvent) {
    event.preventDefault()
    onDropHighlight(null)
    const raw = event.dataTransfer.getData(DRAG_PHASE_REORDER)
    if (!raw) {
      return
    }
    const payload = JSON.parse(raw) as { phaseId: number; sectionId: number }
    if (payload.phaseId === phase.id) {
      return
    }
    await onReorder(payload.phaseId, phase, section.id)
  }

  return (
    <div
      className={cn(
        "flex items-center gap-2 rounded border bg-background px-2 py-1",
        dropHighlight === phaseKey && "ring-2 ring-indigo-300"
      )}
      draggable
      onDragStart={handleDragStart}
      onDragOver={handleDragOver}
      onDragLeave={() => onDropHighlight(null)}
      onDrop={handleDrop}
      onDoubleClick={onEdit}
    >
      <GripVertical className="size-3.5 shrink-0 text-muted-foreground" />
      <span className="flex-1 text-sm">{phase.name}</span>
      {isDefaultHere ? (
        <Tooltip>
          <TooltipTrigger asChild>
            <Badge variant="outline" className="text-[10px]">
              Default
            </Badge>
          </TooltipTrigger>
          <TooltipContent>
            Nasce da sola su ogni commessa nuova, in questa sezione
          </TooltipContent>
        </Tooltip>
      ) : null}
      {otherSections.length > 0 ? (
        <Tooltip>
          <TooltipTrigger asChild>
            <Badge variant="secondary" className="text-[10px]">
              +{otherSections.length}
            </Badge>
          </TooltipTrigger>
          <TooltipContent className="max-w-xs whitespace-pre-line">
            {`Stessa fase, anche in:\n${otherSections
              .map((other) => `• ${other.sectionName}`)
              .join("\n")}`}
          </TooltipContent>
        </Tooltip>
      ) : null}
      <RowActionsMenu
        size="icon-sm"
        actions={[
          { label: "Rinomina fase", icon: Pencil, onClick: onEdit },
          {
            label: isDefaultHere
              ? "Non farla nascere da sola qui"
              : "Falla nascere da sola qui",
            icon: Star,
            onClick: () => onToggleDefault(!isDefaultHere),
          },
          {
            label: "Togli da questa sezione",
            icon: Unlink,
            onClick: onRemoveFromSection,
          },
          {
            label: "Elimina dall'anagrafica",
            icon: Trash2,
            destructive: true,
            separatorBefore: true,
            onClick: onDelete,
          },
        ]}
      />
    </div>
  )
}

function AddSectionPrompt({
  open,
  group,
  templates,
  departments,
  onClose,
  onSaved,
}: {
  open: boolean
  group: CostSectionGroupDto | null
  templates: CostSectionTemplateDto[]
  departments: DepartmentDto[]
  onClose: () => void
  onSaved: (groupId: number) => Promise<void>
}) {
  const [name, setName] = React.useState("")
  const [sectionType, setSectionType] = React.useState("IN_SEDE")
  const [sortOrder, setSortOrder] = React.useState("0")
  const [isDefault, setIsDefault] = React.useState(false)
  const [isDefaultQuote, setIsDefaultQuote] = React.useState(false)
  const [departmentIds, setDepartmentIds] = React.useState<number[]>([])
  const [error, setError] = React.useState<string | null>(null)

  const nextSort = React.useMemo(() => {
    if (!group) {
      return 1
    }
    const groupTemplates = templates.filter((template) => template.groupId === group.id)
    return groupTemplates.length
      ? Math.max(...groupTemplates.map((template) => template.sortOrder)) + 1
      : 1
  }, [group, templates])

  React.useEffect(() => {
    if (!open) {
      return
    }
    setName("")
    setSectionType("IN_SEDE")
    setSortOrder(String(nextSort))
    setIsDefault(false)
    setIsDefaultQuote(false)
    setDepartmentIds([])
    setError(null)
  }, [open, nextSort])

  const activeDepartments = departments
    .filter((dept) => dept.isActive)
    .sort((a, b) => a.sortOrder - b.sortOrder)

  function toggleDepartment(id: number) {
    setDepartmentIds((prev) =>
      prev.includes(id) ? prev.filter((value) => value !== id) : [...prev, id]
    )
  }

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!group || !name.trim()) {
        return
      }
      await createCostSectionTemplate({
        id: 0,
        name: name.trim(),
        sectionType,
        groupId: group.id,
        isDefault,
        isDefaultQuote,
        sortOrder: Number(sortOrder) || nextSort,
        isActive: true,
        departmentIds,
      })
    },
    onSuccess: async () => {
      if (group) {
        await onSaved(group.id)
      }
    },
    onError: (err: Error) => setError(err.message),
  })

  if (!group) {
    return null
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Nuova sezione in {group.name}</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-2">
            <Label>Nome sezione di costo</Label>
            <Input
              value={name}
              autoFocus
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>Tipo sezione di costo</Label>
              <Select value={sectionType} onValueChange={setSectionType}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="IN_SEDE">In sede</SelectItem>
                  <SelectItem value="DA_CLIENTE">Da cliente</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Ordine</Label>
              <Input
                type="number"
                value={sortOrder}
                onChange={(event) => setSortOrder(event.target.value)}
              />
            </div>
          </div>
          <div className="flex flex-wrap gap-4">
            <label className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={isDefault}
                onCheckedChange={(value) => setIsDefault(!!value)}
              />
              Default commessa
            </label>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={isDefaultQuote}
                onCheckedChange={(value) => setIsDefaultQuote(!!value)}
              />
              Default preventivo
            </label>
          </div>
          <div className="space-y-2">
            <Label>Reparti interessati</Label>
            {activeDepartments.length === 0 ? (
              <p className="text-xs text-muted-foreground">Nessun reparto attivo.</p>
            ) : (
              <div className="grid max-h-48 grid-cols-2 gap-1.5 overflow-y-auto rounded-md border p-2">
                {activeDepartments.map((dept) => (
                  <label
                    key={dept.id}
                    className="flex items-center gap-2 text-sm"
                  >
                    <Checkbox
                      checked={departmentIds.includes(dept.id)}
                      onCheckedChange={() => toggleDepartment(dept.id)}
                    />
                    <span className="font-semibold">{dept.code}</span>
                    <span className="truncate text-muted-foreground">{dept.name}</span>
                  </label>
                ))}
              </div>
            )}
            <p className="text-xs text-muted-foreground">
              Puoi comunque aggiungere o rimuovere reparti dopo, trascinandoli dal
              pannello sinistro.
            </p>
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
            Crea
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function DepartmentDragPanel({
  departments,
  onEditDepartment,
  onAddDepartment,
}: {
  departments: DepartmentDto[]
  onEditDepartment: (dept: DepartmentDto) => void
  onAddDepartment: () => void
}) {
  const activeDepartments = departments
    .filter((dept) => dept.isActive)
    .sort((a, b) => a.sortOrder - b.sortOrder)

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <p className="text-xs font-medium text-muted-foreground">
          Reparti (trascina sulla sezione)
        </p>
        <Button type="button" size="icon-sm" variant="ghost" onClick={onAddDepartment}>
          <Plus className="size-3.5" />
        </Button>
      </div>
      <div className="space-y-1">
        {activeDepartments.map((dept) => (
          <div
            key={dept.id}
            draggable
            onDragStart={(event) => {
              event.dataTransfer.setData(DRAG_DEPT, String(dept.id))
              event.dataTransfer.effectAllowed = "copy"
            }}
            onDoubleClick={() => onEditDepartment(dept)}
            className="cursor-grab rounded-md bg-zinc-800 px-2 py-1.5 text-xs text-zinc-50 active:cursor-grabbing"
          >
            <span className="font-semibold">{dept.code}</span>
            <span className="text-zinc-400"> — {dept.name}</span>
            <span className="block text-[10px] text-zinc-500">
              K:{dept.defaultMarkup.toFixed(2)} — {euro(dept.hourlyCost)}/h
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

export { DRAG_PHASE }
