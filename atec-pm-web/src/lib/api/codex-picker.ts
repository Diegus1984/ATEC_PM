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
  unitArticolo: string
  costoArticolo: number | null
  supplierId: number | null
  fornitoreNome: string
  produttore: string
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
