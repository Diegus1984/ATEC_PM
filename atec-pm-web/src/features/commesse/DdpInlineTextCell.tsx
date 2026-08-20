import * as React from "react"

import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { cn } from "@/lib/utils"

/**
 * Campo testo inline di cella con commit su blur/Invio (stesso stile di
 * DdpDestinationSpecCell): sfondo trasparente che prende il colore della riga,
 * bianco solo mentre si scrive. Usato per «Rif. Danea», «Materiale», «Note».
 *
 * `multiline` per i campi di testo libero (Note): diventa una textarea che cresce
 * con il contenuto, così il testo lungo va a capo e la riga si alza invece di
 * essere tagliato (vedi BLOCKS-RULES «la riga si adatta al testo»). Invio conferma,
 * Shift+Invio va a capo. I campi corti (codici, riferimenti) restano a riga singola.
 */
export function DdpInlineTextCell({
  value,
  disabled,
  placeholder,
  multiline,
  onCommit,
}: {
  value: string
  disabled?: boolean
  placeholder?: string
  multiline?: boolean
  onCommit: (value: string) => void
}) {
  const [draft, setDraft] = React.useState(value)

  React.useEffect(() => {
    setDraft(value)
  }, [value])

  const shared = {
    value: draft,
    disabled,
    placeholder,
    onClick: (event: React.MouseEvent) => event.stopPropagation(),
    onDoubleClick: (event: React.MouseEvent) => event.stopPropagation(),
    onBlur: () => {
      if (draft !== value) onCommit(draft)
    },
  }

  const skin =
    "border-transparent bg-transparent text-xs shadow-none placeholder:text-current placeholder:opacity-60 focus:border-input focus:bg-white focus:text-foreground focus:placeholder:text-muted-foreground focus:placeholder:opacity-100"

  if (multiline) {
    return (
      <Textarea
        {...shared}
        rows={1}
        spellCheck={false}
        className={cn(
          "field-sizing-content min-h-8 w-full min-w-[110px] resize-none px-2 py-1 leading-5",
          skin
        )}
        onChange={(event) => setDraft(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter" && !event.shiftKey) {
            event.preventDefault()
            event.currentTarget.blur()
          }
        }}
      />
    )
  }

  return (
    <Input
      {...shared}
      className={cn("h-8 min-w-[110px]", skin)}
      onChange={(event) => setDraft(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.currentTarget.blur()
        }
      }}
    />
  )
}
