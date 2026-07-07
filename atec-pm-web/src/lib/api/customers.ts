import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  CustomerListItem,
  CustomerSaveRequest,
} from "@/lib/api/types"

export async function fetchCustomers(): Promise<CustomerListItem[]> {
  const response = await apiGet<ApiResponse<CustomerListItem[]>>("/api/customers")
  return unwrapApi(response)
}

export async function fetchCustomer(id: number): Promise<CustomerSaveRequest> {
  const response = await apiGet<ApiResponse<CustomerSaveRequest>>(
    `/api/customers/${id}`
  )
  return unwrapApi(response)
}

/** Crea il cliente e ritorna il nuovo id. */
export async function createCustomer(
  request: CustomerSaveRequest
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>("/api/customers", request)
  return unwrapApi(response)
}

export async function updateCustomer(
  id: number,
  request: CustomerSaveRequest
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/customers/${id}`,
    request
  )
  unwrapApi(response)
}

/** Disattivazione (soft delete: is_active=0). */
export async function deleteCustomer(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(`/api/customers/${id}`)
  unwrapApi(response)
}
