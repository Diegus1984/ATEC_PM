// Report di Controllo cross-commessa (dal prototipo Gestione_DDP_New_V30): definizioni
// condivise tra hub (DdpControlloPage) e vista report (DdpControlReportPage).

export interface ControlReportDef {
  key: string
  /** Badge breve mostrato sulla card dell'hub. */
  badge: string
  title: string
  description: string
  /** Vista a sezioni per giorno di consegna previsto (solo IO). */
  groupedByDay?: boolean
  /** Report riservato alle DDP Officina (DC = Da costruire). */
  officinaOnly?: boolean
  /** Colonna data mostrata scaduta in rosso (report ritardi). */
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
    key: "io",
    badge: "IO",
    title: "Materiale in Ordine",
    description:
      "Righe in stato IO, raggruppate per giorno di consegna previsto con valore per giornata.",
    groupedByDay: true,
  },
  {
    key: "dc",
    badge: "DC",
    title: "Materiale da Costruire",
    description: "Righe in stato DC delle sole DDP Officina.",
    officinaOnly: true,
  },
]

export function controlReportDef(key: string | undefined): ControlReportDef | null {
  return CONTROL_REPORTS.find((def) => def.key === key) ?? null
}
