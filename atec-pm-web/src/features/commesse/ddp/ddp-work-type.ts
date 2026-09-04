// ── Natura della lavorazione officina ──────────────────────────────────────
// Stessi valori e stessi colori del Tipo delle Lavorazioni (`WR_TYPE_META`):
// "Internal" = costruita in ATEC, "External" = affidata a un fornitore,
// "Print3D" = stampata in casa (#87). Stringa vuota = non ancora classificata.
//
// Serve al Bilancio, che scompone la voce «Lavorazioni Officine» in interne
// (stampa 3D compresa: è lavoro fatto in casa) ed esterne.

export const WORK_TYPE_INTERNAL = "Internal"
export const WORK_TYPE_EXTERNAL = "External"
export const WORK_TYPE_PRINT3D = "Print3D"

export const WORK_TYPE_META = [
  { value: WORK_TYPE_INTERNAL, label: "Interna", dot: "bg-blue-500" },
  { value: WORK_TYPE_EXTERNAL, label: "Esterna", dot: "bg-purple-500" },
  { value: WORK_TYPE_PRINT3D, label: "Stampa 3D", dot: "bg-emerald-500" },
] as const

/** Etichetta italiana della natura; stringa vuota se non classificata. */
export function workTypeLabel(workType: string | null | undefined): string {
  return WORK_TYPE_META.find((t) => t.value === workType)?.label ?? ""
}

/**
 * Lavorazioni fatte in casa: interne e stampa 3D. Dividono stati, viste e — nel Bilancio —
 * la stessa voce di costo. Gemella di `OfficinaWorkTypes.IsInHouse` lato server.
 */
export function isInHouseWorkType(workType: string | null | undefined): boolean {
  return workType === WORK_TYPE_INTERNAL || workType === WORK_TYPE_PRINT3D
}
