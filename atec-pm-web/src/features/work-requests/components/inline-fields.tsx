import * as React from "react"

import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { cn } from "@/lib/utils"

interface InlineFieldProps {
  value: string
  onCommit: (value: string) => void
  placeholder?: string
  className?: string
  /** `relaxed` mantiene più righe visibili a riposo (es. colonna Note). */
  variant?: "compact" | "relaxed"
}

// Campi testo inline con salvataggio al blur: lo stato locale (draft) evita una
// PATCH per ogni tasto premuto e il refetch che sovrascriverebbe il testo mentre
// l'utente sta ancora digitando. Stile allineato all'AutoTextarea della Check list:
// trasparente a riposo, bordo/fondo visibili solo in modifica, una riga che cresce
// solo col focus.

export function InlineTextarea({
  value,
  onCommit,
  placeholder,
  className,
}: InlineFieldProps) {
  const ref = React.useRef<HTMLTextAreaElement>(null)
  const [draft, setDraft] = React.useState(value)
  const [focused, setFocused] = React.useState(false)

  // Allinea il draft al valore dal server solo quando il campo non è in editing
  React.useEffect(() => {
    if (!focused) setDraft(value)
  }, [value, focused])

  React.useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    el.style.height = "auto"
    const minHeight = 32
    el.style.height = `${Math.max(minHeight, el.scrollHeight + 2)}px`
  }, [draft, focused])

  const commit = () => {
    if (draft !== value) onCommit(draft)
  }

  return (
    <Textarea
      ref={ref}
      rows={1}
      value={draft}
      placeholder={placeholder}
      spellCheck={false}
      className={cn(
        "field-sizing-fixed min-h-0 resize-none rounded-md border border-input bg-white dark:bg-zinc-950 px-2 py-1 text-sm leading-5 shadow-none w-full",
        focused && "focus-visible:ring-1 focus-visible:ring-ring/40",
        className
      )}
      onChange={(e) => setDraft(e.target.value)}
      onFocus={() => setFocused(true)}
      onBlur={() => {
        setFocused(false)
        commit()
      }}
      onKeyDown={(e) => {
        if (e.key === "Enter" && e.ctrlKey) {
          e.preventDefault()
          e.currentTarget.blur()
        }
      }}
    />
  )
}

export function InlineInput({ value, onCommit, placeholder, className }: InlineFieldProps) {
  const [draft, setDraft] = React.useState(value)
  const [focused, setFocused] = React.useState(false)

  React.useEffect(() => {
    if (!focused) setDraft(value)
  }, [value, focused])

  return (
    <Input
      className={cn("h-8 px-2 text-sm shadow-none bg-white dark:bg-zinc-950", className)}
      placeholder={placeholder}
      value={draft}
      onChange={(e) => setDraft(e.target.value)}
      onFocus={() => setFocused(true)}
      onBlur={() => {
        setFocused(false)
        if (draft !== value) onCommit(draft)
      }}
      onKeyDown={(e) => {
        if (e.key === "Enter") e.currentTarget.blur()
      }}
    />
  )
}
