// ── Documenti di commessa: ordinamenti condivisi tra albero e griglia ──────

import type { FileItem } from "@/lib/api/types"

/** Cartelle prima dei file, poi per nome (collazione italiana). */
export function sortItems(items: FileItem[]): FileItem[] {
  return [...items].sort((a, b) => {
    if (a.isFolder !== b.isFolder) {
      return a.isFolder ? -1 : 1
    }
    return a.name.localeCompare(b.name, "it")
  })
}
