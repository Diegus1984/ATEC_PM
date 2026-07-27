// ── Distribuzione prezzo (logica pura) ─────────────────────
// Fedele a CostingTreeControl: righe = sezioni costo (R) + righe materiali (M)
// con vendita>0. Pesi contingency/margin proporzionali alla vendita tra le righe
// visibili non-pinnate; pinnate fisse; shadowed a 0 col costo spalmato sulle visibili.

export interface DistRow {
  rowType: "R" | "M"
  sectionId: number
  itemId: number
  name: string
  sale: number
  contingencyPct: number
  marginPct: number
  contingencyPinned: boolean
  marginPinned: boolean
  isShadowed: boolean
}

export interface DistComputed extends DistRow {
  contingencyAmount: number
  marginAmount: number
  shadowedAmount: number
  sectionTotal: number
}

export const distKey = (r: { rowType: string; sectionId: number; itemId: number }) =>
  `${r.rowType}_${r.sectionId}_${r.itemId}`
const round4 = (n: number) => Math.round(n * 10000) / 10000
const round2 = (n: number) => Math.round(n * 100) / 100

/** Ridistribuisce le % (contingency o margin) tra le righe visibili non-pinnate, proporzionali alla vendita. Muta `rows`. */
function rebalanceField(rows: DistRow[], isCont: boolean): void {
  let pinnedSum = 0
  const unpinned: DistRow[] = []
  for (const r of rows) {
    if (r.isShadowed) {
      if (isCont) r.contingencyPct = 0
      else r.marginPct = 0
      continue
    }
    if (r.sale === 0) continue
    const pinned = isCont ? r.contingencyPinned : r.marginPinned
    if (pinned) pinnedSum += isCont ? r.contingencyPct : r.marginPct
    else unpinned.push(r)
  }
  const remaining = Math.max(0, 1 - pinnedSum)
  const totalSale = unpinned.reduce((a, r) => a + r.sale, 0)
  for (const r of unpinned) {
    const v =
      totalSale > 0
        ? round4((r.sale / totalSale) * remaining)
        : round4(remaining / Math.max(1, unpinned.length))
    if (isCont) r.contingencyPct = v
    else r.marginPct = v
  }
}

/** Clona, ribilancia contingency+margin, calcola importi (peso×pool) e spalma le shadowed. */
export function computeDist(rows: DistRow[], contPool: number, margPool: number): DistComputed[] {
  const work = rows.map((r) => ({ ...r }))
  rebalanceField(work, true)
  rebalanceField(work, false)
  const out: DistComputed[] = work.map((r) => ({
    ...r,
    contingencyAmount: 0,
    marginAmount: 0,
    shadowedAmount: 0,
    sectionTotal: 0,
  }))
  for (const r of out) {
    if (r.isShadowed) {
      r.contingencyPct = 0
      r.marginPct = 0
    } else {
      r.contingencyAmount = r.contingencyPct * contPool
      r.marginAmount = r.marginPct * margPool
      r.sectionTotal = r.sale + r.contingencyAmount + r.marginAmount
    }
  }
  const shadowedSale = out.filter((r) => r.isShadowed).reduce((a, r) => a + r.sale, 0)
  const visibleSale = out.filter((r) => !r.isShadowed).reduce((a, r) => a + r.sale, 0)
  if (shadowedSale > 0 && visibleSale > 0) {
    for (const r of out) {
      if (r.isShadowed) continue
      const quota = r.sale / visibleSale
      r.shadowedAmount = round2(shadowedSale * quota)
      r.sectionTotal += r.shadowedAmount
    }
  }
  return out
}
