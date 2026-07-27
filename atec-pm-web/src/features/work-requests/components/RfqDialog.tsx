import * as React from "react"
import { Plus, Trash2 } from "lucide-react"
import { RowActionsMenu } from "@/components/shared/row-actions"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { DateField } from "@/components/shared/date-field"
import { SupplierSearchCombobox } from "@/components/shared/supplier-search-combobox"
import type { Rfq, SupplierListItem } from "@/lib/api/types"

interface RfqDialogProps {
  open: boolean
  rfqs: Rfq[]
  suppliers: SupplierListItem[]
  onClose: () => void
  onSave: (updatedRfqs: Rfq[]) => void
}

export function RfqDialog({ open, rfqs, suppliers, onClose, onSave }: RfqDialogProps) {
  // Stato locale per gestire le offerte internamente al dialog
  const [localRfqs, setLocalRfqs] = React.useState<Rfq[]>([])

  React.useEffect(() => {
    if (open) {
      setLocalRfqs([...rfqs])
    }
  }, [open, rfqs])

  // Aggiunge una nuova riga di offerta (massimo 4)
  const handleAdd = () => {
    if (localRfqs.length >= 4) return
    setLocalRfqs((prev) => [...prev, { supplier: "", date: "", ok: false }])
  }

  // Rimuove un'offerta in base all'indice
  const handleRemove = (index: number) => {
    setLocalRfqs((prev) => prev.filter((_, i) => i !== index))
  }

  // Modifica un campo specifico di un'offerta
  const handleChange = (index: number, field: keyof Rfq, value: unknown) => {
    setLocalRfqs((prev) =>
      prev.map((rfq, i) => {
        if (i !== index) return rfq
        const updated = { ...rfq, [field]: value }
        
        // Vincolo: solo un'offerta può essere contrassegnata come "ok: true"
        if (field === "ok" && value === true) {
          return { ...updated, ok: true }
        } else if (field === "ok" && value === false) {
          return { ...updated, ok: false }
        }
        
        return updated
      })
    )
  }

  // Salva le modifiche confermando solo i record validi
  const handleSave = () => {
    onSave(localRfqs.filter(r => r.supplier.trim() !== ""))
    onClose()
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent 
        className="max-h-[90vh] overflow-y-auto sm:max-w-xl"
        onOpenAutoFocus={(e) => e.preventDefault()}
      >
        <DialogHeader>
          <DialogTitle>RDO — Richiesta Offerte</DialogTitle>
          <DialogDescription>
            Elenco offerte tracciato a mano (max 4). Fornitore e n° ordine ODA
            arrivano dalla DDP Officina, non da qui.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {localRfqs.map((rfq, index) => (
            <div key={index} className="flex items-start gap-3 rounded-lg border border-border p-3 bg-muted/40">
              <span className="mt-1.5 text-xs font-semibold text-muted-foreground w-4 text-center">
                {index + 1}
              </span>
              
              <div className="flex-1 grid gap-2">
                <div className="grid gap-1">
                  <span className="text-xs font-medium text-muted-foreground">Fornitore</span>
                  <SupplierSearchCombobox
                    suppliers={suppliers}
                    value={rfq.supplier}
                    onValueChange={(name) => handleChange(index, "supplier", name)}
                  />
                </div>

                <div className="grid grid-cols-2 gap-4 items-end">
                  <div className="grid gap-1">
                    <span className="text-xs font-medium text-muted-foreground">Data Offerta</span>
                    <DateField
                      size="sm"
                      value={rfq.date || null}
                      onChange={(val) => handleChange(index, "date", val || "")}
                    />
                  </div>

                  <label className="flex items-center gap-2 text-xs font-medium cursor-pointer h-9 select-none">
                    <Checkbox
                      checked={rfq.ok}
                      onCheckedChange={(value) => {
                        // Deseleziona le altre offerte se questa viene contrassegnata come confermata (offerta ok)
                        if (value === true) {
                          setLocalRfqs(prev => prev.map((r, i) => i === index ? { ...r, ok: true } : { ...r, ok: false }))
                        } else {
                          handleChange(index, "ok", false)
                        }
                      }}
                    />
                    <span>Offerta OK</span>
                  </label>
                </div>
              </div>

              <div className="mt-6 shrink-0">
                <RowActionsMenu
                  size="icon-sm"
                  triggerClassName="size-8 text-muted-foreground hover:text-destructive"
                  actions={[
                    {
                      label: "Elimina offerta",
                      icon: Trash2,
                      destructive: true,
                      onClick: () => handleRemove(index),
                    },
                  ]}
                />
              </div>
            </div>
          ))}

          {localRfqs.length < 4 && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="w-full gap-1.5 rounded-full"
              onClick={handleAdd}
            >
              <Plus className="size-4" />
              Aggiungi Offerta (RDO)
            </Button>
          )}
        </div>

        <DialogFooter className="mt-4">
          <Button variant="outline" className="rounded-full" onClick={onClose}>
            Annulla
          </Button>
          <Button className="rounded-full" onClick={handleSave}>
            Salva Offerte
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
