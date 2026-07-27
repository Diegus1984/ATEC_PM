/** Le 7 sezioni macro-gruppo (stesso ordine FIELD del server). Sempre mostrate, anche vuote. */
export const GAMMA_SEZIONI = [
  "Schede",
  "Azionamenti",
  "Kit Cavi",
  "Motori",
  "Componenti meccanici",
  "Tastierino",
  "Ventole",
] as const

export type GammaSezione = (typeof GAMMA_SEZIONI)[number]

/** Prefisso su text/plain per HTML5 DnD (compatibile cross-browser). */
export const GAMMA_DRAG_PREFIX = "atec-gamma-component:"
