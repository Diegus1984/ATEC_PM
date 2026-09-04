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
  createDdpTreatment,
  updateDdpTreatment,
} from "@/lib/api/ddp-config"
import type { DdpTreatmentItem } from "@/lib/api/types"

import { normDdpTreatment } from "./ddp-treatment-norm"

/**
 * Form «Nuovo/Modifica trattamento» (Nome + Attivo), identico a quello delle destinazioni.
 * Estratto qui per riusarlo dal combobox della distinta officina (pulsante «+»).
 */
export function DdpTreatmentFormDialog({
  open,
  item,
  initialName,
  existingNames,
  onClose,
  onSaved,
}: {
  open: boolean
  item: DdpTreatmentItem | null
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

  const isDuplicate =
    !item &&
    normDdpTreatment(name) !== "" &&
    (existingNames ?? []).some(
      (existing) => normDdpTreatment(existing) === normDdpTreatment(name)
    )

  const saveMutation = useMutation({
    mutationFn: async () => {
      const trimmed = name.trim()
      const payload = { id: item?.id ?? 0, name: trimmed, sortOrder: 0, isActive }
      if (item) await updateDdpTreatment(item.id, payload)
      else await createDdpTreatment(payload)
      return trimmed
    },
    onSuccess: (savedName) => onSaved(savedName),
    onError: (err: Error) => setError(err.message),
  })

  function save() {
    if (isDuplicate) {
      setError("Trattamento già presente.")
      return
    }
    saveMutation.mutate()
  }

  return (
    <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {item ? "Modifica trattamento" : "Nuovo trattamento"}
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
              Trattamento già presente.
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
