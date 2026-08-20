import * as React from "react"
import { Inbox, List, Pencil, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import {
  PmSidebar,
  type PmContainer,
  type PmQuickView,
} from "@/components/shared/pm-sidebar"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { PromptDialog } from "@/features/admin/config-sections/config-sections-dialogs"
import {
  PRIORITY_META,
  compareProjectCodes,
  containerPriorityDots,
  groupNameExists,
} from "@/features/checklist/checklist-utils"
import {
  ALTRE_ATTIVITA_SECTION_LABEL,
  COMMESSE_SECTION_LABEL,
  isCommessaCode,
} from "@/lib/project-code"
import {
  deleteChecklistGroup,
  updateChecklistGroup,
} from "@/lib/api/checklist"
import type {
  ChecklistBoard,
  ChecklistGroup,
  ChecklistProjectLookup,
} from "@/lib/api/types"
import { notifyError, notifySuccess } from "@/lib/toast"

export type ChecklistView =
  | { kind: "inbox" }
  | { kind: "all" }
  | { kind: "priority"; priority: number }
  | { kind: "project"; projectId: number }
  | { kind: "group"; groupId: number }

type ChecklistSidebarProps = {
  view: ChecklistView
  onViewChange: (view: ChecklistView) => void
  board: ChecklistBoard
  /** Commesse aperte: ognuna ha la sua tabella anche prima di avere attività. */
  projectsLookup: ChecklistProjectLookup[]
  inboxCount: number
  allCount: number
  priCount: (priority: number) => number
  /** Ricarica la board dopo una rinomina di gruppo. */
  onGroupRenamed: () => void
  /** Gruppo eliminato: ricarica la board e sposta la vista se era quello aperto. */
  onGroupDeleted: (groupId: number) => void
}

export function ChecklistSidebar({
  view,
  onViewChange,
  board,
  projectsLookup,
  inboxCount,
  allCount,
  priCount,
  onGroupRenamed,
  onGroupDeleted,
}: ChecklistSidebarProps) {
  const quickViews: PmQuickView[] = [
    {
      key: "inbox",
      selected: view.kind === "inbox",
      onClick: () => onViewChange({ kind: "inbox" }),
      icon: <Inbox />,
      label: "Fissa attività",
      count: inboxCount,
    },
    {
      key: "all",
      selected: view.kind === "all",
      onClick: () => onViewChange({ kind: "all" }),
      icon: <List />,
      label: "Tutte le attività",
      count: allCount,
    },
    ...PRIORITY_META.map((p) => ({
      key: `p${p.value}`,
      selected: view.kind === "priority" && view.priority === p.value,
      onClick: () => onViewChange({ kind: "priority", priority: p.value }),
      dotClass: p.dot,
      label: `Priorità ${p.value} — ${p.name}`,
      count: priCount(p.value),
    })),
  ]

  // Commesse con attività (anche chiuse) + tutte le commesse aperte ancora vuote: una
  // commessa appena creata compare subito con la sua tabella, a conteggio zero.
  const boardProjectIds = new Set(board.projects.map((p) => p.projectId))

  const projects = [
    ...board.projects.map((p) => ({
      id: p.projectId,
      code: p.code,
      label: p.display,
      count: p.items.length,
      dots: containerPriorityDots(p.items),
    })),
    ...projectsLookup
      .filter((p) => !boardProjectIds.has(p.id))
      .map((p) => ({
        id: p.id,
        code: p.code,
        label: p.display,
        count: 0,
        dots: [],
      })),
  ]

  const toProjectContainer = (p: (typeof projects)[number]): PmContainer => ({
    key: `p${p.id}`,
    selected: view.kind === "project" && view.projectId === p.id,
    onClick: () => onViewChange({ kind: "project", projectId: p.id }),
    label: p.label,
    count: p.count,
    dots: p.dots,
  })

  // #79: «Commesse» = codice C+data; «Altre Attività» = codice libero + gruppi checklist.
  const projectContainers: PmContainer[] = projects
    .filter((p) => isCommessaCode(p.code))
    .sort((a, b) => compareProjectCodes(a.code, b.code))
    .map(toProjectContainer)

  const groupContainers: PmContainer[] = [
    ...projects
      .filter((p) => !isCommessaCode(p.code))
      .map((p) => ({ label: p.label, container: toProjectContainer(p) })),
    ...board.groups.map((g) => ({
      label: g.name,
      container: {
        key: `g${g.id}`,
        selected: view.kind === "group" && view.groupId === g.id,
        onClick: () => onViewChange({ kind: "group", groupId: g.id }),
        label: g.name,
        count: g.items.length,
        dots: containerPriorityDots(g.items),
        actions: (
          <ChecklistGroupActions
            group={g}
            groups={board.groups}
            onRenamed={onGroupRenamed}
            onDeleted={() => onGroupDeleted(g.id)}
          />
        ),
      } satisfies PmContainer,
    })),
  ]
    .sort((a, b) => a.label.localeCompare(b.label, "it"))
    .map((entry) => entry.container)

  return (
    <PmSidebar
      storageKey="checklist"
      quickViews={quickViews}
      sections={[
        {
          key: "projects",
          label: COMMESSE_SECTION_LABEL,
          containers: projectContainers,
          emptyLabel: "Nessuna commessa",
        },
        {
          key: "groups",
          label: ALTRE_ATTIVITA_SECTION_LABEL,
          containers: groupContainers,
          emptyLabel: "Nessuna altra attività",
        },
      ]}
    />
  )
}

/** Menu «⋮» delle attività (gruppi generici): rinomina ed elimina. */
function ChecklistGroupActions({
  group,
  groups,
  onRenamed,
  onDeleted,
}: {
  group: ChecklistGroup
  groups: ChecklistGroup[]
  onRenamed: () => void
  onDeleted: () => void
}) {
  const confirm = useConfirm()
  const [renaming, setRenaming] = React.useState(false)
  const [name, setName] = React.useState(group.name)

  async function remove() {
    const ok = await confirm({
      title: "Eliminare l'attività?",
      description: `"${group.name}" e tutte le sue voci verranno eliminate.`,
      confirmLabel: "Elimina",
    })
    if (!ok) return
    try {
      await deleteChecklistGroup(group.id)
      onDeleted()
      notifySuccess("Attività eliminata")
    } catch (err) {
      notifyError(err as Error)
    }
  }

  return (
    <>
      <RowActionsMenu
        size="icon-sm"
        triggerClassName="size-6"
        label={group.name}
        actions={[
          {
            label: "Rinomina",
            icon: Pencil,
            onClick: () => {
              setName(group.name)
              setRenaming(true)
            },
          },
          {
            label: "Elimina",
            icon: Trash2,
            destructive: true,
            onClick: () => void remove(),
          },
        ]}
      />
      <PromptDialog
        open={renaming}
        title="Rinomina attività"
        label="Nome"
        value={name}
        onValueChange={setName}
        confirmLabel="Rinomina"
        onClose={() => setRenaming(false)}
        onConfirm={async (value) => {
          if (!value || value === group.name) {
            setRenaming(false)
            return
          }
          if (groupNameExists(groups, value, group.id)) {
            throw new Error("Esiste già un'attività con questo nome")
          }
          await updateChecklistGroup(group.id, {
            name: value,
            rowVersion: group.rowVersion,
          })
          setRenaming(false)
          onRenamed()
          notifySuccess("Attività rinominata")
        }}
      />
    </>
  )
}
