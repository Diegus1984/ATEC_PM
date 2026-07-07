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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { createCategory, updateCategory } from "@/lib/api/quote-catalog"
import type { QuoteCategoryDto, QuoteGroupDto } from "@/lib/api/types"

/**
 * Crea/modifica categoria. Fedele a QuoteCategoryDialog del WPF: combo Gruppo*,
 * Nome*, Descrizione, Ordine. Il parentId NON è gestito qui (resta null come nel
 * WPF: l'annidamento si fa con «+ Sotto-categoria» o trascinando nell'albero).
 */
export function QuoteCategoryDialog({
  open,
  groups,
  category,
  preselectedGroupId,
  onClose,
  onSaved,
}: {
  open: boolean
  groups: QuoteGroupDto[]
  category: QuoteCategoryDto | null
  preselectedGroupId?: number | null
  onClose: () => void
  onSaved: () => void
}) {
  const editId = category?.id ?? null
  const [groupId, setGroupId] = React.useState<string>("")
  const [name, setName] = React.useState("")
  const [description, setDescription] = React.useState("")
  const [sortOrder, setSortOrder] = React.useState("0")
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open) return
    setError(null)
    const initialGroup = category?.groupId ?? preselectedGroupId ?? null
    setGroupId(initialGroup != null ? String(initialGroup) : "")
    setName(category?.name ?? "")
    setDescription(category?.description ?? "")
    setSortOrder(String(category?.sortOrder ?? 0))
  }, [open, category, preselectedGroupId])

  const saveMutation = useMutation({
    mutationFn: async () => {
      const gid = Number.parseInt(groupId, 10)
      if (!gid) {
        throw new Error("Seleziona un gruppo.")
      }
      if (!name.trim()) {
        throw new Error("Il nome è obbligatorio.")
      }
      const dto = {
        groupId: gid,
        parentId: null,
        name: name.trim(),
        description: description.trim(),
        sortOrder: Number.parseInt(sortOrder, 10) || 0,
        isActive: true,
      }
      if (editId != null) {
        await updateCategory(editId, dto)
      } else {
        await createCategory(dto)
      }
    },
    onSuccess: () => onSaved(),
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>
            {editId != null ? "Modifica categoria" : "Nuova categoria"}
          </DialogTitle>
          <DialogDescription>Categoria del catalogo preventivi.</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid gap-2">
            <Label>Gruppo *</Label>
            <Select value={groupId} onValueChange={setGroupId}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Seleziona un gruppo" />
              </SelectTrigger>
              <SelectContent>
                {groups.map((group) => (
                  <SelectItem key={group.id} value={String(group.id)}>
                    {group.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
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
            disabled={!groupId || !name.trim() || saveMutation.isPending}
          >
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
