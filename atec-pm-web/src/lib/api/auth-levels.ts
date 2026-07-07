import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, AuthFeatureDto, AuthLevelDto } from "@/lib/api/types"

export async function fetchAuthLevels(): Promise<AuthLevelDto[]> {
  const response = await apiGet<ApiResponse<AuthLevelDto[]>>("/api/auth-levels")
  return unwrapApi(response)
}

export async function fetchAuthFeatures(): Promise<AuthFeatureDto[]> {
  const response = await apiGet<ApiResponse<AuthFeatureDto[]>>(
    "/api/auth-levels/features"
  )
  return unwrapApi(response)
}

export async function createAuthFeature(request: {
  featureKey: string
  displayName: string
  category: string
  minLevel: number
  behavior: string
}): Promise<void> {
  const response = await apiPost<ApiResponse<string>>(
    "/api/auth-levels/features",
    request
  )
  unwrapApi(response)
}

export async function updateAuthFeature(
  id: number,
  request: { minLevel: number; behavior: string }
): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    `/api/auth-levels/features/${id}`,
    request
  )
  unwrapApi(response)
}

export async function deleteAuthFeature(id: number): Promise<void> {
  const response = await apiDelete<ApiResponse<string>>(
    `/api/auth-levels/features/${id}`
  )
  unwrapApi(response)
}
