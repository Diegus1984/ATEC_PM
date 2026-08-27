import { apiGet, apiPost, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, PagedResult } from "@/lib/api/types"

// Trasferimento catalogo Danea vecchio → Atec_PM (piano F2).

export interface DaneaMigrationStatus {
  oldArticles: number
  newArticles: number
  imagesSourceReachable: boolean
  imagesTargetReachable: boolean
  /** Perche' le immagini non si copiano (vuoto se si copiano). */
  imagesError?: string
  oldArchive: string
  newArchive: string
  /** Valori distinti per i filtri colonna (arrivano con lo status). */
  categories?: string[]
  subcategories?: string[]
  suppliers?: string[]
  manufacturers?: string[]
}

export interface DaneaOldArticle {
  idArticolo: number
  codArticolo: string
  descrizione: string
  categoria: string
  sottocategoria: string
  udm: string
  fornitore: string
  produttore: string
  prezzoForn: number
  extra1: string
  hasImage: boolean
  /** Già presente in Atec_PM: non ritrasferibile. */
  transferred: boolean
}

export interface DaneaTransferResult {
  idArticolo: number
  codArticolo: string
  outcome: "ok" | "skipped" | "error"
  error: string
  imagesCopied: number
  imageWarning: string
  /** ID effettivo in Atec_PM (diverso da idArticolo se l'ID era occupato). */
  idInAtecPm: number
  /** ID rimappato, fornitore copiato… (#129) */
  note: string
}

export interface DaneaTransferReport {
  /** Righe allineate nel Catalogo articoli subito dopo il lotto. */
  catalogAligned?: number
  /** Se l'allineamento non e' riuscito (gli articoli sono passati lo stesso). */
  catalogWarning?: string
  ok: number
  skipped: number
  errors: number
  imagesCopied: number
  results: DaneaTransferResult[]
}

export interface DaneaFilterOptions {
  categories: string[]
  subcategories: string[]
  suppliers: string[]
  manufacturers: string[]
}

/** Ripescaggio automatico dal vecchio archivio (#129). */
export interface DaneaPullStatus {
  enabled: boolean
  intervalHours: number
  /** false finché il primo giro non fissa lo spartiacque. */
  initialized: boolean
  lastSeenId?: number | null
  lastRunAt?: string | null
  isRunning: boolean
  lastMessage: string
  lastError?: string | null
}

export interface DaneaPullReport {
  message: string
  /** true se un trasferimento è stato eseguito (transfer valorizzato). */
  ran: boolean
  newArticles: number
  lastSeenId: number
  transfer?: DaneaTransferReport | null
}

export async function fetchDaneaMigrationStatus(): Promise<DaneaMigrationStatus> {
  const response = await apiGet<ApiResponse<DaneaMigrationStatus>>(
    "/api/danea-migration/status"
  )
  return unwrapApi(response)
}

export async function fetchDaneaMigrationFilterOptions(): Promise<DaneaFilterOptions> {
  const response = await apiGet<ApiResponse<DaneaFilterOptions>>(
    "/api/danea-migration/filter-options"
  )
  return unwrapApi(response)
}

export async function fetchDaneaOldArticles(params: {
  page: number
  pageSize?: number
  search?: string
  onlyMissing?: boolean
  /** Ordina per IDArticolo discendente: i codificati di recente in cima (#129). */
  recentFirst?: boolean
  /** Filtri per colonna (chiave = parametro server: codArticolo/descrizione/categoria/fornitore/extra1). */
  filters?: Record<string, string>
}): Promise<PagedResult<DaneaOldArticle>> {
  const query = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize ?? 50),
  })
  const search = params.search?.trim()
  if (search) query.set("search", search)
  if (params.onlyMissing) query.set("onlyMissing", "true")
  if (params.recentFirst) query.set("recentFirst", "true")
  if (params.filters) {
    for (const [key, value] of Object.entries(params.filters)) {
      const trimmed = value.trim()
      if (trimmed) query.set(key, trimmed)
    }
  }
  const response = await apiGet<ApiResponse<PagedResult<DaneaOldArticle>>>(
    `/api/danea-migration/old-articles?${query.toString()}`
  )
  return unwrapApi(response)
}

export async function transferDaneaArticles(
  articleIds: number[]
): Promise<DaneaTransferReport> {
  const response = await apiPost<ApiResponse<DaneaTransferReport>>(
    "/api/danea-migration/transfer",
    { articleIds }
  )
  return unwrapApi(response)
}

export async function fetchDaneaPullStatus(): Promise<DaneaPullStatus> {
  const response = await apiGet<ApiResponse<DaneaPullStatus>>(
    "/api/danea-migration/pull-status"
  )
  return unwrapApi(response)
}

export async function runDaneaPull(): Promise<DaneaPullReport> {
  const response = await apiPost<ApiResponse<DaneaPullReport>>(
    "/api/danea-migration/pull-old",
    {}
  )
  return unwrapApi(response)
}
