/** Anagrafica dei valori proponibili nei calcoli (tabella `tariff_options`). */

export interface TariffOptionDto {
  id: number
  tariffType: string
  /** Nome della tariffa (#87): «Meccanica», «Stampa 3D»… Vuoto = si legge dal solo importo. */
  label: string
  value: number
}

export interface TariffOptionSaveRequest {
  tariffType: string
  label?: string
  value: number
}

/**
 * Tipi gestiti, allineati a `TariffTypes` lato server. I primi quattro finiscono in una
 * colonna di `project_cost_resources`; `HOURLY_RATE` (blocco 5) alimenta il costo orario
 * delle Officine interne nelle finestre di calcolo a righe.
 */
export const TARIFF_TYPES = [
  { key: "HOURLY_RATE", label: "Tariffa oraria", hint: "Officine interne (meccanica, carpenteria, stampa 3D)" },
  { key: "COST_PER_KM", label: "Rimborso km", hint: "€ al km" },
  { key: "DAILY_FOOD", label: "Vitto / diaria", hint: "€ al giorno" },
  { key: "DAILY_HOTEL", label: "Alloggio", hint: "€ al giorno" },
  { key: "DAILY_ALLOWANCE", label: "Indennità", hint: "€ al giorno" },
] as const

export type TariffTypeKey = (typeof TARIFF_TYPES)[number]["key"]
