import { apiDelete, apiGet, apiPost, unwrapApi } from "@/lib/api/client"
import type {
  AddCompositionRequest,
  ApiResponse,
  CompositionTreeNode,
} from "@/lib/api/types"

/** Albero composizione ricorsivo di un composito Codex. */
export async function fetchCompositionTree(
  codexId: number
): Promise<CompositionTreeNode> {
  const response = await apiGet<ApiResponse<CompositionTreeNode>>(
    `/api/codex/compositions/tree/${codexId}`
  )
  return unwrapApi(response)
}

/** Appende `?conn=` (connectionId SignalR) per la self-exclusion: l'autore non riceve la propria notifica. */
function withConn(url: string, conn?: string | null): string {
  return conn ? `${url}?conn=${encodeURIComponent(conn)}` : url
}

/** Aggiunge un componente (Codex o Catalogo) alla composizione. `quantity` inserisce N righe. */
export async function addComposition(
  request: AddCompositionRequest,
  conn?: string | null
): Promise<void> {
  const response = await apiPost<ApiResponse<number>>(
    withConn("/api/codex/compositions", conn),
    request
  )
  unwrapApi(response)
}

export async function deleteComposition(
  compositionId: number,
  conn?: string | null
): Promise<void> {
  const response = await apiDelete<ApiResponse<boolean>>(
    withConn(`/api/codex/compositions/${compositionId}`, conn)
  )
  unwrapApi(response)
}
