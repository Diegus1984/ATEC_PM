import * as React from "react"

/**
 * Visibilità colonne persistita in localStorage (stesso contratto di DataTableCard).
 * `defaults` vengono uniti al salvato così le colonne nuove restano visibili di default.
 */
export function usePersistedColumnVisibility(
  storageKey: string,
  defaults: Record<string, boolean> = {}
): [
  Record<string, boolean>,
  React.Dispatch<React.SetStateAction<Record<string, boolean>>>,
] {
  const [visibility, setVisibility] = React.useState<Record<string, boolean>>(
    () => {
      try {
        const saved = localStorage.getItem(storageKey)
        if (saved) {
          const parsed = JSON.parse(saved) as Record<string, boolean>
          return { ...defaults, ...parsed }
        }
      } catch {
        /* ignore */
      }
      return { ...defaults }
    }
  )

  React.useEffect(() => {
    try {
      localStorage.setItem(storageKey, JSON.stringify(visibility))
    } catch {
      /* ignore */
    }
  }, [storageKey, visibility])

  return [visibility, setVisibility]
}
