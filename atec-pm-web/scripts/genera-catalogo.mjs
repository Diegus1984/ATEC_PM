// Genera src/config/catalogo.gen.ts dal catalogo unico dei permessi
// (ATEC.PM.Shared/catalogo-permessi.json) — PIANO-PERMESSI-REBUILD.md §12.2.
//
// Agganciato a predev/prebuild: il file generato è sempre fresco alla build, quindi
// una chiave inventata nel client NON COMPILA (il tipo unione ChiaveCatalogo è chiuso).
// Il .gen.ts si committa (§12.8.8): un clone appena scaricato compila senza passi extra.
//
// Le regole di validazione sono le stesse di PermessiCatalogo.Valida() (C#):
// i due validatori devono restare allineati.

import { readFileSync, writeFileSync, existsSync } from "node:fs"
import { fileURLToPath } from "node:url"
import { dirname, join } from "node:path"

const qui = dirname(fileURLToPath(import.meta.url))
const sorgente = join(qui, "..", "..", "ATEC.PM.Shared", "catalogo-permessi.json")
const destinazione = join(qui, "..", "src", "config", "catalogo.gen.ts")

const KIND_NOTI = ["sezione", "voce", "sezione-commessa", "azione", "ambito"]
const MICRO_NOTI = ["prices"]
const FORMATO_CHIAVE = /^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$/

const catalogo = JSON.parse(readFileSync(sorgente, "utf8"))
const albero = catalogo.albero
if (!Array.isArray(albero) || albero.length === 0) {
  console.error("catalogo-permessi.json: 'albero' vuoto o mancante")
  process.exit(1)
}

// ── Validazione (specchio di PermessiCatalogo.Valida) ─────────────────────────
const errori = []
const piatte = []
const scendi = (voce, padre) => {
  piatte.push({ voce, padre })
  for (const figlio of voce.figli ?? []) scendi(figlio, voce)
}
albero.forEach((v) => scendi(v, null))

for (const { voce: v } of piatte) {
  const dove = v.chiave ?? v.label
  if (!KIND_NOTI.includes(v.kind)) errori.push(`[${dove}] kind sconosciuto: '${v.kind}'`)
  if (!v.label?.trim()) errori.push(`[${v.chiave}] label mancante`)

  const chiaveRichiesta = ["voce", "azione", "ambito"].includes(v.kind)
  if (chiaveRichiesta && v.chiave == null) errori.push(`[${v.label}] kind '${v.kind}' senza chiave`)
  if (v.kind === "sezione" && v.chiave != null) errori.push(`[${v.chiave}] una sezione (gruppo) non ha chiave propria`)
  if (v.kind === "sezione-commessa" && v.chiave == null && !v.nota?.trim())
    errori.push(`[${v.label}] sezione-commessa senza chiave e senza nota che lo giustifichi`)

  if (v.chiave != null && !FORMATO_CHIAVE.test(v.chiave))
    errori.push(`[${v.chiave}] formato chiave non valido`)
  if (v.soloClient && !v.motivo?.trim())
    errori.push(`[${dove}] soloClient senza motivo (§12.8.6)`)
  if (v.soloClient && v.ritirata) errori.push(`[${dove}] soloClient e ritirata insieme`)
  for (const micro of v.micros ?? [])
    if (!MICRO_NOTI.includes(micro)) errori.push(`[${dove}] micro sconosciuto: '${micro}'`)
  if ((v.micros?.length ?? 0) > 0 && v.chiave == null) errori.push(`[${v.label}] micros senza chiave`)
  if (["azione", "ambito"].includes(v.kind) && (v.figli?.length ?? 0) > 0)
    errori.push(`[${dove}] kind '${v.kind}' non può avere figli`)
}

const perChiave = new Map()
for (const { voce: v } of piatte) {
  if (v.chiave == null) continue
  if (!perChiave.has(v.chiave)) perChiave.set(v.chiave, [])
  perChiave.get(v.chiave).push(v)
}
for (const [chiave, gruppo] of perChiave) {
  const primarie = gruppo.filter((v) => !v.chiaveCondivisa).length
  const condivise = gruppo.filter((v) => v.chiaveCondivisa).length
  if (primarie === 0) errori.push(`[${chiave}] solo occorrenze chiaveCondivisa: manca la primaria`)
  else if (primarie > 1) errori.push(`[${chiave}] chiave duplicata senza chiaveCondivisa`)
  if (condivise > 1) errori.push(`[${chiave}] più di un duplicato chiaveCondivisa`)
}
for (const { voce: v } of piatte)
  if (v.eredita != null && !perChiave.has(v.eredita))
    errori.push(`[${v.chiave}] eredita '${v.eredita}' che non esiste a catalogo`)

if (errori.length > 0) {
  console.error("catalogo-permessi.json NON VALIDO:")
  for (const e of errori) console.error(" - " + e)
  process.exit(1)
}

// ── Emissione ─────────────────────────────────────────────────────────────────
const chiavi = [...perChiave.keys()].sort()

const pulisci = (v) => {
  const nodo = { kind: v.kind, chiave: v.chiave ?? null, label: v.label }
  if (v.micros?.length) nodo.micros = v.micros
  if (v.soloClient) nodo.soloClient = true
  if (v.eredita) nodo.eredita = v.eredita
  if (v.ritirata) nodo.ritirata = true
  if (v.chiaveCondivisa) nodo.chiaveCondivisa = true
  if (v.figli?.length) nodo.figli = v.figli.map(pulisci)
  return nodo
}

const contenuto = `// GENERATO da scripts/genera-catalogo.mjs — NON MODIFICARE A MANO.
// Fonte unica: ATEC.PM.Shared/catalogo-permessi.json (PIANO-PERMESSI-REBUILD.md §12.2).
// Rigenerato in automatico da predev/prebuild; si committa (§12.8.8).

export type ChiaveCatalogo =
${chiavi.map((k) => `  | "${k}"`).join("\n")}

export type KindCatalogo = ${KIND_NOTI.map((k) => `"${k}"`).join(" | ")}

export interface VoceCatalogoGen {
  kind: KindCatalogo
  chiave: ChiaveCatalogo | null
  label: string
  micros?: readonly string[]
  soloClient?: boolean
  eredita?: ChiaveCatalogo
  ritirata?: boolean
  chiaveCondivisa?: boolean
  figli?: readonly VoceCatalogoGen[]
}

export const CATALOGO_PERMESSI: readonly VoceCatalogoGen[] = ${JSON.stringify(albero.map(pulisci), null, 2)}

/** Tutte le chiavi del catalogo, ordinate (duplicati condivisi esclusi). */
export const CHIAVI_CATALOGO: readonly ChiaveCatalogo[] = [
${chiavi.map((k) => `  "${k}",`).join("\n")}
]
`

const attuale = existsSync(destinazione) ? readFileSync(destinazione, "utf8") : null
if (attuale !== contenuto) {
  writeFileSync(destinazione, contenuto, "utf8")
  console.log(`catalogo.gen.ts rigenerato (${chiavi.length} chiavi)`)
}
