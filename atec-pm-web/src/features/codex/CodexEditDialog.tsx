import * as React from "react"
import { useMutation } from "@tanstack/react-query"

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
import { updateCodexDescription } from "@/lib/api/codex"
import type { CodexListItem } from "@/lib/api/types"

/**
 * Modifica della sola descrizione di un articolo Codex (le altre colonne
 * provengono dalla sincronizzazione e non sono editabili) — vedi CodexController.Update.
 */
export function CodexEditDialog({
  item,
  onClose,
  onSaved,
}: {
  item: CodexListItem | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [descr, setDescr] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (item) {
      setDescr(item.descr)
      setError(null)
    }
  }, [item])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!item) return
      const trimmed = descr.trim()
      if (!trimmed) {
        throw new Error("La descrizione non può essere vuota.")
      }
      await updateCodexDescription(item.id, trimmed)
    },
    onSuccess: async () => {
      await onSaved()
    },
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={item !== null} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Modifica articolo</DialogTitle>
          <DialogDescription>
            Aggiorna la descrizione dell'articolo Codex.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid gap-2">
            <Label>Codice</Label>
            <p className="text-sm font-semibold text-primary tabular-nums">
              {item?.codice}
            </p>
          </div>
          <div className="grid gap-2">
            <Label>Descrizione</Label>
            <Input
              value={descr}
              autoFocus
              onChange={(event) => setDescr(event.target.value)}
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
            disabled={!descr.trim() || saveMutation.isPending}
          >
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
