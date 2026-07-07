import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import {
  ChevronRight,
  GripVertical,
  Pencil,
  Plus,
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
  createCostSectionGroup,
  createCostSectionTemplate,
  deleteCostSectionGroup,
  deleteCostSectionTemplate,
  updateSectionDepartments,
} from "@/lib/api/cost-sections"
import {
  createPhaseTemplate,
  deletePhaseTemplate,
  patchPhaseTemplateField,
  unlinkPhaseFromSection,
} from "@/lib/api/phases"
import type {
  CostSectionGroupDto,
  CostSectionTemplateDto,
  DepartmentDto,
  PhaseTemplateDto,
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

  const refreshMutation = useMutation({ mutationFn: onRefresh })

  const sortedGroups = groups.slice().sort((a, b) => a.sortOrder - b.sortOrder)

  async function handleAddDepartmentToSection(
    section: CostSectionTemplateDto,
    departmentId: number
  ) {
    if (section.departmentIds.includes(departmentId)) {
      return
    }
    await updateSectionDepartments(section.id, [
      ...section.departmentIds,
      departmentId,
    ])
    await refreshMutation.mutateAsync()
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
    await updateSectionDepartments(
      section.id,
      section.departmentIds.filter((id) => id !== departmentId)
    )
    await refreshMutation.mutateAsync()
  }

  async function handleReorderPhase(
    movedPhaseId: number,
    targetPhase: PhaseTemplateDto,
    sectionId: number
  ) {
    const movedPhase = phases.find((phase) => phase.id === movedPhaseId)
    if (!movedPhase || movedPhase.id === targetPhase.id) {
      return
    }

    const sectionPhases = phases
      .filter((phase) => phase.costSectionTemplateId === sectionId)
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder)

    const withoutMoved = sectionPhases.filter((phase) => phase.id !== movedPhase.id)
    const targetIndex = withoutMoved.findIndex((phase) => phase.id === targetPhase.id)
    withoutMoved.splice(targetIndex < 0 ? 0 : targetIndex, 0, movedPhase)

    for (let index = 0; index < withoutMoved.length; index++) {
      const newSort = index + 1
      const phase = withoutMoved[index]
      if (phase.sortOrder !== newSort) {
        await patchPhaseTemplateField(phase.id, {
          field: "sort_order",
          value: String(newSort),
        })
      }
    }

    await refreshMutation.mutateAsync()
  }

  async function handleLinkPhaseById(
    phaseId: number,
    section: CostSectionTemplateDto
  ) {
    await patchPhaseTemplateField(phaseId, {
      field: "cost_section_template_id",
      value: String(section.id),
    })
    await refreshMutation.mutateAsync()
  }

  async function handleDeleteGroup(group: CostSectionGroupDto) {
    const ok = await confirm({
      title: "Elimina gruppo",
      description: `Eliminare il gruppo "${group.name}" e le sue sezioni?`,
      confirmLabel: "Elimina",
    })
    if (!ok) {
      return
    }
    await deleteCostSectionGroup(group.id)
    await refreshMutation.mutateAsync()
  }

  async function handleDeleteSection(section: CostSectionTemplateDto) {
    const ok = await confirm({
      title: "Elimina sezione",
      description: `Eliminare la sezione "${section.name}"?`,
      confirmLabel: "Elimina",
    })
    if (!ok) {
      return
    }
    await deleteCostSectionTemplate(section.id)
    await refreshMutation.mutateAsync()
  }

  async function handleDeletePhase(phase: PhaseTemplateDto) {
    const ok = await confirm({
      title: "Elimina fase",
      description: `Eliminare la fase "${phase.name}"?`,
      confirmLabel: "Elimina",
    })
    if (!ok) {
      return
    }
    await deletePhaseTemplate(phase.id)
    await refreshMutation.mutateAsync()
  }

  async function handleUnlinkPhase(phase: PhaseTemplateDto) {
    await unlinkPhaseFromSection(phase.id)
    await refreshMutation.mutateAsync()
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-xs text-muted-foreground">
          {groups.length} gruppi — {templates.length} sezioni —{" "}
          {phases.filter((phase) => phase.costSectionTemplateId != null).length}{" "}
          fasi collegate
        </p>
        <Button size="sm" variant="outline" onClick={() => setAddGroupOpen(true)}>
          <Plus />
          Nuovo gruppo
        </Button>
      </div>

      <div className="space-y-2">
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
                      label: "Nuova sezione",
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
                        phases={phases.filter(
                          (phase) => phase.costSectionTemplateId === section.id
                        )}
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
                        onUnlinkPhase={handleUnlinkPhase}
                      />
                    ))
                  )}
                </div>
              </Collapsible>
            </div>
          )
        })}
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
            : "Nuova fase"
        }
        label="Nome fase"
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
          await createPhaseTemplate({
            name,
            category: addPhaseSection.name,
            costSectionTemplateId: addPhaseSection.id,
            sortOrder: maxSort,
            isDefault: false,
          })
          setAddPhaseSection(null)
          await refreshMutation.mutateAsync()
        }}
      />
    </div>
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
  onUnlinkPhase,
}: {
  section: CostSectionTemplateDto
  departments: DepartmentDto[]
  phases: PhaseTemplateDto[]
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
  onUnlinkPhase: (phase: PhaseTemplateDto) => void
}) {
  const sectionKey = `section-${section.id}`
  const typeColor = section.sectionType === "DA_CLIENTE" ? "#D97706" : "#059669"
  const typeLabel =
    section.sectionType === "DA_CLIENTE" ? "DA CLIENTE" : "IN SEDE"

  const sectionDepartments = section.departmentIds
    .map((id) => departments.find((dept) => dept.id === id))
    .filter((dept): dept is DepartmentDto => dept != null)

  const sortedPhases = phases
    .slice()
    .sort((a, b) => a.category.localeCompare(b.category) || a.sortOrder - b.sortOrder)

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
        <div className="ml-auto">
          <RowActionsMenu
            size="icon-sm"
            actions={[
              { label: "Modifica sezione", icon: Pencil, onClick: onEditSection },
              { label: "Nuova fase", icon: Plus, onClick: onAddPhase },
              {
                label: "Elimina sezione",
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

      {sortedPhases.length > 0 ? (
        <div className="mt-2 space-y-1 rounded-md bg-amber-50/50 p-2 dark:bg-amber-950/20">
          <p className="text-[10px] font-medium uppercase tracking-wide text-amber-700 dark:text-amber-400">
            Fasi template ({sortedPhases.length})
          </p>
          {sortedPhases.map((phase) => (
            <PhaseRow
              key={phase.id}
              phase={phase}
              section={section}
              dropHighlight={dropHighlight}
              onDropHighlight={onDropHighlight}
              onReorder={onReorderPhase}
              onEdit={() => onEditPhase(phase)}
              onDelete={() => onDeletePhase(phase)}
              onUnlink={() => onUnlinkPhase(phase)}
            />
          ))}
        </div>
      ) : null}
    </div>
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
  onUnlink,
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
  onUnlink: () => void
}) {
  const phaseKey = `phase-${phase.id}`

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
      <span className="text-[10px] text-muted-foreground">{phase.category}</span>
      <RowActionsMenu
        size="icon-sm"
        actions={[
          { label: "Modifica fase", icon: Pencil, onClick: onEdit },
          { label: "Scollega dalla sezione", icon: Unlink, onClick: onUnlink },
          {
            label: "Elimina fase",
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
            <Label>Nome sezione</Label>
            <Input
              value={name}
              autoFocus
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>Tipo sezione</Label>
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
