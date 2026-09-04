import { readFileSync } from "node:fs"

import { describe, expect, it } from "vitest"

import { TINYMCE_PLUGINS } from "@/lib/tinymce-plugins"

// Guardiano sui sorgenti: `lib/tinymce.ts` (che qui NON si può importare: TinyMCE vuole un
// browser) deve importare ESATTAMENTE i plugin di TINYMCE_PLUGINS. Nominarne uno senza
// importarlo = 404 silenzioso a runtime e bottone mancante; importarlo senza nominarlo =
// peso morto nel chunk.
describe("TinyMCE: plugin nominati = plugin impacchettati", () => {
  const sorgente = readFileSync(new URL("./tinymce.ts", import.meta.url), "utf8")
  const importati = [...sorgente.matchAll(/^import "tinymce\/plugins\/([a-z]+)"/gm)].map(
    (m) => m[1]
  )

  it("ogni plugin della lista è importato nel bundle, e viceversa", () => {
    expect([...importati].sort()).toEqual([...TINYMCE_PLUGINS].sort())
  })

  it("niente doppioni", () => {
    expect(new Set(TINYMCE_PLUGINS).size).toBe(TINYMCE_PLUGINS.length)
    expect(new Set(importati).size).toBe(importati.length)
  })

  it("il core viene prima di tutto il resto (i plugin si registrano sul globale)", () => {
    const righe = sorgente.split(/\r?\n/).filter((r) => r.startsWith("import "))
    expect(righe[0]).toBe('import tinymce from "tinymce"')
  })
})
