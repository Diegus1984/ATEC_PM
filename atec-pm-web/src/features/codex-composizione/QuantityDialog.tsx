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

/**
 * Piccolo dialog per la quantità, equivalente del `QuantityDialog` WPF (intero >= 1,
 * Invio conferma, valore preselezionato per sovrascriverlo subito). Due modalità:
 * `add` (default) all'aggiunta via drag&drop, `edit` per cambiare la quantità di una
 * riga esistente (badge ×N nell'albero). `onConfirm` viene invocato una sola volta:
 * un secondo Invio/click ravvicinato è ignorato (niente doppio inserimento).
 */
export function QuantityDialog({
  open,
  childCodice,
  mode = "add",
  initialQuantity = 1,
  onConfirm,
  onCancel,
}: {
  open: boolean
  childCodice: string
  mode?: "add" | "edit"
  initialQuantity?: number
  onConfirm: (quantity: number) => void
  onCancel: () => void
}) {
  const [value, setValue] = React.useState("1")
  const submittedRef = React.useRef(false)

  React.useEffect(() => {
    if (open) {
      setValue(String(Math.max(1, initialQuantity)))
      submittedRef.current = false
    }
  }, [open, initialQuantity])

  function confirm() {
    if (submittedRef.current) return
    const qty = Math.max(1, Math.floor(Number(value) || 0))
    if (qty >= 1) {
      submittedRef.current = true
      onConfirm(qty)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onCancel()}>
      <DialogContent className="sm:max-w-xs">
        <DialogHeader>
          <DialogTitle>Quantità</DialogTitle>
          <DialogDescription>
            {mode === "edit" ? "Nuova quantità di " : "Quante unità di "}
            <span className="font-mono font-medium text-foreground">
              {childCodice}
            </span>
            {mode === "edit" ? " nella composizione." : " aggiungere?"}
          </DialogDescription>
        </DialogHeader>

        <div className="grid gap-2">
          <Label htmlFor="composition-quantity">Quantità</Label>
          <Input
            id="composition-quantity"
            type="number"
            min={1}
            autoFocus
            value={value}
            onFocus={(event) => event.target.select()}
            onChange={(event) => setValue(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.preventDefault()
                confirm()
              }
            }}
          />
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onCancel}>
            Annulla
          </Button>
          <Button onClick={confirm}>{mode === "edit" ? "Salva" : "Aggiungi"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
