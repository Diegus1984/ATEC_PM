import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { SupplierSearchCombobox } from "@/components/shared/supplier-search-combobox"
import {
  createCatalogItem,
  fetchCatalogItem,
  updateCatalogItem,
} from "@/lib/api/catalog"
import { fetchSuppliers } from "@/lib/api/suppliers"
import { parseDecimal } from "@/lib/format"

const EMPTY = {
  code: "",
  description: "",
  category: "",
  subcategory: "",
  unit: "PZ",
  unitCost: "0",
  listPrice: "0",
  supplierId: null as number | null,
  supplierCode: "",
  manufacturer: "",
  barcode: "",
  notes: "",
}

type FormState = typeof EMPTY

export function CatalogItemDialog({
  open,
  itemId,
  onClose,
  onSaved,
}: {
  open: boolean
  itemId: number | "new" | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const isNew = itemId === "new"
  const editId = typeof itemId === "number" ? itemId : null

  const [form, setForm] = React.useState<FormState>(EMPTY)
  const [error, setError] = React.useState<string | null>(null)

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  const suppliersQuery = useQuery({
    queryKey: ["suppliers"],
    queryFn: fetchSuppliers,
    enabled: open,
  })

  React.useEffect(() => {
    if (!open) {
      return
    }
    setError(null)

    if (isNew || editId == null) {
      setForm(EMPTY)
      return
    }

    let cancelled = false
    void fetchCatalogItem(editId)
      .then((data) => {
        if (cancelled) return
        setForm({
          code: data.code,
          description: data.description,
          category: data.category,
          subcategory: data.subcategory,
          unit: data.unit || "PZ",
          unitCost: String(data.unitCost ?? 0),
          listPrice: String(data.listPrice ?? 0),
          supplierId: data.supplierId,
          supplierCode: data.supplierCode,
          manufacturer: data.manufacturer,
          barcode: data.barcode,
          notes: data.notes,
        })
      })
      .catch((err: Error) => {
        if (!cancelled) setError(err.message)
      })
    return () => {
      cancelled = true
    }
  }, [open, isNew, editId])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!form.code.trim() || !form.description.trim()) {
        throw new Error("Codice e descrizione sono obbligatori.")
      }
      await (editId != null
        ? updateCatalogItem(editId, {
            id: editId,
            code: form.code.trim(),
            description: form.description.trim(),
            category: form.category.trim(),
            subcategory: form.subcategory.trim(),
            unit: form.unit.trim() || "PZ",
            unitCost: parseDecimal(form.unitCost),
            listPrice: parseDecimal(form.listPrice),
            supplierId: form.supplierId,
            supplierCode: form.supplierCode.trim(),
            manufacturer: form.manufacturer.trim(),
            barcode: form.barcode.trim(),
            notes: form.notes.trim(),
            isActive: true,
          })
        : createCatalogItem({
            id: 0,
            code: form.code.trim(),
            description: form.description.trim(),
            category: form.category.trim(),
            subcategory: form.subcategory.trim(),
            unit: form.unit.trim() || "PZ",
            unitCost: parseDecimal(form.unitCost),
            listPrice: parseDecimal(form.listPrice),
            supplierId: form.supplierId,
            supplierCode: form.supplierCode.trim(),
            manufacturer: form.manufacturer.trim(),
            barcode: form.barcode.trim(),
            notes: form.notes.trim(),
            isActive: true,
          }))
    },
    onSuccess: async () => {
      await onSaved()
    },
    onError: (err: Error) => setError(err.message),
  })

  const suppliers = suppliersQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>
            {isNew ? "Nuovo articolo" : "Modifica articolo"}
          </DialogTitle>
          <DialogDescription>
            Anagrafica articolo di catalogo, prezzi e fornitore.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid grid-cols-[1fr_2fr] gap-4">
            <div className="grid gap-2">
              <Label>Codice</Label>
              <Input
                value={form.code}
                autoFocus
                onChange={(event) => set("code", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Descrizione</Label>
              <Input
                value={form.description}
                onChange={(event) => set("description", event.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-3 gap-4">
            <div className="grid gap-2">
              <Label>Categoria</Label>
              <Input
                value={form.category}
                onChange={(event) => set("category", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Sottocategoria</Label>
              <Input
                value={form.subcategory}
                onChange={(event) => set("subcategory", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>UdM</Label>
              <Input
                value={form.unit}
                onChange={(event) => set("unit", event.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Costo acquisto (€)</Label>
              <Input
                inputMode="decimal"
                value={form.unitCost}
                onChange={(event) => set("unitCost", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Prezzo listino (€)</Label>
              <Input
                inputMode="decimal"
                value={form.listPrice}
                onChange={(event) => set("listPrice", event.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Fornitore</Label>
              <SupplierSearchCombobox
                suppliers={suppliers}
                value={
                  suppliers.find((s) => s.id === form.supplierId)?.companyName ?? ""
                }
                onValueChange={(_, supplier) =>
                  set("supplierId", supplier ? supplier.id : null)
                }
              />
            </div>
            <div className="grid gap-2">
              <Label>Codice fornitore</Label>
              <Input
                value={form.supplierCode}
                onChange={(event) => set("supplierCode", event.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Produttore</Label>
              <Input
                value={form.manufacturer}
                onChange={(event) => set("manufacturer", event.target.value)}
              />
            </div>
            <div className="grid gap-2">
              <Label>Barcode</Label>
              <Input
                value={form.barcode}
                onChange={(event) => set("barcode", event.target.value)}
              />
            </div>
          </div>

          <div className="grid gap-2">
            <Label>Note</Label>
            <Textarea
              value={form.notes}
              rows={3}
              onChange={(event) => set("notes", event.target.value)}
            />
          </div>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={
              !form.code.trim() ||
              !form.description.trim() ||
              saveMutation.isPending
            }
          >
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
