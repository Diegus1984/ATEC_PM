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
import type { GammaRobotDto, GammaRobotSaveRequest } from "@/lib/api/types"

export function RobotDialog({
  open,
  robot,
  onClose,
  onSave,
}: {
  open: boolean
  robot: GammaRobotDto | null
  onClose: () => void
  onSave: (request: GammaRobotSaveRequest) => void
}) {
  const [modello, setModello] = React.useState("")
  const [serie, setSerie] = React.useState("")
  const [brand, setBrand] = React.useState("ABB")
  const [note, setNote] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open) return
    setModello(robot?.modello ?? "")
    setSerie(robot?.serie ?? "")
    setBrand(robot?.brand || "ABB")
    setNote(robot?.note ?? "")
    setError(null)
  }, [open, robot])

  function submit() {
    if (!modello.trim()) {
      setError("Modello obbligatorio")
      return
    }
    onSave({
      modello: modello.trim(),
      serie: serie.trim() || null,
      brand: brand.trim() || "ABB",
      note: note.trim() || null,
    })
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{robot ? "Modifica robot" : "Nuovo robot"}</DialogTitle>
          <DialogDescription>
            Anagrafica modello robot (Gamma Robot).
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-3">
          <div className="grid gap-1.5">
            <Label htmlFor="gamma-modello">Modello *</Label>
            <Input
              id="gamma-modello"
              value={modello}
              onChange={(e) => setModello(e.target.value)}
              autoFocus
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="gamma-serie">Serie</Label>
            <Input
              id="gamma-serie"
              value={serie}
              onChange={(e) => setSerie(e.target.value)}
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="gamma-brand">Brand</Label>
            <Input
              id="gamma-brand"
              value={brand}
              onChange={(e) => setBrand(e.target.value)}
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="gamma-note">Note</Label>
            <Input
              id="gamma-note"
              value={note}
              onChange={(e) => setNote(e.target.value)}
            />
          </div>
          {error ? (
            <p className="text-sm text-destructive">{error}</p>
          ) : null}
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button type="button" onClick={submit}>
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
