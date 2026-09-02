import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { ArrowDown, ArrowUp, Printer, ChevronDown, ChevronRight } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  fetchDdpControlReport,
} from "@/lib/api/ddp-manager"
import { fetchDdpStatuses } from "@/lib/api/ddp-config"
import type { DdpControlReportRow, DdpStatusItem } from "@/lib/api/types"
import { dateToIso, formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { useProjectHub } from "@/lib/signalr/use-project-hub"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { type ControlReportDef } from "./ddp-control-defs"
import { intestazioneCommessa } from "./ddp-view-props"
import { printDdpTables, type ExportTable } from "./ddp-export"

// ── Colonne ─────────────────────────────────────────────────────

interface ReportColumn {
  id: string
  label: string
  value: (row: DdpControlReportRow) => string
  numeric?: boolean
  /** Data di consegna: rossa quando scaduta. */
  due?: boolean
}

// withCommessa: colonna "Commessa" in tabella solo quando le righe di più commesse
// convivono nella stessa tabella (report per giorno); nelle card per commessa il
// riferimento sta già nell'header della card.
function reportColumns(officina: boolean, withCommessa: boolean): ReportColumn[] {
  const cols: ReportColumn[] = [
    ...(withCommessa
      ? [
          {
            id: "commessa",
            label: "Commessa",
            value: (row: DdpControlReportRow) =>
              intestazioneCommessa(row.projectCode, row.projectTitle, row.customerName),
          },
        ]
      : []),
    { id: "riga", label: "Riga", value: (row) => String(row.rowNumber), numeric: true },
    {
      id: "dataprev",
      label: "Data Prevista",
      value: (row) => formatDateShort(row.dateNeeded),
      due: true,
    },
    // Stesso nome delle DDP di commessa (segnalazione #61).
    { id: "rich", label: "Inserito da", value: (row) => row.requestedBy },
    {
      id: "codice",
      label: officina ? "Codice 101" : "Codice",
      value: (row) => row.partNumber,
    },
    { id: "desc", label: "Descrizione", value: (row) => row.description },
    {
      id: "qta",
      label: "Qtà",
      value: (row) => String(row.quantity ?? 0),
      numeric: true,
    },
  ]
  if (officina) {
    cols.push(
      { id: "materiale", label: "Materiale", value: (row) => row.material },
      { id: "trattamento", label: "Trattamento", value: (row) => row.treatment }
    )
  } else {
    cols.push(
      { id: "um", label: "UM", value: (row) => row.unit },
      { id: "produttore", label: "Produttore", value: (row) => row.manufacturer }
    )
  }
  cols.push(
    { id: "fornitore", label: "Fornitore", value: (row) => row.supplierName },
    { id: "stato", label: "Stato", value: (row) => row.itemStatus },
    { id: "rif", label: "Rif. Danea", value: (row) => row.daneaRef },
    {
      id: "dest",
      label: "Destinazione",
      value: (row) =>
        row.destinationSpec
          ? `${row.destination} · ${row.destinationSpec}`
          : row.destination,
    },
    { id: "note", label: "Note", value: (row) => row.notes },
    {
      id: "cu",
      label: "Costo unit.",
      value: (row) => euro(row.unitCost),
      numeric: true,
    },
    {
      id: "tot",
      label: "Totale",
      value: (row) => euro(row.unitCost == null ? null : row.quantity * row.unitCost),
      numeric: true,
    }
  )
  return cols
}

// Chiave di ordinamento della colonna: numerica dove ha senso, altrimenti il testo
// mostrato; le date ordinano in ISO con i null in coda.
function sortValue(
  row: DdpControlReportRow,
  col: ReportColumn
): string | number {
  switch (col.id) {
    case "riga":
      return row.rowNumber
    case "qta":
      return row.quantity
    case "cu":
      return row.unitCost ?? 0
    case "tot":
      return row.quantity * (row.unitCost ?? 0)
    case "dataprev":
      return row.dateNeeded ?? "9999-12-31"
    case "commessa":
      return `${row.projectCode}#${String(row.rowNumber).padStart(6, "0")}`
    default:
      return col.value(row)
  }
}

// Gruppi per giorno di consegna (report IO): righe senza data in coda.
interface DayGroup {
  day: string | null
  late: boolean
  rows: DdpControlReportRow[]
  value: number
}

function groupByDay(rows: DdpControlReportRow[], today: string): DayGroup[] {
  const groups = new Map<string, DayGroup>()
  for (const row of rows) {
    const day = row.dateNeeded ? row.dateNeeded.slice(0, 10) : null
    const key = day ?? "z-none"
    let group = groups.get(key)
    if (!group) {
      group = { day, late: day != null && day < today, rows: [], value: 0 }
      groups.set(key, group)
    }
    group.rows.push(row)
    group.value += row.quantity * (row.unitCost ?? 0)
  }
  return Array.from(groups.entries())
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([, group]) => group)
}

