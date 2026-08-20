import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import { notifyError } from "@/lib/toast"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
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
  fetchCategories,
  fetchGroups,
  fetchProducts,
} from "@/lib/api/quote-catalog"
import { addQuoteProduct } from "@/lib/api/quotes"
import { useDebounced } from "@/lib/use-debounced"
import { euro } from "@/lib/format"

const ALL_GROUPS = "__all_groups__"
const ALL_CATS = "__all_cats__"

interface PickItem {
  productId: number
  itemType: string
  code: string
  name: string
  variantCount: number
  priceRange: string
}

/**
 * Picker catalogo per aggiungere prodotti al preventivo. Fedele a AddQuoteItemDialog del WPF:
 * combo gruppo/categoria + ricerca, doppio click aggiunge il prodotto con TUTTE le varianti
 * (POST /items/product/{id}); blocca i duplicati già presenti.
 */
export function AddQuoteItemDialog({
  open,
  quoteId,
  existingProductIds,
  onClose,
  onItemAdded,
}: {
  open: boolean
  quoteId: number
  existingProductIds: Set<number>
  onClose: () => void
  onItemAdded: () => void
}) {
  const [groupId, setGroupId] = React.useState<string>(ALL_GROUPS)
  const [categoryId, setCategoryId] = React.useState<string>(ALL_CATS)
  const [search, setSearch] = React.useState("")
  const [lastAdded, setLastAdded] = React.useState<string | null>(null)
  const debSearch = useDebounced(search.trim().toLowerCase(), 300)

  React.useEffect(() => {
    if (open) {
      setGroupId(ALL_GROUPS)
      setCategoryId(ALL_CATS)
      setSearch("")
      setLastAdded(null)
    }
  }, [open])

  const groupsQuery = useQuery({
    queryKey: ["quote-catalog-groups"],
    queryFn: () => fetchGroups(),
    enabled: open,
  })

  const numericGroup = groupId === ALL_GROUPS ? undefined : Number.parseInt(groupId, 10)
  const numericCat = categoryId === ALL_CATS ? undefined : Number.parseInt(categoryId, 10)

  const categoriesQuery = useQuery({
    queryKey: ["quote-catalog-cats", numericGroup],
    queryFn: () => fetchCategories(numericGroup),
    enabled: open && numericGroup !== undefined,
  })

  const productsQuery = useQuery({
    queryKey: ["quote-catalog-pick", numericGroup, numericCat],
    queryFn: () =>
      fetchProducts(
        numericCat !== undefined
          ? { categoryId: numericCat }
          : numericGroup !== undefined
            ? { groupId: numericGroup }
            : {}
      ),
    enabled: open,
  })

  const items = React.useMemo<PickItem[]>(() => {
    const products = productsQuery.data ?? []
    return products.map((p) => {
      let priceRange = ""
      if (p.variants.length > 0) {
        const sells = p.variants.map((v) => v.sellPrice)
        const min = Math.min(...sells)
        const max = Math.max(...sells)
        priceRange = min === max ? euro(min) : `${euro(min)} – ${euro(max)}`
      }
      return {
        productId: p.id,
        itemType: p.itemType,
        code: p.code,
        name: p.name,
        variantCount: p.variants.length,
        priceRange,
      }
    })
  }, [productsQuery.data])

  const filtered = React.useMemo(() => {
    if (!debSearch) return items
    return items.filter(
      (i) =>
        i.code.toLowerCase().includes(debSearch) ||
        i.name.toLowerCase().includes(debSearch)
    )
  }, [items, debSearch])

  const addMutation = useMutation({
    mutationFn: (item: PickItem) => addQuoteProduct(quoteId, item.productId),
    onSuccess: (_id, item) => {
      setLastAdded(
        item.variantCount > 0
          ? `✓ ${item.name} aggiunto con ${item.variantCount} varianti`
          : `✓ ${item.name} aggiunto`
      )
      onItemAdded()
    },
    onError: (err: Error) => notifyError(err),
  })

  function handleAdd(item: PickItem) {
    if (existingProductIds.has(item.productId)) {
      notifyError(`'${item.name}' è già presente nel preventivo.`)
      return
    }
    addMutation.mutate(item)
  }

  const groups = groupsQuery.data ?? []
  const categories = categoriesQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="flex max-h-[88vh] flex-col gap-3 overflow-hidden sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>Aggiungi prodotto dal catalogo</DialogTitle>
        </DialogHeader>

        <div className="flex flex-wrap items-center gap-2">
          <LookupCombobox
            options={groups.map((g) => ({ id: String(g.id), name: g.name }))}
            value={groupId === ALL_GROUPS ? null : groupId}
            onValueChange={(id) => {
              setGroupId(id ?? ALL_GROUPS)
              setCategoryId(ALL_CATS)
            }}
            placeholder="Tutti i gruppi"
            noneLabel="Tutti i gruppi"
            searchPlaceholder="Cerca gruppo…"
            emptyText="Nessun gruppo trovato"
            className="h-8 w-48"
          />
          <LookupCombobox
            options={categories.map((c) => ({ id: String(c.id), name: c.name }))}
            value={categoryId === ALL_CATS ? null : categoryId}
            onValueChange={(id) => setCategoryId(id ?? ALL_CATS)}
            disabled={numericGroup === undefined}
            placeholder="Tutte le categorie"
            noneLabel="Tutte le categorie"
            searchPlaceholder="Cerca categoria…"
            emptyText="Nessuna categoria trovata"
            className="h-8 w-48"
          />
          <Input
            value={search}
            placeholder="Cerca codice o nome…"
            className="h-8 flex-1"
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>

        <GridScroller fill className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-16">TIPO</TableHead>
                <TableHead className="w-32">CODICE</TableHead>
                <TableHead>NOME</TableHead>
                <TableHead className="w-20 text-center">VAR.</TableHead>
                <TableHead className="w-40 text-right">PREZZO</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {productsQuery.isLoading ? (
                <TableRow>
                  <TableCell colSpan={5} className="text-center text-sm text-muted-foreground">
                    Caricamento…
                  </TableCell>
                </TableRow>
              ) : filtered.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={5} className="text-center text-sm text-muted-foreground">
                    Nessuna voce.
                  </TableCell>
                </TableRow>
              ) : (
                filtered.map((item) => (
                  <TableRow
                    key={item.productId}
                    className="cursor-pointer"
                    onDoubleClick={() => handleAdd(item)}
                    title="Doppio click per aggiungere"
                  >
                    <TableCell>
                      <span
                        className="inline-block rounded px-1.5 py-0.5 text-[10px] font-semibold text-white"
                        style={{ backgroundColor: item.itemType === "content" ? "#7C3AED" : "#2563EB" }}
                      >
                        {item.itemType === "content" ? "Cont." : "Prod."}
                      </span>
                    </TableCell>
                    <TableCell className="font-mono text-xs">{item.code}</TableCell>
                    <TableCell className="font-medium">{item.name}</TableCell>
                    <TableCell className="text-center">{item.variantCount}</TableCell>
                    <TableCell className="text-right tabular-nums">{item.priceRange}</TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </GridScroller>

        <DialogFooter className="flex items-center sm:justify-between">
          <span className="text-xs text-muted-foreground">
            {lastAdded ?? `${filtered.length} voci disponibili — doppio click per aggiungere`}
          </span>
          <Button variant="outline" onClick={onClose}>
            Chiudi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
