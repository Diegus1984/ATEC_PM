import * as React from "react"

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

export interface LocalVariantValues {
  code: string
  name: string
  unit: string
  quantity: number
  sellPrice: number
  costPrice: number
}

function parseDecimal(value: string): number {
  const n = Number((value ?? "").replace(",", "."))
  return Number.isFinite(n) ? n : 0
}

/** Aggiunge una variante locale a un prodotto del preventivo. Fedele a AddLocalVariantDialog del WPF. */
export function AddLocalVariantDialog({
  open,
  productName,
  onClose,
  onAdd,
}: {
  open: boolean
  productName: string
  onClose: () => void
  onAdd: (values: LocalVariantValues) => void
}) {
  const [name, setName] = React.useState("")
  const [code, setCode] = React.useState("")
  const [unit, setUnit] = React.useState("nr.")
  const [price, setPrice] = React.useState("0")
  const [cost, setCost] = React.useState("0")
  const [qty, setQty] = React.useState("1")
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (open) {
      setName("")
      setCode("")
      setUnit("nr.")
      setPrice("0")
      setCost("0")
      setQty("1")
      setError(null)
    }
  }, [open])

  function confirm() {
    if (!name.trim()) {
      setError("Il nome è obbligatorio.")
      return
    }
    const q = parseDecimal(qty)
    onAdd({
      code: code.trim(),
      name: name.trim(),
      unit: unit.trim() || "nr.",
      quantity: q > 0 ? q : 1,
      sellPrice: parseDecimal(price),
      costPrice: parseDecimal(cost),
    })
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Aggiungi variante locale</DialogTitle>
          <DialogDescription>Nuova variante per: {productName}</DialogDescription>
        </DialogHeader>

        <div className="space-y-3">
          <div className="grid gap-2">
            <Label>Nome variante *</Label>
            <Input value={name} autoFocus onChange={(e) => setName(e.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="grid gap-2">
              <Label>Codice</Label>
              <Input value={code} onChange={(e) => setCode(e.target.value)} />
            </div>
            <div className="grid gap-2">
              <Label>UdM</Label>
              <Input value={unit} onChange={(e) => setUnit(e.target.value)} />
            </div>
          </div>
          <div className="grid grid-cols-3 gap-3">
            <div className="grid gap-2">
              <Label>Prezzo vendita €</Label>
              <Input inputMode="decimal" value={price} className="text-right" onChange={(e) => setPrice(e.target.value)} />
            </div>
            <div className="grid gap-2">
              <Label>Costo aziendale €</Label>
              <Input inputMode="decimal" value={cost} className="text-right" onChange={(e) => setCost(e.target.value)} />
            </div>
            <div className="grid gap-2">
              <Label>Qtà</Label>
              <Input inputMode="decimal" value={qty} className="text-right" onChange={(e) => setQty(e.target.value)} />
            </div>
          </div>
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button onClick={confirm} disabled={!name.trim()}>
            Aggiungi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
