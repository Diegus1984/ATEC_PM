import { apiGet, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  ApiResponse,
  AuthFeatureDto,
  AuthLevelDto,
  AuthRoleFeatureDto,
  FeatureAccess,
} from "@/lib/api/types"

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

/** Concessioni per ruolo (ruoli di reparto: è la loro lista bianca). */
export async function fetchRoleFeatures(): Promise<AuthRoleFeatureDto[]> {
  const response = await apiGet<ApiResponse<AuthRoleFeatureDto[]>>(
    "/api/auth-levels/role-features"
  )
  return unwrapApi(response)
}

/** Assegna una concessione a un ruolo; `access: null` la revoca. */
export async function setRoleFeature(request: {
  roleName: string
  featureKey: string
  access: FeatureAccess | null
}): Promise<void> {
  const response = await apiPut<ApiResponse<string>>(
    "/api/auth-levels/role-features",
    request
  )
  unwrapApi(response)
}

// 🧹 `createAuthFeature` / `deleteAuthFeature` sono usciti col passo 7 del rebuild: dal passo 2
// `auth_features` è la proiezione di `catalogo-permessi.json` (EnsureCatalogo la riallinea a
// ogni avvio), quindi una funzione si registra aggiungendo la voce al file — non da un form,
// che creerebbe chiavi che nessun endpoint usa o cancellerebbe righe che tornano al riavvio.

/** Livello minimo e comportamento: manopole del motore VECCHIO (restano per il rollback). */
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
