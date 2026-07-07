import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  ChevronDown,
  ChevronRight,
  Copy,
  FilterX,
  FolderPlus,
  Pencil,
  Plus,
  RefreshCw,
  Trash2,
} from "lucide-react"

import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { RowActionsMenu } from "@/components/shared/row-actions"
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
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
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
  createCategory,
  deleteCategory,
  deleteGroup,
  deleteProduct,
  duplicateProduct,
  fetchCatalogTree,
  fetchPriceLists,
  fetchProduct,
  fetchProducts,
  moveCategory,
  moveProduct,
} from "@/lib/api/quote-catalog"
import type {
  QuoteCategoryDto,
  QuoteGroupDto,
  QuoteProductDto,
} from "@/lib/api/types"
import { useDebounced } from "@/lib/use-debounced"
import { cn } from "@/lib/utils"

import { QuoteCategoryDialog } from "./QuoteCategoryDialog"
import { QuoteGroupDialog } from "./QuoteGroupDialog"
import { QuoteProductDialog } from "./QuoteProductDialog"

// ── Helper ─────────────────────────────────────────────────

const ALL_PRICE_LISTS = "__all__"

/** Colonne griglia prodotti (per colspan righe varianti / vuoto). */
const PRODUCT_TABLE_COL_COUNT = 9

/** Ordinamento naturale ("IRB 120" prima di "IRB 1200"), come il WPF. */
function naturalCompare(a: string, b: string): number {
  return a.localeCompare(b, "it", { numeric: true, sensitivity: "base" })
}

/** Match jolly: abc=contiene, abc*=inizia, *abc=finisce, *abc*=contiene. */
function matchWildcard(value: string | undefined, filter: string): boolean {
  const f = filter.trim().toLowerCase()
  if (!f) return true
  const v = (value ?? "").toLowerCase()
  const startsWild = f.startsWith("*")
  const endsWild = f.endsWith("*")
  if (startsWild && endsWild) return v.includes(f.replace(/^\*+|\*+$/g, ""))
  if (endsWild) return v.startsWith(f.replace(/\*+$/g, ""))
  if (startsWild) return v.endsWith(f.replace(/^\*+/g, ""))
  return v.includes(f)
}

