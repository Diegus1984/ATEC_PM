import { canWriteFeature } from "@/lib/auth/permissions"

/**
 * Chi può ricodificare in Codex (nuova codifica manuale, 21/07/2026): assegna o rimuove il
 * «codice nuovo» sull'articolo. È una modifica, non una lettura, quindi `canWriteFeature`:
 * con la concessione in sola lettura i comandi devono sparire, non solo essere respinti dall'API.
 */
export function canRecodeCodex(): boolean {
  return canWriteFeature("action.recode_codex")
}

/**
 * Chi può scrivere il Codice ATEC su Danea (campo Extra1).
 *
 * Fino alla Fase E le due cose condividevano lo stesso helper, perché entrambe partivano dallo
 * stesso livello — ma sono due mestieri diversi e ora hanno due chiavi separate: la ricodifica
 * tocca il codice interno dell'articolo e vive solo dentro il Codex, la mappatura ATEC scrive
 * sull'archivio Danea e si comanda anche da fuori (Catalogo, distinta DDP commerciale). Chiavi
 * distinte = si può concedere l'una senza l'altra.
 */
export function canAssignAtecCode(): boolean {
  return canWriteFeature("action.assign_atec_code")
}
