import * as React from "react"
import { useQuery } from "@tanstack/react-query"

import { fetchSalProspetto } from "@/lib/api/sal"
import type { SalProspettoRow } from "@/lib/api/types"
import { canAccessFeature } from "@/lib/auth/permissions"

/** Vista SAL su cui atterra il clic: le stesse due della pagina `/sal`. */
export type SalWarningKind = "fatturazione" | "incasso"

export interface SalWarningItem extends SalProspettoRow {
  kind: SalWarningKind
}

/**
 * I warning SAL **attivi**, quelli e solo quelli che le viste «Warning Fatturazione» e
 * «Warning incasso fattura» della pagina `/sal` mostrano (#114).
 *
 * <p>Stessa sorgente delle viste — <code>/api/sal/prospetto</code>, stessa chiave di cache —
 * apposta: la card della Dashboard deve dire quello che dice la pagina, e un secondo
 * conteggio calcolato altrove torna a divergere. Prima questa card leggeva
 * <code>/api/deadlines</code>, che usa una soglia fissa a 7 giorni diversa dal pre-warning
 * «dal lunedì della settimana precedente»: i numeri non coincidevano.</p>
 *
 * <p>Ne segue anche la seconda richiesta della #114 — «se dalla pagina SAL i warning sono
 * risolti, la card si aggiorna di conseguenza»: aggiornare un SAL cambia il prospetto, e la
 * card legge il prospetto.</p>
 */
export function useSalWarnings(): SalWarningItem[] {
  const query = useQuery({
    queryKey: ["sal-prospetto"],
    queryFn: fetchSalProspetto,
    enabled: canAccessFeature("nav.sal"),
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
    staleTime: 0,
  })

  return React.useMemo<SalWarningItem[]>(() => {
    const rows = query.data ?? []
    return rows
      .filter((r) => r.alert === "warn" || r.alert === "pre" || r.alert === "incasso")
      .map<SalWarningItem>((r) => ({
        ...r,
        kind: r.alert === "incasso" ? "incasso" : "fatturazione",
      }))
      .sort((a, b) => {
        // Prima gli incassi scaduti, poi le fatturazioni scadute, poi i pre-warning;
        // a parità, la data più vecchia in cima (è quella che aspetta da più tempo).
        const rank = (row: SalWarningItem) =>
          row.alert === "incasso" ? 0 : row.alert === "warn" ? 1 : 2
        if (rank(a) !== rank(b)) return rank(a) - rank(b)
        return (a.dataFatt ?? "").localeCompare(b.dataFatt ?? "")
      })
  }, [query.data])
}
