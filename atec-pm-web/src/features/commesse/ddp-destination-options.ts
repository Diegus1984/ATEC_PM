import type { DdpDestinationItem } from "@/lib/api/types"

/** Valore sentinella per «nessuna destinazione» nelle Select (Radix vieta value=""). */
export const DDP_DESTINATION_NONE = "__none__"

/**
 * Nomi selezionabili per la destinazione di una riga DDP: le destinazioni
 * attive di Conf. DDP più l'eventuale valore corrente non più in lista
 * (rinominato/disattivato), mantenuto per non azzerarlo per sbaglio.
 */
export function buildDestinationOptions(
  destinations: DdpDestinationItem[],
  current: string | null | undefined
): string[] {
  // name/current possono arrivare null da righe storiche del DB: mai assumere stringa.
  const options = destinations
    .filter((d) => d.isActive && (d.name ?? "").trim())
    .map((d) => d.name)
    .sort((a, b) => a.localeCompare(b, "it", { sensitivity: "base" }))
  const safeCurrent = (current ?? "").trim()
  if (safeCurrent && !options.includes(safeCurrent)) {
    options.push(safeCurrent)
  }
  return options
}