function fmt2(value: number): string {
  return value.toLocaleString("it-IT", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}

// ── Modello albero ─────────────────────────────────────────

type NodeKind = "pricelist" | "group" | "category" | "product"

interface UINode {
  key: string
  kind: NodeKind
  id: number
  name: string
  count: number
  /** Categoria: id del proprio gruppo. */
  groupId?: number
  /** Contesto categoria per «Nuovo prodotto»: categoria = proprio id, prodotto = id categoria padre. */
  categoryId?: number
  children: UINode[]
  expandable: boolean
  group?: QuoteGroupDto
  category?: QuoteCategoryDto
}

function buildCategoryNode(cat: QuoteCategoryDto): UINode {
  const childCats = [...(cat.children ?? [])]
    .sort((a, b) => naturalCompare(a.name, b.name))
    .map(buildCategoryNode)
  const productNodes: UINode[] = [...(cat.products ?? [])]
    .sort((a, b) => naturalCompare(a.name, b.name))
    .map((prod) => ({
      key: `product:${prod.id}`,
      kind: "product" as const,
      id: prod.id,
      name: prod.name,
      count: 0,
      categoryId: cat.id,
      children: [],
      expandable: false,
    }))
  const children = [...childCats, ...productNodes]
  return {
    key: `category:${cat.id}`,
    kind: "category",
    id: cat.id,
    name: cat.name,
    count: cat.productCount,
    groupId: cat.groupId,
    categoryId: cat.id,
    children,
    expandable: children.length > 0,
    category: cat,
  }
}

function buildGroupNode(group: QuoteGroupDto): UINode {
  const children = [...(group.categories ?? [])]
    .sort((a, b) => naturalCompare(a.name, b.name))
    .map(buildCategoryNode)
  return {
    key: `group:${group.id}`,
    kind: "group",
    id: group.id,
    name: group.name,
    count: group.productCount,
    children,
    expandable: true,
    group,
  }
}

function buildRoots(groups: QuoteGroupDto[], showAll: boolean): UINode[] {
  const sortedGroups = [...groups].sort(
    (a, b) => a.sortOrder - b.sortOrder || naturalCompare(a.name, b.name)
  )
  if (!showAll) {
    return sortedGroups.map(buildGroupNode)
  }
  // Raggruppa per listino (i gruppi senza listino → «Senza listino»).
  const byList = new Map<number, { name: string; groups: QuoteGroupDto[] }>()
  for (const group of sortedGroups) {
    const id = group.priceListId ?? 0
    const name = group.priceListName || "Senza listino"
    const entry = byList.get(id) ?? { name, groups: [] }
    entry.groups.push(group)
    byList.set(id, entry)
  }
  return [...byList.entries()]
    .sort((a, b) => naturalCompare(a[1].name, b[1].name))
    .map(([id, entry]) => ({
      key: `pricelist:${id}`,
      kind: "pricelist" as const,
      id,
      name: entry.name,
      count: entry.groups.reduce((acc, g) => acc + g.productCount, 0),
      children: entry.groups.map(buildGroupNode),
      expandable: true,
    }))
}

// Ricerca: calcola chiavi visibili + chiavi da espandere (rami con discendenti che matchano).
function computeSearch(
  roots: UINode[],
  terms: string[]
): { visible: Set<string>; open: Set<string> } {
  const visible = new Set<string>()
  const open = new Set<string>()
  const walk = (node: UINode): boolean => {
    const selfMatch = terms.every((t) => node.name.toLowerCase().includes(t))
    let anyChild = false
    for (const child of node.children) {
      if (walk(child)) anyChild = true
    }
    const isVisible = selfMatch || anyChild
    if (isVisible) visible.add(node.key)
    if (anyChild) open.add(node.key)
    return isVisible
  }
  roots.forEach(walk)
  return { visible, open }
}

interface Selection {
  kind: NodeKind
  id: number
  name: string
  categoryId?: number
}

// ── Tabella prodotti: view model ───────────────────────────

interface VariantView {
  code: string
  name: string
  cost: number
  markup: number
  sell: number
}

interface ProductView {
  id: number
  itemType: string
  code: string
  name: string
  autoInclude: boolean
  variantCount: number
  priceRange: string
  costRange: string
  variants: VariantView[]
}

function toProductView(p: QuoteProductDto): ProductView {
  const variants: VariantView[] = p.variants.map((v) => {
    const markup = v.markupValue > 0 ? v.markupValue : 1.3
    return { code: v.code, name: v.name, cost: v.costPrice, markup, sell: v.costPrice * markup }
  })
  let priceRange = "—"
  let costRange = "—"
  if (p.itemType !== "content" && variants.length > 0) {
    const sells = variants.map((v) => v.sell)
    const costs = variants.map((v) => v.cost)
    if (variants.length === 1) {
      priceRange = `${fmt2(sells[0])}€`
      costRange = `${fmt2(costs[0])}€`
    } else {
      const minP = Math.min(...sells)
      const maxP = Math.max(...sells)
      const minC = Math.min(...costs)
      const maxC = Math.max(...costs)
      priceRange = minP === maxP ? `${fmt2(minP)}€` : `${fmt2(minP)}€ – ${fmt2(maxP)}€`
      costRange = minC === maxC ? `${fmt2(minC)}€` : `${fmt2(minC)}€ – ${fmt2(maxC)}€`
    }
  }
  return {
    id: p.id,
    itemType: p.itemType,
    code: p.code,
    name: p.name,
    autoInclude: p.autoInclude,
    variantCount: p.variants.length,
    priceRange,
    costRange,
    variants,
  }
}

// ── Nodo albero (ricorsivo) ────────────────────────────────

interface DragItem {
  kind: "category" | "product"
  id: number
}

interface TreeCtx {
  expanded: Record<string, string>
  toggle: (parentKey: string, node: UINode) => void
  searching: boolean
  visible: Set<string>
  openKeys: Set<string>
  selectedKey: string | null
  onSelect: (node: UINode) => void
  onEditGroup: (group: QuoteGroupDto) => void
  onDeleteGroup: (node: UINode) => void
  onAddSub: (node: UINode) => void
  onEditCategory: (cat: QuoteCategoryDto) => void
  onDeleteCategory: (node: UINode) => void
  dragRef: React.MutableRefObject<DragItem | null>
  hoverKey: string | null
  setHoverKey: (key: string | null) => void
  onDropOnCategory: (target: UINode, dragged: DragItem) => void
  onDropOnGroup: (target: UINode, dragged: DragItem) => void
}

function NodeRow({
  node,
  parentKey,
  depth,
  ctx,
}: {
  node: UINode
  parentKey: string
  depth: number
  ctx: TreeCtx
}) {
  const isOpen = ctx.searching
    ? ctx.openKeys.has(node.key)
    : ctx.expanded[parentKey] === node.key
  const draggable = node.kind === "category" || node.kind === "product"
  const isDropTarget = node.kind === "category" || node.kind === "group"

  const row = (
    <div
      role="button"
      tabIndex={0}
      draggable={draggable}
      onDragStart={(event) => {
        if (!draggable) return
        ctx.dragRef.current = { kind: node.kind as "category" | "product", id: node.id }
        event.dataTransfer.effectAllowed = "move"
        event.dataTransfer.setData("text/plain", node.name)
      }}
      onDragEnd={() => {
        ctx.dragRef.current = null
        ctx.setHoverKey(null)
      }}
      onDragOver={(event) => {
        if (!isDropTarget) return
        const item = ctx.dragRef.current
        if (!item || (item.kind === "category" && item.id === node.id)) return
        if (node.kind === "group" && item.kind === "product") return
        event.preventDefault()
        event.stopPropagation()
        ctx.setHoverKey(node.key)
      }}
      onDrop={(event) => {
        if (!isDropTarget) return
        event.preventDefault()
        event.stopPropagation()
        const item = ctx.dragRef.current
        ctx.setHoverKey(null)
        if (!item) return
        if (node.kind === "category") ctx.onDropOnCategory(node, item)
        else if (node.kind === "group") ctx.onDropOnGroup(node, item)
      }}
      onClick={() => ctx.onSelect(node)}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault()
          ctx.onSelect(node)
        }
      }}
      className={cn(
        "flex cursor-pointer items-center gap-1 rounded px-1.5 py-1 text-sm hover:bg-muted",
        ctx.selectedKey === node.key && "bg-muted",
        ctx.hoverKey === node.key && "ring-2 ring-primary ring-offset-1"
      )}
      style={{ paddingLeft: depth * 14 + 6 }}
    >
      {node.expandable ? (
        <button
          type="button"
          className="shrink-0 text-muted-foreground"
          onClick={(event) => {
            event.stopPropagation()
            ctx.toggle(parentKey, node)
          }}
        >
          {isOpen ? (
            <ChevronDown className="size-3.5" />
          ) : (
            <ChevronRight className="size-3.5" />
          )}
        </button>
      ) : (
        <span className="inline-block w-3.5 shrink-0" />
      )}
      <span
        className={cn(
          "truncate",
          node.kind === "pricelist" && "font-bold text-[#4F6EF7]",
          node.kind === "group" && "font-semibold",
          node.kind === "category" && "font-medium",
          node.kind === "product" && "text-muted-foreground"
        )}
      >
        {node.name}
      </span>
      {node.count > 0 ? (
        <span className="shrink-0 text-xs text-muted-foreground">({node.count})</span>
      ) : null}
    </div>
  )

  // Context menu su gruppi e categorie (come il WPF).
  let head = row
  if (node.kind === "group") {
    head = (
      <ContextMenu>
        <ContextMenuTrigger asChild>{row}</ContextMenuTrigger>
        <ContextMenuContent>
          <ContextMenuItem onSelect={() => node.group && ctx.onEditGroup(node.group)}>
            Modifica gruppo
          </ContextMenuItem>
          <ContextMenuItem
            variant="destructive"
            onSelect={() => ctx.onDeleteGroup(node)}
          >
            Elimina gruppo
          </ContextMenuItem>
        </ContextMenuContent>
      </ContextMenu>
    )
  } else if (node.kind === "category") {
    head = (
      <ContextMenu>
        <ContextMenuTrigger asChild>{row}</ContextMenuTrigger>
        <ContextMenuContent>
          <ContextMenuItem onSelect={() => ctx.onAddSub(node)}>
            + Sotto-categoria
          </ContextMenuItem>
          <ContextMenuSeparator />
          <ContextMenuItem
            onSelect={() => node.category && ctx.onEditCategory(node.category)}
          >
            Modifica categoria
          </ContextMenuItem>
          <ContextMenuItem
            variant="destructive"
            onSelect={() => ctx.onDeleteCategory(node)}
          >
            Elimina categoria
          </ContextMenuItem>
        </ContextMenuContent>
      </ContextMenu>
    )
  }

  return (
    <div>
      {head}
      {isOpen
        ? node.children
            .filter((child) => !ctx.searching || ctx.visible.has(child.key))
            .map((child) => (
              <NodeRow
                key={child.key}
                node={child}
                parentKey={node.key}
                depth={depth + 1}
                ctx={ctx}
              />
            ))
        : null}
    </div>
  )
}

