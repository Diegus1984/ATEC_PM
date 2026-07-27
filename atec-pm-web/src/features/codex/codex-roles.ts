/** Ruoli abilitati alla ricodifica Codex (nuova codifica manuale, 21/07/2026). */
export function canRecodeCodex(role: string | undefined): boolean {
  return role === "ADMIN" || role === "PM" || role === "RESP_REPARTO"
}
