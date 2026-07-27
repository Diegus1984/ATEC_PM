import type * as React from "react"

/**
 * Sostituto di `flexRender` di TanStack per le nostre tabelle: le funzioni
 * `cell`/`header` vengono CHIAMATE (producono l'elemento) invece che montate
 * come componenti React.
 *
 * Perché: flexRender usa la funzione colonna come *tipo* di componente. Le
 * `columns` vengono ricreate a ogni refetch (dipendono da handler e stati
 * mutazione), quindi il tipo cambiava identità a ogni aggiornamento dati e
 * React smontava/rimontava l'intera cella — chiudendo qualsiasi popover o
 * menu aperto (es. combo Trattamento DDP Officina) al primo refresh in
 * background (staleTime 0 + realtime).
 *
 * Vincolo: niente hook direttamente nelle funzioni colonna — per le celle con
 * stato usare componenti dedicati (com'è già convenzione nel progetto).
 */
export function renderColumnDef(def: unknown, ctx: unknown): React.ReactNode {
  if (def == null) return null
  return typeof def === "function"
    ? ((def as (c: unknown) => React.ReactNode)(ctx) ?? null)
    : (def as React.ReactNode)
}
