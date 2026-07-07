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
import { Textarea } from "@/components/ui/textarea"
import { createGroup, updateGroup } from "@/lib/api/quote-catalog"
import type { QuoteGroupDto } from "@/lib/api/types"

/**
 * Crea/modifica gruppo di catalogo. Fedele a QuoteGroupDialog del WPF:
 * campi Nome*, Descrizione, Ordine. Il listino (priceListId) NON è gestito qui
 * (resta null come nel WPF: i gruppi senza listino confluiscono in «Senza listino»).
 */
export function QuoteGroupDialog({
  open,
  group,
  onClose,
  onSaved,
}: {
  open: boolean
  group: QuoteGroupDto | null
  onClose: () => void
  onSaved: () => void
}) {
  const editId = group?.id ?? null
  const [name, setName] = React.useState("")
  const [description, setDescription] = React.useState("")
  const [sortOrder, setSortOrder] = React.useState("0")
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open) return
    setError(null)
    setName(group?.name ?? "")
    setDescription(group?.description ?? "")
    setSortOrder(String(group?.sortOrder ?? 0))
  }, [open, group])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!name.trim()) {
        throw new Error("Il nome è obbligatorio.")
      }
      const dto = {
        priceListId: null,
        name: name.trim(),
        description: description.trim(),
        sortOrder: Number.parseInt(sortOrder, 10) || 0,
        isActive: true,
      }
      if (editId != null) {
        await updateGroup(editId, dto)
      } else {
        await createGroup(dto)
      }
    },
    onSuccess: () => onSaved(),
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{editId != null ? "Modifica gruppo" : "Nuovo gruppo"}</DialogTitle>
          <DialogDescription>Gruppo del catalogo preventivi.</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid gap-2">
            <Label>Nome *</Label>
            <Input
              value={name}
              autoFocus
              onChange={(event) => setName(event.target.value)}
            />
          </div>
          <div className="grid gap-2">
            <Label>Descrizione</Label>
            <Textarea
              value={description}
              rows={2}
              onChange={(event) => setDescription(event.target.value)}
            />
          </div>
          <div className="grid gap-2">
            <Label>Ordine</Label>
            <Input
              inputMode="numeric"
              value={sortOrder}
              className="w-28"
              onChange={(event) => setSortOrder(event.target.value)}
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
            disabled={!name.trim() || saveMutation.isPending}
          >
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
