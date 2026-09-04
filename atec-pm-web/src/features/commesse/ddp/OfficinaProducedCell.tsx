import * as React from "react"

import { Input } from "@/components/ui/input"

/** Input inline pezzi prodotti (commit blur/Invio), stile cella DDP. */
export function OfficinaProducedCell({
  quantity,
  quantityProduced,
  disabled,
  onCommit,
}: {
  quantity: number
  quantityProduced: number
  disabled?: boolean
  onCommit: (value: number) => void
}) {
  const [draft, setDraft] = React.useState(String(quantityProduced))

  React.useEffect(() => {
    setDraft(String(quantityProduced))
  }, [quantityProduced])

  const commit = () => {
    const n = Number(String(draft).replace(",", "."))
    const next = Number.isFinite(n) ? Math.max(0, Math.min(quantity, n)) : 0
    if (next !== quantityProduced) onCommit(next)
    else setDraft(String(quantityProduced))
  }

  return (
    <div
      className="inline-flex items-center gap-0 text-xs tabular-nums text-current"
      onClick={(e) => e.stopPropagation()}
      onDoubleClick={(e) => e.stopPropagation()}
    >
      <Input
        inputMode="decimal"
        disabled={disabled}
        value={draft}
        className="h-7 w-9 border-transparent bg-transparent px-0.5 text-center text-xs tabular-nums text-current shadow-none focus:border-input focus:bg-white focus:text-foreground"
        onChange={(e) => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter") e.currentTarget.blur()
        }}
      />
      <span className="text-current">/{quantity}</span>
    </div>
  )
}
