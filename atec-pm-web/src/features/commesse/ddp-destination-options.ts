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
  current: string
): string[] {
  const options = [...destinations]
    .filter((d) => d.isActive)
    .sort((a, b) => a.name.localeCompare(b.name, "it", { sensitivity: "base" }))
    .map((d) => d.name)
  if (current.trim() && !options.includes(current)) {
    options.push(current)
  }
  return options
}
