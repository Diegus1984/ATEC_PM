/** Template documentali di commessa — allineati a ATEC.PM.Shared/DTOs. */

export interface TemplateFolderNode {
  id: number
  parentId: number | null
  name: string
  sortOrder: number
  children: TemplateFolderNode[]
  files: TemplateFileItem[]
}

export interface TemplateFileItem {
  id: number
  fileName: string
  fileSize: number
  uploadedAt: string
}
