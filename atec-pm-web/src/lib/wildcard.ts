// Ricerca con jolly `*` (port di WildcardMatcher), condivisa dai filtri di
// pagina: `*abc` = finisce con, `abc*` = inizia con, `*abc*` o testo semplice
// = contiene. Confronto case-insensitive, pattern vuoto = tutto passa.

export function wildcardMatch(text: string | undefined | null, pattern: string): boolean {
  const t = (text ?? "").toLowerCase()
  const p = (pattern ?? "").trim().toLowerCase()
  if (p.length === 0) return true
  const starts = p.startsWith("*")
  const ends = p.endsWith("*")
  const core = p.replace(/^\*+/, "").replace(/\*+$/, "")
  if (starts && ends) return t.includes(core)
  if (starts) return t.endsWith(core)
  if (ends) return t.startsWith(core)
  return t.includes(p)
}
