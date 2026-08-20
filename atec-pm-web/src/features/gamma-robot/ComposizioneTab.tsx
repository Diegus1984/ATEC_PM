import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  ChevronDown,
  ChevronRight,
  MoreHorizontal,
  Pencil,
  Plus,
  Trash2,
  X,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { QuantityDialog } from "@/features/codex-composizione/QuantityDialog"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
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
  addGammaDistinta,
  createGammaQuadro,
  createGammaRobot,
  deleteGammaDistinta,
  deleteGammaQuadro,
  deleteGammaRobot,
  fetchGammaComponents,
  fetchGammaDistinta,
  fetchGammaQuadri,
  fetchGammaRobots,
  updateGammaDistinta,
  updateGammaQuadro,
  updateGammaRobot,
} from "@/lib/api/gamma-robot"
import type {
  GammaComponentDto,
  GammaDistintaItemDto,
  GammaQuadroDto,
  GammaQuadroSaveRequest,
  GammaRobotDto,
  GammaRobotSaveRequest,
} from "@/lib/api/types"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { GAMMA_DRAG_PREFIX, GAMMA_SEZIONI } from "./constants"
import {
  buildQuadroLabel,
  buildQuadroSubtitle,
  filterRobots,
  formatEuro,
  sezioneForCategoria,
  wildMatch,
} from "./helpers"
import { QuadroDialog } from "./QuadroDialog"
import { RobotDialog } from "./RobotDialog"

