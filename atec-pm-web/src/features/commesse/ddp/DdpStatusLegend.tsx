// ── Legenda degli stati riga DDP: popover richiamabile lungo tutto il giro ────
// (DDP di commessa e Inbox Acquisti). Le voci arrivano da Conf. DDP via la
// query «ddp-statuses» già in cache nelle pagine che la montano.

import { CircleHelp } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import type { DdpStatusItem } from "@/lib/api/types"

export function DdpStatusLegend({ statuses }: { statuses: DdpStatusItem[] }) {
  const attivi = statuses
    .filter((s) => s.isActive)
    .sort((a, b) => a.sortOrder - b.sortOrder)
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button
          size="sm"
          variant="ghost"
          className="gap-1 text-muted-foreground"
          title="Cosa significano gli stati delle righe"
        >
          <CircleHelp className="h-4 w-4" />
          Legenda stati
        </Button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 p-3">
        <div className="mb-2 text-xs font-semibold">Stati delle righe di distinta</div>
        {attivi.length === 0 ? (
          <div className="text-xs text-muted-foreground">Legenda non disponibile.</div>
        ) : (
          <div className="max-h-64 space-y-1 overflow-y-auto text-xs">
            {attivi.map((s) => (
              <div key={s.statusKey} className="flex items-center gap-2">
                <span
                  className="h-3 w-3 shrink-0 rounded-sm border"
                  style={s.colorBg ? { backgroundColor: s.colorBg } : undefined}
                />
                <span className="w-10 shrink-0 font-mono text-[10px] text-muted-foreground">
                  {s.statusKey}
                </span>
                <span className="truncate">{s.label}</span>
              </div>
            ))}
          </div>
        )}
        <div className="mt-2 border-t pt-2 text-[11px] leading-snug text-muted-foreground">
          Lo stato si cambia dal menu della colonna Stato; i passaggi ammessi sono
          configurati in Conf. DDP.
        </div>
      </PopoverContent>
    </Popover>
  )
}
