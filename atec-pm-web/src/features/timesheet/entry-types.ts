// Tipi di registrazione ore — etichette e colori centralizzati (come
// TypeLabel/TypeColors nel client WPF), riusati da pagina e dialog.

export interface EntryTypeMeta {
  value: string
  label: string
  /** Classi token per il chip (sfondo + testo). */
  chipClass: string
}

export const ENTRY_TYPES: EntryTypeMeta[] = [
  {
    value: "REGULAR",
    label: "Ordinarie",
    chipClass: "bg-secondary text-secondary-foreground",
  },
  {
    value: "OVERTIME",
    label: "Straordinario",
    chipClass: "bg-warning/15 text-warning",
  },
  {
    value: "TRAVEL",
    label: "Trasferta",
    chipClass: "bg-success/15 text-success",
  },
]

const BY_VALUE = new Map(ENTRY_TYPES.map((type) => [type.value, type]))

export function entryTypeLabel(value: string): string {
  return BY_VALUE.get(value)?.label ?? "Ordinarie"
}

export function entryTypeChipClass(value: string): string {
  return BY_VALUE.get(value)?.chipClass ?? "bg-secondary text-secondary-foreground"
}
