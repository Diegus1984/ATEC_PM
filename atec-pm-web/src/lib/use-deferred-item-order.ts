import * as React from "react"

/**
 * Ordine visuale delle righe congelato tra un «Aggiorna» e l'altro.
 *
 * Le modifiche ai campi (priorità, stato, data, …) aggiornano i valori in tabella
 * senza riposizionare le righe, anche quando l'ordine arriva già fatto dal server
 * e la scrittura provoca un refetch. `layoutEpoch` (pulsante Aggiorna) e `resortKey`
 * (cambio vista/filtro o criterio di ordinamento) riapplicano `sortItems`.
 *
 * Usato da: Check List, foglio MoM, Lavorazioni Officine (le quattro viste),
 * DDP Officina di commessa, Inbox Acquisti.
 */
export function useDeferredItemOrder<T extends { id: number }>(
  items: T[],
  sortItems: (items: T[]) => T[],
  layoutEpoch: number,
  resortKey?: string | number | null
): T[] {
  const [orderIds, setOrderIds] = React.useState<number[]>([])
  const sortRef = React.useRef(sortItems)
  sortRef.current = sortItems
  const itemsRef = React.useRef(items)
  itemsRef.current = items

  const applySort = React.useCallback(() => {
    setOrderIds(sortRef.current(itemsRef.current).map((item) => item.id))
  }, [])

  React.useEffect(() => {
    applySort()
  }, [layoutEpoch, resortKey, applySort])

  React.useEffect(() => {
    setOrderIds((prev) => {
      const idSet = new Set(items.map((item) => item.id))
      if (prev.length === 0) {
        return sortRef.current(items).map((item) => item.id)
      }
      const kept = prev.filter((id) => idSet.has(id))
      if (kept.length === 0 && items.length > 0) {
        return sortRef.current(items).map((item) => item.id)
      }
      const keptSet = new Set(kept)
      const newcomers = sortRef
        .current(items.filter((item) => !keptSet.has(item.id)))
        .map((item) => item.id)
      if (kept.length === prev.length && newcomers.length === 0) return prev
      return [...kept, ...newcomers]
    })
  }, [items])

  return React.useMemo(() => {
    const map = new Map(items.map((item) => [item.id, item]))
    const ordered = orderIds
      .map((id) => map.get(id))
      .filter((row): row is T => row != null)
    if (ordered.length === items.length) return ordered
    const known = new Set(orderIds)
    const missing = sortRef.current(items.filter((item) => !known.has(item.id)))
    return [...ordered, ...missing]
  }, [orderIds, items])
}
