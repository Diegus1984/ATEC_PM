// ── Avviso nel planner: il collegamento con ATEC Risorse (VPS) non funziona (#147) ──

import { useQuery } from "@tanstack/react-query"
import { AlertTriangle } from "lucide-react"
import { Link } from "react-router-dom"

import { fetchSyncSalute } from "@/lib/api/risorse-sync"
import { canAccessFeature } from "@/lib/auth/permissions"
import { formatDateTimeOrDash } from "@/lib/date-iso"

/** Ogni quanto si rilegge la salute mentre il planner resta aperto: il motore gira ogni 60 s. */
const SALUTE_REFRESH_MS = 60_000

/**
 * Difesa UTC (come nella scheda della sincronizzazione): il server manda date-ora UTC, ma se
 * la stringa ISO arriva senza "Z" né offset il browser la leggerebbe come ora locale.
 */
function utc(value: string | null | undefined): string | null | undefined {
  if (!value) return value
  return /(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : `${value}Z`
}

function durata(minuti: number): string {
  if (minuti >= 24 * 60) {
    const giorni = Math.floor(minuti / (24 * 60))
    return giorni === 1 ? "1 giorno" : `${giorni} giorni`
  }
  if (minuti >= 120) return `${Math.floor(minuti / 60)} ore`
  return `${minuti} minuti`
}

/**
 * Barra ambra sopra la toolbar del planner quando la sincronizzazione col VPS è attiva ma
 * non c'è un giro riuscito da oltre 10 minuti (`GET sync/salute`, chiave della pagina, non
 * del pannello admin). A collegamento buono non si vede niente. Se la lettura fallisce il
 * giro si ferma (niente martellate su un server giù) e riparte al prossimo montaggio.
 */
export function SyncVpsWarning() {
  const query = useQuery({
    queryKey: ["risorse-sync-salute"],
    queryFn: fetchSyncSalute,
    refetchInterval: (q) => (q.state.error ? false : SALUTE_REFRESH_MS),
    refetchIntervalInBackground: false,
    retry: false,
  })
  const salute = query.data
  if (!salute?.vpsNonRisponde) return null

  return (
    <div
      role="status"
      className="no-print mb-2 flex shrink-0 items-start gap-2 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900"
    >
      <AlertTriangle className="mt-0.5 size-4 shrink-0 text-amber-600" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <span className="font-semibold">
          ATEC Risorse (VPS) non risponde da {durata(salute.minutiSenzaRisposta)}.
        </span>{" "}
        Le modifiche fatte qui non arrivano al programma sul VPS, e quelle fatte là non
        arrivano qui, finché il collegamento non torna: il motore riprova da solo ogni minuto.
        Ultimo scambio riuscito: {formatDateTimeOrDash(utc(salute.ultimoGiroOkUtc))}.
        {canAccessFeature("nav.digest_email") ? (
          <>
            {" "}
            <Link to="/digest-email" className="underline underline-offset-2">
              Stato e registro della sincronizzazione
            </Link>
          </>
        ) : null}
        {salute.errore ? (
          <span className="block text-xs text-amber-800/90">Dettaglio: {salute.errore}</span>
        ) : null}
      </div>
    </div>
  )
}
