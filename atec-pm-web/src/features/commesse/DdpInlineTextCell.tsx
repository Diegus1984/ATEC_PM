import * as React from "react"

import { Input } from "@/components/ui/input"

/**
 * Input inline di cella con commit su blur/Invio (stesso stile di
 * DdpDestinationSpecCell): sfondo trasparente che prende il colore della riga,
 * bianco solo mentre si scrive. Usato per «Rif. Danea».
 */
export function DdpInlineTextCell({
  value,
  disabled,
  placeholder,
  onCommit,
}: {
  value: string
  disabled?: boolean
  placeholder?: string
  onCommit: (value: string) => void
}) {
  const [draft, setDraft] = React.useState(value)

  React.useEffect(() => {
    setDraft(value)
  }, [value])

  return (
    <Input
      value={draft}
      disabled={disabled}
      placeholder={placeholder}
      className="h-8 min-w-[110px] border-transparent bg-transparent text-xs shadow-none placeholder:text-current placeholder:opacity-60 focus:border-input focus:bg-white focus:text-foreground focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"
      onClick={(event) => event.stopPropagation()}
      onDoubleClick={(event) => event.stopPropagation()}
      onChange={(event) => setDraft(event.target.value)}
      onBlur={() => {
        if (draft !== value) {
          onCommit(draft)
        }
      }}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.currentTarget.blur()
        }
      }}
    />
  )
}
