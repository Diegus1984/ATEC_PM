import type { StatoCombo } from "@/lib/api/types"

/** L'etichetta a video dei tre stati: le parole dell'Excel di Paolo, non i codici. */
export function etichettaStato(stato: StatoCombo): string {
  if (stato === "NO") return "non abilitato"
  return stato === "READ" ? "sola lettura" : "lettura e scrittura"
}
