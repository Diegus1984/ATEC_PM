// ── Prezzo Codex a 0 → proposta di aggiornamento dal dialogo officina ──────

import * as React from "react"

import { fetchCodex } from "@/lib/api/codex"
import type { OfficinaItemSaveRequest } from "@/lib/api/types"

import type { CodexPriceInfo } from "./officina-shared"

const EMPTY: CodexPriceInfo = { checked: false, showCheckbox: false, codexId: null }

/**
 * All'apertura del dialogo cerca il codice nel Codex: se l'articolo esiste ed è
 * a prezzo 0, offre la spunta «aggiorna prezzo in anagrafica». Salta le righe
 * padre (il loro costo è la somma dei componenti, non un prezzo di listino).
 */
export function useCodexPriceCheck(
  form: OfficinaItemSaveRequest | null,
  parentIdsWithChildren: Set<number>
): [CodexPriceInfo, React.Dispatch<React.SetStateAction<CodexPriceInfo>>] {
  const [info, setInfo] = React.useState<CodexPriceInfo>(EMPTY)

  React.useEffect(() => {
    setInfo(EMPTY)
    if (!form || !form.partNumber) return
    if (parentIdsWithChildren.has(form.id)) return

    let active = true
    const cleanCode = form.partNumber.replace(/\./g, "").trim()
    void (async () => {
      try {
        const res = await fetchCodex({ filters: { codice: cleanCode } })
        if (!active) return
        const exactMatch = res.items.find(
          (x) => x.codice.replace(/\./g, "") === cleanCode
        )
        if (exactMatch && (exactMatch.prezzoForn ?? 0) === 0) {
          setInfo({ checked: false, showCheckbox: true, codexId: exactMatch.id })
        }
      } catch (e) {
        console.error("Errore verifica prezzo Codex", e)
      }
    })()
    return () => {
      active = false
    }
  }, [form, parentIdsWithChildren])

  return [info, setInfo]
}
