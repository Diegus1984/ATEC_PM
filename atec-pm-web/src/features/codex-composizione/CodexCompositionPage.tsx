import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ChevronDown, ChevronUp, RefreshCw, X } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { fetchCatalogItems } from "@/lib/api/catalog"
import {
  addComposition,
  deleteComposition,
  fetchCompositionTree,
  updateCompositionQuantity,
} from "@/lib/api/codex-compositions"
import { fetchAllCodex, fetchCodex } from "@/lib/api/codex"
import type { CodexListItem, CompositionTreeNode } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { useCodexHub } from "@/lib/signalr/use-codex-hub"
import { useDebounced } from "@/lib/use-debounced"
import { cn } from "@/lib/utils"

import { CodexImportDialog } from "./CodexImportDialog"
import { QuantityDialog } from "./QuantityDialog"
import { wildcardMatch } from "@/lib/wildcard"

// ── CONFIGURAZIONE TIPI ────────────────────────────────────

interface CompositionTypeConfig {
  code: string
  label: string
  childPrefixes: string[]
  allowCatalog: boolean
}

const COMPOSITION_TYPES: CompositionTypeConfig[] = [
  { code: "501", label: "Gruppo meccanico", childPrefixes: ["1", "2", "3", "4"], allowCatalog: true },
  { code: "601", label: "Assieme meccanico", childPrefixes: ["5"], allowCatalog: false },
  { code: "701", label: "Layout meccanico", childPrefixes: ["6"], allowCatalog: false },
]

// Etichette per la combo sotto-tipo della griglia "Articoli disponibili".
const PREFIX_LABELS: Record<string, string> = {
  "1": "1xx — Commerciale",
  "2": "2xx — Elettrico",
  "3": "3xx — Pneumatico",
  "4": "4xx — Meccanico",
  "5": "5xx — Gruppo mecc.",
  "6": "6xx — Assieme mecc.",
}

// Accenti di sezione (replicano i tre header colorati della pagina WPF).
const ACCENT_COMPOSITI = "#4F6EF7"
const ACCENT_ARTICOLI = "#D97706"
const ACCENT_COMPOSIZIONE = "#12B76A"

// Sfondi tenui per famiglia di codice (1xx…7xx), come nei nodi dell'albero WPF.
const NODE_COLORS: Record<string, string> = {
  "1": "#DBEDF8",
  "2": "#E8F5E9",
  "3": "#FFF3E0",
  "4": "#F3E5F5",
  "5": "#FDF6D6",
  "6": "#E0F2F1",
  "7": "#FCE4EC",
}

const ALL = "__all__"

interface AvailableItem {
  id: number
  codice: string
  descr: string
  source: "codex" | "catalog"
}

interface PendingAdd {
  parentId: number
  child: AvailableItem
}

// ── HELPER ─────────────────────────────────────────────────

/** Match specifico per il codice che rimuove i punti sia dal valore che dal filtro. */
function matchCodice(codice: string | undefined, filter: string): boolean {
  const cleanCodice = (codice ?? "").replace(/\./g, "")
  const cleanFilter = filter.replace(/\./g, "")
  return wildcardMatch(cleanCodice, cleanFilter)
}

/** Inserisce un punto prima delle ultime 3 cifre (formattazione codice Codex). */
function formatCodice(codice: string): string {
  const raw = (codice ?? "").replace(/\./g, "")
  if (raw.length > 3) {
    return `${raw.slice(0, raw.length - 3)}.${raw.slice(raw.length - 3)}`
  }
  return raw
}

function nodeColor(codice: string): string {
  return NODE_COLORS[codice.charAt(0)] ?? "#F5F5F5"
}

function nodeIcon(node: { source: string; codice: string }): string {
  if (node.source === "catalog") return "🛒"
  const prefix = node.codice.charAt(0)
  if (prefix === "7") return "📦"
  if (prefix === "6") return "🔧"
  if (prefix === "5") return "⚙"
  return "🔩"
}

/**
 * Validazione gerarchia lato client (anteprima del drop). Il server riapplica le
 * stesse regole all'aggiunta. Gli articoli di catalogo sono sempre ammessi.
 */
