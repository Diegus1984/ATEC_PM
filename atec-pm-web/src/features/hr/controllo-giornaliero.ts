import type { HrDailyCheck, HrDailyCheckEmployee, HrDay } from "@/lib/api/types"

import { statoGiornata, type StatoLetto } from "./stato-giornata"

// La logica pura del «Controllo di ieri» (09/09/2026): quali righe si mettono a video, i numeri
// dei riquadri, come si chiama il blocco di giorni. Il blocco lo decide il server
// (HrControlloGiornaliero); qui si legge e basta. Difesa da controllo-giornaliero.test.ts.

/** Una riga della griglia: una persona in un giorno del blocco. */
export interface RigaControllo {
  chiave: string
  dipendente: HrDailyCheckEmployee
  dataIso: string
  giorno: HrDay
  stato: StatoLetto
  /** false = sabato, domenica o festivo: la riga c'è solo perché la persona ha timbrato. */
  lavorativo: boolean
}

/**
 * Chi non ha il codice Ecos non può ricevere timbrature: la sua giornata vuota non è
 * un'anomalia sua ma un collegamento da fare, e si dice così invece di «Nessuna timbratura».
 */
export const NON_COLLEGATO: StatoLetto = {
  label: "Non collegato a Ecos",
  tone: "dim",
  riposo: false,
  assenza: false,
  assenzaParziale: false,
}

/**
 * Le righe nell'ordine di lettura: giorno per giorno, dentro il giorno le persone nell'ordine
 * del server (cognome). Nei giorni lavorativi ci sono TUTTI — è il punto della pagina: chi non
 * ha timbrato si vede in rosso —; nei giorni di riposo solo chi ha lavorato lo stesso, così un
 * sabato di trentasette «Riposo» non nasconde l'unico che era in officina.
 */
export function righeControllo(controllo: HrDailyCheck): RigaControllo[] {
  const righe: RigaControllo[] = []
  controllo.days.forEach((blocco, indice) => {
    const dataIso = blocco.date.slice(0, 10)
    for (const dipendente of controllo.employees) {
      const giorno = dipendente.days[indice]
      if (!giorno) continue
      const stato = statoDi(dipendente, giorno)
      if (!blocco.isWorkingDay && !haLavorato(stato)) continue
      righe.push({
        chiave: `${dipendente.employeeId}|${dataIso}`,
        dipendente,
        dataIso,
        giorno,
        stato,
        lavorativo: blocco.isWorkingDay,
      })
    }
  })
  return righe
}

function statoDi(dipendente: HrDailyCheckEmployee, giorno: HrDay): StatoLetto {
  // Una rettifica a mano fa dati anche senza Ecos: allora la giornata si legge come le altre.
  if (!dipendente.ecosLinked && !giorno.hasData && giorno.punches.length === 0) return NON_COLLEGATO
  return statoGiornata(giorno)
}

/** In un giorno di riposo la riga c'è solo se dice qualcosa: timbrature, anomalie, notte finita. */
function haLavorato(stato: StatoLetto): boolean {
  return stato !== NON_COLLEGATO && !stato.riposo && !stato.assenza
}

/** «Da sistemare» = tutto ciò che la pillola mostra in rosso o in ambra. */
export function daSistemare(riga: RigaControllo): boolean {
  return riga.stato.tone === "bad" || riga.stato.tone === "warn"
}

export interface RiassuntoControllo {
  dipendenti: number
  regolari: number
  daSistemare: number
  /** Ferie, permessi, malattia, infortunio (giornate intere o parziali). */
  assenti: number
  /** Giornate lavorative passate senza timbrature né assenza: le più urgenti fra le rosse. */
  senzaTimbrature: number
}

/** I numeri dei riquadri, contati sulle righe a video. */
export function riassuntoControllo(
  controllo: HrDailyCheck,
  righe: RigaControllo[]
): RiassuntoControllo {
  let regolari = 0
  let sistemare = 0
  let assenti = 0
  let senzaTimbrature = 0
  for (const r of righe) {
    if (r.stato.tone === "ok") regolari++
    if (daSistemare(r)) sistemare++
    if (r.stato.assenza) assenti++
    if (r.stato.label.startsWith("Nessuna timbratura")) senzaTimbrature++
  }
  return {
    dipendenti: controllo.employees.length,
    regolari,
    daSistemare: sistemare,
    assenti,
    senzaTimbrature,
  }
}

/** La data del server («2026-09-04T00:00:00») come giorno locale, senza scivolare di fuso. */
function giornoLocale(iso: string): Date {
  const [anno, mese, giorno] = iso.slice(0, 10).split("-").map(Number)
  return new Date(anno, mese - 1, giorno)
}

function maiuscola(testo: string): string {
  return testo ? testo[0].toUpperCase() + testo.slice(1) : testo
}

/** «venerdì 4 settembre» (minuscolo: sta dentro una frase). */
function giornoInParole(iso: string, conMese = true): string {
  const d = giornoLocale(iso)
  const settimana = d.toLocaleDateString("it-IT", { weekday: "long" })
  const mese = conMese ? ` ${d.toLocaleDateString("it-IT", { month: "long" })}` : ""
  return `${settimana} ${d.getDate()}${mese}`
}

/** «Venerdì 4 settembre»: l'intestazione di un giorno del blocco. */
export function etichettaGiorno(iso: string): string {
  return maiuscola(giornoInParole(iso))
}

/**
 * Il titolo del blocco: «Lunedì 7 settembre» quando è un giorno solo, «Da venerdì 4 a
 * domenica 6 settembre» quando sono di più (il mese una volta sola se è lo stesso).
 */
export function descriviBlocco(controllo: Pick<HrDailyCheck, "from" | "to">): string {
  const da = controllo.from.slice(0, 10)
  const a = controllo.to.slice(0, 10)
  if (da === a) return etichettaGiorno(a)
  const stessoMese = da.slice(0, 7) === a.slice(0, 7)
  return `Da ${giornoInParole(da, !stessoMese)} a ${giornoInParole(a)}`
}

/**
 * Perché si vedono più giorni, detto a chi legge: il fine settimana entra nel blocco di
 * lunedì, la festa in quello del giorno dopo.
 */
export function spiegaBlocco(controllo: Pick<HrDailyCheck, "days">): string {
  const riposi = controllo.days.filter((g) => !g.isWorkingDay).length
  if (riposi === 0) return ""
  const lavorativi = controllo.days.length - riposi
  const parteRiposo =
    riposi === 1 ? "un giorno di riposo" : `${riposi} giorni di riposo`
  return lavorativi > 0
    ? `L'ultimo giorno lavorativo più ${parteRiposo}: nei riposi compare solo chi ha timbrato.`
    : `${maiuscola(parteRiposo)}: compare solo chi ha timbrato.`
}
