import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  HrBadges,
  HrCartellinoMese,
  HrImportEsito,
  HrMappaturaRiga,
  HrStato,
} from "@/lib/api/types"

/**
 * Cartellino mensile. Senza `employeeId` il server risponde il PROPRIO: chi ha la sola
 * lettura vede solo sé stesso, il cartellino altrui richiede la scrittura su
 * `nav.hr_timbrature` (403 con messaggio chiaro, non un mese vuoto).
 */
export async function fetchHrCartellino(
  anno: number,
  mese: number,
  employeeId?: number | null
): Promise<HrCartellinoMese> {
  const extra = employeeId != null ? `&employeeId=${employeeId}` : ""
  const r = await apiGet<ApiResponse<HrCartellinoMese>>(
    `/api/hr/cartellino?anno=${anno}&mese=${mese}${extra}`
  )
  return unwrapApi(r)
}

export async function fetchHrStato(): Promise<HrStato> {
  const r = await apiGet<ApiResponse<HrStato>>("/api/hr/stato")
  return unwrapApi(r)
}

/** Import a mano da Ecos; `completo` ignora il cursore e ripassa tutto lo storico
 *  (serve dopo aver collegato un dipendente nuovo: le sue timbrature vecchie
 *  erano state scartate). */
export async function importaTimbrature(completo = false): Promise<HrImportEsito> {
  const r = await apiPost<ApiResponse<HrImportEsito>>(
    `/api/hr/import${completo ? "?completo=true" : ""}`
  )
  return unwrapApi(r)
}

export async function fetchHrMappatura(): Promise<HrMappaturaRiga[]> {
  const r = await apiGet<ApiResponse<HrMappaturaRiga[]>>("/api/hr/mappatura")
  return unwrapApi(r)
}

/** I badge letti VIVI da Ecos; `configurato=false` = credenziali assenti sul server. */
export async function fetchHrBadges(): Promise<HrBadges> {
  const r = await apiGet<ApiResponse<HrBadges>>("/api/hr/mappatura/badges")
  return unwrapApi(r)
}

export async function salvaHrMappatura(
  employeeId: number,
  ecosEmplCode: string | null
): Promise<void> {
  const r = await apiPut<ApiResponse<boolean>>(`/api/hr/mappatura/${employeeId}`, {
    ecosEmplCode,
  })
  unwrapApi(r)
}

export async function inviaHrRettifica(payload: {
  employeeId: number
  /** ISO locale «yyyy-MM-ddTHH:mm:ss». */
  orario: string
  verso: "IN" | "OUT"
  motivo: string
}): Promise<void> {
  const r = await apiPost<ApiResponse<boolean>>("/api/hr/rettifica", payload)
  unwrapApi(r)
}

export async function eliminaHrRettifica(id: number): Promise<void> {
  const r = await apiDelete<ApiResponse<boolean>>(`/api/hr/rettifica/${id}`)
  unwrapApi(r)
}
