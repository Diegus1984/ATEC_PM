import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Label } from "@/components/ui/label"
import { fetchPmLookup } from "@/lib/api/projects"
import { convertQuote } from "@/lib/api/quotes"

/**
 * Converte un preventivo IMPIANTO in commessa. Fedele a ConvertQuoteDialog del WPF:
 * scelta del PM, POST /api/quotes/{id}/convert {PmId} → id nuova commessa.
 */
export function ConvertQuoteDialog({
  open,
  quoteId,
  quoteNumber,
  onClose,
  onConverted,
}: {
  open: boolean
  quoteId: number | null
  quoteNumber: string
  onClose: () => void
  onConverted: (projectId: number) => void
}) {
  const [pmId, setPmId] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)

  const pmQuery = useQuery({
    queryKey: ["pm-lookup"],
    queryFn: fetchPmLookup,
    enabled: open,
  })

  React.useEffect(() => {
    if (open) {
      setPmId("")
      setError(null)
    }
  }, [open])

  const convertMutation = useMutation({
    mutationFn: async () => {
      const id = Number.parseInt(pmId, 10)
      if (!id) throw new Error("Seleziona un Project Manager.")
      if (quoteId == null) throw new Error("Preventivo non valido.")
      return convertQuote(quoteId, { pmId: id })
    },
    onSuccess: (projectId) => onConverted(projectId),
    onError: (err: Error) => setError(err.message),
  })

  const pms = [...(pmQuery.data ?? [])].sort((a, b) => a.name.localeCompare(b.name))

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Converti in commessa</DialogTitle>
          <DialogDescription>
            Il preventivo {quoteNumber} verrà convertito in una nuova commessa
            (vengono copiati costing, materiali, fasi).
          </DialogDescription>
        </DialogHeader>

        <div className="grid gap-2">
          <Label>Project Manager *</Label>
          <LookupCombobox
            options={pms.map((pm) => ({ id: String(pm.id), name: pm.name }))}
            value={pmId || null}
            onValueChange={(id) => setPmId(id ?? "")}
            placeholder="Seleziona un PM"
            searchPlaceholder="Cerca PM…"
            emptyText="Nessun PM trovato"
          />
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            disabled={!pmId || convertMutation.isPending}
            onClick={() => convertMutation.mutate()}
          >
            {convertMutation.isPending ? "Conversione…" : "Converti"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
