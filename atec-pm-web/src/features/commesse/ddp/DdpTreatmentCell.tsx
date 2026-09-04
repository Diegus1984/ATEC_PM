import type { DdpTreatmentItem } from "@/lib/api/types"
import { DdpTreatmentCombo } from "./DdpTreatmentCombo"

/**
 * Cella «Trattamento»: testo + menu «⋮» (DropdownMenu, come Stato/Fornitore).
 */
export function DdpTreatmentCell({
  treatment,
  treatments,
  disabled,
  onTreatmentChange,
}: {
  treatment: string
  treatments: DdpTreatmentItem[]
  disabled?: boolean
  onTreatmentChange: (treatment: string) => void
}) {
  return (
    <div
      className="flex min-w-[150px] items-center gap-1"
      onClick={(event) => event.stopPropagation()}
      onDoubleClick={(event) => event.stopPropagation()}
    >
      <span
        className="min-w-0 flex-1 truncate font-semibold whitespace-nowrap"
        title={treatment || undefined}
      >
        {treatment || "—"}
      </span>
      <DdpTreatmentCombo
        treatment={treatment}
        treatments={treatments}
        disabled={disabled}
        onSelect={onTreatmentChange}
      />
    </div>
  )
}
