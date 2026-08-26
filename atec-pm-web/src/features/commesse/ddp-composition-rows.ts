/**
 * Raggruppamento «composizione» delle griglie DDP, condiviso fra officina e commerciale (#119).
 *
 * Dal 25/08/2026 i componenti di un gruppo Codex (5xx) si dividono fra le due distinte, e
 * l'intestazione collassabile deve comportarsi identica in tutte e due: stesso ordinamento
 * padre→figli, stesso rollup dei costi sul padre, stessa numerazione. Scriverlo due volte
 * significava vederlo divergere al primo ritocco, quindi la regola sta qui e le due griglie
 * passano solo l'accessore del proprio campo padre (`parentOfficinaItemId` di là,
 * `parentBomItemId` di qua: tabelle diverse, stessa idea).
 */

/** Il minimo che una riga deve avere per essere raggruppata. */
export interface CompositionRowBase {
  id: number
  partNumber: string
  quantity: number
  compositionQty?: number | null
}

/** Id delle righe che hanno almeno un componente importato dalla composizione. */
export function collectParentIds<T>(
  list: T[],
  parentIdOf: (row: T) => number | null | undefined
): Set<number> {
  const set = new Set<number>()
  for (const item of list) {
    const pid = parentIdOf(item)
    if (pid != null) set.add(pid)
  }
  return set
}

/**
 * Ordina la distinta padre→componenti (i figli per codice, gli orfani in coda) e calcola i
 * costi: il costo unitario di un padre con componenti è la **somma dei figli × la loro
 * quantità di composizione**, non il costo scritto sulla sua riga.
 *
 * 🪤 Il costo può essere `null` per chi non ha il micro «vede prezzi» (§12.3): il rollup
 * resta `null` invece di diventare 0, altrimenti un utente senza permessi vedrebbe un
 * totale finto al posto del trattino.
 */
export function buildCompositionRows<T extends CompositionRowBase>(
  list: T[],
  parentIdOf: (row: T) => number | null | undefined,
  parentIdsWithChildren: Set<number>,
  unitCostOf: (row: T) => number | null
): (T & { rowNumber: string; unitCost: number | null; totalCost: number | null })[] {
  const parents = list.filter((it) => parentIdOf(it) == null)
  const children = list.filter((it) => parentIdOf(it) != null)

  const childrenByParent: Record<number, T[]> = {}
  for (const child of children) {
    const pid = parentIdOf(child)!
    if (!childrenByParent[pid]) childrenByParent[pid] = []
    childrenByParent[pid].push(child)
  }
  for (const pid of Object.keys(childrenByParent)) {
    childrenByParent[Number(pid)].sort((a, b) =>
      (a.partNumber || "").localeCompare(b.partNumber || "", undefined, {
        numeric: true,
        sensitivity: "base",
      })
    )
  }

  // I figli il cui padre non è in elenco (filtri di stato, riga cancellata) finiscono in
  // coda invece di sparire: una riga che non si vede più è un pezzo che nessuno ordina.
  const daPiazzare = new Set(Object.keys(childrenByParent).map(Number))
  const sorted: T[] = []
  for (const parent of parents) {
    sorted.push(parent)
    const figli = childrenByParent[parent.id]
    if (figli) {
      sorted.push(...figli)
      daPiazzare.delete(parent.id)
    }
  }
  for (const pid of daPiazzare) sorted.push(...childrenByParent[pid])

  let parentCount = 0
  return sorted.map((item) => {
    let rowNumber = "•"
    if (parentIdOf(item) == null) {
      parentCount++
      rowNumber = String(parentCount)
    }

    let unitCost = unitCostOf(item)
    if (parentIdsWithChildren.has(item.id)) {
      const figli = childrenByParent[item.id] ?? []
      const costi = figli.map((f) => unitCostOf(f))
      unitCost = costi.some((c) => c == null)
        ? null
        : costi.reduce<number>(
            (sum, c, i) => sum + (c as number) * (figli[i].compositionQty ?? 1),
            0
          )
    }

    return {
      ...item,
      rowNumber,
      unitCost,
      totalCost: unitCost == null ? null : unitCost * item.quantity,
    }
  })
}
