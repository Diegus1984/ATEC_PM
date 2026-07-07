import { Inbox, List } from "lucide-react"

import { PmSidebar, type PmContainer, type PmQuickView } from "@/components/shared/pm-sidebar"
import { PRIORITY_META, containerPriorityDots } from "@/features/checklist/checklist-utils"
import type { ChecklistBoard } from "@/lib/api/types"

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
  inboxCount: number
  allCount: number
  priCount: (priority: number) => number
}

export function ChecklistSidebar({
  view,
  onViewChange,
  board,
  inboxCount,
  allCount,
  priCount,
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

  const isContainerSelected = (kind: "project" | "group", id: number) =>
    (view.kind === "project" && view.projectId === id && kind === "project") ||
    (view.kind === "group" && view.groupId === id && kind === "group")

  const selectContainer = (kind: "project" | "group", id: number) =>
    onViewChange(kind === "project" ? { kind: "project", projectId: id } : { kind: "group", groupId: id })

  const containers: PmContainer[] = [
    ...board.projects.map((p) => ({
      key: `p${p.projectId}`,
      kind: "project" as const,
      id: p.projectId,
      label: p.display,
      count: p.items.length,
      dots: containerPriorityDots(p.items),
    })),
    ...board.groups.map((g) => ({
      key: `g${g.id}`,
      kind: "group" as const,
      id: g.id,
      label: g.name,
      count: g.items.length,
      dots: containerPriorityDots(g.items),
    })),
  ]
    .sort((a, b) => a.label.localeCompare(b.label, "it"))
    .map((c) => ({
      key: c.key,
      selected: isContainerSelected(c.kind, c.id),
      onClick: () => selectContainer(c.kind, c.id),
      label: c.label,
      count: c.count,
      dots: c.dots,
    }))

  return (
    <PmSidebar
      storageKey="checklist"
      quickViews={quickViews}
      containers={containers}
      containersLabel="Commesse / Attività"
      emptyLabel="Nessuna tabella"
    />
  )
}