export function ComposizioneTab({
  isAdmin,
  onOpenProduct,
}: {
  isAdmin: boolean
  onOpenProduct: (productId: number) => void
}) {
  const qc = useQueryClient()
  const confirm = useConfirm()

  const [robotSearch, setRobotSearch] = React.useState("")
  const [compSearch, setCompSearch] = React.useState("")
  const [expanded, setExpanded] = React.useState<Set<number>>(new Set())
  const [quadriByRobot, setQuadriByRobot] = React.useState<
    Record<number, GammaQuadroDto[]>
  >({})
  const [selectedRobot, setSelectedRobot] = React.useState<GammaRobotDto | null>(
    null
  )
  const [selectedQuadro, setSelectedQuadro] = React.useState<GammaQuadroDto | null>(
    null
  )
  const [dropTarget, setDropTarget] = React.useState<string | null>(null)

  const [robotDialog, setRobotDialog] = React.useState<{
    open: boolean
    robot: GammaRobotDto | null
  }>({ open: false, robot: null })
  const [quadroDialog, setQuadroDialog] = React.useState<{
    open: boolean
    quadro: GammaQuadroDto | null
  }>({ open: false, quadro: null })
  const [qtyDialog, setQtyDialog] = React.useState<{
    open: boolean
    mode: "add" | "edit"
    code: string
    initial: number
    pending: null | {
      kind: "add"
      comp: GammaComponentDto
      sezione: string
      isAlternate: boolean
      slot: string | null
    } | {
      kind: "edit"
      row: GammaDistintaItemDto
    }
  }>({ open: false, mode: "add", code: "", initial: 1, pending: null })

  const robotsQuery = useQuery({
    queryKey: ["gamma-robot", "robots"],
    queryFn: fetchGammaRobots,
  })

  const componentsQuery = useQuery({
    queryKey: ["gamma-robot", "components"],
    queryFn: fetchGammaComponents,
  })

  const distintaQuery = useQuery({
    queryKey: ["gamma-robot", "distinta", selectedQuadro?.id],
    queryFn: () => fetchGammaDistinta(selectedQuadro!.id),
    enabled: selectedQuadro != null,
  })

  const robots = filterRobots(robotsQuery.data ?? [], robotSearch)
  const components = (componentsQuery.data ?? []).filter((c) => {
    const f = compSearch.trim()
    if (!f) return true
    return (
      wildMatch(c.code, f) ||
      wildMatch(c.name, f) ||
      wildMatch(c.categoria, f)
    )
  })

  const distinta = distintaQuery.data ?? []

  async function ensureQuadri(robotId: number) {
    if (quadriByRobot[robotId]) return
    const quadri = await fetchGammaQuadri(robotId)
    setQuadriByRobot((prev) => ({ ...prev, [robotId]: quadri }))
  }

  async function toggleRobot(robot: GammaRobotDto) {
    const next = new Set(expanded)
    if (next.has(robot.id)) {
      next.delete(robot.id)
      setExpanded(next)
      return
    }
    next.add(robot.id)
    setExpanded(next)
    await ensureQuadri(robot.id)
  }

  async function selectRobot(robot: GammaRobotDto) {
    setSelectedRobot(robot)
    setSelectedQuadro(null)
    const next = new Set(expanded)
    next.add(robot.id)
    setExpanded(next)
    await ensureQuadri(robot.id)
  }

  async function refreshTree() {
    await robotsQuery.refetch()
    setQuadriByRobot({})
    const openIds = [...expanded]
    for (const id of openIds) {
      const quadri = await fetchGammaQuadri(id)
      setQuadriByRobot((prev) => ({ ...prev, [id]: quadri }))
    }
  }

  function invalidateDistinta() {
    void qc.invalidateQueries({
      queryKey: ["gamma-robot", "distinta", selectedQuadro?.id],
    })
  }

  const saveRobotMut = useMutation({
    mutationFn: async (req: GammaRobotSaveRequest) => {
      if (robotDialog.robot) {
        return updateGammaRobot(robotDialog.robot.id, req)
      }
      return createGammaRobot(req)
    },
    onSuccess: async () => {
      setRobotDialog({ open: false, robot: null })
      await refreshTree()
    },
    onError: (err: Error) => notifyError(err.message),
  })

  const saveQuadroMut = useMutation({
    mutationFn: async (req: GammaQuadroSaveRequest) => {
      if (quadroDialog.quadro) {
        return updateGammaQuadro(quadroDialog.quadro.id, req)
      }
      if (!selectedRobot) throw new Error("Seleziona prima un robot")
      return createGammaQuadro(selectedRobot.id, req)
    },
    onSuccess: async () => {
      const editingId = quadroDialog.quadro?.id
      setQuadroDialog({ open: false, quadro: null })
      await refreshTree()
      if (editingId && selectedQuadro?.id === editingId) {
        const list = await fetchGammaQuadri(selectedQuadro.robotId)
        const updated = list.find((q) => q.id === editingId)
        if (updated) setSelectedQuadro(updated)
        invalidateDistinta()
      }
    },
    onError: (err: Error) => notifyError(err.message),
  })

  function beginAdd(
    comp: GammaComponentDto,
    sezione: string,
    isAlternate: boolean,
    slot: string | null
  ) {
    if (!isAdmin || !selectedQuadro) return
    setQtyDialog({
      open: true,
      mode: "add",
      code: comp.code,
      initial: 1,
      pending: { kind: "add", comp, sezione, isAlternate, slot },
    })
  }

  async function onQtyConfirm(qty: number) {
    const pending = qtyDialog.pending
    setQtyDialog((s) => ({ ...s, open: false, pending: null }))
    if (!pending) return
    try {
      if (pending.kind === "add") {
        if (!selectedQuadro) return
        await addGammaDistinta({
          quadroId: selectedQuadro.id,
          productId: pending.comp.productId,
          sezione: pending.sezione,
          slot: pending.slot,
          qty,
          isAlternate: pending.isAlternate,
          isOptional: false,
        })
        invalidateDistinta()
      } else {
        await updateGammaDistinta(pending.row.id, { qty })
        invalidateDistinta()
      }
    } catch (err) {
      notifyError(err instanceof Error ? err.message : "Errore")
    }
  }

  function onDragStart(e: React.DragEvent, comp: GammaComponentDto) {
    if (!isAdmin) {
      e.preventDefault()
      return
    }
    e.dataTransfer.setData(
      "text/plain",
      `${GAMMA_DRAG_PREFIX}${JSON.stringify(comp)}`
    )
    e.dataTransfer.effectAllowed = "copy"
  }

  function parseDrag(e: React.DragEvent): GammaComponentDto | null {
    const raw = e.dataTransfer.getData("text/plain")
    if (!raw.startsWith(GAMMA_DRAG_PREFIX)) return null
    try {
      return JSON.parse(raw.slice(GAMMA_DRAG_PREFIX.length)) as GammaComponentDto
    } catch {
      return null
    }
  }

  function allowDrop(e: React.DragEvent, key: string) {
    if (!isAdmin || !selectedQuadro) return
    if (![...e.dataTransfer.types].includes("text/plain")) return
    e.preventDefault()
    e.dataTransfer.dropEffect = "copy"
    setDropTarget(key)
  }

  function onDropSezione(e: React.DragEvent, sezione: string) {
    e.preventDefault()
    setDropTarget(null)
    const comp = parseDrag(e)
    if (!comp) return
    beginAdd(comp, sezione, false, null)
  }

  function onDropPrincipal(e: React.DragEvent, principal: GammaDistintaItemDto) {
    e.preventDefault()
    e.stopPropagation()
    setDropTarget(null)
    const comp = parseDrag(e)
    if (!comp) return
    beginAdd(
      comp,
      principal.sezione ?? GAMMA_SEZIONI[0],
      true,
      principal.slot
    )
  }

  async function removeRow(row: GammaDistintaItemDto) {
    const sameSlot = distinta.filter(
      (d) => d.sezione === row.sezione && d.slot === row.slot
    )
    const isPrincipal = !row.isAlternate
    const altCount = isPrincipal
      ? sameSlot.filter((d) => d.isAlternate).length
      : 0
    const question =
      isPrincipal && altCount > 0
        ? `Rimuovere «${row.productCode}» e le sue ${altCount} alternative?`
        : `Rimuovere «${row.productCode}» dalla distinta?`
    const ok = await confirm({
      title: "Conferma rimozione",
      description: question,
      confirmLabel: "Rimuovi",
      destructive: true,
    })
    if (!ok) return
    const toDelete = isPrincipal ? sameSlot : [row]
    try {
      for (const d of toDelete) {
        await deleteGammaDistinta(d.id)
      }
      invalidateDistinta()
    } catch (err) {
      notifyError(err instanceof Error ? err.message : "Errore")
    }
  }

  async function toggleOptional(row: GammaDistintaItemDto) {
    try {
      await updateGammaDistinta(row.id, { isOptional: !row.isOptional })
      invalidateDistinta()
    } catch (err) {
      notifyError(err instanceof Error ? err.message : "Errore")
    }
  }

  async function deleteSelection() {
    if (!selectedRobot && !selectedQuadro) {
      notifyError("Seleziona un robot o un quadro da eliminare.")
      return
    }
    if (selectedQuadro) {
      const ok = await confirm({
        title: "Conferma eliminazione",
        description:
          "Eliminare il quadro selezionato e tutta la sua distinta?",
        confirmLabel: "Elimina",
        destructive: true,
      })
      if (!ok) return
      try {
        await deleteGammaQuadro(selectedQuadro.id)
        setSelectedQuadro(null)
        await refreshTree()
      } catch (err) {
        notifyError(err instanceof Error ? err.message : "Errore")
      }
      return
    }
    if (selectedRobot) {
      const ok = await confirm({
        title: "Conferma eliminazione",
        description: `Eliminare il robot «${selectedRobot.modello}»?`,
        confirmLabel: "Elimina",
        destructive: true,
      })
      if (!ok) return
      try {
        await deleteGammaRobot(selectedRobot.id)
        setSelectedRobot(null)
        setSelectedQuadro(null)
        await refreshTree()
      } catch (err) {
        notifyError(err instanceof Error ? err.message : "Errore")
      }
    }
  }

  const basics = distinta.filter((d) => !d.isAlternate && !d.isOptional).length
  const opz = distinta.filter((d) => !d.isAlternate && d.isOptional).length
  const alt = distinta.filter((d) => d.isAlternate).length
  const totBase = distinta
    .filter((d) => !d.isAlternate && !d.isOptional && d.prezzoVb != null)
    .reduce((s, d) => s + (d.prezzoVb ?? 0), 0)
  const totOpz = distinta
    .filter((d) => !d.isAlternate && d.isOptional && d.prezzoVb != null)
    .reduce((s, d) => s + (d.prezzoVb ?? 0), 0)

  return (
    <div className="flex min-h-[560px] flex-col gap-3">
      {isAdmin ? (
        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={() => setRobotDialog({ open: true, robot: null })}
          >
            <Plus className="size-3.5" />
            Robot
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={!selectedRobot}
            onClick={() => setQuadroDialog({ open: true, quadro: null })}
          >
            <Plus className="size-3.5" />
            Quadro
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={!selectedRobot && !selectedQuadro}
            onClick={() => {
              if (selectedQuadro) {
                setQuadroDialog({ open: true, quadro: selectedQuadro })
              } else if (selectedRobot) {
                setRobotDialog({ open: true, robot: selectedRobot })
              }
            }}
          >
            <Pencil className="size-3.5" />
            Modifica
          </Button>
          <Button
            type="button"
            size="sm"
            variant="outline"
            disabled={!selectedRobot && !selectedQuadro}
            onClick={() => void deleteSelection()}
          >
            <Trash2 className="size-3.5" />
            Elimina
          </Button>
          <span className="text-xs text-muted-foreground">
            Trascina un componente sulla sezione (principale) o su un
            principale (alternativa).
          </span>
        </div>
      ) : (
        <p className="text-xs text-muted-foreground">
          Sola lettura — le modifiche richiedono ruolo ADMIN.
        </p>
      )}

      <div className="grid min-h-0 flex-1 grid-cols-1 gap-3 lg:grid-cols-[260px_240px_1fr]">
        {/* Albero robot/quadro */}
        <div className="flex flex-col rounded-lg border">
          <div className="border-b p-2">
            <Input
              placeholder="Filtra robot…"
              value={robotSearch}
              onChange={(e) => setRobotSearch(e.target.value)}
              className="h-8"
            />
          </div>
          <div className="min-h-0 flex-1 overflow-auto px-1 py-1">
            {robots.map((robot) => {
              const isOpen = expanded.has(robot.id)
              const quadri = quadriByRobot[robot.id]
              return (
                <div key={robot.id}>
                  <div
                    className={cn(
                      "flex w-full items-center gap-1 rounded-md px-2 py-1.5 text-sm hover:bg-muted",
                      selectedRobot?.id === robot.id &&
                        !selectedQuadro &&
                        "bg-muted"
                    )}
                  >
                    <button
                      type="button"
                      className="shrink-0 text-muted-foreground"
                      onClick={() => void toggleRobot(robot)}
                      aria-label={isOpen ? "Comprimi" : "Espandi"}
                    >
                      {isOpen ? (
                        <ChevronDown className="size-3.5" />
                      ) : (
                        <ChevronRight className="size-3.5" />
                      )}
                    </button>
                    <button
                      type="button"
                      className="flex min-w-0 flex-1 items-center gap-1 text-left"
                      onClick={() => void selectRobot(robot)}
                    >
                      <span className="truncate font-medium">{robot.modello}</span>
                      <span className="ml-auto text-xs text-muted-foreground">
                        {robot.quadriCount}
                      </span>
                    </button>
                  </div>
                  {isOpen ? (
                    <ul className="mb-1 ml-5 space-y-0.5 border-l pl-2">
                      {(quadri ?? []).map((q) => (
                        <li key={q.id}>
                          <button
                            type="button"
                            className={cn(
                              "w-full rounded-md px-2 py-1 text-left text-xs hover:bg-muted",
                              selectedQuadro?.id === q.id && "bg-muted font-medium"
                            )}
                            onClick={() => {
                              setSelectedRobot(robot)
                              setSelectedQuadro(q)
                            }}
                          >
                            {buildQuadroLabel(q)}
                          </button>
                        </li>
                      ))}
                    </ul>
                  ) : null}
                </div>
              )
            })}
          </div>
          <div className="border-t px-2 py-1 text-xs text-muted-foreground">
            {robotsQuery.data?.length ?? 0} robot
          </div>
        </div>

        {/* Componenti drag source */}
        <div className="flex flex-col rounded-lg border">
          <div className="border-b p-2">
            <Input
              placeholder="Filtro * jolly…"
              value={compSearch}
              onChange={(e) => setCompSearch(e.target.value)}
              className="h-8"
            />
          </div>
          <GridScroller fill>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="text-xs">Codice</TableHead>
                  <TableHead className="text-xs">Nome</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {components.map((c) => (
                  <TableRow
                    key={c.productId}
                    draggable={isAdmin}
                    onDragStart={(e) => onDragStart(e, c)}
                    className={cn(isAdmin && "cursor-grab")}
                    onDoubleClick={(e) => {
                      if (e.shiftKey) {
                        onOpenProduct(c.productId)
                        return
                      }
                      if (isAdmin && selectedQuadro) {
                        beginAdd(
                          c,
                          sezioneForCategoria(c.categoria),
                          false,
                          null
                        )
                      }
                    }}
                  >
                    <TableCell className="py-1 font-mono text-[11px] font-semibold">
                      {c.code}
                    </TableCell>
                    <TableCell className="max-w-[120px] truncate py-1 text-xs">
                      {c.name}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </GridScroller>
          <div className="border-t px-2 py-1 text-xs text-muted-foreground">
            {components.length} componenti
          </div>
        </div>

        {/* Distinta editabile */}
        <div className="flex flex-col rounded-lg border">
          <div className="border-b px-3 py-2">
            <h3 className="text-sm font-semibold">
              {selectedRobot?.modello ?? "Seleziona un quadro"}
            </h3>
            <p className="text-xs text-muted-foreground">
              {selectedQuadro
                ? buildQuadroSubtitle(selectedQuadro)
                : "Scegli un quadro a sinistra, poi trascina i componenti nelle sezioni."}
            </p>
          </div>
          <div
            className="min-h-0 flex-1 space-y-2 overflow-auto p-2"
            onDragOver={(e) => {
              if (!isAdmin || !selectedQuadro) return
              if ([...e.dataTransfer.types].includes("text/plain")) {
                e.preventDefault()
              }
            }}
            onDrop={(e) => {
              e.preventDefault()
              setDropTarget(null)
              const comp = parseDrag(e)
              if (!comp || !selectedQuadro) return
              beginAdd(comp, sezioneForCategoria(comp.categoria), false, null)
            }}
            onDragLeave={() => setDropTarget(null)}
          >
            {!selectedQuadro ? (
              <p className="p-4 text-sm text-muted-foreground">
                Seleziona un quadro per modificarne la distinta.
              </p>
            ) : distintaQuery.isLoading ? (
              <p className="p-4 text-sm text-muted-foreground">Caricamento…</p>
            ) : (
              GAMMA_SEZIONI.map((sezione) => {
                const sezItems = distinta.filter(
                  (d) => (d.sezione ?? "") === sezione
                )
                const slots = new Map<string, GammaDistintaItemDto[]>()
                for (const item of sezItems) {
                  const key = item.slot ?? ""
                  const list = slots.get(key)
                  if (list) list.push(item)
                  else slots.set(key, [item])
                }
                const dropKey = `sez:${sezione}`
                return (
                  <div
                    key={sezione}
                    className={cn(
                      "rounded-md border bg-muted/30",
                      dropTarget === dropKey && "ring-2 ring-primary"
                    )}
                    onDragOver={(e) => allowDrop(e, dropKey)}
                    onDrop={(e) => onDropSezione(e, sezione)}
                  >
                    <div className="flex items-center gap-2 px-2 py-1.5">
                      <span className="text-xs font-semibold">{sezione}</span>
                      <span className="text-xs text-muted-foreground">
                        ({sezItems.length})
                      </span>
                    </div>
                    <ul className="space-y-0.5 px-1 pb-1">
                      {[...slots.values()].map((g) => {
                        const principal =
                          g.find((x) => !x.isAlternate) ?? g[0]
                        const alts = g.filter((x) => x !== principal)
                        const pKey = `p:${principal.id}`
                        return (
                          <li key={principal.id}>
                            <DistintaRow
                              row={principal}
                              isPrincipal
                              isAdmin={isAdmin}
                              highlight={dropTarget === pKey}
                              onDragOver={(e) => allowDrop(e, pKey)}
                              onDrop={(e) => onDropPrincipal(e, principal)}
                              onOpenProduct={onOpenProduct}
                              onRemove={() => void removeRow(principal)}
                              onToggleOptional={() => void toggleOptional(principal)}
                              onEditQty={() =>
                                setQtyDialog({
                                  open: true,
                                  mode: "edit",
                                  code: principal.productCode ?? "?",
                                  initial: principal.qty,
                                  pending: { kind: "edit", row: principal },
                                })
                              }
                            />
                            {alts.map((altRow) => (
                              <div key={altRow.id} className="ml-4">
                                <DistintaRow
                                  row={altRow}
                                  isPrincipal={false}
                                  isAdmin={isAdmin}
                                  highlight={false}
                                  onOpenProduct={onOpenProduct}
                                  onRemove={() => void removeRow(altRow)}
                                  onToggleOptional={() =>
                                    void toggleOptional(altRow)
                                  }
                                  onEditQty={() =>
                                    setQtyDialog({
                                      open: true,
                                      mode: "edit",
                                      code: altRow.productCode ?? "?",
                                      initial: altRow.qty,
                                      pending: { kind: "edit", row: altRow },
                                    })
                                  }
                                />
                              </div>
                            ))}
                          </li>
                        )
                      })}
                    </ul>
                  </div>
                )
              })
            )}
          </div>
          {selectedQuadro ? (
            <div className="flex flex-wrap items-center justify-between gap-2 border-t px-3 py-1.5 text-xs text-muted-foreground">
              <span>
                {basics} componenti
                {alt > 0 ? `  ·  ${alt} alternative` : ""}
                {opz > 0 ? `  ·  ${opz} opzioni` : ""}
              </span>
              <span className="font-medium text-foreground">
                {opz > 0
                  ? `VB base: ${formatEuro(totBase)} €    ·    +opzioni: ${formatEuro(totBase + totOpz)} €`
                  : `VB: ${formatEuro(totBase)} €`}
              </span>
            </div>
          ) : null}
        </div>
      </div>

      <RobotDialog
        open={robotDialog.open}
        robot={robotDialog.robot}
        onClose={() => setRobotDialog({ open: false, robot: null })}
        onSave={(req) => saveRobotMut.mutate(req)}
      />
      <QuadroDialog
        open={quadroDialog.open}
        quadro={quadroDialog.quadro}
        onClose={() => setQuadroDialog({ open: false, quadro: null })}
        onSave={(req) => saveQuadroMut.mutate(req)}
      />
      <QuantityDialog
        open={qtyDialog.open}
        childCodice={qtyDialog.code}
        mode={qtyDialog.mode}
        initialQuantity={qtyDialog.initial}
        onConfirm={(qty) => void onQtyConfirm(qty)}
        onCancel={() =>
          setQtyDialog((s) => ({ ...s, open: false, pending: null }))
        }
      />
    </div>
  )
}

function DistintaRow({
  row,
  isPrincipal,
  isAdmin,
  highlight,
  onDragOver,
  onDrop,
  onOpenProduct,
  onRemove,
  onToggleOptional,
  onEditQty,
}: {
  row: GammaDistintaItemDto
  isPrincipal: boolean
  isAdmin: boolean
  highlight: boolean
  onDragOver?: (e: React.DragEvent) => void
  onDrop?: (e: React.DragEvent) => void
  onOpenProduct: (productId: number) => void
  onRemove: () => void
  onToggleOptional: () => void
  onEditQty: () => void
}) {
  return (
    <div
      className={cn(
        "flex items-center gap-1 rounded-md bg-background px-2 py-1 text-sm",
        row.isAlternate && "bg-muted/40",
        highlight && "ring-2 ring-primary"
      )}
      onDragOver={isPrincipal ? onDragOver : undefined}
      onDrop={isPrincipal ? onDrop : undefined}
      onDoubleClick={() => {
        if (row.productId) onOpenProduct(row.productId)
      }}
    >
      {row.isAlternate ? (
        <Badge variant="outline" className="text-[10px]">
          ALT
        </Badge>
      ) : null}
      {row.isOptional ? (
        <Badge variant="secondary" className="text-[10px]">
          OPT
        </Badge>
      ) : null}
      <span className="font-mono text-xs font-semibold">
        {row.productCode ?? row.codeRaw ?? "?"}
      </span>
      <span className="min-w-0 flex-1 truncate text-xs text-muted-foreground">
        — {row.productName ?? ""}
      </span>
      {row.qty > 1 ? (
        <span className="text-xs font-semibold text-primary">×{row.qty}</span>
      ) : null}
      {isAdmin ? (
        <>
          {isPrincipal ? (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="size-6"
                >
                  <MoreHorizontal className="size-3.5" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onClick={onToggleOptional}>
                  {row.isOptional ? "Togli opzione" : "Segna come opzione"}
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onEditQty}>
                  Modifica quantità…
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          ) : null}
          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="size-6 text-destructive"
            onClick={onRemove}
            title="Rimuovi"
          >
            <X className="size-3.5" />
          </Button>
        </>
      ) : null}
    </div>
  )
}
