import {
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  apiUpload,
  unwrapApi,
} from "@/lib/api/client"
import type {
  ApiResponse,
  CategoryMoveRequest,
  ProductMoveRequest,
  QuoteCatalogImportDto,
  QuoteCatalogTreeDto,
  QuoteCategoryDto,
  QuoteCategorySaveDto,
  QuoteGroupDto,
  QuoteGroupSaveDto,
  QuotePriceListDto,
  QuotePriceListSaveDto,
  QuoteProductDto,
  QuoteProductSaveDto,
} from "@/lib/api/types"

const BASE = "/api/quote-catalog"

// ── Listini ────────────────────────────────────────────────

export async function fetchPriceLists(): Promise<QuotePriceListDto[]> {
  const response = await apiGet<ApiResponse<QuotePriceListDto[]>>(
    `${BASE}/price-lists`
  )
  return unwrapApi(response)
}

export async function createPriceList(
  request: QuotePriceListSaveDto
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${BASE}/price-lists`,
    request
  )
  return unwrapApi(response)
}

export async function updatePriceList(
  id: number,
  request: QuotePriceListSaveDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${BASE}/price-lists/${id}`,
    request
  )
  unwrapApi(response)
}

/** Elimina listino (i gruppi orfani passano a price_list_id=NULL). */
export async function deletePriceList(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `${BASE}/price-lists/${id}`
  )
  unwrapApi(response)
}

// ── Albero completo (gruppi → categorie → prodotti → varianti) ──

export async function fetchCatalogTree(
  priceListId?: number
): Promise<QuoteCatalogTreeDto> {
  const query =
    priceListId !== undefined ? `?priceListId=${priceListId}` : ""
  const response = await apiGet<ApiResponse<QuoteCatalogTreeDto>>(
    `${BASE}/tree${query}`
  )
  return unwrapApi(response)
}

// ── Gruppi ─────────────────────────────────────────────────

export async function fetchGroups(
  priceListId?: number
): Promise<QuoteGroupDto[]> {
  const query =
    priceListId !== undefined ? `?priceListId=${priceListId}` : ""
  const response = await apiGet<ApiResponse<QuoteGroupDto[]>>(
    `${BASE}/groups${query}`
  )
  return unwrapApi(response)
}

export async function createGroup(request: QuoteGroupSaveDto): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(`${BASE}/groups`, request)
  return unwrapApi(response)
}

export async function updateGroup(
  id: number,
  request: QuoteGroupSaveDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${BASE}/groups/${id}`,
    request
  )
  unwrapApi(response)
}

export async function deleteGroup(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`${BASE}/groups/${id}`)
  unwrapApi(response)
}

// ── Categorie (albero via parentId) ────────────────────────

export async function fetchCategories(
  groupId?: number
): Promise<QuoteCategoryDto[]> {
  const query = groupId !== undefined ? `?groupId=${groupId}` : ""
  const response = await apiGet<ApiResponse<QuoteCategoryDto[]>>(
    `${BASE}/categories${query}`
  )
  return unwrapApi(response)
}

export async function createCategory(
  request: QuoteCategorySaveDto
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${BASE}/categories`,
    request
  )
  return unwrapApi(response)
}

export async function updateCategory(
  id: number,
  request: QuoteCategorySaveDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${BASE}/categories/${id}`,
    request
  )
  unwrapApi(response)
}

/** Sposta una categoria (cambia gruppo e/o parent; il server blocca gli auto-cicli). */
export async function moveCategory(
  id: number,
  request: CategoryMoveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${BASE}/categories/${id}/move`,
    request
  )
  unwrapApi(response)
}

export async function deleteCategory(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `${BASE}/categories/${id}`
  )
  unwrapApi(response)
}

// ── Prodotti ───────────────────────────────────────────────

export interface FetchProductsParams {
  categoryId?: number
  groupId?: number
}

/** Prodotti con varianti. Con categoryId include le sotto-categorie ricorsivamente. */
export async function fetchProducts(
  params: FetchProductsParams = {}
): Promise<QuoteProductDto[]> {
  const query = new URLSearchParams()
  if (params.categoryId !== undefined) {
    query.set("categoryId", String(params.categoryId))
  }
  if (params.groupId !== undefined) {
    query.set("groupId", String(params.groupId))
  }
  const qs = query.toString()
  const response = await apiGet<ApiResponse<QuoteProductDto[]>>(
    `${BASE}/products${qs ? `?${qs}` : ""}`
  )
  return unwrapApi(response)
}

export async function fetchProduct(id: number): Promise<QuoteProductDto> {
  const response = await apiGet<ApiResponse<QuoteProductDto>>(
    `${BASE}/products/${id}`
  )
  return unwrapApi(response)
}

/** Crea prodotto con varianti. Ritorna l'id. */
export async function createProduct(
  request: QuoteProductSaveDto
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${BASE}/products`,
    request
  )
  return unwrapApi(response)
}

/**
 * Aggiorna prodotto e varianti (elimina le mancanti, aggiorna id>0, inserisce id==0).
 * Ripulisce dal disco le immagini rimosse dalla descrizione.
 */
export async function updateProduct(
  id: number,
  request: QuoteProductSaveDto
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${BASE}/products/${id}`,
    request
  )
  unwrapApi(response)
}

/** Sposta il prodotto in un'altra categoria. */
export async function moveProduct(
  id: number,
  request: ProductMoveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `${BASE}/products/${id}/move`,
    request
  )
  unwrapApi(response)
}

/** Clona il prodotto (code +"-COPY", name +" (copia)") con tutte le varianti. Ritorna l'id del clone. */
export async function duplicateProduct(id: number): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `${BASE}/products/${id}/duplicate`
  )
  return unwrapApi(response)
}

/** Elimina prodotto e varianti; ripulisce dal disco le immagini orfane. */
export async function deleteProduct(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(`${BASE}/products/${id}`)
  unwrapApi(response)
}

/** Bonifica: rimuove le immagini base64 inline (residuo CKEditor) da description_rtf. */
export async function cleanupProductImages(): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    `${BASE}/products/cleanup-images`
  )
  unwrapApi(response)
}

/**
 * Carica un'immagine per la descrizione prodotto (campo form `file`, max 50 MB).
 * Ritorna il path RELATIVO (`/uploads/cms/products/...`) da inserire nell'<img src>.
 * Accetta un Blob (es. da TinyMCE `blobInfo.blob()`) con nome file opzionale.
 */
export async function uploadProductImage(
  file: Blob,
  filename = "image.png"
): Promise<string> {
  const form = new FormData()
  form.append("file", file, filename)
  const response = await apiUpload<ApiResponse<string>>(
    `${BASE}/products/upload`,
    form
  )
  return unwrapApi(response)
}

// ── Import massivo (listini→gruppi→categorie→prodotti→varianti) ──

export async function importCatalog(
  request: QuoteCatalogImportDto
): Promise<string> {
  const response = await apiPost<ApiResponse<string>>(`${BASE}/import`, request)
  return unwrapApi(response)
}
