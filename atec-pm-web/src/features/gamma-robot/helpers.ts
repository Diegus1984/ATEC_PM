import type {
  GammaDistintaItemDto,
  GammaQuadroDto,
  GammaRobotDto,
} from "@/lib/api/types"

import { GAMMA_SEZIONI } from "./constants"

export function buildQuadroLabel(q: GammaQuadroDto): string {
  const parts: string[] = []
  if (q.controllore?.trim()) parts.push(q.controllore)
  if (q.generazione?.trim() && q.generazione !== q.controllore) {
    parts.push(`[${q.generazione}]`)
  }
  if (q.payload?.trim()) parts.push(`${q.payload}kg`)
  if (q.areaLavoro?.trim()) parts.push(`${q.areaLavoro}m`)
  const head = parts.length > 0 ? parts.join("  ") : "Quadro"
  return `${head}  (${q.componentiCount})`
}

export function buildQuadroSubtitle(q: GammaQuadroDto): string {
  const parts: string[] = []
  if (q.controllore?.trim()) parts.push(`Controllore ${q.controllore}`)
  if (q.generazione?.trim()) parts.push(`Gen. ${q.generazione}`)
  if (q.payload?.trim()) parts.push(`Payload ${q.payload} kg`)
  if (q.areaLavoro?.trim()) parts.push(`Area ${q.areaLavoro} m`)
  if (q.osVersion?.trim()) parts.push(`OS ${q.osVersion}`)
  return parts.join("   ·   ")
}

export function filterRobots(
  robots: GammaRobotDto[],
  filter: string
): GammaRobotDto[] {
  const f = filter.trim().toLowerCase()
  if (!f) return robots
  return robots.filter(
    (r) =>
      r.modello.toLowerCase().includes(f) ||
      (r.serie ?? "").toLowerCase().includes(f)
  )
}

/** Filtro con jolly `*` (come WPF WildMatch). */
export function wildMatch(value: string | null | undefined, filter: string): boolean {
  if (!filter) return true
  const v = (value ?? "").toLowerCase()
  const f = filter.toLowerCase()
  const startsWild = f.startsWith("*")
  const endsWild = f.endsWith("*")
  if (startsWild && endsWild) return v.includes(f.replaceAll("*", ""))
  if (endsWild) return v.startsWith(f.replace(/\*+$/, ""))
  if (startsWild) return v.endsWith(f.replace(/^\*+/, ""))
  return v.includes(f)
}

export function sezioneForCategoria(categoria: string | null | undefined): string {
  if (categoria && (GAMMA_SEZIONI as readonly string[]).includes(categoria)) {
    return categoria
  }
  return GAMMA_SEZIONI[0]
}

export interface GammaSlotRow {
  sezione: string | null
  slot: string | null
  productId: number
  productCode: string | null
  productName: string | null
  prezzoVb: number | null
  isOptional: boolean
  principal: GammaDistintaItemDto
  alternatives: GammaDistintaItemDto[]
}

/** Raggruppa distinta per (sezione, slot): principale + alternative. */
export function groupDistintaSlots(items: GammaDistintaItemDto[]): GammaSlotRow[] {
  const map = new Map<string, GammaDistintaItemDto[]>()
  for (const item of items) {
    const key = `${item.sezione ?? ""}\0${item.slot ?? ""}`
    const list = map.get(key)
    if (list) list.push(item)
    else map.set(key, [item])
  }

  const rows: GammaSlotRow[] = []
  for (const g of map.values()) {
    const principal =
      g.find((x) => !x.isAlternate && !x.isOptional) ??
      g.find((x) => !x.isAlternate) ??
      g[0]
    const alternatives = g.filter((x) => x !== principal)
    rows.push({
      sezione: principal.sezione,
      slot: principal.slot,
      productId: principal.productId ?? 0,
      productCode: principal.productCode,
      productName: principal.productName,
      prezzoVb: principal.prezzoVb,
      isOptional: principal.isOptional,
      principal,
      alternatives,
    })
  }
  return rows
}

export function formatEuro(value: number): string {
  return value.toLocaleString("it-IT", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
}