function validateDropLocal(targetCodice: string, child: AvailableItem): string | null {
  if (child.source === "catalog") return null
  const target = targetCodice.charAt(0)
  const childPrefix = child.codice.charAt(0)
  switch (target) {
    case "5":
      return ["1", "2", "3", "4"].includes(childPrefix) ? null : "5xx accetta solo 1xx-4xx"
    case "6":
      return childPrefix === "5" ? null : "6xx accetta solo 5xx"
    case "7":
      return childPrefix === "6" ? null : "7xx accetta solo 6xx"
    default:
      return "Questo nodo non può contenere figli"
  }
}

function countComponents(node: CompositionTreeNode): number {
  return node.children.reduce((acc, child) => acc + 1 + countComponents(child), 0)
}

/** Pezzi totali = somma delle quantità di tutte le righe (senza esplosione ricorsiva). */
function countPieces(node: CompositionTreeNode): number {
  return node.children.reduce((acc, child) => acc + child.quantity + countPieces(child), 0)
}

/** Clona l'albero applicando la nuova quantità alla riga indicata (update ottimistico). */
function patchQuantity(
  node: CompositionTreeNode,
  compositionId: number,
  quantity: number
): CompositionTreeNode {
  return {
    ...node,
    quantity: node.compositionId === compositionId ? quantity : node.quantity,
    children: node.children.map((child) => patchQuantity(child, compositionId, quantity)),
  }
}

// ── ALBERO ─────────────────────────────────────────────────

interface TreeCtx {
  canEdit: boolean
  dragItem: React.MutableRefObject<AvailableItem | null>
  hoverKey: string | null
  setHoverKey: (key: string | null) => void
  /** Aggiunge `child` sotto il nodo `parentCodexId`; valida contro `parentCodice`. */
  onDropTo: (parentCodexId: number, parentCodice: string, child: AvailableItem) => void
  onRemove: (node: CompositionTreeNode) => void
  /** Apre il dialog quantità (click sul numero dello stepper). */
  onEditQuantity: (node: CompositionTreeNode) => void
  /** Incrementa/decrementa la quantità di una riga (freccette ▲/▼, delta ±1). */
  onChangeQuantity: (node: CompositionTreeNode, delta: number) => void
}

function NodeRow({
  node,
  depth,
  isRoot,
  ctx,
}: {
  node: CompositionTreeNode
  depth: number
  isRoot: boolean
  ctx: TreeCtx
}) {
  const isCodex = node.source !== "catalog"
  // I nodi Codex non-radice accettano figli (drop con annidamento); la radice è
  // gestita dal contenitore. Il server valida comunque la gerarchia all'aggiunta.
  const droppable = ctx.canEdit && isCodex && !isRoot
  const key = `node-${node.compositionId}-${node.codexId}`
  const display = isCodex ? formatCodice(node.codice) : node.codice

  const dndHandlers = droppable
    ? {
        onDragOver: (event: React.DragEvent) => {
          const item = ctx.dragItem.current
          if (!item || validateDropLocal(node.codice, item) !== null) return
          event.preventDefault()
          event.stopPropagation()
          ctx.setHoverKey(key)
        },
        onDrop: (event: React.DragEvent) => {
          event.preventDefault()
          event.stopPropagation()
          const item = ctx.dragItem.current
          ctx.setHoverKey(null)
          if (!item || validateDropLocal(node.codice, item) !== null) return
          ctx.onDropTo(node.codexId, node.codice, item)
        },
      }
    : {}

  return (
    <div>
      <div
        {...dndHandlers}
        className={cn(
          "flex items-center gap-2 rounded-md px-2 py-1",
          ctx.hoverKey === key && "ring-2 ring-primary ring-offset-1"
        )}
        style={{
          marginLeft: depth * 16,
          backgroundColor: nodeColor(node.codice),
          color: "#1A1D26",
        }}
      >
        <span className="shrink-0">{nodeIcon(node)}</span>
        <span
          className={cn(
            "shrink-0 font-mono",
            isRoot ? "text-base font-bold" : "text-sm font-semibold"
          )}
        >
          {display}
        </span>
        <span className="truncate text-sm" style={{ color: "#444" }}>
          — {node.descr}
        </span>
        {!isRoot ? (
          <span className="ml-auto flex shrink-0 items-center gap-1">
            {ctx.canEdit ? (
              <span className="flex items-stretch overflow-hidden rounded border border-black/20 bg-white/75">
                <button
                  type="button"
                  title="Imposta quantità"
                  onClick={() => ctx.onEditQuantity(node)}
                  className="min-w-5 px-1 font-mono text-[11px] font-semibold tabular-nums hover:bg-white"
                >
                  {node.quantity}
                </button>
                <span className="flex flex-col border-l border-black/10">
                  <button
                    type="button"
                    title="Aumenta quantità"
                    onClick={() => ctx.onChangeQuantity(node, 1)}
                    className="flex h-[10px] items-center justify-center px-0.5 text-black/60 hover:bg-white hover:text-black"
                  >
                    <ChevronUp className="size-2.5" />
                  </button>
                  <button
                    type="button"
                    title="Diminuisci quantità"
                    disabled={node.quantity <= 1}
                    onClick={() => ctx.onChangeQuantity(node, -1)}
                    className="flex h-[10px] items-center justify-center border-t border-black/10 px-0.5 text-black/60 hover:bg-white hover:text-black disabled:pointer-events-none disabled:opacity-30"
                  >
                    <ChevronDown className="size-2.5" />
                  </button>
                </span>
              </span>
            ) : (
              <span className="rounded-full border border-black/10 px-2 font-mono text-[11px] leading-5 tabular-nums opacity-70">
                ×{node.quantity}
              </span>
            )}
            {ctx.canEdit ? (
              <Button
                variant="ghost"
                size="icon-sm"
                className="shrink-0 text-destructive hover:bg-destructive/10"
                title="Rimuovi il componente dalla composizione (tutte le quantità)"
                onClick={() => ctx.onRemove(node)}
              >
                <X className="size-3.5" />
              </Button>
            ) : null}
          </span>
        ) : null}
      </div>

      {isRoot ? (
        <RootGroups node={node} ctx={ctx} />
      ) : (
        [...node.children]
          .sort((a, b) => a.codice.localeCompare(b.codice))
          .map((child) => (
            <NodeRow
              key={key + "/" + child.compositionId}
              node={child}
              depth={depth + 1}
              isRoot={false}
              ctx={ctx}
            />
          ))
      )}
    </div>
  )
}

