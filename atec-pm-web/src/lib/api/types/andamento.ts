/**
 * Andamento — la sezione con cui il venditore riferisce alla proprietà.
 * Allineato a `ATEC.PM.Shared/DTOs/Andamento_DTOs.cs`.
 */

export interface AndamentoKpi {
  righeTotali: number
  bozze: number
  /** Righe escluse dai valori perché senza importo: si dichiarano, non si nascondono. */
  senzaImporto: number

  emesse: number
  valoreEmesse: number
  valoreEmesseMax: number
  haForbice: boolean

  prese: number
  valorePrese: number
  valoreOrdini: number
  preseACorpo: number
  preseAConsuntivo: number
  /** Prese senza importo d'ordine né «a consuntivo»: debito dei dati. */
  preseIncomplete: number

  aperte: number
  valoreAperte: number
  valoreAperteMax: number

  perse: number
  valorePerse: number
  sospese: number
  valoreSospese: number

  /** Prese su (prese + perse). `null` finché non si è chiuso niente. */
  conversionePct: number | null

  portafoglioPonderato: number
  apertConChance: number
}

export interface AndamentoMese {
  mese: number
  etichetta: string
  emesse: number
  prese: number
  aperte: number
  numeroEmesse: number
}

export interface AndamentoSerieAnno {
  anno: number
  mesi: number[]
}

export interface AndamentoGruppo {
  chiave: string
  etichetta: string
  emesse: number
  valoreEmesse: number
  prese: number
  valorePrese: number
  aperte: number
  valoreAperte: number
  conversionePct: number | null
}

export interface AndamentoChance {
  /** 0-100, oppure -1 per «non valutata». */
  livello: number
  etichetta: string
  offerte: number
  valore: number
  valorePonderato: number
}

export interface AndamentoOfferta {
  id: number
  numero: string
  cliente: string
  descrizione: string
  data: string | null
  importo: number | null
  chance: number | null
  venditore: string
}

export interface AndamentoPortafoglio {
  mese: string
  etichetta: string
  valore: number
  offerte: number
}

export interface AndamentoOfferte {
  anno: number
  kpi: AndamentoKpi
  mensile: AndamentoMese[]
  mensileMultiAnno: AndamentoSerieAnno[]
  perVenditore: AndamentoGruppo[]
  perTipo: AndamentoGruppo[]
  perCategoria: AndamentoGruppo[]
  topClienti: AndamentoGruppo[]
  perChance: AndamentoChance[]
  topAperte: AndamentoOfferta[]
  portafoglio: AndamentoPortafoglio[]
  portafoglioNota: string
}
