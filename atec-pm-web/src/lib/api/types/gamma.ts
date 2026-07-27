/** Gamma Robot: quadri, distinte, componenti — allineati a ATEC.PM.Shared/DTOs. */

// ── Gamma Robot ──────────────────────────────────────────────
export interface GammaRobotDto {
  id: number
  modello: string
  serie: string | null
  brand: string
  note: string | null
  quadriCount: number
}

export interface GammaQuadroDto {
  id: number
  robotId: number
  controllore: string | null
  generazione: string | null
  payload: string | null
  areaLavoro: string | null
  osVersion: string | null
  systemKey: string | null
  note: string | null
  componentiCount: number
}

export interface GammaDistintaItemDto {
  id: number
  quadroId: number
  productId: number | null
  sezione: string | null
  slot: string | null
  codeRaw: string | null
  qty: number
  isAlternate: boolean
  isOptional: boolean
  note: string | null
  rawText: string | null
  productCode: string | null
  productName: string | null
  prezzoVb: number | null
}

export interface GammaComponentDto {
  productId: number
  code: string
  name: string
  categoria: string | null
  prezzoVb: number | null
  robotCount: number
}

export interface GammaUsageDto {
  modello: string
  controllore: string | null
  generazione: string | null
  slot: string | null
  isAlternate: boolean
  occorrenze: number
}

export interface GammaDistintaAddRequest {
  quadroId: number
  productId: number
  sezione?: string | null
  slot?: string | null
  qty?: number
  isAlternate?: boolean
  isOptional?: boolean
  note?: string | null
}

export interface GammaDistintaUpdateRequest {
  qty?: number | null
  isAlternate?: boolean | null
  isOptional?: boolean | null
  note?: string | null
}

export interface GammaRobotSaveRequest {
  modello: string
  serie?: string | null
  brand?: string
  note?: string | null
}

export interface GammaQuadroSaveRequest {
  controllore?: string | null
  generazione?: string | null
  payload?: string | null
  areaLavoro?: string | null
  osVersion?: string | null
  systemKey?: string | null
  note?: string | null
}