// ── Badge tipo ─────────────────────────────────────────────

function TypeBadge({ itemType }: { itemType: string }) {
  const isContent = itemType === "content"
  return (
    <span
      className="inline-block rounded px-1.5 py-0.5 text-[11px] font-semibold text-white"
      style={{ backgroundColor: isContent ? "#6B7280" : "#2563EB" }}
    >
      {isContent ? "Cont." : "Prod."}
    </span>
  )
}

// ── PAGINA ─────────────────────────────────────────────────

export function CatalogoPreventiviPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const [priceListId, setPriceListId] = React.useState<string>(ALL_PRICE_LISTS)
  const [selected, setSelected] = React.useState<Selection | null>(null)
  const [expanded, setExpanded] = React.useState<Record<string, string>>({})
  const [treeSearch, setTreeSearch] = React.useState("")
  const debTreeSearch = useDebounced(treeSearch.trim().toLowerCase(), 250)

  // Filtri colonna prodotti (jolly).
  const [fCode, setFCode] = React.useState("")
  const [fName, setFName] = React.useState("")
  const debFCode = useDebounced(fCode, 300)
  const debFName = useDebounced(fName, 300)

  const [expandedProducts, setExpandedProducts] = React.useState<Set<number>>(new Set())

  // Dialoghi.
  const [groupDialog, setGroupDialog] = React.useState<{ open: boolean; group: QuoteGroupDto | null }>(
    { open: false, group: null }
  )
  const [categoryDialog, setCategoryDialog] = React.useState<{
    open: boolean
    category: QuoteCategoryDto | null
    preselectedGroupId: number | null
  }>({ open: false, category: null, preselectedGroupId: null })
  const [productDialog, setProductDialog] = React.useState<{
    open: boolean
    productId: number | null
    categoryId: number
  }>({ open: false, productId: null, categoryId: 0 })
  const [subCat, setSubCat] = React.useState<{
    groupId: number
    parentId: number
    parentName: string
    name: string
  } | null>(null)

  const dragRef = React.useRef<DragItem | null>(null)
  const [hoverKey, setHoverKey] = React.useState<string | null>(null)

  const selectedPriceListId =
    priceListId === ALL_PRICE_LISTS ? undefined : Number.parseInt(priceListId, 10)

  const priceListsQuery = useQuery({
    queryKey: ["quote-price-lists"],
    queryFn: fetchPriceLists,
  })

  const treeQuery = useQuery({
    queryKey: ["quote-catalog-tree", selectedPriceListId ?? 0],
    queryFn: () => fetchCatalogTree(selectedPriceListId),
  })

  const productsQuery = useQuery({
    queryKey: ["quote-products", selected?.kind, selected?.id],
    enabled: selected != null,
    queryFn: async (): Promise<QuoteProductDto[]> => {
      if (!selected) return []
      if (selected.kind === "group") return fetchProducts({ groupId: selected.id })
      if (selected.kind === "category") return fetchProducts({ categoryId: selected.id })
      const product = await fetchProduct(selected.id)
      return [product]
    },
  })

  function invalidateTree() {
    void queryClient.invalidateQueries({ queryKey: ["quote-catalog-tree"] })
  }
  function invalidateProducts() {
    void queryClient.invalidateQueries({ queryKey: ["quote-products"] })
  }

  // Mutazioni.
  const deleteGroupMutation = useMutation({
    mutationFn: (id: number) => deleteGroup(id),
    onSuccess: () => {
      invalidateTree()
      setSelected(null)
    },
    onError: (err: Error) => notifyError(err),
  })
  const deleteCategoryMutation = useMutation({
    mutationFn: (id: number) => deleteCategory(id),
    onSuccess: () => {
      invalidateTree()
      setSelected(null)
    },
    onError: (err: Error) => notifyError(err),
  })
  const deleteProductMutation = useMutation({
    mutationFn: (id: number) => deleteProduct(id),
    onSuccess: () => {
      invalidateTree()
      invalidateProducts()
    },
    onError: (err: Error) => notifyError(err),
  })
  const duplicateProductMutation = useMutation({
    mutationFn: (id: number) => duplicateProduct(id),
    onSuccess: () => {
      invalidateTree()
      invalidateProducts()
    },
    onError: (err: Error) => notifyError(err),
  })
  const moveCategoryMutation = useMutation({
    mutationFn: (vars: { id: number; newParentId: number | null; newGroupId: number }) =>
      moveCategory(vars.id, { newParentId: vars.newParentId, newGroupId: vars.newGroupId }),
    onSuccess: invalidateTree,
    onError: (err: Error) => notifyError(err),
  })
  const moveProductMutation = useMutation({
    mutationFn: (vars: { id: number; categoryId: number }) =>
      moveProduct(vars.id, { categoryId: vars.categoryId }),
    onSuccess: () => {
      invalidateTree()
      invalidateProducts()
    },
    onError: (err: Error) => notifyError(err),
  })
  const createSubMutation = useMutation({
    mutationFn: (vars: { groupId: number; parentId: number; name: string }) =>
      createCategory({
        groupId: vars.groupId,
        parentId: vars.parentId,
        name: vars.name,
        description: "",
        sortOrder: 0,
        isActive: true,
      }),
    onSuccess: () => {
      invalidateTree()
      setSubCat(null)
    },
    onError: (err: Error) => notifyError(err),
  })

  // Albero.
  const tree = treeQuery.data
  const groups = React.useMemo(() => tree?.groups ?? [], [tree])
  const roots = React.useMemo(
    () => buildRoots(groups, selectedPriceListId === undefined),
    [groups, selectedPriceListId]
  )
  const terms = React.useMemo(
    () => (debTreeSearch ? debTreeSearch.split(/\s+/).filter(Boolean) : []),
    [debTreeSearch]
  )
  const searching = terms.length > 0
  const { visible, open: openKeys } = React.useMemo(
    () => computeSearch(roots, terms),
    [roots, terms]
  )

  function toggle(parentKey: string, node: UINode) {
    setExpanded((prev) => {
      const next = { ...prev }
      if (next[parentKey] === node.key) delete next[parentKey]
      else next[parentKey] = node.key
      return next
    })
  }

  function selectNode(node: UINode) {
    if (node.kind === "pricelist") return
    setSelected({
      kind: node.kind,
      id: node.id,
      name: node.name,
      categoryId: node.categoryId,
    })
  }

  // Azioni gruppi/categorie.
  function handleDeleteGroup(node: UINode) {
    void confirm({
      title: "Elimina gruppo",
      description: `Eliminare il gruppo "${node.name}" e tutte le sue categorie/prodotti?`,
      confirmLabel: "Elimina",
      destructive: true,
    }).then((ok) => {
      if (ok) deleteGroupMutation.mutate(node.id)
    })
  }
  function handleDeleteCategory(node: UINode) {
    void confirm({
      title: "Elimina categoria",
      description: `Eliminare la categoria "${node.name}" e tutti i suoi prodotti?`,
      confirmLabel: "Elimina",
      destructive: true,
    }).then((ok) => {
      if (ok) deleteCategoryMutation.mutate(node.id)
    })
  }
  function handleAddSub(node: UINode) {
    if (node.groupId == null) return
    setSubCat({ groupId: node.groupId, parentId: node.id, parentName: node.name, name: "" })
  }

  function handleDropOnCategory(target: UINode, dragged: DragItem) {
    if (dragged.kind === "product") {
      moveProductMutation.mutate({ id: dragged.id, categoryId: target.id })
    } else if (dragged.kind === "category" && dragged.id !== target.id && target.groupId != null) {
      moveCategoryMutation.mutate({
        id: dragged.id,
        newParentId: target.id,
        newGroupId: target.groupId,
      })
    }
  }
  function handleDropOnGroup(target: UINode, dragged: DragItem) {
    if (dragged.kind === "category") {
      moveCategoryMutation.mutate({ id: dragged.id, newParentId: null, newGroupId: target.id })
    }
  }

  // Azioni prodotti.
  function handleNewProduct() {
    if (selected?.categoryId == null) {
      notifyError("Seleziona prima una categoria.")
      return
    }
    setProductDialog({ open: true, productId: null, categoryId: selected.categoryId })
  }
  function handleEditProduct(id: number) {
    const categoryId = selected?.categoryId ?? 0
    setProductDialog({ open: true, productId: id, categoryId })
  }
  function handleDeleteProduct(view: ProductView) {
    void confirm({
      title: "Elimina prodotto",
      description: `Eliminare "${view.name}"?`,
      confirmLabel: "Elimina",
      destructive: true,
    }).then((ok) => {
      if (ok) deleteProductMutation.mutate(view.id)
    })
  }

  // Lista prodotti filtrata.
  const productViews = React.useMemo(() => {
    const items = (productsQuery.data ?? []).map(toProductView)
    items.sort((a, b) => naturalCompare(a.name, b.name))
    return items
  }, [productsQuery.data])
  const filteredProducts = React.useMemo(
    () =>
      productViews.filter(
        (p) => matchWildcard(p.code, debFCode) && matchWildcard(p.name, debFName)
      ),
    [productViews, debFCode, debFName]
  )

  const hasProductColumnFilters = !!(debFCode || debFName)

  function clearProductColumnFilters() {
    setFCode("")
    setFName("")
  }

  const priceLists = priceListsQuery.data ?? []
  const showNewProduct = selected?.kind === "category" || selected?.kind === "product"

  const ctx: TreeCtx = {
    expanded,
    toggle,
    searching,
    visible,
    openKeys,
    selectedKey: selected ? `${selected.kind}:${selected.id}` : null,
    onSelect: selectNode,
    onEditGroup: (group) => setGroupDialog({ open: true, group }),
    onDeleteGroup: handleDeleteGroup,
    onAddSub: handleAddSub,
    onEditCategory: (category) =>
      setCategoryDialog({ open: true, category, preselectedGroupId: category.groupId }),
    onDeleteCategory: handleDeleteCategory,
    dragRef,
    hoverKey,
    setHoverKey,
    onDropOnCategory: handleDropOnCategory,
    onDropOnGroup: handleDropOnGroup,
  }

  const visibleRoots = roots.filter((node) => !searching || visible.has(node.key))

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>Catalogo Preventivi</CardTitle>
          <CardDescription>
            Listini → gruppi → categorie → prodotti → varianti. Trascina categorie e
            prodotti nell'albero per spostarli.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="grid gap-4 lg:grid-cols-[320px_minmax(0,1fr)]">
            {/* ── SINISTRA: albero ── */}
            <div className="flex h-[76vh] flex-col overflow-hidden rounded-md border">
              <div className="flex items-center gap-2 border-b bg-muted/40 p-2">
                <span className="text-xs font-semibold text-[#4F6EF7]">LISTINO</span>
                <Select value={priceListId} onValueChange={(value) => {
                  setPriceListId(value)
                  setSelected(null)
                }}>
                  <SelectTrigger size="sm" className="h-8 flex-1">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={ALL_PRICE_LISTS}>Tutti i listini</SelectItem>
                    {priceLists.map((pl) => (
                      <SelectItem key={pl.id} value={String(pl.id)}>
                        {pl.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="flex items-center gap-2 border-b p-2">
                <Button
                  size="sm"
                  onClick={() => setGroupDialog({ open: true, group: null })}
                >
                  <Plus className="size-4" />
                  Gruppo
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    const gid =
                      selected?.kind === "category"
                        ? groups.find((g) => g.categories.some((c) => c.id === selected.id))?.id
                        : selected?.kind === "group"
                          ? selected.id
                          : null
                    setCategoryDialog({
                      open: true,
                      category: null,
                      preselectedGroupId: gid ?? null,
                    })
                  }}
                >
                  <FolderPlus className="size-4" />
                  Categoria
                </Button>
                <Button
                  size="icon-sm"
                  variant="ghost"
                  className="ml-auto"
                  title="Aggiorna"
                  onClick={() => void treeQuery.refetch()}
                >
                  <RefreshCw className={treeQuery.isFetching ? "animate-spin" : ""} />
                </Button>
              </div>
              <div className="border-b p-2">
                <Input
                  value={treeSearch}
                  placeholder="Cerca gruppo o categoria…"
                  className="h-8"
                  onChange={(event) => setTreeSearch(event.target.value)}
                />
              </div>
              <div className="min-h-0 flex-1 overflow-auto p-1">
                {treeQuery.isLoading ? (
                  <p className="p-3 text-sm text-muted-foreground">Caricamento…</p>
                ) : visibleRoots.length === 0 ? (
                  <p className="p-3 text-sm text-muted-foreground">Nessun gruppo.</p>
                ) : (
                  visibleRoots.map((node) => (
                    <NodeRow key={node.key} node={node} parentKey="root" depth={0} ctx={ctx} />
                  ))
                )}
              </div>
              <div className="border-t px-3 py-1.5 text-[11px] text-muted-foreground">
                {tree
                  ? `${tree.totalGroups} gruppi · ${tree.totalCategories} categorie · ${tree.totalProducts} prodotti`
                  : "—"}
              </div>
            </div>

            {/* ── DESTRA: prodotti ── */}
            <div className="flex h-[76vh] flex-col overflow-hidden rounded-md border">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b p-3">
                <h2 className="min-w-0 flex-1 truncate text-lg font-bold">
                  {selected?.name ?? "Seleziona un gruppo o categoria"}
                </h2>
                <div className="flex flex-wrap items-center gap-2">
                  {hasProductColumnFilters ? (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={clearProductColumnFilters}
                    >
                      <FilterX />
                      Pulisci filtri
                    </Button>
                  ) : null}
                  {showNewProduct ? (
                    <Button size="sm" onClick={handleNewProduct}>
                      <Plus className="size-4" />
                      Nuovo prodotto
                    </Button>
                  ) : null}
                </div>
              </div>
              {selected != null ? (
                <p className="border-b px-3 py-1.5 text-xs text-muted-foreground">
                  Ricerca colonne: <code className="font-mono">abc</code> contiene ·{" "}
                  <code className="font-mono">abc*</code> inizia con ·{" "}
                  <code className="font-mono">*abc</code> finisce con
                </p>
              ) : null}
              <div className="min-h-0 flex-1 overflow-auto">
                {selected == null ? (
                  <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                    Seleziona un gruppo o una categoria a sinistra.
                  </div>
                ) : productsQuery.isLoading ? (
                  <p className="p-4 text-sm text-muted-foreground">Caricamento prodotti…</p>
                ) : (
                  <Table>
                    <TableHeader className="sticky top-0 z-10 bg-muted/50">
                      <TableRow className="hover:bg-transparent">
                        <TableHead className="w-8" />
                        <TableHead className="w-16">Tipo</TableHead>
                        <TableHead className="w-36">Codice</TableHead>
                        <TableHead>Nome</TableHead>
                        <TableHead className="w-20 text-center">Varianti</TableHead>
                        <TableHead className="w-40 text-right">Prezzo cliente</TableHead>
                        <TableHead className="w-40 text-right">Costo aziendale</TableHead>
                        <TableHead className="w-14 text-center">Auto-incl.</TableHead>
                        <TableHead className="w-12 text-right">Azioni</TableHead>
                      </TableRow>
                      <TableRow className="hover:bg-transparent">
                        <TableHead className="h-auto px-2 py-2" />
                        <TableHead className="h-auto px-2 py-2" />
                        <TableHead className="h-auto px-2 py-2 align-middle">
                          <ColumnFilterInput
                            value={fCode}
                            onChange={setFCode}
                          />
                        </TableHead>
                        <TableHead className="h-auto px-2 py-2 align-middle">
                          <ColumnFilterInput
                            value={fName}
                            onChange={setFName}
                          />
                        </TableHead>
                        <TableHead className="h-auto px-2 py-2" />
                        <TableHead className="h-auto px-2 py-2" />
                        <TableHead className="h-auto px-2 py-2" />
                        <TableHead className="h-auto px-2 py-2" />
                        <TableHead className="h-auto px-2 py-2" />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {filteredProducts.map((p) => {
                        const isExpanded = expandedProducts.has(p.id)
                        return (
                          <React.Fragment key={p.id}>
                            <TableRow
                              className="cursor-pointer"
                              onDoubleClick={() => handleEditProduct(p.id)}
                            >
                              <TableCell>
                                <button
                                  type="button"
                                  className="text-muted-foreground"
                                  title="Mostra varianti"
                                  onClick={(event) => {
                                    event.stopPropagation()
                                    setExpandedProducts((prev) => {
                                      const next = new Set(prev)
                                      if (next.has(p.id)) next.delete(p.id)
                                      else next.add(p.id)
                                      return next
                                    })
                                  }}
                                >
                                  {isExpanded ? (
                                    <ChevronDown className="size-4" />
                                  ) : (
                                    <ChevronRight className="size-4" />
                                  )}
                                </button>
                              </TableCell>
                              <TableCell>
                                <TypeBadge itemType={p.itemType} />
                              </TableCell>
                              <TableCell className="font-mono text-xs">{p.code}</TableCell>
                              <TableCell className="font-semibold">{p.name}</TableCell>
                              <TableCell className="text-center">{p.variantCount}</TableCell>
                              <TableCell className="text-right font-semibold tabular-nums">
                                {p.priceRange}
                              </TableCell>
                              <TableCell className="text-right tabular-nums text-[#16A34A]">
                                {p.costRange}
                              </TableCell>
                              <TableCell className="text-center">
                                {p.autoInclude ? "✓" : ""}
                              </TableCell>
                              <TableCell className="text-right">
                                <div
                                  className="flex justify-end"
                                  onClick={(event) => event.stopPropagation()}
                                >
                                  <RowActionsMenu
                                    size="icon-sm"
                                    label={p.name}
                                    actions={[
                                      {
                                        label: "Modifica",
                                        icon: Pencil,
                                        onClick: () => handleEditProduct(p.id),
                                      },
                                      {
                                        label: "Duplica",
                                        icon: Copy,
                                        onClick: () => duplicateProductMutation.mutate(p.id),
                                      },
                                      {
                                        label: "Elimina",
                                        icon: Trash2,
                                        destructive: true,
                                        separatorBefore: true,
                                        onClick: () => handleDeleteProduct(p),
                                      },
                                    ]}
                                  />
                                </div>
                              </TableCell>
                            </TableRow>
                            {isExpanded ? (
                              <TableRow className="bg-muted/30 hover:bg-muted/30">
                                <TableCell colSpan={PRODUCT_TABLE_COL_COUNT} className="p-0">
                                  <div className="px-10 py-2">
                                    <p className="mb-1 text-xs font-semibold text-muted-foreground">
                                      VARIANTI ({p.variantCount}):
                                    </p>
                                    {p.variants.length === 0 ? (
                                      <p className="text-xs text-muted-foreground">Nessuna variante.</p>
                                    ) : (
                                      <div className="space-y-1">
                                        {p.variants.map((v, i) => (
                                          <div
                                            key={i}
                                            className="grid grid-cols-[90px_1fr_110px_70px_90px] items-center gap-2 rounded border bg-background px-2 py-1 text-xs"
                                          >
                                            <span className="text-muted-foreground">{v.code}</span>
                                            <span className="font-medium">{v.name}</span>
                                            <span className="text-right text-[#16A34A]">
                                              {fmt2(v.cost)}€
                                            </span>
                                            <span className="text-center text-muted-foreground">
                                              x{v.markup.toLocaleString("it-IT", {
                                                minimumFractionDigits: 3,
                                                maximumFractionDigits: 3,
                                              })}
                                            </span>
                                            <span className="text-right font-semibold">
                                              {fmt2(v.sell)}€
                                            </span>
                                          </div>
                                        ))}
                                      </div>
                                    )}
                                  </div>
                                </TableCell>
                              </TableRow>
                            ) : null}
                          </React.Fragment>
                        )
                      })}
                      {filteredProducts.length === 0 ? (
                        <TableRow>
                          <TableCell
                            colSpan={PRODUCT_TABLE_COL_COUNT}
                            className="h-24 text-center text-sm text-muted-foreground"
                          >
                            {hasProductColumnFilters
                              ? "Nessun prodotto corrisponde ai filtri impostati."
                              : "Nessun prodotto."}
                          </TableCell>
                        </TableRow>
                      ) : null}
                    </TableBody>
                  </Table>
                )}
              </div>
              <div className="flex items-center justify-between border-t px-3 py-1.5 text-[11px] text-muted-foreground">
                <span>
                  {selected
                    ? `${filteredProducts.length} prodotti su ${productViews.length}`
                    : "Pronto"}
                </span>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Dialoghi */}
      <QuoteGroupDialog
        open={groupDialog.open}
        group={groupDialog.group}
        onClose={() => setGroupDialog({ open: false, group: null })}
        onSaved={() => {
          setGroupDialog({ open: false, group: null })
          invalidateTree()
        }}
      />
      <QuoteCategoryDialog
        open={categoryDialog.open}
        groups={groups}
        category={categoryDialog.category}
        preselectedGroupId={categoryDialog.preselectedGroupId}
        onClose={() =>
          setCategoryDialog({ open: false, category: null, preselectedGroupId: null })
        }
        onSaved={() => {
          setCategoryDialog({ open: false, category: null, preselectedGroupId: null })
          invalidateTree()
        }}
      />
      <QuoteProductDialog
        open={productDialog.open}
        categoryId={productDialog.categoryId}
        productId={productDialog.productId}
        onClose={() => setProductDialog({ open: false, productId: null, categoryId: 0 })}
        onSaved={() => {
          setProductDialog({ open: false, productId: null, categoryId: 0 })
          invalidateTree()
          invalidateProducts()
        }}
      />

      {/* Nuova sotto-categoria (nome) — fedele all'InputBox del WPF */}
      <Dialog open={subCat != null} onOpenChange={(next) => !next && setSubCat(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Nuova sotto-categoria</DialogTitle>
          </DialogHeader>
          <div className="grid gap-2">
            <Label>Nome sotto-categoria di «{subCat?.parentName}»</Label>
            <Input
              value={subCat?.name ?? ""}
              autoFocus
              onChange={(event) =>
                setSubCat((prev) => (prev ? { ...prev, name: event.target.value } : prev))
              }
              onKeyDown={(event) => {
                if (event.key === "Enter" && subCat?.name.trim()) {
                  createSubMutation.mutate({
                    groupId: subCat.groupId,
                    parentId: subCat.parentId,
                    name: subCat.name.trim(),
                  })
                }
              }}
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setSubCat(null)}>
              Annulla
            </Button>
            <Button
              disabled={!subCat?.name.trim() || createSubMutation.isPending}
              onClick={() => {
                if (subCat?.name.trim()) {
                  createSubMutation.mutate({
                    groupId: subCat.groupId,
                    parentId: subCat.parentId,
                    name: subCat.name.trim(),
                  })
                }
              }}
            >
              {createSubMutation.isPending ? "Creazione…" : "Crea"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