/** Raggruppa i figli diretti della radice in "Componenti Codex" e "Componenti commerciali". */
function RootGroups({ node, ctx }: { node: CompositionTreeNode; ctx: TreeCtx }) {
  const codex = node.children
    .filter((c) => c.source !== "catalog")
    .sort((a, b) => a.codice.localeCompare(b.codice))
  const catalog = node.children
    .filter((c) => c.source === "catalog")
    .sort((a, b) => a.codice.localeCompare(b.codice))
  const codexPieces = codex.reduce((acc, c) => acc + c.quantity, 0)
  const catalogPieces = catalog.reduce((acc, c) => acc + c.quantity, 0)

  function groupDrop(event: React.DragEvent) {
    event.preventDefault()
    event.stopPropagation()
    const item = ctx.dragItem.current
    ctx.setHoverKey(null)
    if (!item || validateDropLocal(node.codice, item) !== null) return
    ctx.onDropTo(node.codexId, node.codice, item)
  }

  function groupDragOver(groupKey: string) {
    return (event: React.DragEvent) => {
      const item = ctx.dragItem.current
      if (!item || validateDropLocal(node.codice, item) !== null) return
      event.preventDefault()
      event.stopPropagation()
      ctx.setHoverKey(groupKey)
    }
  }

  return (
    <div className="mt-1 space-y-1">
      {codex.length > 0 ? (
        <div className="ml-4">
          <div
            onDragOver={groupDragOver("group-codex")}
            onDrop={groupDrop}
            className={cn(
              "rounded-md px-2 py-1 text-sm font-semibold",
              ctx.hoverKey === "group-codex" && "ring-2 ring-primary ring-offset-1"
            )}
            style={{ backgroundColor: "#EEF2FF", color: ACCENT_COMPOSITI }}
          >
            🔩 Componenti Codex{" "}
            <span className="text-xs font-normal text-muted-foreground">
              ({codex.length} componenti · {codexPieces} pezzi)
            </span>
          </div>
          {codex.map((child) => (
            <NodeRow key={`codex/${child.compositionId}`} node={child} depth={2} isRoot={false} ctx={ctx} />
          ))}
        </div>
      ) : null}

      {catalog.length > 0 ? (
        <div className="ml-4">
          <div
            onDragOver={groupDragOver("group-catalog")}
            onDrop={groupDrop}
            className={cn(
              "rounded-md px-2 py-1 text-sm font-semibold",
              ctx.hoverKey === "group-catalog" && "ring-2 ring-primary ring-offset-1"
            )}
            style={{ backgroundColor: "#FFF8F0", color: ACCENT_ARTICOLI }}
          >
            🛒 Componenti commerciali{" "}
            <span className="text-xs font-normal text-muted-foreground">
              ({catalog.length} componenti · {catalogPieces} pezzi)
            </span>
          </div>
          {catalog.map((child) => (
            <NodeRow key={`catalog/${child.compositionId}`} node={child} depth={2} isRoot={false} ctx={ctx} />
          ))}
        </div>
      ) : null}
    </div>
  )
}

