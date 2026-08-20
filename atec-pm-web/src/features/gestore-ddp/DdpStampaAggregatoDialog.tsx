import * as React from "react"
import { Printer } from "lucide-react"

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
import { Label } from "@/components/ui/label"
import { cn } from "@/lib/utils"

import type { DdpAvanzSection, DdpSectionKey } from "./ddp-sintesi-logic"

/**
 * Scelta delle tabelle da mandare nel PDF unico della scheda «Avanzamento».
 * I contatori mostrano le sole righe **accese**: è il numero di righe che finirà davvero
 * nella stampa, non quello del contatore di sezione (che non cala mai).
 */
export function DdpStampaAggregatoDialog({
  open,
  onOpenChange,
  sezioni,
  isOff,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Sezioni già nell'ordine canonico del tipo di DDP (`model.sezioni`). */
  sezioni: DdpAvanzSection[]
  /** Righe spente sul DB: escluse dal conteggio e dal PDF. */
  isOff: (sectionKey: string, rowId: number) => boolean
  onConfirm: (keys: DdpSectionKey[]) => void
}) {
  const [selected, setSelected] = React.useState<Set<DdpSectionKey>>(new Set())

  // L'elenco delle sezioni si legge da un ref: un refresh realtime del modello non deve
  // rimettere tutte le spunte mentre l'utente sta scegliendo.
  const sezioniRef = React.useRef(sezioni)
  React.useEffect(() => {
    sezioniRef.current = sezioni
  }, [sezioni])

  React.useEffect(() => {
    if (open) {
      setSelected(new Set(sezioniRef.current.map((section) => section.key)))
    }
  }, [open])

  const allSelected = sezioni.length > 0 && selected.size === sezioni.length

  const toggle = (key: DdpSectionKey, on: boolean) => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (on) next.add(key)
      else next.delete(key)
      return next
    })
  }

  // Le sezioni selezionate, sempre nell'ordine di `model.sezioni` (mai in quello di
  // spunta): il PDF aggregato deve uscire nell'ordine canonico.
  const chosen = sezioni.filter((section) => selected.has(section.key))
  const printableRows = chosen.reduce(
    (sum, section) =>
      sum + section.rows.filter((row) => !isOff(section.key, row.id)).length,
    0
  )

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[90vh] max-w-2xl flex-col p-6">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Printer className="size-5 text-primary" />
            Stampa Aggregato
          </DialogTitle>
          <DialogDescription>
            Le tabelle scelte finiscono in un unico PDF, nell&apos;ordine della scheda.
            Il numero accanto a ogni voce è quello delle righe accese.
          </DialogDescription>
        </DialogHeader>

        <div className="min-h-0 flex-1 space-y-3 overflow-y-auto py-1 pr-1">
          <div className="flex items-center gap-2 rounded-md bg-muted/40 px-2.5 py-2">
            <Checkbox
              id="ddp-print-all"
              checked={allSelected}
              onCheckedChange={(checked) =>
                setSelected(
                  checked === true
                    ? new Set(sezioni.map((section) => section.key))
                    : new Set()
                )
              }
            />
            <Label
              htmlFor="ddp-print-all"
              className="cursor-pointer text-sm font-semibold"
            >
              Seleziona tutte le tabelle
            </Label>
          </div>

          <div className="grid gap-1.5 md:grid-cols-2">
            {sezioni.map((section) => {
              const on = selected.has(section.key)
              const count = section.rows.filter(
                (row) => !isOff(section.key, row.id)
              ).length
              const id = `ddp-print-${section.key}`
              return (
                <div
                  key={section.key}
                  className={cn(
                    "flex items-center gap-2 rounded-md border px-2.5 py-2 transition-opacity",
                    !on && "opacity-55"
                  )}
                >
                  <Checkbox
                    id={id}
                    checked={on}
                    onCheckedChange={(checked) => toggle(section.key, checked === true)}
                  />
                  <Label htmlFor={id} className="min-w-0 flex-1 cursor-pointer font-normal">
                    <span className="min-w-0 truncate">{section.title}</span>
                  </Label>
                  <span className="shrink-0 rounded-full bg-muted px-2 py-0.5 text-xs font-semibold tabular-nums text-muted-foreground">
                    {count}
                  </span>
                </div>
              )
            })}
          </div>
        </div>

        <DialogFooter className="items-center border-t pt-4 sm:justify-between">
          <span className="text-xs text-muted-foreground">
            {chosen.length} {chosen.length === 1 ? "tabella" : "tabelle"} ·{" "}
            {printableRows} {printableRows === 1 ? "riga" : "righe"} da stampare
          </span>
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Chiudi
            </Button>
            <Button
              disabled={chosen.length === 0}
              onClick={() => {
                onConfirm(chosen.map((section) => section.key))
                onOpenChange(false)
              }}
            >
              <Printer />
              Stampa PDF
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
