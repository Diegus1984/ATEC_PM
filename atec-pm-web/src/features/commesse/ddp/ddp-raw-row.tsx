// ── #135 — la riga del GREZZO nella DDP Commerciale ────────────────────────────
// Un particolare a disegno «101» può derivare da un commerciale «201»: la barra o la
// lamiera da comprare. La riga del grezzo nella distinta commerciale NON la fa il
// client — la crea, la aggiorna e la cancella il server seguendo la DDP Officina.
// Qui dentro c'è solo il modo di riconoscerla a video e di dire, con le stesse parole
// in ogni punto (griglia e dialog), che cos'è e dove si toglie.
//
// 🪤 Niente icona `Link2`: in tutto il software vuol dire «mappatura codice ATEC», e
// nella colonna Cod. ATEC di questa stessa griglia compare già con quel senso. Due
// significati per la stessa icona nella stessa riga sarebbero un errore, non uno stile.

import type { DdpRowItem } from "@/lib/api/types"

/** La riga è il grezzo di uno o più 101: il codice del 201 arriva già dalla lista. */
export function isRawRow(row: Pick<DdpRowItem, "rawCodexCode">): boolean {
  return (row.rawCodexCode ?? "").trim().length > 0
}

/** «Grezzo di 101240826.001, 101240826.002» — senza sorgenti resta il solo «Grezzo». */
export function rawRowLabel(row: Pick<DdpRowItem, "rawSources">): string {
  const sources = (row.rawSources ?? "").trim()
  return sources ? `Grezzo di ${sources}` : "Grezzo di un particolare a disegno"
}

/**
 * Cos'è la riga E dove si toglie, in una frase sola.
 *
 * 🪤 La spiegazione sta qui e non sulle voci «Elimina» del menu di riga perché lì non
 * si vedrebbe mai: le voci disabilitate del menu hanno `pointer-events: none`, quindi
 * un `title` sopra non compare (e `RowAction` non prevede il campo). Il badge in
 * colonna «Codice» è sulla stessa riga del menu: è lì che la si va a leggere.
 */
export function rawRowTitle(row: Pick<DdpRowItem, "rawSources">): string {
  return `${rawRowLabel(row)}: si toglie dalla DDP Officina, o togliendo la derivazione dall'articolo Codex`
}

/**
 * Pillola «Grezzo» sullo stile dei badge di destinazione del picker Codex.
 *
 * #142 — grezzo «scoperto» (`rawNeedsMapping`): pillola ambra «da associare», e con
 * `onAssocia` diventa il bottone che apre il dialog di associazione del 201 — il
 * rimedio sta sulla riga stessa, non in un'altra pagina.
 *
 * 🪤 I colori del testo sono espliciti (non ereditati): la riga della griglia porta un
 * `color` inline preso dallo stato, e senza classe propria la scritta prenderebbe quello.
 */
export function RawRowBadge({
  row,
  onAssocia,
}: {
  row: DdpRowItem
  /** #142: presente (e riga scoperta) = la pillola apre l'associazione del 201. */
  onAssocia?: () => void
}) {
  if (row.rawNeedsMapping) {
    const titolo =
      `${rawRowLabel(row)}: il 201 di derivazione non è associato a NESSUN articolo ` +
      `commerciale — la riga non cambia stato e non entra in RDO finché non lo associ` +
      (onAssocia ? " (clic per associare)." : " (Codex → Articoli Danea).")
    const classi =
      "inline-flex shrink-0 items-center gap-1.5 rounded-full border border-amber-300 bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-800 dark:border-amber-700 dark:bg-amber-950/40 dark:text-amber-300"
    const contenuto = (
      <>
        <span className="size-1.5 rounded-full bg-amber-500" />
        Grezzo — da associare
      </>
    )
    return onAssocia ? (
      <button
        type="button"
        className={`${classi} cursor-pointer hover:bg-amber-100 dark:hover:bg-amber-900/40`}
        title={titolo}
        onClick={(e) => {
          e.stopPropagation()
          onAssocia()
        }}
      >
        {contenuto}
      </button>
    ) : (
      <span className={classi} title={titolo}>
        {contenuto}
      </span>
    )
  }
  return (
    <span
      className="inline-flex shrink-0 items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[11px] font-medium text-emerald-700 dark:border-emerald-800 dark:bg-emerald-950/40 dark:text-emerald-300"
      title={rawRowTitle(row)}
    >
      <span className="size-1.5 rounded-full bg-emerald-500" />
      Grezzo
    </span>
  )
}
