import type * as React from "react"

/** Filtro colonna custom (es. combo con ricerca al posto dell'input testo). */
export interface DataTableColumnFilterInputProps {
  value: unknown
  onChange: (value: unknown) => void
}

declare module "@tanstack/react-table" {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface ColumnMeta<TData, TValue> {
    filterInput?: (props: DataTableColumnFilterInputProps) => React.ReactNode
  }
}
