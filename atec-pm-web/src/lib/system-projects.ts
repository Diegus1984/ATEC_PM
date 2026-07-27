/** Codice della commessa di sistema per lavorazioni generiche (non operative). */
export const SYSTEM_PROJECT_INTERNA = "INTERNA"

export function isSystemProjectCode(code: string | null | undefined): boolean {
  return (code ?? "").trim().toUpperCase() === SYSTEM_PROJECT_INTERNA
}

/** Etichetta corta in griglia Lavorazioni. */
export function formatWorkRequestProjectLabel(
  code: string | null | undefined,
  title: string | null | undefined
): string {
  if (isSystemProjectCode(code)) return "INTERNA — Generica"
  const c = (code ?? "").trim()
  const t = (title ?? "").trim()
  if (c && t) return `${c} — ${t}`
  return c || t || "—"
}