function dayLabel(day: string | null): string {
  if (!day) return "Data non definita"
  const weekday = new Date(`${day}T00:00:00`).toLocaleDateString("it-IT", {
    weekday: "long",
  })
  return `${formatDateShort(day)} · ${weekday}`
}

// Gruppi per commessa (tutti i report tranne IO): una card per commessa.
interface ProjectGroup {
  code: string
  title: string
  customerName: string
  rows: DdpControlReportRow[]
  value: number
  overdue: number
}

function groupByProject(rows: DdpControlReportRow[], today: string): ProjectGroup[] {
  const groups = new Map<string, ProjectGroup>()
  for (const row of rows) {
    let group = groups.get(row.projectCode)
    if (!group) {
      group = {
        code: row.projectCode,
        title: row.projectTitle ?? "",
        customerName: row.customerName,
        rows: [],
        value: 0,
        overdue: 0,
      }
      groups.set(row.projectCode, group)
    }
    group.rows.push(row)
    group.value += row.quantity * (row.unitCost ?? 0)
    if (row.dateNeeded != null && row.dateNeeded.slice(0, 10) < today) group.overdue++
  }
  return Array.from(groups.values()).sort((a, b) =>
    a.code.localeCompare(b.code, "it")
  )
}

// ── Vista report (contenuto del pannello Report di Controllo) ───

