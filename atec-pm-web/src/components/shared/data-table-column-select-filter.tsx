import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import type { DataTableColumnFilterInputProps } from "@/components/shared/data-table-column-meta"

const ALL_VALUE = "__all__"

interface DataTableColumnSelectFilterProps extends DataTableColumnFilterInputProps {
  options: string[]
  allLabel?: string
}

/** Filtro colonna a combo: valori distinti dalla griglia (es. Tipo, Commessa). */
export function DataTableColumnSelectFilter({
  value,
  onChange,
  options,
  allLabel = "Tutti",
}: DataTableColumnSelectFilterProps) {
  const selected = value ? String(value) : ALL_VALUE

  return (
    <Select
      value={selected}
      onValueChange={(next) => onChange(next === ALL_VALUE ? undefined : next)}
    >
      <SelectTrigger className="h-8 w-full bg-background dark:bg-background">
        <SelectValue placeholder={allLabel} />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value={ALL_VALUE}>{allLabel}</SelectItem>
        {options.map((option) => (
          <SelectItem key={option} value={option}>
            {option}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/** Filtro esatto per valore colonna (combo select). */
export function exactColumnFilterFn(
  row: { getValue: (columnId: string) => unknown },
  columnId: string,
  filterValue: unknown
): boolean {
  if (filterValue === undefined || filterValue === null || filterValue === "") {
    return true
  }
  const cell = row.getValue(columnId)
  if (cell === null || cell === undefined || cell === "") {
    return false
  }
  return String(cell) === String(filterValue)
}
