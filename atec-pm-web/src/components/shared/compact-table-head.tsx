import { TableHead } from "@/components/ui/table"
import { cn } from "@/lib/utils"

const HEAD_BASE =
  "h-auto min-h-10 py-1.5 align-top whitespace-normal leading-tight text-xs font-semibold"

/**
 * Intestazione di colonna compatta a più righe per griglie editabili dense
 * (SAL, fogli commessa): consente colonne più strette risparmiando spazio orizzontale.
 */
export function CompactTableHead({
  label,
  className,
  align = "left",
  title,
}: {
  label: string | readonly [string, string] | readonly [string, string, string]
  className?: string
  align?: "left" | "center" | "right"
  /** Tooltip con etichetta completa (default: label su una riga). */
  title?: string
}) {
  const lines = typeof label === "string" ? [label] : [...label]
  const tooltip = title ?? lines.join(" ")

  return (
    <TableHead
      title={tooltip}
      className={cn(
        HEAD_BASE,
        align === "center" && "text-center",
        align === "right" && "text-right",
        className
      )}
    >
      <span
        className={cn(
          "flex flex-col gap-0",
          align === "center" && "items-center",
          align === "right" && "items-end"
        )}
      >
        {lines.map((line) => (
          <span key={line}>{line}</span>
        ))}
      </span>
    </TableHead>
  )
}
