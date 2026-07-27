import type { DdpStatusItem } from "@/lib/api/types"

/** Codice stato DDP «Annullato» (setup Conf. DDP / DB). */
export const DDP_STATUS_CANCELLED = "ANN"

/** Etichetta del tipo di distinta (OFFICINA vs COMMERCIALE) per titoli e badge. */
export function ddpTypeLabel(ddpType: string): string {
  return ddpType === "OFFICINA" ? "OFFICINA" : "COMMERCIALE"
}

/** Codice stato DDP «Da Ordinare» (setup Conf. DDP / DB). */
export const DDP_STATUS_TO_ORDER = "DO"

/** Codice stato DDP «Verificare se disponibile a magazzino» — default nuove righe commerciali. */
export const DDP_STATUS_VERIFY = "VER"

/**
 * Finestra opzioni della matrice avanzamenti (v7, per tipo di distinta): stati
 * selezionabili dallo stato corrente. Regole:
 *  - solo stati attivi (lo stato corrente resta visibile anche se disattivato);
 *  - riga senza stato → finestra di partenza "INIZIO" della matrice
 *    (sulla commerciale esclude DC);
 *  - (tipo, stato) non governato dalla matrice → finestra completa;
 *  - stato governato → stato corrente + transizioni ammesse.
 */
export function filterStatusOptions(
  statuses: DdpStatusItem[],
  currentStatusKey: string | null | undefined,
  transitions?: Record<string, string[]>
): DdpStatusItem[] {
  const current = (currentStatusKey ?? "").toUpperCase()
  const allowed = transitions ? transitions[current || "INIZIO"] : undefined
  return statuses.filter((status) => {
    const key = status.statusKey.toUpperCase()
    if (key === current) return true
    if (!status.isActive) return false
    return allowed === undefined || allowed.includes(key)
  })
}

/** Quantità editabile sulla DDP commerciale: VER (ingresso) o DO (da ordinare). */
export function isCommercialQtyEditable(statusKey: string | null | undefined): boolean {
  const key = (statusKey ?? "").toUpperCase()
  return key === DDP_STATUS_VERIFY || key === DDP_STATUS_TO_ORDER
}
