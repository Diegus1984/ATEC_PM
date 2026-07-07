import { Input } from "@/components/ui/input"

/**
 * Casella di ricerca per colonna, da mettere sotto l'header nelle tabelle
 * (sfondo bianco per risaltare sull'header grigio). Usata sia dalla variante
 * client {@link DataTableCardFiltered} sia dalle tabelle paginate server-side
 * (Catalogo, Codex) per pilotare i filtri server.
 */
export function ColumnFilterInput({
  value,
  onChange,
  placeholder = "Filtra…",
}: {
  value: string
  onChange: (value: string) => void
  placeholder?: string
}) {
  return (
    <Input
      value={value}
      placeholder={placeholder}
      className="h-8 bg-background dark:bg-background"
      onChange={(event) => onChange(event.target.value)}
    />
  )
}
