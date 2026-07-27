import { apiDelete, apiGet, apiPost, apiPut, unwrapApi } from "@/lib/api/client"
import type {
  AddCompositionRequest,
  ApiResponse,
  CompositionChildItem,
  CompositionTreeNode,
  CodexImportItem,
  CodexImportPreviewResult,
  CodexImportCommitRequest,
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

/** Figli diretti (primo livello) della composizione di un articolo Codex. Vuoto = non è un padre. */
export async function fetchCompositionChildren(
  parentId: number
): Promise<CompositionChildItem[]> {
  const response = await apiGet<ApiResponse<CompositionChildItem[]>>(
    `/api/codex/compositions/${parentId}`
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

/** Aggiorna la quantità di una riga di composizione (sortOrder 0 = non toccare). */
export async function updateCompositionQuantity(
  compositionId: number,
  quantity: number,
  conn?: string | null
): Promise<void> {
  const response = await apiPut<ApiResponse<number>>(
    withConn(`/api/codex/compositions/${compositionId}`, conn),
    { quantity, sortOrder: 0 }
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

/** Richiede una preview degli articoli da importare verificandone l'esistenza e la validità. */
export async function fetchImportPreview(
  parentId: number,
  items: CodexImportItem[]
): Promise<CodexImportPreviewResult[]> {
  const response = await apiPost<ApiResponse<CodexImportPreviewResult[]>>(
    `/api/codex/compositions/import-preview/${parentId}`,
    items
  )
  return unwrapApi(response)
}

/** Esegue l'importazione finale degli articoli risolti nella composizione. */
export async function commitImport(
  request: CodexImportCommitRequest,
  conn?: string | null
): Promise<void> {
  const response = await apiPost<ApiResponse<boolean>>(
    withConn("/api/codex/compositions/import-commit", conn),
    request
  )
  unwrapApi(response)
}
