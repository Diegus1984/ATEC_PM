import { TableCell } from "@/components/ui/table"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import type { HrDay } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { FASCE_LABELS, isZero, oreLeggibili } from "./ore"
import type { ToneStato } from "./stato-giornata"

// Le celle della griglia del cartellino (02/09/2026, «leggibile da chi non usa il computer
// tutti i giorni»), in un posto solo: le usano il cartellino di una persona e il «Controllo di
// ieri» di tutti, così un orario, un totale o una fascia si leggono allo stesso modo ovunque.
// Solo componenti qui (fast refresh): le funzioni sulle durate stanno in ore.ts.

/**
 * Una cella di orario: in grande l'ora che vale, in piccolo — sempre, anche quando coincide —
 * l'orario che Ecos ha ADESSO per quella timbratura (Diego, 09/09/2026, anteprima «A»):
 * prima della scrittura è quello timbrato, dopo è uguale all'ora calcolata, e quando le due
 * coincidono la giornata è sincronizzata. Se non coincidono la riga piccola è in ambra: si vede
 * a colpo d'occhio cosa cambierà su Ecos. L'orario originale non sparisce: sta nel dettaglio
 * della giornata e nel registro degli invii. «??:??» del motore (uscita mai timbrata) diventa
 * una parola.
 */
export function CellaOra({
  valore,
  timbrato,
  spenta,
  suEcos,
}: {
  valore: string
  timbrato: string
  spenta: boolean
  /** L'ora che Ecos ha adesso, se diversa da quella timbrata (`oraSuEcos`). */
  suEcos?: string | null
}) {
  if (valore === "??:??") {
    return (
      <TableCell className="leading-tight">
        <span className="text-destructive font-semibold">Non timbrata</span>
      </TableCell>
    )
  }
  if (!valore) {
    return (
      <TableCell className="leading-tight">
        <span className="text-muted-foreground">—</span>
        {/* «--:--» è il segnaposto del grezzo per la timbratura che non c'è: sotto un
            trattino non dice niente. */}
        {timbrato && timbrato !== "--:--" && (
          <span className="block text-[11px] text-muted-foreground">timbrato {timbrato}</span>
        )}
      </TableCell>
    )
  }
  // Senza ora timbrata (orario stimato o rettificato a mano) la riga resta, invisibile,
  // così tutte le celle hanno la stessa altezza.
  const conTimbrata = Boolean(timbrato)
  const suEcosAdesso = suEcos ?? timbrato
  // Ambra = su Ecos c'è ancora un orario diverso da quello calcolato (da scrivere).
  const diversa = conTimbrata && !spenta && suEcosAdesso !== valore.replace("*", "")
  return (
    <TableCell className="leading-tight">
      <span className={cn("tabular-nums font-semibold", spenta && "text-muted-foreground")}>
        {valore}
      </span>
      <span
        className={cn(
          "block text-[11px] tabular-nums",
          diversa ? "text-amber-600 dark:text-amber-500" : "text-muted-foreground",
          !conTimbrata && "invisible"
        )}
        aria-hidden={!conTimbrata}
        title={
          conTimbrata
            ? diversa
              ? `Su Ecos c'è ${suEcosAdesso}: con «Scrivi su Ecos» diventa ${valore.replace("*", "")}`
              : "Su Ecos c'è lo stesso orario della giornata calcolata"
            : undefined
        }
      >
        timbrato {conTimbrata ? suEcosAdesso : "—"}
      </span>
    </TableCell>
  )
}

/** La colonna «Ore»: le ore ordinarie della giornata, un trattino quando sono zero. */
export function CellaOre({ g }: { g: HrDay }) {
  return (
    <TableCell className="text-right tabular-nums font-medium">
      {isZero(g.regularHours) && g.regularHours !== "---" ? (
        <span className="text-muted-foreground">—</span>
      ) : (
        oreLeggibili(g.regularHours)
      )}
    </TableCell>
  )
}

/**
 * La colonna «Straord.»: le ore col dettaglio delle fasce CCNL nel tooltip; «notte» quando
 * non c'è straordinario ma ci sono ore notturne maggiorate (fascia b, #145).
 */
export function CellaStraordinario({ g }: { g: HrDay }) {
  const fasceEntries = Object.entries(g.bands ?? {})
  const dettaglio = (titolo: string) => (
    <TooltipContent className="text-xs">
      <p className="mb-1 font-semibold">{titolo}</p>
      {fasceEntries.map(([k, v]) => (
        <div key={k}>
          <b>{FASCE_LABELS[k] ?? `Fascia ${k}`}:</b> {v}
        </div>
      ))}
    </TooltipContent>
  )
  return (
    <TableCell className="text-right tabular-nums">
      {isZero(g.overtime) && fasceEntries.length > 0 ? (
        <Tooltip>
          <TooltipTrigger asChild>
            <span className="cursor-help text-xs text-muted-foreground underline decoration-dotted">
              notte
            </span>
          </TooltipTrigger>
          {dettaglio("Maggiorazioni CCNL:")}
        </Tooltip>
      ) : isZero(g.overtime) ? (
        <span className="text-muted-foreground">—</span>
      ) : fasceEntries.length > 0 ? (
        <Tooltip>
          <TooltipTrigger asChild>
            <span className="cursor-help underline decoration-dotted">
              {oreLeggibili(g.overtime)}
            </span>
          </TooltipTrigger>
          {dettaglio("Dettaglio fasce CCNL:")}
        </Tooltip>
      ) : (
        oreLeggibili(g.overtime)
      )}
    </TableCell>
  )
}

/** Un riquadro di riepilogo in testa alla pagina: etichetta, numero grande, una riga sotto. */
export function Riquadro({
  etichetta,
  valore,
  dettaglio,
  tone,
}: {
  etichetta: string
  valore: string
  dettaglio: string
  tone?: ToneStato
}) {
  return (
    <div
      className={cn(
        "rounded-lg border bg-card px-4 py-3 shadow-xs",
        tone === "bad" && "border-destructive/40"
      )}
    >
      <p className="text-sm text-muted-foreground">{etichetta}</p>
      <p
        className={cn(
          "text-2xl font-bold leading-tight tabular-nums",
          tone === "bad" && "text-destructive",
          tone === "warn" && "text-amber-600 dark:text-amber-500"
        )}
      >
        {valore}
      </p>
      <p className="text-sm text-muted-foreground">{dettaglio}</p>
    </div>
  )
}