// ── PAGINA ─────────────────────────────────────────────────

export function CodexCompositionPage() {
  const queryClient = useQueryClient()


  const confirm = useConfirm()
  const canEdit = getSession()?.user.userRole === "ADMIN"

  const [typeCode, setTypeCode] = React.useState("501")
  const [selectedParent, setSelectedParent] = React.useState<CodexListItem | null>(null)

  // Filtri griglia superiore (Compositi) e inferiore (Articoli disponibili).
  const [compCode, setCompCode] = React.useState("")
  const [compDescr, setCompDescr] = React.useState("")
  const [availCode, setAvailCode] = React.useState("")
  const [availDescr, setAvailDescr] = React.useState("")

  // Combo sotto-tipo + sorgente della griglia inferiore.
  const [childType, setChildType] = React.useState(ALL)
  const [source, setSource] = React.useState<"codex" | "catalog">("codex")

  // Stato drag&drop.
  const dragItem = React.useRef<AvailableItem | null>(null)
  const [hoverKey, setHoverKey] = React.useState<string | null>(null)
  const [pendingAdd, setPendingAdd] = React.useState<PendingAdd | null>(null)
  // Riga di cui si sta modificando la quantità (badge ×N).
  const [editQty, setEditQty] = React.useState<CompositionTreeNode | null>(null)

  // Import distinta da file STEP: il pulsante apre direttamente il selettore file,
  // il composito padre si ricava dalla radice del file (nessuna selezione richiesta).
  const importFileRef = React.useRef<HTMLInputElement>(null)
  const [importFile, setImportFile] = React.useState<File | null>(null)
  // Composito da selezionare dopo un cambio tipo programmato (post-import STEP):
  // l'effect su typeCode azzererebbe la selezione appena fatta.
  const pendingSelectRef = React.useRef<CodexListItem | null>(null)

  const activeType =
    COMPOSITION_TYPES.find((type) => type.code === typeCode) ?? COMPOSITION_TYPES[0]
  const allowCatalog = activeType.allowCatalog
  const effectiveSource: "codex" | "catalog" = allowCatalog ? source : "codex"

  // Reset al cambio tipo (come CmbType_SelectionChanged nel WPF). Se il cambio è
  // stato innescato dall'import STEP, seleziona il composito importato anziché azzerare.
  React.useEffect(() => {
    setSelectedParent(pendingSelectRef.current)
    pendingSelectRef.current = null
    setChildType(ALL)
    setSource("codex")
  }, [typeCode])

  // Compositi del tipo selezionato: filtrati server-side per prefisso (es. 501%),
  // così TUTTI i 5xx/6xx/7xx vengono caricati (prima ci si fermava a 10k righe e i
  // 501, ordinati dopo 1xx/2xx, restavano fuori → "nessun composito 501"). Il filtro
  // per codice/descrizione è poi applicato client-side con i jolly, come nel WPF.
  const compositesQuery = useQuery({
    queryKey: ["codex-by-prefix", typeCode],
    queryFn: () => fetchAllCodex({ codicePrefixes: [typeCode] }),
  })

  const treeQuery = useQuery({
    queryKey: ["composition-tree", selectedParent?.id],
    queryFn: () => fetchCompositionTree(selectedParent!.id),
    enabled: selectedParent != null,
  })

  // Articoli disponibili: ricerca server-side paginata (i prefissi figli possono
  // contare decine di migliaia di righe → niente caricamento client-side completo).
  // I jolly su codice/descrizione sono applicati dal server (stesso helper LIKE).
  const childPrefixes = childType === ALL ? activeType.childPrefixes : [childType]
  const debAvailCode = useDebounced(availCode.trim(), 300)
  const debAvailDescr = useDebounced(availDescr.trim(), 300)

  const codexAvailableQuery = useQuery({
    queryKey: ["codex-available", childPrefixes, debAvailCode, debAvailDescr],
    queryFn: () =>
      fetchCodex({
        codicePrefixes: childPrefixes,
        filters: { codice: debAvailCode, descr: debAvailDescr },
        pageSize: 100,
      }),
    enabled: effectiveSource === "codex",
  })

  const catalogQuery = useQuery({
    queryKey: ["catalog-available", debAvailCode, debAvailDescr],
    queryFn: () =>
      fetchCatalogItems({
        filters: { code: debAvailCode, description: debAvailDescr },
        pageSize: 100,
      }),
    enabled: effectiveSource === "catalog",
  })

  // Real-time: quando un altro utente modifica una composizione, ricarica l'albero
  // aperto (hub /hubs/codex). connectionIdRef serve per la self-exclusion sulle mutazioni.
  const connectionIdRef = useCodexHub(() => {
    void queryClient.invalidateQueries({ queryKey: ["composition-tree"] })
  })

  const addMutation = useMutation({
    mutationFn: (vars: { parentId: number; child: AvailableItem; quantity: number }) =>
      addComposition(
        {
          parentCodexId: vars.parentId,
          childCodexId: vars.child.source === "codex" ? vars.child.id : null,
          childCatalogId: vars.child.source === "catalog" ? vars.child.id : null,
          quantity: vars.quantity,
        },
        connectionIdRef.current
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["composition-tree"] }),
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: (compositionId: number) =>
      deleteComposition(compositionId, connectionIdRef.current),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["composition-tree"] }),
    onError: (err: Error) => notifyError(err),
  })

  const quantityMutation = useMutation({
    mutationFn: (vars: { compositionId: number; quantity: number }) =>
      updateCompositionQuantity(vars.compositionId, vars.quantity, connectionIdRef.current),
    // Update ottimistico: i click rapidi su ▲/▼ partono dal valore già aggiornato in cache.
    onMutate: async (vars) => {
      await queryClient.cancelQueries({ queryKey: ["composition-tree"] })
      queryClient.setQueryData<CompositionTreeNode>(
        ["composition-tree", selectedParent?.id],
        (old) => (old ? patchQuantity(old, vars.compositionId, vars.quantity) : old)
      )
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["composition-tree"] }),
    onError: (err: Error) => {
      notifyError(err)
      void queryClient.invalidateQueries({ queryKey: ["composition-tree"] })
    },
  })

  // Compositi (griglia superiore): l'insieme è già ristretto al prefisso del tipo
  // lato server; qui si applica solo il filtro jolly client-side (come il WPF).
  const composites = React.useMemo(() => {
    const items = compositesQuery.data ?? []
    return items
      .filter((item) => matchCodice(item.codice, compCode))
      .filter((item) => wildcardMatch(item.descr, compDescr))
      .sort((a, b) => a.codice.localeCompare(b.codice))
  }, [compositesQuery.data, compCode, compDescr])

  // Articoli disponibili (griglia inferiore): risultati server-side (Codex o Catalogo).
  const available = React.useMemo<AvailableItem[]>(() => {
    if (effectiveSource === "catalog") {
      return (catalogQuery.data?.items ?? []).map((item) => ({
        id: item.id,
        codice: item.code,
        descr: item.description,
        source: "catalog" as const,
      }))
    }
    return (codexAvailableQuery.data?.items ?? []).map((item) => ({
      id: item.id,
      codice: item.codice,
      descr: item.descr,
      source: "codex" as const,
    }))
  }, [effectiveSource, catalogQuery.data, codexAvailableQuery.data])

  const availableTotal =
    effectiveSource === "catalog"
      ? catalogQuery.data?.totalCount ?? 0
      : codexAvailableQuery.data?.totalCount ?? 0
  const availableLoading =
    effectiveSource === "catalog" ? catalogQuery.isLoading : codexAvailableQuery.isLoading

  async function handleRemove(node: CompositionTreeNode) {
    const ok = await confirm({
      title: "Rimuovi componente",
      description: `Rimuovere "${node.codice} — ${node.descr}" dalla composizione?`,
      confirmLabel: "Rimuovi",
    })
    if (ok) deleteMutation.mutate(node.compositionId)
  }

  // Drop su un nodo/radice → apre il dialog quantità (validazione già fatta a monte).
  function handleDropTo(parentCodexId: number, _parentCodice: string, child: AvailableItem) {
    setPendingAdd({ parentId: parentCodexId, child })
  }

  // Doppio click su un articolo → aggiunta rapida (quantità 1) alla radice, come il WPF.
  function handleQuickAdd(child: AvailableItem) {
    if (!canEdit || !selectedParent) return
    addMutation.mutate({ parentId: selectedParent.id, child, quantity: 1 })
  }

  const treeData = treeQuery.data
  const componentCount = treeData ? countComponents(treeData) : 0
  const pieceCount = treeData ? countPieces(treeData) : 0
  const statusText = selectedParent
    ? treeData
      ? `${componentCount} componenti · ${pieceCount} pezzi nella composizione`
      : "Caricamento composizione…"
    : `${composites.length} compositi ${typeCode} trovati`

  const ctx: TreeCtx = {
    canEdit,
    dragItem,
    hoverKey,
    setHoverKey,
    onDropTo: handleDropTo,
    onRemove: handleRemove,
    onEditQuantity: setEditQty,
    onChangeQuantity: (node, delta) => {
      const next = node.quantity + delta
      if (next >= 1) {
        quantityMutation.mutate({ compositionId: node.compositionId, quantity: next })
      }
    },
  }

  // Drop sull'area albero (spazio vuoto / fuori dai nodi) → aggiunta alla radice.
  const rootDroppable = canEdit && selectedParent != null
  function rootDragOver(event: React.DragEvent) {
    if (!rootDroppable || !selectedParent) return
    const item = dragItem.current
    if (!item || validateDropLocal(selectedParent.codice, item) !== null) return
    event.preventDefault()
    setHoverKey("root")
  }
  function rootDrop(event: React.DragEvent) {
    if (!rootDroppable || !selectedParent) return
    event.preventDefault()
    const item = dragItem.current
    setHoverKey(null)
    if (!item || validateDropLocal(selectedParent.codice, item) !== null) return
    handleDropTo(selectedParent.id, selectedParent.codice, item)
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Composizione Codex</CardTitle>
              <CardDescription>
                Distinta dei compositi meccanici (5xx/6xx/7xx) — trascina gli
                articoli disponibili sull'albero o fai doppio click.
              </CardDescription>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-sm text-muted-foreground">Tipo:</span>
              <Select value={typeCode} onValueChange={setTypeCode}>
                <SelectTrigger className="w-56">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {COMPOSITION_TYPES.map((type) => (
                    <SelectItem key={type.code} value={type.code}>
                      {type.code} — {type.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    void compositesQuery.refetch()
                    if (selectedParent) void treeQuery.refetch()
                    if (effectiveSource === "catalog") void catalogQuery.refetch()
                    else void codexAvailableQuery.refetch()
                  }}
                  disabled={compositesQuery.isFetching}
                >
                  <RefreshCw className={compositesQuery.isFetching ? "animate-spin" : ""} />
                  Aggiorna
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => importFileRef.current?.click()}
                  disabled={!canEdit}
                  title="Importa la distinta dal file STEP dell'assieme"
                >
                  Importa
                </Button>
                <input
                  ref={importFileRef}
                  type="file"
                  accept=".step,.stp,.STEP,.STP"
                  className="hidden"
                  onChange={(event) => {
                    const file = event.target.files?.[0]
                    if (file) setImportFile(file)
                    // Permette di riselezionare lo stesso file.
                    event.target.value = ""
                  }}
                />
            </div>
          </div>
        </CardHeader>

        <CardContent>
          <div className="grid gap-4 lg:grid-cols-2">
            {/* ── SINISTRA: Compositi + Articoli disponibili ── */}
            <div className="flex flex-col gap-4">
              {/* Griglia superiore: Compositi */}
              <div className="flex h-[26rem] flex-col overflow-hidden rounded-md border">
                <div className="flex items-center gap-2 border-b bg-muted/40 px-3 py-2">
                  <span
                    className="text-xs font-semibold tracking-wide"
                    style={{ color: ACCENT_COMPOSITI }}
                  >
                    COMPOSITI
                  </span>
                </div>
                <div className="flex gap-2 border-b p-2">
                  <Input
                    value={compCode}
                    placeholder="Codice"
                    className="h-8 w-32"
                    onChange={(event) => setCompCode(event.target.value)}
                  />
                  <Input
                    value={compDescr}
                    placeholder="Descrizione"
                    className="h-8 flex-1"
                    onChange={(event) => setCompDescr(event.target.value)}
                  />
                </div>
                <div className="min-h-0 flex-1 overflow-y-auto">
                  {compositesQuery.isLoading ? (
                    <p className="p-4 text-center text-sm text-muted-foreground">Caricamento…</p>
                  ) : composites.length === 0 ? (
                    <p className="p-4 text-center text-sm text-muted-foreground">
                      Nessun composito {typeCode}.
                    </p>
                  ) : (
                    composites.map((item) => (
                      <button
                        key={item.id}
                        type="button"
                        className={cn(
                          "flex w-full items-center gap-2 border-b px-3 py-2 text-left last:border-b-0 hover:bg-muted",
                          selectedParent?.id === item.id && "bg-muted"
                        )}
                        onClick={() => setSelectedParent(item)}
                      >
                        <span className="shrink-0 font-mono text-sm font-medium">
                          {item.codice}
                        </span>
                        <span className="truncate text-xs text-muted-foreground">
                          {item.descr || "—"}
                        </span>
                      </button>
                    ))
                  )}
                </div>
              </div>

              {/* Griglia inferiore: Articoli disponibili */}
              <div className="flex h-[26rem] flex-col overflow-hidden rounded-md border">
                <div className="flex flex-wrap items-center gap-2 border-b bg-muted/40 px-3 py-2">
                  <span
                    className="text-xs font-semibold tracking-wide"
                    style={{ color: ACCENT_ARTICOLI }}
                  >
                    ARTICOLI DISPONIBILI
                  </span>
                  <Select
                    value={childType}
                    onValueChange={setChildType}
                    disabled={effectiveSource === "catalog"}
                  >
                    <SelectTrigger size="sm" className="h-7 w-40">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={ALL}>Tutti</SelectItem>
                      {activeType.childPrefixes.map((prefix) => (
                        <SelectItem key={prefix} value={prefix}>
                          {PREFIX_LABELS[prefix] ?? `${prefix}xx`}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <Select
                    value={effectiveSource}
                    onValueChange={(value) => setSource(value as "codex" | "catalog")}
                    disabled={!allowCatalog}
                  >
                    <SelectTrigger size="sm" className="h-7 w-28">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="codex">Codex</SelectItem>
                      <SelectItem value="catalog">Catalogo</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="flex gap-2 border-b p-2">
                  <Input
                    value={availCode}
                    placeholder="Codice"
                    className="h-8 w-32"
                    onChange={(event) => setAvailCode(event.target.value)}
                  />
                  <Input
                    value={availDescr}
                    placeholder="Descrizione"
                    className="h-8 flex-1"
                    onChange={(event) => setAvailDescr(event.target.value)}
                  />
                </div>
                <div className="min-h-0 flex-1 overflow-y-auto">
                  {availableLoading ? (
                    <p className="p-4 text-center text-sm text-muted-foreground">Caricamento…</p>
                  ) : available.length === 0 ? (
                    <p className="p-4 text-center text-sm text-muted-foreground">
                      Nessun articolo.
                    </p>
                  ) : (
                    available.map((item) => (
                      <div
                        key={`${item.source}-${item.id}`}
                        draggable={canEdit}
                        onDragStart={(event) => {
                          dragItem.current = item
                          event.dataTransfer.effectAllowed = "copy"
                          event.dataTransfer.setData("text/plain", item.codice)
                        }}
                        onDragEnd={() => {
                          dragItem.current = null
                          setHoverKey(null)
                        }}
                        onDoubleClick={() => handleQuickAdd(item)}
                        title={
                          canEdit ? "Trascina sull'albero o doppio click per aggiungere" : undefined
                        }
                        className={cn(
                          "flex items-center gap-2 border-b px-3 py-2 last:border-b-0 hover:bg-muted",
                          canEdit && "cursor-grab active:cursor-grabbing"
                        )}
                      >
                        <span className="shrink-0 font-mono text-sm font-medium">
                          {item.codice}
                        </span>
                        <span className="truncate text-xs text-muted-foreground">
                          {item.descr || "—"}
                        </span>
                      </div>
                    ))
                  )}
                </div>
                {available.length > 0 ? (
                  <div className="border-t px-3 py-1 text-[11px] text-muted-foreground">
                    {availableTotal > available.length
                      ? `Primi ${available.length} di ${availableTotal} — affina la ricerca`
                      : `${available.length} articoli`}
                  </div>
                ) : null}
              </div>
            </div>

            {/* ── DESTRA: Composizione (albero) ── */}
            <div className="flex h-[26rem] flex-col overflow-hidden rounded-md border lg:h-[53rem]">
              <div className="flex items-center gap-2 border-b bg-muted/40 px-3 py-2">
                <span
                  className="text-xs font-semibold tracking-wide"
                  style={{ color: ACCENT_COMPOSIZIONE }}
                >
                  COMPOSIZIONE
                  {selectedParent ? ` — ${selectedParent.codice}` : ""}
                </span>
              </div>
              <div
                onDragOver={rootDragOver}
                onDrop={rootDrop}
                className={cn(
                  "min-h-0 flex-1 overflow-y-auto p-2",
                  hoverKey === "root" && "ring-2 ring-inset ring-primary"
                )}
              >
                {!selectedParent ? (
                  <div className="flex h-full items-center justify-center text-center text-sm text-muted-foreground">
                    Seleziona un composito a sinistra per vederne la distinta.
                  </div>
                ) : treeQuery.isLoading ? (
                  <p className="p-4 text-sm text-muted-foreground">Caricamento…</p>
                ) : treeQuery.isError ? (
                  <p className="p-4 text-sm text-destructive">
                    {(treeQuery.error as Error).message}
                  </p>
                ) : treeData ? (
                  <>
                    <NodeRow node={treeData} depth={0} isRoot ctx={ctx} />
                    {treeData.children.length === 0 ? (
                      <p className="px-2 py-4 text-sm text-muted-foreground">
                        Nessun componente.{" "}
                        {canEdit ? "Trascina qui un articolo disponibile." : ""}
                      </p>
                    ) : null}
                  </>
                ) : null}
              </div>
            </div>
          </div>

          {/* ── STATUS BAR ── */}
          <div className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t pt-2 text-xs">
            <span className="text-muted-foreground">{statusText}</span>
            <span className="italic text-muted-foreground/80">
              Filtri: abc = contiene · abc* = inizia con · *abc = finisce con —
              Trascina gli articoli disponibili sull'albero
            </span>
          </div>
        </CardContent>
      </Card>
      <CodexImportDialog
        file={importFile}
        connRef={connectionIdRef}
        onSuccess={(parent) => {
          setImportFile(null)
          // Mostra subito la distinta importata: seleziona il composito, cambiando
          // tipo (501/601/701) se il file era di un tipo diverso da quello attivo.
          const prefix = parent.codice.replace(/\./g, "").slice(0, 3)
          if (prefix !== typeCode && COMPOSITION_TYPES.some((type) => type.code === prefix)) {
            pendingSelectRef.current = parent
            setTypeCode(prefix)
          } else {
            setSelectedParent(parent)
          }
          void queryClient.invalidateQueries({ queryKey: ["composition-tree"] })
        }}
        onCancel={() => setImportFile(null)}
      />

      <QuantityDialog
        open={pendingAdd != null}
        childCodice={pendingAdd?.child.codice ?? ""}
        onCancel={() => setPendingAdd(null)}
        onConfirm={(quantity) => {
          // Chiude subito il dialog (come il modale WPF) e poi aggiunge: niente
          // doppio inserimento se l'utente preme Invio due volte.
          const add = pendingAdd
          setPendingAdd(null)
          if (add) {
            addMutation.mutate({ parentId: add.parentId, child: add.child, quantity })
          }
        }}
      />

      <QuantityDialog
        open={editQty != null}
        mode="edit"
        childCodice={editQty?.codice ?? ""}
        initialQuantity={editQty?.quantity ?? 1}
        onCancel={() => setEditQty(null)}
        onConfirm={(quantity) => {
          const node = editQty
          setEditQty(null)
          if (node && quantity !== node.quantity) {
            quantityMutation.mutate({ compositionId: node.compositionId, quantity })
          }
        }}
      />
    </div>
  )
}
