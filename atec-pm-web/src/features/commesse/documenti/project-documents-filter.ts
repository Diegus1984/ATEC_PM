import type { FileItem, FileTreeItem } from "@/lib/api/types"

/** Uniforma i separatori di percorso (l'API file-tree può restituire backslash). */
export function normalizeDocPath(path: string): string {
  return path.replace(/\\/g, "/")
}

export function docNameMatches(name: string, term: string): boolean {
  if (!term) return true
  return name.toLowerCase().includes(term.toLowerCase())
}

/** Filtra una lista piatta (contenuto cartella corrente). */
export function filterFileItems(items: FileItem[], term: string): FileItem[] {
  const trimmed = term.trim()
  if (!trimmed) return items
  return items.filter((item) => docNameMatches(item.name, trimmed))
}

/**
 * Filtra l'albero documenti: mantiene nodi il cui nome corrisponde o che hanno
 * discendenti corrispondenti. Se una cartella corrisponde, mostra tutti i figli.
 */
export function filterFileTree(
  items: FileTreeItem[],
  term: string
): FileTreeItem[] {
  const trimmed = term.trim()
  if (!trimmed) return items

  const result: FileTreeItem[] = []
  for (const item of items) {
    const path = normalizeDocPath(item.relativePath)
    if (item.isFolder) {
      const filteredChildren = filterFileTree(item.children, trimmed)
      if (docNameMatches(item.name, trimmed)) {
        result.push({ ...item, relativePath: path })
      } else if (filteredChildren.length > 0) {
        result.push({ ...item, relativePath: path, children: filteredChildren })
      }
    } else if (docNameMatches(item.name, trimmed)) {
      result.push({ ...item, relativePath: path })
    }
  }
  return result
}

/** Raccoglie tutti i percorsi cartella in un albero (per auto-espansione in ricerca). */
export function collectFolderPaths(items: FileTreeItem[]): string[] {
  const paths: string[] = []
  for (const item of items) {
    if (!item.isFolder) continue
    paths.push(normalizeDocPath(item.relativePath))
    paths.push(...collectFolderPaths(item.children))
  }
  return paths
}