export function DdpControlReportView({
  def,
  ddpType,
}: {
  def: ControlReportDef
  ddpType: "COMMERCIAL" | "OFFICINA"
}) {
  const [sort, setSort] = React.useState<{ col: string; dir: 1 | -1 } | null>(null)
  // Righe disattivate (escluse dalla stampa): solo stato di sessione, come nel prototipo.
  const [offIds, setOffIds] = React.useState<Set<number>>(new Set())
  const [collapsedParentIds, setCollapsedParentIds] = React.useState<Set<number>>(new Set())

  // Cambio report o distinta dalla sidebar: reset di ordinamento e selezioni.
  React.useEffect(() => {
    setSort(null)
    setOffIds(new Set())
    setCollapsedParentIds(new Set())
  }, [def, ddpType])

  const today = dateToIso(new Date())
  const officina = ddpType === "OFFICINA"

  // staleTime 0: le righe arrivano dalle distinte modificate in altre pagine e l'hub
  // esclude l'autore della modifica — al rientro nel report si rilegge sempre dal server.
  const rowsQuery = useQuery({
    queryKey: ["ddp-control-report", def.key, ddpType],
    queryFn: () => fetchDdpControlReport(def.key, ddpType),
    staleTime: 0,
  })
  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })

  useProjectHub("all", () => {
    void rowsQuery.refetch()
  })

  const statusDefs = React.useMemo(() => {
    const map = new Map<string, DdpStatusItem>()
    for (const status of statusesQuery.data ?? []) map.set(status.statusKey, status)
    return map
  }, [statusesQuery.data])

  // Solo il report per giorno mescola più commesse nella stessa tabella.
  const allColumns = reportColumns(officina, def.groupedByDay === true)
  // Menu «Colonne»: chiave unica per tutti i report (le colonne sono le stesse),
  // stampa inclusa — si stampa quello che si vede.
  const [visibleCols, setVisibleCols] = usePersistedColumnVisibility(
    "ddp-control-report-columns-v1",
    Object.fromEntries(allColumns.map((col) => [col.id, true]))
  )
  const columns = allColumns.filter((col) => visibleCols[col.id] ?? true)
  const columnToggles = allColumns.map((col) => ({
    id: col.id,
    label: col.label,
    checked: visibleCols[col.id] ?? true,
    onToggle: (value: boolean) =>
      setVisibleCols((prev) => ({ ...prev, [col.id]: value })),
  }))

  const parentIdsWithChildren = React.useMemo(() => {
    const set = new Set<number>()
    const list = rowsQuery.data ?? []
    for (const item of list) {
      if (item.parentOfficinaItemId != null) {
        set.add(item.parentOfficinaItemId)
      }
    }
    return set
  }, [rowsQuery.data])

  const toggleParentCollapse = React.useCallback((parentId: number) => {
    setCollapsedParentIds((prev) => {
      const next = new Set(prev)
      if (next.has(parentId)) next.delete(parentId)
      else next.add(parentId)
      return next
    })
  }, [])

  const processedRows = React.useMemo(() => {
    const list = rowsQuery.data ?? []
    if (!officina) return list

    // Group rows by projectId
    const byProject: Record<number, DdpControlReportRow[]> = {}
    for (const row of list) {
      byProject[row.projectId] = byProject[row.projectId] ?? []
      byProject[row.projectId].push(row)
    }

    const result: DdpControlReportRow[] = []

    for (const projIdStr in byProject) {
      const projId = Number(projIdStr)
      const projRows = byProject[projId]

      const parents = projRows.filter((it) => it.parentOfficinaItemId == null)
      const children = projRows.filter((it) => it.parentOfficinaItemId != null)

      const childrenMap: Record<number, typeof children> = {}
      children.forEach((child) => {
        const pid = child.parentOfficinaItemId!
        childrenMap[pid] = childrenMap[pid] ?? []
        childrenMap[pid].push(child)
      })

      // Sort children by partNumber
      Object.keys(childrenMap).forEach((pidStr) => {
        const pid = Number(pidStr)
        childrenMap[pid].sort((a, b) =>
          (a.partNumber || "").localeCompare(b.partNumber || "", undefined, {
            numeric: true,
            sensitivity: "base",
          })
        )
      })

      const sortedProjRows: typeof projRows = []
      parents.forEach((parent) => {
        sortedProjRows.push(parent)
        const pChildren = childrenMap[parent.id]
        if (pChildren) {
          sortedProjRows.push(...pChildren)
          delete childrenMap[parent.id]
        }
      })

      // Push remaining orphans
      Object.values(childrenMap).forEach((orphans) => {
        sortedProjRows.push(...orphans)
      })

      // Group children for cost calculation
      const parentToChildrenLookup: Record<number, typeof children> = {}
      projRows.forEach((child) => {
        if (child.parentOfficinaItemId != null) {
          const pid = child.parentOfficinaItemId
          parentToChildrenLookup[pid] = parentToChildrenLookup[pid] ?? []
          parentToChildrenLookup[pid].push(child)
        }
      })

      let parentCount = 0
      const decorated = sortedProjRows.map((item) => {
        let displayIndex: string | number = "•"
        if (item.parentOfficinaItemId == null) {
          parentCount++
          displayIndex = parentCount
        }

        let unitCost = item.unitCost
        const hasChildren = parentIdsWithChildren.has(item.id)
        if (hasChildren) {
          const itemChildren = parentToChildrenLookup[item.id] ?? []
          unitCost = itemChildren.reduce(
            (sum, child) => sum + (child.unitCost ?? 0) * (child.compositionQty ?? 1),
            0
          )
        }

        return {
          ...item,
          rowNumber: displayIndex,
          unitCost,
        } as DdpControlReportRow
      })

      result.push(...decorated)
    }

    return result
  }, [rowsQuery.data, officina, parentIdsWithChildren])

  const rows = processedRows
  const sortCol = sort ? allColumns.find((col) => col.id === sort.col) : undefined
  const sorted =
    sort && sortCol
      ? [...rows].sort((a, b) => {
          const va = sortValue(a, sortCol)
          const vb = sortValue(b, sortCol)
          const cmp =
            typeof va === "number" && typeof vb === "number"
              ? va - vb
              : String(va).localeCompare(String(vb), "it")
          return cmp * sort.dir
        })
      : rows

  const visibleRows = React.useMemo(() => {
    if (!officina) return sorted
    return sorted.filter((row) => {
      if (row.parentOfficinaItemId != null) {
        return !collapsedParentIds.has(row.parentOfficinaItemId)
      }
      return true
    })
  }, [sorted, officina, collapsedParentIds])

  const activeCount = visibleRows.filter((row) => !offIds.has(row.id)).length

  function toggleRow(id: number, active: boolean) {
    setOffIds((prev) => {
      const next = new Set(prev)
      if (active) next.delete(id)
      else next.add(id)
      return next
    })
  }

  function setAll(active: boolean) {
    setOffIds(active ? new Set() : new Set(visibleRows.map((row) => row.id)))
  }

  // Attiva/disattiva in blocco le righe di un gruppo (giorno o commessa).
  function setGroupRows(groupRows: DdpControlReportRow[], active: boolean) {
    setOffIds((prev) => {
      const next = new Set(prev)
      for (const row of groupRows) {
        if (active) next.delete(row.id)
        else next.add(row.id)
      }
      return next
    })
  }

  function handleSort(colId: string) {
    setSort((prev) =>
      prev?.col === colId
        ? { col: colId, dir: prev.dir === 1 ? -1 : 1 }
        : { col: colId, dir: 1 }
    )
  }

  function exportTable(tableRows: DdpControlReportRow[], title: string): ExportTable {
    return {
      title,
      headers: columns.map((col) => col.label),
      rows: tableRows.map((row) => columns.map((col) => col.value(row))),
    }
  }

  function print() {
    const active = visibleRows.filter((row) => !offIds.has(row.id))
    const subtitle = `${def.title} — DDP ${officina ? "Officine" : "Commerciali"} · ${active.length} righe · riferimento ${formatDateShort(today)}`
    // Stampa a sezioni: una per giorno (report IO) o una per commessa (gli altri).
    const tables = def.groupedByDay
      ? groupByDay(active, today).map((group) =>
          exportTable(
            group.rows,
            `${dayLabel(group.day)}${group.late ? " · CONSEGNA SCADUTA" : ""} — ${group.rows.length} righe · ${euro(group.value)}`
          )
        )
      : groupByProject(active, today).map((group) =>
          exportTable(
            group.rows,
            `${intestazioneCommessa(group.code, group.title, group.customerName)} — ${group.rows.length} righe · ${euro(group.value)}`
          )
        )
    printDdpTables(`Report di Controllo — ${def.badge}`, subtitle, tables)
  }

  const headerRow = (
    <TableRow>
      <TableHead className="w-8" />
      {columns.map((col) => (
        <TableHead
          key={col.id}
          className={cn("cursor-pointer select-none whitespace-nowrap", col.numeric && "text-right")}
          onClick={() => handleSort(col.id)}
        >
          <span className="inline-flex items-center gap-1">
            {col.label}
            {sort?.col === col.id ? (
              sort.dir === 1 ? (
                <ArrowUp className="size-3" />
              ) : (
                <ArrowDown className="size-3" />
              )
            ) : null}
          </span>
        </TableHead>
      ))}
    </TableRow>
  )

  function bodyRows(tableRows: DdpControlReportRow[]) {
    return tableRows.map((row) => {
      const active = !offIds.has(row.id)
      const overdue = row.dateNeeded != null && row.dateNeeded.slice(0, 10) < today
      return (
        <TableRow key={row.id} className={cn(!active && "opacity-50")}>
          <TableCell>
            <Checkbox
              checked={active}
              onCheckedChange={(checked) => toggleRow(row.id, checked === true)}
              aria-label="Includi nella stampa"
            />
          </TableCell>
          {columns.map((col) => {
            if (col.id === "stato") {
              const statusDef = statusDefs.get(row.itemStatus)
              return (
                <TableCell key={col.id}>
                  <span
                    className="inline-flex rounded-full px-2 py-0.5 text-xs font-bold"
                    style={{
                      backgroundColor: statusDef?.colorBg ?? "#CCCCCC",
                      color: statusDef?.colorFg ?? "#000000",
                    }}
                  >
                    {row.itemStatus || "ND"}
                  </span>
                </TableCell>
              )
            }
            if (col.id === "riga" && officina) {
              const isChild = row.parentOfficinaItemId != null
              return (
                <TableCell
                  key={col.id}
                  className={cn(
                    isChild ? "pl-3 italic font-normal text-muted-foreground" : "opacity-80 tabular-nums font-medium",
                    !active && "line-through"
                  )}
                >
                  {row.rowNumber}
                </TableCell>
              )
            }
            if (col.id === "codice" && officina) {
              const isChild = row.parentOfficinaItemId != null
              const hasChildren = parentIdsWithChildren.has(row.id)
              const isCollapsed = collapsedParentIds.has(row.id)
              return (
                <TableCell key={col.id} className={cn(!active && "line-through")}>
                  <span className="flex items-center gap-1 font-medium">
                    {isChild ? (
                      <span
                        className="mr-1 select-none text-muted-foreground"
                        title={`Componente di composizione (${row.compositionQty ?? 1} per padre): segue la quantità del padre`}
                      >
                        ↳
                      </span>
                    ) : hasChildren ? (
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation()
                          toggleParentCollapse(row.id)
                        }}
                        className="mr-1 inline-flex size-5 items-center justify-center rounded hover:bg-muted"
                        title={isCollapsed ? "Espandi componenti" : "Collassa componenti"}
                      >
                        {isCollapsed ? (
                          <ChevronRight className="size-4" strokeWidth={2.5} />
                        ) : (
                          <ChevronDown className="size-4" strokeWidth={2.5} />
                        )}
                      </button>
                    ) : null}
                    {row.partNumber || "—"}
                  </span>
                </TableCell>
              )
            }
            return (
              <TableCell
                key={col.id}
                className={cn(
                  "whitespace-nowrap",
                  col.id === "desc" && "max-w-[280px] whitespace-normal break-words",
                  col.numeric && "text-right tabular-nums",
                  col.due && (def.dueRed || overdue) && row.dateNeeded
                    ? "font-semibold text-destructive"
                    : undefined,
                  !active && "line-through"
                )}
                title={col.id === "desc" || col.id === "note" ? col.value(row) : undefined}
              >
                {col.value(row)}
              </TableCell>
            )
          })}
        </TableRow>
      )
    })
  }

  const groups = def.groupedByDay ? groupByDay(visibleRows, today) : null
  const projectGroups = def.groupedByDay ? null : groupByProject(visibleRows, today)

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <div className="min-w-0 flex-1">
          <h2 className="flex items-center gap-2 text-base font-semibold">
            <span className="inline-flex rounded-md bg-primary/10 px-2 py-0.5 text-xs font-bold uppercase tracking-wide text-primary">
              {def.badge}
            </span>
            {def.title}
          </h2>
          <p className="text-sm text-muted-foreground">{def.description}</p>
        </div>
        <ColumnsMenu columns={columnToggles} />
        <Button variant="outline" size="sm" onClick={print} disabled={activeCount === 0}>
          <Printer className="mr-1.5 size-4" />
          Stampa PDF
        </Button>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground">
        <span>
          <strong className="text-foreground">{sorted.length}</strong> righe —{" "}
          <strong className="text-foreground">{activeCount}</strong> selezionate per la
          stampa
          {groups ? (
            <>
              {" "}
              — <strong className="text-foreground">{groups.length}</strong> giorni di
              consegna
            </>
          ) : projectGroups ? (
            <>
              {" "}
              — <strong className="text-foreground">{projectGroups.length}</strong>{" "}
              commesse
            </>
          ) : null}
        </span>
        <span className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => setAll(true)}>
            Attiva tutte
          </Button>
          <Button variant="ghost" size="sm" onClick={() => setAll(false)}>
            Disattiva tutte
          </Button>
        </span>
      </div>

      {rowsQuery.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : rowsQuery.error ? (
        <p className="text-sm text-destructive">{(rowsQuery.error as Error).message}</p>
      ) : sorted.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Nessuna riga per questo report.
        </p>
      ) : groups ? (
        <div className="space-y-4">
          {groups.map((group) => {
            const allOff = group.rows.every((row) => offIds.has(row.id))
            return (
              <Card key={group.day ?? "none"} className={cn(allOff && "opacity-70")}>
                <CardContent className="space-y-2 pt-4">
                  <div className="flex flex-wrap items-center gap-2">
                    <span
                      className={cn(
                        "font-semibold",
                        group.late && "text-destructive"
                      )}
                    >
                      {dayLabel(group.day)}
                    </span>
                    {group.late ? (
                      <span className="rounded-full bg-destructive/10 px-2 py-0.5 text-xs font-semibold text-destructive">
                        Consegna scaduta
                      </span>
                    ) : null}
                    <span className="text-sm text-muted-foreground">
                      {group.rows.length} righe · {euro(group.value)}
                    </span>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="ml-auto"
                      onClick={() => setGroupRows(group.rows, allOff)}
                    >
                      {allOff ? "Attiva giorno" : "Disattiva giorno"}
                    </Button>
                  </div>
                  <GridScroller>
                    <Table>
                      <TableHeader>{headerRow}</TableHeader>
                      <TableBody>{bodyRows(group.rows)}</TableBody>
                    </Table>
                  </GridScroller>
                </CardContent>
              </Card>
            )
          })}
        </div>
      ) : (
        // Una card per commessa, stesso formato dei gruppi del Gestore DDP:
        // header codice + cliente, righe della commessa nella tabella interna.
        <div className="space-y-4">
          {(projectGroups ?? []).map((group) => {
            const allOff = group.rows.every((row) => offIds.has(row.id))
            return (
              <Card key={group.code} className={cn(allOff && "opacity-70")}>
                <CardContent className="space-y-2 pt-4">
                  <div className="flex flex-wrap items-baseline gap-2">
                    <span className="font-semibold">{group.code}</span>
                    {group.title ? (
                      <span className="text-sm">{group.title}</span>
                    ) : null}
                    <span className="text-sm text-muted-foreground">
                      {group.customerName}
                    </span>
                    {group.overdue > 0 && def.dueRed ? (
                      <span className="rounded-full bg-destructive/10 px-2 py-0.5 text-xs font-semibold text-destructive">
                        {group.overdue} in ritardo
                      </span>
                    ) : null}
                    <span className="text-sm text-muted-foreground">
                      {group.rows.length} righe · {euro(group.value)}
                    </span>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="ml-auto"
                      onClick={() => setGroupRows(group.rows, allOff)}
                    >
                      {allOff ? "Attiva commessa" : "Disattiva commessa"}
                    </Button>
                  </div>
                  <GridScroller>
                    <Table>
                      <TableHeader>{headerRow}</TableHeader>
                      <TableBody>{bodyRows(group.rows)}</TableBody>
                    </Table>
                  </GridScroller>
                </CardContent>
              </Card>
            )
          })}
        </div>
      )}
    </div>
  )
}
