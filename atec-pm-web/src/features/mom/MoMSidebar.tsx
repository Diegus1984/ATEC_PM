import * as React from "react"
import { Flame, List, Star, Users } from "lucide-react"

import { PmSidebar, type PmContainer, type PmQuickView } from "@/components/shared/pm-sidebar"
import type { MoMListItem } from "@/lib/api/types"

export type MoMView =
  | { kind: "all" }
  | { kind: "open" }
  | { kind: "p1" }
  | { kind: "dashboard" }
  | { kind: "project"; projectCode: string }
  | { kind: "riunioni" }

interface MoMSidebarProps {
  view: MoMView
  onViewChange: (view: MoMView) => void
  items: MoMListItem[]
}

type CommessaData = {
  key: string
  kind: "project" | "riunioni"
  code: string
  label: string
  count: number
  dots: { dotClass: string; label: string }[]
}

export function MoMSidebar({ view, onViewChange, items }: MoMSidebarProps) {
  // Conteggi per le viste rapide
  const totalOpenActions = items.reduce((sum, item) => sum + item.openCount, 0)
  const totalP1Actions = items.reduce((sum, item) => sum + item.p1Count, 0)
  const dashboardCount = items.filter((item) => item.inDashboard).length

  const quickViews: PmQuickView[] = [
    {
      key: "all",
      selected: view.kind === "all",
      onClick: () => onViewChange({ kind: "all" }),
      icon: <List />,
      label: "Tutti i verbali",
      count: items.length,
    },
    {
      key: "open",
      selected: view.kind === "open",
      onClick: () => onViewChange({ kind: "open" }),
      icon: <Flame className="text-amber-600 dark:text-amber-500" />,
      label: "Azioni aperte",
      count: totalOpenActions,
    },
    {
      key: "p1",
      selected: view.kind === "p1",
      onClick: () => onViewChange({ kind: "p1" }),
      icon: <Flame className="text-red-600 dark:text-red-500 font-bold" />,
      label: "Priorità P1",
      count: totalP1Actions,
    },
    {
      key: "dashboard",
      selected: view.kind === "dashboard",
      onClick: () => onViewChange({ kind: "dashboard" }),
      icon: <Star className="text-yellow-500 dark:text-yellow-400" />,
      label: "In dashboard",
      count: dashboardCount,
    },
  ]

  // Raggruppamento per commessa (COMMESSA) e riunioni (RIUNIONE)
  const commesseData = React.useMemo<CommessaData[]>(() => {
    const map = new Map<string, { code: string; title: string; count: number; p1: number; p2: number; p3: number }>()
    let riunioniCount = 0
    let riunioniP1 = 0
    let riunioniP2 = 0
    let riunioniP3 = 0

    for (const item of items) {
      if (item.tipo === "COMMESSA") {
        const code = item.projectCode || "Senza commessa"
        const title = item.projectTitle || ""
        const existing = map.get(code)
        if (existing) {
          existing.count += 1
          existing.p1 += item.p1Count
          existing.p2 += item.p2Count
          existing.p3 += item.p3Count
        } else {
          map.set(code, {
            code,
            title,
            count: 1,
            p1: item.p1Count,
            p2: item.p2Count,
            p3: item.p3Count,
          })
        }
      } else {
        riunioniCount += 1
        riunioniP1 += item.p1Count
        riunioniP2 += item.p2Count
        riunioniP3 += item.p3Count
      }
    }

    const projectsList: CommessaData[] = Array.from(map.values())
      .sort((a, b) => a.code.localeCompare(b.code, "it"))
      .map((p) => {
        const display = p.title ? `${p.code} — ${p.title}` : p.code
        const dots: { dotClass: string; label: string }[] = []
        if (p.p1 > 0) dots.push({ dotClass: "bg-red-500", label: `${p.p1} Alta` })
        if (p.p2 > 0) dots.push({ dotClass: "bg-amber-500", label: `${p.p2} Media` })
        if (p.p3 > 0) dots.push({ dotClass: "bg-teal-500", label: `${p.p3} Bassa` })

        return {
          key: `project-${p.code}`,
          kind: "project" as const,
          code: p.code,
          label: display,
          count: p.count,
          dots,
        }
      })

    const riunioniDots: { dotClass: string; label: string }[] = []
    if (riunioniP1 > 0) riunioniDots.push({ dotClass: "bg-red-500", label: `${riunioniP1} Alta` })
    if (riunioniP2 > 0) riunioniDots.push({ dotClass: "bg-amber-500", label: `${riunioniP2} Media` })
    if (riunioniP3 > 0) riunioniDots.push({ dotClass: "bg-teal-500", label: `${riunioniP3} Bassa` })

    const list: CommessaData[] = [...projectsList]

    if (riunioniCount > 0) {
      list.push({
        key: "riunioni",
        kind: "riunioni",
        code: "RIUNIONE",
        label: "Riunioni Generiche",
        count: riunioniCount,
        dots: riunioniDots,
      })
    }

    return list
  }, [items])

  const isContainerSelected = (c: CommessaData) => {
    if (c.kind === "riunioni" && view.kind === "riunioni") return true
    if (c.kind === "project" && view.kind === "project" && view.projectCode === c.code) return true
    return false
  }

  const selectContainer = (c: CommessaData) => {
    if (c.kind === "riunioni") {
      onViewChange({ kind: "riunioni" })
    } else {
      onViewChange({ kind: "project", projectCode: c.code })
    }
  }

  const containers: PmContainer[] = commesseData.map((c) => ({
    key: c.key,
    selected: isContainerSelected(c),
    onClick: () => selectContainer(c),
    label: c.label,
    count: c.count,
    dots: c.dots,
    railIcon:
      c.kind === "riunioni" ? (
        <Users className="size-4 text-muted-foreground" />
      ) : undefined,
  }))

  return (
    <PmSidebar
      storageKey="mom"
      quickViews={quickViews}
      containers={containers}
      containersLabel="Commesse / Riunioni"
      emptyLabel="Nessun verbale"
    />
  )
}
