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
 * Piccolo dialog per la quantità all'aggiunta di un componente via drag&drop,
 * equivalente del `QuantityDialog` WPF (intero >= 1, Invio conferma, "1"
 * preselezionato per sovrascriverlo subito). `onConfirm` viene invocato una sola
 * volta: un secondo Invio/click ravvicinato è ignorato (niente doppio inserimento).
 */
export function QuantityDialog({
  open,
  childCodice,
  onConfirm,
  onCancel,
}: {
  open: boolean
  childCodice: string
  onConfirm: (quantity: number) => void
  onCancel: () => void
}) {
  const [value, setValue] = React.useState("1")
  const submittedRef = React.useRef(false)

  React.useEffect(() => {
    if (open) {
      setValue("1")
      submittedRef.current = false
    }
  }, [open])

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
            Quante unità di{" "}
            <span className="font-mono font-medium text-foreground">
              {childCodice}
            </span>{" "}
            aggiungere?
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
          <Button onClick={confirm}>Aggiungi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
