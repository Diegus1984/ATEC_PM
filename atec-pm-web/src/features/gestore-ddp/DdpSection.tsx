import * as React from "react"
import { ChevronRight, Printer } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Collapsible } from "@/components/ui/collapsible"
import { cn } from "@/lib/utils"

/**
 * Sezione a fisarmonica della Sintesi DDP (titolo + contatore + azioni + Stampa PDF).
 * `<Collapsible>` con i token di progetto: mai `{open ? … : null}`, altrimenti
 * l'animazione non ha nulla da misurare.
 */
export function DdpSection({
  id,
  title,
  count,
  open,
  onToggle,
  onPrint,
  actions,
  children,
}: {
  id: string
  title: string
  /** Numero di righe mostrato accanto al titolo (non cala spegnendo righe). */
  count?: number
  open: boolean
  onToggle: (id: string) => void
  onPrint?: () => void
  /** Azioni a destra del titolo (es. «Tutte accese» / «Tutte spente»). */
  actions?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <div id={`ddp-section-${id}`} className="rounded-lg border bg-card scroll-mt-16">
      <div className="flex items-center justify-between gap-2 px-3 py-2">
        <button
          type="button"
          aria-expanded={open}
          className="flex min-w-0 flex-1 items-center gap-2 text-left text-sm font-semibold"
          onClick={() => onToggle(id)}
        >
          <ChevronRight
            className={cn(
              "size-4 shrink-0 text-muted-foreground transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
              open && "rotate-90"
            )}
          />
          <span className="truncate">{title}</span>
          {count != null ? (
            <span className="shrink-0 rounded-full bg-muted px-2 py-0.5 text-xs font-semibold tabular-nums text-muted-foreground">
              {count}
            </span>
          ) : null}
        </button>
        <div
          className="flex shrink-0 items-center gap-1.5"
          // Le azioni non devono aprire o chiudere la sezione.
          onClick={(event) => event.stopPropagation()}
        >
          {actions}
          {onPrint ? (
            <Button variant="outline" size="xs" onClick={onPrint}>
              <Printer className="size-3" />
              Stampa PDF
            </Button>
          ) : null}
        </div>
      </div>
      <Collapsible open={open}>
        <div className="border-t p-3">{children}</div>
      </Collapsible>
    </div>
  )
}
