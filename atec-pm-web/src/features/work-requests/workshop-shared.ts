import type { WorkshopRow } from "@/lib/api/types"

/** Le viste rapide di «Lavorazioni Officine» (#83; «stampa3d» dal #87). */
export type WorkshopView =
  | "interne"
  | "stampa3d"
  | "esterne"
  | "urgenze"
  | "trattamenti"

/**
 * Regole di appartenenza alle viste, parola per parola dalla segnalazione #83.
 *
 * Interne: Tipo Interna + «Da costruire» (DC). Se la riga passa a «parzialmente costruito»
 * (PAR) resta, con i numeri aggiornati. Se passa a «materiale in trattamento» (MIT) esce da
 * qui e resta in Trattamenti. Qualunque altro stato la toglie dalla pagina.
 *
 * Esterne: identica, con Tipo Esterna e «Da ordinare» (DO) al posto di DC.
 *
 * Le righe manuali non hanno uno stato DDP: ci sono finché non vengono consegnate, e la
 * vista gliela dà il solo Tipo.
 */
const STATI_INTERNE = new Set(["DC", "PAR"])
const STATI_ESTERNE = new Set(["DO", "PAR"])

function stato(row: WorkshopRow): string {
  return (row.itemStatus ?? "").trim().toUpperCase()
}

function tipo(row: WorkshopRow): string {
  return (row.workType ?? "").trim().toLowerCase()
}

export function isInterna(row: WorkshopRow): boolean {
  if (tipo(row) !== "internal") return false
  return row.source === "MANUAL" || STATI_INTERNE.has(stato(row))
}

/**
 * Stampa 3D (#87): stesse identiche regole delle interne — è lavoro fatto in casa, cambia
 * solo la macchina che lo fa e la tariffa con cui se ne calcola il costo.
 */
export function isStampa3D(row: WorkshopRow): boolean {
  if (tipo(row) !== "print3d") return false
  return row.source === "MANUAL" || STATI_INTERNE.has(stato(row))
}

export function isEsterna(row: WorkshopRow): boolean {
  if (tipo(row) !== "external") return false
  return row.source === "MANUAL" || STATI_ESTERNE.has(stato(row))
}

/**
 * Trattamenti: la riga con un trattamento indicato compare qui **oltre** che nella sua vista,
 * e ci resta anche quando il materiale è partito (MIT), che è il momento in cui sparisce dalle
 * altre due. Uno stato MIT senza trattamento scritto è comunque materiale in trattamento.
 */
export function isTrattamento(row: WorkshopRow): boolean {
  return (row.treatment ?? "").trim().length > 0 || stato(row) === "MIT"
}

/**
 * Urgenze: la spunta «Segnala ultra critica», che la #83 tiene sulle lavorazioni fatte in
 * casa (sulle esterne non c'è nemmeno la casella). Dal #87 ci sta dentro anche la stampa 3D:
 * la spunta si può dare, quindi la riga urgente deve comparire qui.
 */
export function isUrgenza(row: WorkshopRow): boolean {
  return row.isUltraCritical && (isInterna(row) || isStampa3D(row))
}

export function filtraPerVista(rows: WorkshopRow[], view: WorkshopView): WorkshopRow[] {
  switch (view) {
    case "interne":
      return rows.filter(isInterna)
    case "stampa3d":
      return rows.filter(isStampa3D)
    case "esterne":
      return rows.filter(isEsterna)
    case "urgenze":
      return rows.filter(isUrgenza)
    case "trattamenti":
      return rows.filter(isTrattamento)
  }
}

/**
 * Chiave di riga: `source` + id. Gli id di distinta e quelli delle righe manuali vivono in
 * due tabelle diverse e si sovrappongono — senza il prefisso due righe distinte si
 * scambierebbero stato di selezione e ordine in griglia.
 */
export function rowKey(row: WorkshopRow): string {
  return `${row.source}-${row.id}`
}

/** La data su cui la vista ordina e filtra: «Consegnato il» per le esterne (#83), altrove la richiesta. */
export function dataDiVista(row: WorkshopRow, view: WorkshopView): string {
  return view === "esterne" ? row.deliveredAt : row.requestDate
}

/**
 * Data richiesta scaduta (#84): il confronto è con **oggi del browser**, come chiede la
 * segnalazione, non con il `DaysLate` che il server calcola su `CURDATE()` — sono la stessa
 * cosa in LAN, ma è la macchina di chi guarda a dover decidere cosa è in ritardo.
 * «Supera di un giorno» = la data è passata: oggi stesso non è ancora un ritardo.
 */
export function isDataRichiestaScaduta(requestDate: string, oggi: string): boolean {
  const data = (requestDate ?? "").slice(0, 10)
  return data !== "" && data < oggi
}

export const TITOLI_VISTA: Record<WorkshopView, string> = {
  interne: "Lavorazioni Interne",
  stampa3d: "Stampa 3D",
  esterne: "Lavorazioni Esterne",
  urgenze: "Urgenze",
  trattamenti: "Trattamenti",
}

export const DESCRIZIONI_VISTA: Record<WorkshopView, string> = {
  interne:
    "Righe da costruire in casa (Tipo Interna, stato Da costruire o parzialmente costruito). Data richiesta e note si scrivono qui; il resto segue la DDP Officina.",
  stampa3d:
    "Righe da stampare in casa (Tipo Stampa 3D, stato Da costruire o parzialmente costruito). Data richiesta e note si scrivono qui; il resto segue la DDP Officina.",
  esterne:
    "Righe da ordinare fuori (Tipo Esterna, stato Da ordinare o parzialmente consegnato). Data richiesta e note si scrivono qui; il resto segue la DDP Officina.",
  urgenze: "Le lavorazioni fatte in casa — interne e stampa 3D — segnalate come ultra critiche.",
  trattamenti:
    "Righe con un trattamento indicato, comprese quelle con il materiale già in trattamento.",
}
