import type { HrDay } from "@/lib/api/types"

import { isZero, minutiDa } from "./ore"
import { giaGiustificata, statoGiornata } from "./stato-giornata"

/**
 * I numeri del mese e i filtri del cartellino di una persona: le stesse regole contano il
 * riquadro e scelgono le righe che si vedono cliccandoci sopra (Diego, 10/09/2026: «tutte le
 * modifiche fatte qua devono esserci anche sulla pagina cartellino di una persona»).
 *
 * <p>Sta qui, fuori dalla pagina, per la stessa ragione di `controllo-giornaliero.ts`: il
 * numero grande e le righe filtrate non devono poter dire due cose diverse, e una regola sola
 * si può provare con un test.</p>
 */

/** Quale riquadro sta filtrando il mese; «tutti» = nessun filtro. */
export type FiltroCartellino =
  | "tutti"
  | "lavorate"
  | "straordinario"
  | "assenze"
  | "sistemare"
  | "segnalare"

const REGOLE: Record<Exclude<FiltroCartellino, "tutti">, (g: HrDay) => boolean> = {
  lavorate: (g) => g.hasData && !isZero(g.regularHours) && !statoGiornata(g).assenza,
  straordinario: (g) => g.hasData && !isZero(g.overtime),
  assenze: (g) => g.hasData && statoGiornata(g).assenza,
  // Da sistemare = quello che la pillola mostra in rosso o in ambra, come nel Controllo di
  // ieri: le anomalie del motore, i giorni senza timbrature, le ore che mancano sul contratto
  // e le entrate anticipate ancora da decidere.
  sistemare: (g) => {
    const tone = statoGiornata(g).tone
    return tone === "bad" || tone === "warn"
  },
  segnalare: (g) => g.canRemind,
}

/**
 * Dove ha senso mettere una causale che copra le ore mancanti: una giornata da sistemare che
 * non sia già un'assenza né un riposo. Stessa regola di `daGiustificare` nel Controllo di
 * ieri, qui applicata alla giornata invece che alla riga.
 */
export function daGiustificareGiornata(g: HrDay): boolean {
  const st = statoGiornata(g)
  return (REGOLE.sistemare(g) || giaGiustificata(g)) && !st.assenza && !st.riposo
}

export function filtraGiornate(giornate: HrDay[], filtro: FiltroCartellino): HrDay[] {
  return filtro === "tutti" ? giornate : giornate.filter(REGOLE[filtro])
}

export interface TotaliMese {
  /** Minuti ordinari, straordinari e di notturno ordinario (fasce B1 e B2). */
  ordinarie: number
  straordinario: number
  notturno: number
  giorniLavorati: number
  giorniStraordinario: number
  /** Giornate in rosso o in ambra: quelle che il riquadro «da sistemare» conta e filtra. */
  daSistemare: number
  /** Fra quelle da sistemare, quante hanno già avuto l'email. */
  segnalate: number
  assenzeIntere: number
  assenzeParziali: number
  /** Le lettere delle fasce CCNL toccate nel mese. */
  fasce: string[]
}

export function totaliMese(giornate: HrDay[]): TotaliMese {
  let ordinarie = 0
  let straordinario = 0
  let notturno = 0
  let giorniLavorati = 0
  let giorniStraordinario = 0
  let daSistemare = 0
  let segnalate = 0
  let assenzeIntere = 0
  let assenzeParziali = 0
  const fasce = new Set<string>()

  for (const g of giornate) {
    if (REGOLE.sistemare(g)) {
      daSistemare++
      if (g.lastReminderAt) segnalate++
    }
    if (!g.hasData) continue

    const st = statoGiornata(g)
    if (st.assenza) {
      if (st.assenzaParziale) assenzeParziali++
      else assenzeIntere++
    }
    ordinarie += minutiDa(g.regularHours)
    straordinario += minutiDa(g.overtime)
    if (REGOLE.lavorate(g)) giorniLavorati++
    if (REGOLE.straordinario(g)) giorniStraordinario++
    for (const k of Object.keys(g.bands ?? {})) fasce.add(k)
    // Fascia b (#145): il notturno ordinario non è straordinario, ma è maggiorato.
    notturno += minutiDa(g.bands?.B1 ?? "") + minutiDa(g.bands?.B2 ?? "")
  }

  return {
    ordinarie,
    straordinario,
    notturno,
    giorniLavorati,
    giorniStraordinario,
    daSistemare,
    segnalate,
    assenzeIntere,
    assenzeParziali,
    fasce: [...fasce],
  }
}
