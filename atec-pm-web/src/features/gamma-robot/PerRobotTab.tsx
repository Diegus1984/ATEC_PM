import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { ChevronDown, ChevronRight, RefreshCw } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  fetchGammaDistinta,
  fetchGammaQuadri,
  fetchGammaRobots,
} from "@/lib/api/gamma-robot"
import type { GammaQuadroDto, GammaRobotDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import {
  buildQuadroLabel,
  buildQuadroSubtitle,
  filterRobots,
  formatEuro,
  groupDistintaSlots,
} from "./helpers"

export function PerRobotTab({
  onOpenProduct,
}: {
  onOpenProduct: (productId: number) => void
}) {
  const [search, setSearch] = React.useState("")
  const [expanded, setExpanded] = React.useState<Set<number>>(new Set())
  const [quadriByRobot, setQuadriByRobot] = React.useState<
    Record<number, GammaQuadroDto[]>
  >({})
  const [loadingQuadri, setLoadingQuadri] = React.useState<Set<number>>(new Set())
  const [selectedQuadro, setSelectedQuadro] = React.useState<GammaQuadroDto | null>(
    null
  )
  const [selectedRobot, setSelectedRobot] = React.useState<GammaRobotDto | null>(
    null
  )

  const robotsQuery = useQuery({
    queryKey: ["gamma-robot", "robots"],
    queryFn: fetchGammaRobots,
  })

  const distintaQuery = useQuery({
    queryKey: ["gamma-robot", "distinta", selectedQuadro?.id],
    queryFn: () => fetchGammaDistinta(selectedQuadro!.id),
    enabled: selectedQuadro != null,
  })

  const robots = filterRobots(robotsQuery.data ?? [], search)

  async function toggleRobot(robot: GammaRobotDto) {
    const next = new Set(expanded)
    if (next.has(robot.id)) {
      next.delete(robot.id)
      setExpanded(next)
      return
    }
    next.add(robot.id)
    setExpanded(next)
    if (quadriByRobot[robot.id]) return

    setLoadingQuadri((prev) => new Set(prev).add(robot.id))
    try {
      const quadri = await fetchGammaQuadri(robot.id)
      setQuadriByRobot((prev) => ({ ...prev, [robot.id]: quadri }))
    } finally {
      setLoadingQuadri((prev) => {
        const s = new Set(prev)
        s.delete(robot.id)
        return s
      })
    }
  }

  const slots = groupDistintaSlots(distintaQuery.data ?? [])
  const bySezione = new Map<string, typeof slots>()
  for (const row of slots) {
    const key = row.sezione ?? "(senza sezione)"
    const list = bySezione.get(key)
    if (list) list.push(row)
    else bySezione.set(key, [row])
  }

  const totaleBase = slots
    .filter((r) => !r.isOptional && r.prezzoVb != null)
    .reduce((s, r) => s + (r.prezzoVb ?? 0), 0)
  const totaleOpz = slots
    .filter((r) => r.isOptional && r.prezzoVb != null)
    .reduce((s, r) => s + (r.prezzoVb ?? 0), 0)
  const principali = slots.filter((r) => !r.isOptional).length
  const opzioni = slots.filter((r) => r.isOptional).length
  const alternative = slots.reduce((s, r) => s + r.alternatives.length, 0)
  const senzaPrezzo = slots.filter(
    (r) => r.prezzoVb == null || r.prezzoVb <= 0
  ).length

  return (
    <div className="grid min-h-[520px] grid-cols-1 gap-4 lg:grid-cols-[280px_1fr]">
      <div className="flex flex-col gap-2 rounded-lg border">
        <div className="flex items-center gap-2 border-b p-2">
          <Input
            placeholder="Cerca modello / serie…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="h-8"
          />
          <Button
            type="button"
            variant="outline"
            size="icon"
            className="size-8 shrink-0"
            onClick={() => void robotsQuery.refetch()}
            title="Aggiorna"
          >
            <RefreshCw className="size-3.5" />
          </Button>
        </div>
        <div className="min-h-0 flex-1 overflow-auto px-1 pb-2">
          {robotsQuery.isLoading ? (
            <p className="px-2 py-3 text-sm text-muted-foreground">Caricamento…</p>
          ) : robots.length === 0 ? (
            <p className="px-2 py-3 text-sm text-muted-foreground">Nessun robot</p>
          ) : (
            <ul className="space-y-0.5">
              {robots.map((robot) => {
                const isOpen = expanded.has(robot.id)
                const quadri = quadriByRobot[robot.id]
                const loading = loadingQuadri.has(robot.id)
                return (
                  <li key={robot.id}>
                    <button
                      type="button"
                      className="flex w-full items-center gap-1 rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted"
                      onClick={() => void toggleRobot(robot)}
                    >
                      {isOpen ? (
                        <ChevronDown className="size-3.5 shrink-0 text-muted-foreground" />
                      ) : (
                        <ChevronRight className="size-3.5 shrink-0 text-muted-foreground" />
                      )}
                      <span className="truncate font-medium">{robot.modello}</span>
                      <span className="ml-auto text-xs text-muted-foreground">
                        {robot.quadriCount}
                      </span>
                    </button>
                    {isOpen ? (
                      <ul className="mb-1 ml-5 space-y-0.5 border-l pl-2">
                        {loading ? (
                          <li className="px-2 py-1 text-xs text-muted-foreground">
                            …
                          </li>
                        ) : !quadri || quadri.length === 0 ? (
                          <li className="px-2 py-1 text-xs text-muted-foreground">
                            (nessun quadro)
                          </li>
                        ) : (
                          quadri.map((q) => (
                            <li key={q.id}>
                              <button
                                type="button"
                                className={cn(
                                  "w-full rounded-md px-2 py-1 text-left text-xs hover:bg-muted",
                                  selectedQuadro?.id === q.id &&
                                    "bg-muted font-medium"
                                )}
                                onClick={() => {
                                  setSelectedQuadro(q)
                                  setSelectedRobot(robot)
                                }}
                              >
                                {buildQuadroLabel(q)}
                              </button>
                            </li>
                          ))
                        )}
                      </ul>
                    ) : null}
                  </li>
                )
              })}
            </ul>
          )}
        </div>
        <div className="border-t px-3 py-1.5 text-xs text-muted-foreground">
          {robotsQuery.data?.length ?? 0} robot
        </div>
      </div>

      <div className="flex min-h-0 flex-col rounded-lg border">
        <div className="border-b px-4 py-3">
          <h3 className="text-base font-semibold">
            {selectedRobot?.modello ?? "Seleziona un quadro"}
          </h3>
          <p className="text-sm text-muted-foreground">
            {selectedQuadro
              ? buildQuadroSubtitle(selectedQuadro)
              : "Scegli un quadro a sinistra per vedere la distinta."}
          </p>
        </div>

        <div className="min-h-0 flex-1 overflow-auto">
          {!selectedQuadro ? (
            <p className="p-6 text-sm text-muted-foreground">
              Nessun quadro selezionato.
            </p>
          ) : distintaQuery.isLoading ? (
            <p className="p-6 text-sm text-muted-foreground">Caricamento distinta…</p>
          ) : slots.length === 0 ? (
            <p className="p-6 text-sm text-muted-foreground">Distinta vuota.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[120px]">Codice</TableHead>
                  <TableHead>Nome</TableHead>
                  <TableHead className="w-[80px] text-right">Prezzo VB</TableHead>
                  <TableHead className="w-[100px]">Flags</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {[...bySezione.entries()].map(([sezione, rows]) => (
                  <React.Fragment key={sezione}>
                    <TableRow className="bg-muted/50 hover:bg-muted/50">
                      <TableCell
                        colSpan={4}
                        className="py-1.5 text-xs font-semibold text-foreground"
                      >
                        {sezione}
                      </TableCell>
                    </TableRow>
                    {rows.map((row) => (
                      <React.Fragment key={row.principal.id}>
                        <TableRow
                          className="cursor-pointer"
                          onDoubleClick={() => {
                            if (row.productId > 0) onOpenProduct(row.productId)
                          }}
                        >
                          <TableCell className="font-mono text-xs font-semibold">
                            {row.productCode ?? "—"}
                          </TableCell>
                          <TableCell className="text-sm">
                            {row.productName ?? "—"}
                            {row.alternatives.length > 0 ? (
                              <span className="ml-2 text-xs text-muted-foreground">
                                ▸ {row.alternatives.length} alt.
                              </span>
                            ) : null}
                          </TableCell>
                          <TableCell className="text-right text-sm tabular-nums">
                            {row.prezzoVb != null
                              ? `${formatEuro(row.prezzoVb)} €`
                              : "—"}
                          </TableCell>
                          <TableCell>
                            {row.isOptional ? (
                              <Badge variant="secondary">OPT</Badge>
                            ) : null}
                          </TableCell>
                        </TableRow>
                        {row.alternatives.map((alt) => (
                          <TableRow
                            key={alt.id}
                            className="cursor-pointer bg-muted/20"
                            onDoubleClick={() => {
                              if (alt.productId) onOpenProduct(alt.productId)
                            }}
                          >
                            <TableCell className="pl-8 font-mono text-xs">
                              {alt.productCode ?? "—"}
                            </TableCell>
                            <TableCell className="text-sm text-muted-foreground">
                              {alt.productName ?? "—"}
                            </TableCell>
                            <TableCell className="text-right text-sm tabular-nums">
                              {alt.prezzoVb != null
                                ? `${formatEuro(alt.prezzoVb)} €`
                                : "—"}
                            </TableCell>
                            <TableCell>
                              <Badge variant="outline">ALT</Badge>
                              {alt.isOptional ? (
                                <Badge variant="secondary" className="ml-1">
                                  OPT
                                </Badge>
                              ) : null}
                            </TableCell>
                          </TableRow>
                        ))}
                      </React.Fragment>
                    ))}
                  </React.Fragment>
                ))}
              </TableBody>
            </Table>
          )}
        </div>

        {selectedQuadro ? (
          <div className="flex flex-wrap items-center justify-between gap-2 border-t px-4 py-2 text-xs text-muted-foreground">
            <span>
              {principali} componenti
              {alternative > 0 ? `  ·  ${alternative} alternative` : ""}
              {opzioni > 0 ? `  ·  ${opzioni} opzioni` : ""}
              {senzaPrezzo > 0 ? `  ·  ${senzaPrezzo} senza prezzo` : ""}
              {"  ·  doppio click = scheda prodotto"}
            </span>
            <span className="font-medium text-foreground">
              {opzioni > 0
                ? `Totale VB base: ${formatEuro(totaleBase)} €    ·    +opzioni: ${formatEuro(totaleBase + totaleOpz)} €`
                : `Totale VB: ${formatEuro(totaleBase)} €`}
            </span>
          </div>
        ) : null}
      </div>
    </div>
  )
}
