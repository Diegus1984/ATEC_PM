import { ChevronDown, ChevronUp } from "lucide-react"

import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

function formatQuantity(quantity: number): string {
  return Number.isInteger(quantity)
    ? String(quantity)
    : quantity.toLocaleString("it-IT", { maximumFractionDigits: 2 })
}

/** Qtà in griglia con triangoli su/giù (incremento rapido). */
export function DdpQuantityStepper({
  quantity,
  disabled,
  decrementDisabled,
  onIncrement,
  onDecrement,
  className,
}: {
  quantity: number
  disabled?: boolean
  decrementDisabled?: boolean
  onIncrement: () => void
  onDecrement: () => void
  className?: string
}) {
  return (
    <div
      className={cn("inline-flex items-center gap-0.5", className)}
      onClick={(event) => event.stopPropagation()}
      onDoubleClick={(event) => event.stopPropagation()}
    >
      <span className="min-w-[1.25rem] text-center text-sm tabular-nums">
        {formatQuantity(quantity)}
      </span>
      <div className="flex flex-col">
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="size-4 rounded-sm p-0 text-muted-foreground hover:bg-muted hover:text-foreground"
          disabled={disabled}
          aria-label="Aumenta quantità"
          onClick={(event) => {
            event.stopPropagation()
            onIncrement()
          }}
        >
          <ChevronUp className="size-3" strokeWidth={2.5} />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="size-4 rounded-sm p-0 text-muted-foreground hover:bg-muted hover:text-foreground"
          disabled={disabled || decrementDisabled}
          aria-label="Diminuisci quantità"
          onClick={(event) => {
            event.stopPropagation()
            onDecrement()
          }}
        >
          <ChevronDown className="size-3" strokeWidth={2.5} />
        </Button>
      </div>
    </div>
  )
}
