/** Tipi trasversali (involucro API, paginazione, lookup) — allineati a ATEC.PM.Shared/DTOs. */

export interface ApiResponse<T> {
  success: boolean
  data?: T
  message: string
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  hasMore: boolean
}

export interface LookupItem {
  id: number
  name: string
}

export interface FieldUpdateRequest {
  field: string
  value: string | null
}
