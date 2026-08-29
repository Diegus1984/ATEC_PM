import { canAccessFeature } from "@/lib/auth/permissions"
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

/**
 * Privilegio che libera dalla matrice (segnalazione #140): chi ce l'ha sceglie qualunque
 * stato. Serve a rimettere in riga un collega che ha sbagliato assegnazione — senza,
 * la matrice rende certi errori definitivi. Stessa chiave che controlla il server.
 */
export const DDP_MATRICE_SCAVALCABILE = "action.ddp_status_override"

/**
 * La matrice da applicare a CHI STA GUARDANDO: `undefined` per chi ha il privilegio
 * (#140), e `undefined` è già il modo con cui `filterStatusOptions` dice «finestra
 * completa». Si chiama dove la mappa viene costruita, non nelle celle: così la regola
 * sta in un punto solo e le griglie restano ignare di chi è l'utente.
 */
export function ddpTransitionsPerUtente(
  transitions: Record<string, string[]>
): Record<string, string[]> | undefined {
  return canAccessFeature(DDP_MATRICE_SCAVALCABILE) ? undefined : transitions
}

/** Quantità editabile sulla DDP commerciale: VER (ingresso) o DO (da ordinare). */
export function isCommercialQtyEditable(statusKey: string | null | undefined): boolean {
  const key = (statusKey ?? "").toUpperCase()
  return key === DDP_STATUS_VERIFY || key === DDP_STATUS_TO_ORDER
}
