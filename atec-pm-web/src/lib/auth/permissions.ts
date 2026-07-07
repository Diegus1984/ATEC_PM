import { apiGet, unwrapApi } from "@/lib/api/client"
import type { ApiResponse, AuthFeatureDto, AuthFeaturesContextDto, AuthLevelDto } from "@/lib/api/types"

let userLevel = 0
let features = new Map<string, AuthFeatureDto>()
let levels: AuthLevelDto[] = []

export async function loadAuthFeatures(): Promise<void> {
  const response = await apiGet<ApiResponse<AuthFeaturesContextDto>>(
    "/api/auth-levels/features/my"
  )
  const context = unwrapApi(response)

  userLevel = context.userLevel
  levels = context.levels
  features = new Map(
    context.features.map((feature) => [feature.featureKey.toLowerCase(), feature])
  )
}

export function clearAuthFeatures(): void {
  userLevel = 0
  features = new Map()
  levels = []
}

/** Stessa logica di PermissionEngine.CanAccess in C#. */
export function canAccessFeature(featureKey: string): boolean {
  if (features.size === 0) {
    return true
  }

  const feature = features.get(featureKey.toLowerCase())
  if (!feature) {
    return true
  }

  return userLevel >= feature.minLevel
}

export function getUserLevel(): number {
  return userLevel
}

export function getAuthLevels(): AuthLevelDto[] {
  return levels
}

export function getAuthFeatures(): AuthFeatureDto[] {
  return Array.from(features.values())
}
