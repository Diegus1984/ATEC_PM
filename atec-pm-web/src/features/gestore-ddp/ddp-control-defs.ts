// Report di Controllo cross-commessa (segnalazione #62): tutti gli stati previsti
// per DDP Commerciali e Officine, con sotto-voci separate Commerciali / Officine.
// Allineati alle sezioni «Avanzamento» della Sintesi (ddp-sintesi-logic).

export interface ControlReportDef {
  key: string
  /** Badge breve mostrato sulla card dell'hub. */
  badge: string
  title: string
  description: string
  /** Vista a sezioni per giorno di consegna previsto (solo IO). */
  groupedByDay?: boolean
  /** Report riservato alle DDP Officina (niente sotto-voce Commerciali). */
  officinaOnly?: boolean
  /** Colonna data mostrata scaduta in rosso (report ritardi / date). */
  dueRed?: boolean
}

export const CONTROL_REPORTS: ControlReportDef[] = [
  {
    key: "rit",
    badge: "RITARDO",
    title: "Materiali in Ritardo di Consegna",
    description:
      "Righe con data di consegna prevista anteriore a oggi, non ancora consegnate né escluse.",
    dueRed: true,
  },
  {
    key: "ver",
    badge: "VER",
    title: "Materiale da Verificare a Magazzino",
    description: "Righe in stato VER: verificare la disponibilità a magazzino.",
  },
  {
    key: "chek",
    badge: "CHEK",
    title: "Controllo Tecnico / Commerciale",
    description:
      "Righe in stato CHEK: materiale che necessita controllo tecnico o commerciale.",
  },
  {
    key: "ro",
    badge: "RO",
    title: "Richieste di Offerta",
    description: "Righe in stato RO: quotazioni richieste e ancora da chiudere.",
  },
  {
    key: "do",
    badge: "DO",
    title: "Materiale da Ordinare",
    description: "Righe in stato DO: ordini ancora da emettere.",
  },
  {
    key: "dc",
    badge: "DC",
    title: "Materiale da Costruire",
    description: "Righe in stato DC delle sole DDP Officina.",
    officinaOnly: true,
  },
  {
    key: "io",
    badge: "IO",
    title: "Materiale in Ordine",
    description:
      "Righe in stato IO, raggruppate per giorno di consegna previsto con valore per giornata.",
    groupedByDay: true,
    dueRed: true,
  },
  {
    key: "par",
    badge: "PAR",
    title: "Parzialmente Consegnato / Costruito",
    description:
      "Righe in stato PAR: consegnate o costruite solo in parte.",
    dueRed: true,
  },
  {
    key: "mit",
    badge: "MIT",
    title: "Materiale in Trattamento",
    description:
      "Righe in stato MIT: materiale presso il fornitore di trattamento (tipico Officina).",
    officinaOnly: true,
    dueRed: true,
  },
  {
    key: "del",
    badge: "MAG",
    title: "Materiale a Magazzino",
    description:
      "Righe consegnate o gestite (stati aggregazione A2, escluso ASS che ha il report dedicato).",
  },
  {
    key: "ass",
    badge: "ASS",
    title: "Materiale Assegnato al Montatore",
    description: "Righe in stato ASS: già assegnate al montaggio.",
    officinaOnly: true,
  },
  {
    key: "stop",
    badge: "STOP",
    title: "DDP Stop",
    description:
      "Righe escluse dai conteggi (stati aggregazione A9: annullate, sospese, sostituite, rimesse a magazzino).",
  },
]

export function controlReportDef(key: string | undefined): ControlReportDef | null {
  return CONTROL_REPORTS.find((def) => def.key === key) ?? null
}
