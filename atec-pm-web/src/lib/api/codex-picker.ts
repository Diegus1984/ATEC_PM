import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, PagedResult } from "@/lib/api/types"

/**
 * Riga della vista Codex del picker DDP (#128): un codice ATEC con (eventualmente)
 * UNO dei suoi articoli Danea abbinati — un abbinamento = una riga, così codice
 * fornitore e produttore si cercano e si scelgono direttamente.
 */
export interface CodexPickerRow {
  codexId: number
  codiceAtec: string
  descr: string
  umCodex: string
  fornitoreCodex: string
  prezzoCodex: number | null
  catalogItemId: number | null
  codiceArticolo: string
  /** «Cod. prod. forn.» di Danea: come lo chiama il produttore (KTR M19…). */
  codiceFornitore: string
  unitArticolo: string
  costoArticolo: number | null
  supplierId: number | null
  fornitoreNome: string
  produttore: string
  /**
   * #142, solo righe di `derivati-101`: il 201 di derivazione del lavorato. Lì
   * codexId/codiceAtec sono del 101, articolo/fornitore/costo del grezzo.
   * Null/vuoto = riga del picker normale.
   */
  grezzoCodexId?: number | null
  grezzoCodice?: string
}

export async function fetchCodexPickerRows(params: {
  page?: number
  pageSize?: number
  codicePrefixes?: string[]
  /** Chiavi = parametri server: codice, descr, articolo, fornitore, produttore. */
  filters?: Record<string, string>
}): Promise<PagedResult<CodexPickerRow>> {
  const query = new URLSearchParams({
    page: String(params.page ?? 1),
    pageSize: String(params.pageSize ?? 50),
  })
  if (params.codicePrefixes && params.codicePrefixes.length > 0) {
    query.set("codicePrefixes", params.codicePrefixes.join(","))
  }
  if (params.filters) {
    for (const [key, value] of Object.entries(params.filters)) {
      const trimmed = value.trim()
      if (trimmed) query.set(key, trimmed)
    }
  }
  const response = await apiGet<ApiResponse<PagedResult<CodexPickerRow>>>(
    `/api/codex/picker?${query.toString()}`
  )
  return unwrapApi(response)
}

/**
 * #142 — i lavorati 101 con grezzo commerciale (derivazione #135), visti dal lato
 * acquisti: articolo/fornitore/costo vengono dall'abbinamento Danea del 201.
 * Un abbinamento = una riga; un 201 senza articoli resta visibile (caso «scoperto»).
 */
export async function fetchCodexDerivati101(params: {
  page?: number
  pageSize?: number
  /** Chiavi = parametri server: codice, descr, articolo, fornitore, produttore. */
  filters?: Record<string, string>
}): Promise<PagedResult<CodexPickerRow>> {
  const query = new URLSearchParams({
    page: String(params.page ?? 1),
    pageSize: String(params.pageSize ?? 50),
  })
  if (params.filters) {
    for (const [key, value] of Object.entries(params.filters)) {
      const trimmed = value.trim()
      if (trimmed) query.set(key, trimmed)
    }
  }
  const response = await apiGet<ApiResponse<PagedResult<CodexPickerRow>>>(
    `/api/codex/picker/derivati-101?${query.toString()}`
  )
  return unwrapApi(response)
}
