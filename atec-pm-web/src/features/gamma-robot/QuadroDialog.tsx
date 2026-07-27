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
import type { GammaQuadroDto, GammaQuadroSaveRequest } from "@/lib/api/types"

export function QuadroDialog({
  open,
  quadro,
  onClose,
  onSave,
}: {
  open: boolean
  quadro: GammaQuadroDto | null
  onClose: () => void
  onSave: (request: GammaQuadroSaveRequest) => void
}) {
  const [controllore, setControllore] = React.useState("")
  const [generazione, setGenerazione] = React.useState("")
  const [payload, setPayload] = React.useState("")
  const [areaLavoro, setAreaLavoro] = React.useState("")
  const [osVersion, setOsVersion] = React.useState("")
  const [systemKey, setSystemKey] = React.useState("")
  const [note, setNote] = React.useState("")

  React.useEffect(() => {
    if (!open) return
    setControllore(quadro?.controllore ?? "")
    setGenerazione(quadro?.generazione ?? "")
    setPayload(quadro?.payload ?? "")
    setAreaLavoro(quadro?.areaLavoro ?? "")
    setOsVersion(quadro?.osVersion ?? "")
    setSystemKey(quadro?.systemKey ?? "")
    setNote(quadro?.note ?? "")
  }, [open, quadro])

  function submit() {
    onSave({
      controllore: controllore.trim() || null,
      generazione: generazione.trim() || null,
      payload: payload.trim() || null,
      areaLavoro: areaLavoro.trim() || null,
      osVersion: osVersion.trim() || null,
      systemKey: systemKey.trim() || null,
      note: note.trim() || null,
    })
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{quadro ? "Modifica quadro" : "Nuovo quadro"}</DialogTitle>
          <DialogDescription>
            Configurazione controllore / payload / OS.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-3">
          <div className="grid gap-1.5">
            <Label htmlFor="gamma-ctrl">Controllore</Label>
            <Input
              id="gamma-ctrl"
              value={controllore}
              onChange={(e) => setControllore(e.target.value)}
              autoFocus
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="gamma-gen">Generazione</Label>
            <Input
              id="gamma-gen"
              value={generazione}
              onChange={(e) => setGenerazione(e.target.value)}
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="grid gap-1.5">
              <Label htmlFor="gamma-payload">Payload (kg)</Label>
              <Input
                id="gamma-payload"
                value={payload}
                onChange={(e) => setPayload(e.target.value)}
              />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="gamma-area">Area (m)</Label>
              <Input
                id="gamma-area"
                value={areaLavoro}
                onChange={(e) => setAreaLavoro(e.target.value)}
              />
            </div>
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="gamma-os">OS version</Label>
            <Input
              id="gamma-os"
              value={osVersion}
              onChange={(e) => setOsVersion(e.target.value)}
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="gamma-syskey">System key</Label>
            <Input
              id="gamma-syskey"
              value={systemKey}
              onChange={(e) => setSystemKey(e.target.value)}
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="gamma-qnote">Note</Label>
            <Input
              id="gamma-qnote"
              value={note}
              onChange={(e) => setNote(e.target.value)}
            />
          </div>
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
