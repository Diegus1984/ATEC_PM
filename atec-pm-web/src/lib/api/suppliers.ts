import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  SupplierListItem,
  SupplierSaveRequest,
} from "@/lib/api/types"

export async function fetchSuppliers(): Promise<SupplierListItem[]> {
  const response = await apiGet<ApiResponse<SupplierListItem[]>>("/api/suppliers")
  return unwrapApi(response)
}

export async function fetchSupplier(id: number): Promise<SupplierSaveRequest> {
  const response = await apiGet<ApiResponse<SupplierSaveRequest>>(
    `/api/suppliers/${id}`
  )
  return unwrapApi(response)
}

/** Crea il fornitore e ritorna il nuovo id. */
export async function createSupplier(
  request: SupplierSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/suppliers", request)
  return unwrapApi(response)
}

export async function updateSupplier(
  id: number,
  request: SupplierSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/suppliers/${id}`,
    request
  )
  unwrapApi(response)
}

/** Disattivazione (soft delete: is_active=0). */
export async function deleteSupplier(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(`/api/suppliers/${id}`)
  unwrapApi(response)
}
