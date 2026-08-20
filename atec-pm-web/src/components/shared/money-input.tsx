import * as React from "react"

import { Input } from "@/components/ui/input"
import { euro, parseDecimal } from "@/lib/format"
import { cn } from "@/lib/utils"

/**
 * Campo importo: mostra il valore formattato (`100.000,00 €`) quando non è in
 * modifica e il numero grezzo appena ci clicchi dentro — così si legge come il
 * resto dell'app ma resta scrivibile (vedi BLOCKS-RULES «importi sempre con € e
 * due decimali»).
 *
 * Il valore resta una STRINGA verso il chiamante, come un input normale: chi lo usa
 * decide quando convertirlo (di norma nel commit con `parseDecimal`). Accetta sia la
 * virgola sia il punto come separatore decimale.
 */
export function MoneyInput({
  value,
  onChange,
  onCommit,
  disabled,
  placeholder,
  className,
  onKeyDown,
  format = euro,
  ...rest
}: {
  value: string
  onChange: (value: string) => void
  /** Chiamato al blur e su Invio (il valore è già in `value`). */
  onCommit?: () => void
  disabled?: boolean
  placeholder?: string
  className?: string
  onKeyDown?: React.KeyboardEventHandler<HTMLInputElement>
  /**
   * Come si legge l'importo quando il campo NON è in modifica. Default `euro()`.
   * Da cambiare solo dove il € completo non ci sta (griglie a molte colonne):
   * il valore raccolto e la normalizzazione al blur non cambiano comunque.
   */
  format?: (value: number) => string
} & Omit<
  React.ComponentProps<typeof Input>,
  | "value"
  | "onChange"
  | "type"
  | "disabled"
  | "placeholder"
  | "className"
  | "onKeyDown"
>) {
  const [focused, setFocused] = React.useState(false)

  const shown =
    focused || value.trim() === "" ? value : format(parseDecimal(value))

  return (
    <Input
      {...rest}
      // Sempre text: con type=number il browser rifiuterebbe la stringa formattata.
      type="text"
      inputMode="decimal"
      value={shown}
      disabled={disabled}
      placeholder={placeholder}
      className={cn("text-right tabular-nums", className)}
      onFocus={(event) => {
        setFocused(true)
        // Selezione totale: si riscrive l'importo senza dover cancellare.
        event.currentTarget.select()
      }}
      onChange={(event) => onChange(event.target.value)}
      onBlur={() => {
        setFocused(false)
        // Normalizza quello che resta nello stato del form: chi salva fa `Number(...)`
        // o `parseDecimal(...)` e non deve inciampare su «1.234,50».
        const trimmed = value.trim()
        if (trimmed !== "") {
          const canonical = String(parseDecimal(trimmed))
          if (canonical !== trimmed) onChange(canonical)
        }
        onCommit?.()
      }}
      onKeyDown={(event) => {
        // Invio: prima esce dal campo (così scatta la normalizzazione + onCommit),
        // poi lascia comunque passare l'handler del chiamante (es. «salva e chiudi»).
        if (event.key === "Enter") event.currentTarget.blur()
        onKeyDown?.(event)
      }}
    />
  )
}
