import * as React from "react"
import {
  ChevronsDownUp,
  ChevronsUpDown,
  Copy,
  FolderTree,
  Link2,
  Pencil,
  Plus,
  RefreshCw,
  Trash2,
} from "lucide-react"

import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuRadioGroup,
  ContextMenuRadioItem,
  ContextMenuSeparator,
  ContextMenuSub,
  ContextMenuSubContent,
  ContextMenuSubTrigger,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
import { useCopyText } from "@/components/shared/copy-text"
import type { ProjectListItem } from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"

import type { CommessaSection } from "./commessa-sections"
import {
  allowedProjectStatuses,
  PROJECT_STATUS_META,
} from "./project-status"

function commessaPath(projectId: number, section?: string): string {
  return section
    ? `/commesse/${projectId}/${section}`
    : `/commesse/${projectId}`
}

function commessaHref(projectId: number, section?: string): string {
  return `${window.location.origin}${commessaPath(projectId, section)}`
}

/** Menu tasto destro sul nodo commessa (codice + cliente). */
export function CommessaProjectContextMenu({
  project,
  isExpanded,
  sections,
  canEdit,
  canDelete,
  onOpenDashboard,
  onOpenSection,
  onToggleExpand,
  onEdit,
  onChangeStatus,
  onDelete,
  children,
}: {
  project: ProjectListItem
  isExpanded: boolean
  sections: CommessaSection[]
  canEdit: boolean
  canDelete: boolean
  onOpenDashboard: () => void
  onOpenSection: (sectionKey: string) => void
  onToggleExpand: () => void
  onEdit: () => void
  onChangeStatus: (status: string) => void
  onDelete: () => void
  children: React.ReactNode
}) {
  const copiaTesto = useCopyText()

  // #89: chi ha la chiave «Opera su commesse sospese o chiuse» può anche riaprire una
  // Completata da qui (stessa regola del dialog e della colonna Stato in Dashboard).
  const statusChoices = allowedProjectStatuses(
    project.status,
    canWriteFeature("action.project_locked_write")
  ).filter((value) => value !== project.status)
  const showStatus = canEdit && statusChoices.length > 0

  return (
    <ContextMenu>
      <ContextMenuTrigger asChild>{children}</ContextMenuTrigger>
      <ContextMenuContent className="min-w-52">
        <ContextMenuItem onSelect={onOpenDashboard}>
          Apri Dashboard
        </ContextMenuItem>
        <ContextMenuSub>
          <ContextMenuSubTrigger>Apri sezione</ContextMenuSubTrigger>
          <ContextMenuSubContent className="min-w-52">
            {sections.map((section) => (
              <ContextMenuItem
                key={section.key}
                onSelect={() => onOpenSection(section.key)}
              >
                {section.icon ? (
                  <span className="w-4 text-center text-sm leading-none">
                    {section.icon}
                  </span>
                ) : null}
                {section.label}
              </ContextMenuItem>
            ))}
          </ContextMenuSubContent>
        </ContextMenuSub>
        <ContextMenuItem onSelect={onToggleExpand}>
          {isExpanded ? (
            <>
              <ChevronsDownUp />
              Comprimi
            </>
          ) : (
            <>
              <ChevronsUpDown />
              Espandi
            </>
          )}
        </ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem
          onSelect={() => void copiaTesto(project.code, "Codice commessa")}
        >
          <Copy />
          Copia codice
        </ContextMenuItem>
        <ContextMenuItem
          onSelect={() =>
            void copiaTesto(commessaHref(project.id), "Link alla commessa")
          }
        >
          <Link2 />
          Copia link
        </ContextMenuItem>
        {canEdit || canDelete || showStatus ? <ContextMenuSeparator /> : null}
        {canEdit ? (
          <ContextMenuItem onSelect={onEdit}>
            <Pencil />
            Modifica…
          </ContextMenuItem>
        ) : null}
        {showStatus ? (
          <ContextMenuSub>
            <ContextMenuSubTrigger>Cambia stato</ContextMenuSubTrigger>
            <ContextMenuSubContent className="min-w-44">
              <ContextMenuRadioGroup value={project.status}>
                {PROJECT_STATUS_META.filter(
                  (item) =>
                    item.value === project.status ||
                    statusChoices.includes(item.value)
                ).map((item) => {
                  const Icon = item.icon
                  return (
                    <ContextMenuRadioItem
                      key={item.value}
                      value={item.value}
                      disabled={item.value === project.status}
                      onSelect={() => {
                        if (item.value !== project.status) {
                          onChangeStatus(item.value)
                        }
                      }}
                    >
                      <Icon className={item.className} />
                      {item.label}
                    </ContextMenuRadioItem>
                  )
                })}
              </ContextMenuRadioGroup>
            </ContextMenuSubContent>
          </ContextMenuSub>
        ) : null}
        {canDelete ? (
          <ContextMenuItem variant="destructive" onSelect={onDelete}>
            <Trash2 />
            Elimina commessa
          </ContextMenuItem>
        ) : null}
      </ContextMenuContent>
    </ContextMenu>
  )
}

/** Menu tasto destro su una sotto-sezione (Dashboard, DDP, …). */
export function CommessaSectionContextMenu({
  projectId,
  section,
  onOpen,
  children,
}: {
  projectId: number
  section: CommessaSection
  onOpen: () => void
  children: React.ReactNode
}) {
  const copiaTesto = useCopyText()

  return (
    <ContextMenu>
      <ContextMenuTrigger asChild>{children}</ContextMenuTrigger>
      <ContextMenuContent className="min-w-44">
        <ContextMenuItem onSelect={onOpen}>Apri {section.label}</ContextMenuItem>
        <ContextMenuItem
          onSelect={() =>
            void copiaTesto(commessaHref(projectId, section.key), "Link alla sezione")
          }
        >
          <Link2 />
          Copia link
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  )
}

/** Menu tasto destro sullo spazio vuoto dell'albero. */
export function CommessaTreeBackgroundMenu({
  onNewProject,
  onRefresh,
  onExpandAll,
  onCollapseAll,
  children,
}: {
  onNewProject: () => void
  onRefresh: () => void
  onExpandAll: () => void
  onCollapseAll: () => void
  children: React.ReactNode
}) {
  return (
    <ContextMenu>
      <ContextMenuTrigger asChild>{children}</ContextMenuTrigger>
      <ContextMenuContent className="min-w-48">
        <ContextMenuItem onSelect={onNewProject}>
          <Plus />
          Nuova commessa
        </ContextMenuItem>
        <ContextMenuItem onSelect={onRefresh}>
          <RefreshCw />
          Aggiorna elenco
        </ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem onSelect={onExpandAll}>
          <FolderTree />
          Espandi tutte
        </ContextMenuItem>
        <ContextMenuItem onSelect={onCollapseAll}>
          <ChevronsDownUp />
          Comprimi tutte
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  )
}
