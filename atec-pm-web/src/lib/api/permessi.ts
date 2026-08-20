import { apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  EsitoApplicaClasseDto,
  RigaPermessiDto,
  SchedaPermessiDto,
  StatoCombo,
} from "@/lib/api/types"

/** Elenco delle persone attive con un'utenza. */
export async function fetchElencoPermessi(): Promise<RigaPermessiDto[]> {
  const response = await apiGet<ApiResponse<RigaPermessiDto[]>>("/api/permessi")
  return unwrapApi(response)
}

/** La scheda di una persona: 9 aree, catalogo intero, ultime 20 modifiche. */
export async function fetchSchedaPermessi(
  employeeId: number
): Promise<SchedaPermessiDto> {
  const response = await apiGet<ApiResponse<SchedaPermessiDto>>(
    `/api/permessi/${employeeId}`
  )
  return unwrapApi(response)
}

/**
 * Cambia una combo. Si passa `areaId` (una delle 9 aree, che può comandare due chiavi)
 * OPPURE `featureKey` (una singola funzione avanzata) — mai tutti e due.
 * Quello che si tocca qui diventa `MANO`: «Applica classe» smetterà di sovrascriverlo.
 */
export async function impostaPermesso(request: {
  employeeId: number
  areaId?: string
  featureKey?: string
  stato: StatoCombo
}): Promise<void> {
  const response = await apiPut<ApiResponse<null>>("/api/permessi/combo", request)
  unwrapApi(response)
}

/**
 * Applica il pacchetto della classe.
 *
 * ⚠️ Con `anteprima: true` **non scrive niente** e torna l'elenco esatto dei cambi: è quello
 * che l'utente conferma. Chiamarla direttamente con `anteprima: false` senza aver mostrato
 * l'anteprima è esattamente il gesto che il piano vieta (§4.4), perché un timbro di massa può
 * cancellare in silenzio le eccezioni messe apposta sulle singole persone.
 */
export async function applicaClasse(request: {
  employeeIds: number[]
  anteprima: boolean
}): Promise<EsitoApplicaClasseDto> {
  const response = await apiPost<ApiResponse<EsitoApplicaClasseDto>>(
    "/api/permessi/applica-classe",
    request
  )
  return unwrapApi(response)
}

/** Copia i permessi di un collega: arriva tutto, marcato `MANO`. */
export async function copiaPermessi(request: {
  daEmployeeId: number
  aEmployeeId: number
}): Promise<void> {
  const response = await apiPost<ApiResponse<null>>("/api/permessi/copia", request)
  unwrapApi(response)
}

/** Riporta al valore della classe una funzione, o tutte se `featureKey` è omesso. */
export async function riallineaAllaClasse(request: {
  employeeId: number
  featureKey?: string
}): Promise<EsitoApplicaClasseDto> {
  const response = await apiPost<ApiResponse<EsitoApplicaClasseDto>>(
    "/api/permessi/riallinea",
    request
  )
  return unwrapApi(response)
}
