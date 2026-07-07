import {
  apiDelete,
  apiGet,
  apiPost,
  apiPut,
  apiUpload,
  unwrapApi,
} from "@/lib/api/client"
import type { ApiResponse, TemplateFolderNode } from "@/lib/api/types"

export async function fetchProjectTemplateTree(): Promise<TemplateFolderNode[]> {
  const response = await apiGet<ApiResponse<TemplateFolderNode[]>>(
    "/api/project-templates/tree"
  )
  return unwrapApi(response)
}

export async function createTemplateFolder(
  name: string,
  parentId: number | null
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    "/api/project-templates/folders",
    { name, parentId, sortOrder: 0 }
  )
  return unwrapApi(response)
}

export async function updateTemplateFolder(
  id: number,
  name: string,
  parentId: number | null,
  sortOrder: number
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/project-templates/folders/${id}`,
    { name, parentId, sortOrder }
  )
  return unwrapApi(response)
}

export async function deleteTemplateFolder(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/project-templates/folders/${id}`
  )
  return unwrapApi(response)
}

export async function uploadTemplateFile(
  folderId: number,
  file: File
): Promise<number> {
  const formData = new FormData()
  formData.append("file", file)
  const response = await apiUpload<ApiResponse<number>>(
    `/api/project-templates/folders/${folderId}/upload`,
    formData
  )
  return unwrapApi(response)
}

export async function deleteTemplateFile(id: number): Promise<boolean> {
  const response = await apiDelete<ApiResponse<boolean>>(
    `/api/project-templates/files/${id}`
  )
  return unwrapApi(response)
}

export async function renameTemplateFile(
  id: number,
  name: string
): Promise<number> {
  const response = await apiPut<ApiResponse<number>>(
    `/api/project-templates/files/${id}`,
    { name }
  )
  return unwrapApi(response)
}

export async function moveTemplateFile(
  fileId: number,
  folderId: number
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/project-templates/files/${fileId}/move`,
    { folderId }
  )
  return unwrapApi(response)
}

export async function copyTemplateFile(
  fileId: number,
  folderId: number
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/project-templates/files/${fileId}/copy`,
    { folderId }
  )
  return unwrapApi(response)
}

export async function copyTemplateFolder(
  folderId: number,
  targetFolderId: number
): Promise<number> {
  const response = await apiPost<ApiResponse<number>>(
    `/api/project-templates/folders/${folderId}/copy`,
    { folderId: targetFolderId }
  )
  return unwrapApi(response)
}

/** Trova un nodo cartella per id nell'albero. */
export function findTemplateFolder(
  nodes: TemplateFolderNode[],
  folderId: number
): TemplateFolderNode | null {
  for (const node of nodes) {
    if (node.id === folderId) {
      return node
    }
    const child = findTemplateFolder(node.children, folderId)
    if (child) {
      return child
    }
  }
  return null
}

/** Elenco flat di tutte le cartelle (per select parent). */
export function flattenTemplateFolders(
  nodes: TemplateFolderNode[],
  depth = 0
): Array<{ id: number; label: string }> {
  const result: Array<{ id: number; label: string }> = []
  for (const node of nodes) {
    const prefix = depth > 0 ? `${"—".repeat(depth)} ` : ""
    result.push({ id: node.id, label: `${prefix}${node.name}` })
    result.push(...flattenTemplateFolders(node.children, depth + 1))
  }
  return result
}
