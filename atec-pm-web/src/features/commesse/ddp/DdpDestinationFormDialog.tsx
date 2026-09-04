import * as React from "react"
import { useMutation } from "@tanstack/react-query"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import {
  createDdpDestination,
  updateDdpDestination,
} from "@/lib/api/ddp-config"
import type { DdpDestinationItem } from "@/lib/api/types"

import { normDdpDestination } from "./ddp-destination-norm"

/**
 * Form «Nuova/Modifica destinazione» (Nome + Attivo), identico a quello della pagina
 * Conf. DDP → Destinazioni. Estratto qui per riusarlo dal combobox della distinta
 * (pulsante «+»). L'anti-duplicato è lato server (Create case-insensitive): se la voce
 * esiste già non viene duplicata (e se disattivata viene riattivata).
 */
export function DdpDestinationFormDialog({
  open,
  item,
  initialName,
  existingNames,
  onClose,
  onSaved,
}: {
  open: boolean
  item: DdpDestinationItem | null
  /** Precompila il Nome quando si crea una voce nuova (es. testo digitato nella ricerca). */
  initialName?: string
  /** Nomi già esistenti: bloccano il salvataggio di un duplicato (case-insensitive). */
  existingNames?: string[]
  onClose: () => void
  onSaved: (savedName: string) => void
}) {
  const uid = React.useId()
  const [name, setName] = React.useState("")
  const [isActive, setIsActive] = React.useState(true)
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open) return
    setName(item?.name ?? initialName ?? "")
    setIsActive(item?.isActive ?? true)
    setError(null)
  }, [open, item, initialName])

  // Duplicato solo in creazione: il nome normalizzato non deve già esistere.
  const isDuplicate =
    !item &&
    normDdpDestination(name) !== "" &&
    (existingNames ?? []).some(
      (existing) => normDdpDestination(existing) === normDdpDestination(name)
    )

  const saveMutation = useMutation({
    mutationFn: async () => {
      const trimmed = name.trim()
      const payload = { id: item?.id ?? 0, name: trimmed, sortOrder: 0, isActive }
      if (item) await updateDdpDestination(item.id, payload)
      else await createDdpDestination(payload)
      return trimmed
    },
    onSuccess: (savedName) => onSaved(savedName),
    onError: (err: Error) => setError(err.message),
  })

  function save() {
    if (isDuplicate) {
      setError("Destinazione già presente.")
      return
    }
    saveMutation.mutate()
  }

  return (
    <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {item ? "Modifica destinazione" : "Nuova destinazione"}
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor={`${uid}-name`}>Nome</Label>
            <Input
              id={`${uid}-name`}
              value={name}
              autoFocus
              aria-invalid={isDuplicate}
              onChange={(event) => setName(event.target.value)}
              onKeyDown={(event) => {
                if (
                  event.key === "Enter" &&
                  name.trim() &&
                  !isDuplicate &&
                  !saveMutation.isPending
                ) {
                  save()
                }
              }}
            />
          </div>
          <div className="flex items-center gap-2">
            <Switch
              id={`${uid}-active`}
              checked={isActive}
              onCheckedChange={setIsActive}
            />
            <Label htmlFor={`${uid}-active`}>Attivo</Label>
          </div>
          {isDuplicate ? (
            <p className="text-sm text-destructive">
              Destinazione già presente.
            </p>
          ) : error ? (
            <p className="text-sm text-destructive">{error}</p>
          ) : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={save}
            disabled={!name.trim() || isDuplicate || saveMutation.isPending}
          >
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
