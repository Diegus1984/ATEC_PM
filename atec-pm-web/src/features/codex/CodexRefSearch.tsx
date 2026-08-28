import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { fetchCodex } from "@/lib/api/codex"
import { useDebounced } from "@/lib/use-debounced"

/** Articolo scelto come riferimento: `id` è l'id Codex dell'articolo, non della derivazione. */
export interface RefItem {
  id: number
  codice: string
  descr: string
}

/**
 * Campo di ricerca riferimento (201/401): digitando ≥2 caratteri cerca tra gli
 * articoli Codex il cui codice inizia col prefisso indicato, e lascia scegliere.
 *
 * Vive in un file proprio perché lo usano due posti (#135): la nascita del codice
 * (CodexGeneratePanel) e la scheda articolo (CodexEditDialog), dove la derivazione
 * si può cambiare anche dopo.
 */
export function CodexRefSearch({
  prefix,
  label,
  placeholder,
  value,
  disabled,
  onSelect,
}: {
  prefix: string
  label: string
  placeholder: string
  value: RefItem | null
  /** Sola lettura: mostra la derivazione ma non lascia cercare né togliere. */
  disabled?: boolean
  onSelect: (item: RefItem | null) => void
}) {
  const [text, setText] = React.useState("")
  const [focused, setFocused] = React.useState(false)
  const debounced = useDebounced(text.trim(), 300)

  const query = useQuery({
    queryKey: ["codex-ref", prefix, debounced],
    queryFn: () => fetchCodex({ search: debounced, codicePrefixes: [prefix], pageSize: 50 }),
    enabled: !value && debounced.length >= 2,
  })

  const results = React.useMemo(
    () =>
      (query.data?.items ?? [])
        .filter((item) => item.codice.startsWith(prefix))
        .slice(0, 25),
    [query.data, prefix]
  )

  const showResults = focused && !value && debounced.length >= 2

  return (
    <div className="grid gap-1.5">
      <Label className="text-xs text-muted-foreground">{label}</Label>
      {value ? (
        <div className="flex items-center justify-between gap-2 rounded-md border bg-background px-3 py-2 text-sm">
          <span className="truncate tabular-nums">
            <span className="font-medium">{value.codice}</span>
            {value.descr ? ` — ${value.descr}` : ""}
          </span>
          {disabled ? null : (
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              className="shrink-0"
              onClick={() => {
                onSelect(null)
                setText("")
              }}
            >
              <X />
              <span className="sr-only">Rimuovi riferimento</span>
            </Button>
          )}
        </div>
      ) : disabled ? (
        <p className="rounded-md border bg-muted/40 px-3 py-2 text-sm text-muted-foreground">
          Nessun riferimento.
        </p>
      ) : (
        <div className="relative">
          <Input
            value={text}
            placeholder={placeholder}
            className="bg-background"
            onChange={(event) => setText(event.target.value)}
            onFocus={() => setFocused(true)}
            onBlur={() => window.setTimeout(() => setFocused(false), 150)}
          />
          {showResults ? (
            <div className="absolute z-50 mt-1 max-h-[480px] w-full overflow-auto rounded-md border bg-popover p-1 shadow-md">
              {query.isFetching && results.length === 0 ? (
                <p className="px-2 py-1.5 text-sm text-muted-foreground">
                  Ricerca…
                </p>
              ) : results.length === 0 ? (
                <p className="px-2 py-1.5 text-sm text-muted-foreground">
                  Nessun risultato.
                </p>
              ) : (
                results.map((item) => (
                  <button
                    type="button"
                    key={item.id}
                    className="flex w-full flex-col items-start rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
                    onMouseDown={(event) => {
                      // onMouseDown così non perde il focus prima del click
                      event.preventDefault()
                      onSelect({
                        id: item.id,
                        codice: item.codice,
                        descr: item.descr,
                      })
                      setText("")
                      setFocused(false)
                    }}
                  >
                    <span className="font-medium tabular-nums">
                      {item.codice}
                    </span>
                    {item.descr ? (
                      <span className="truncate text-xs text-muted-foreground">
                        {item.descr}
                      </span>
                    ) : null}
                  </button>
                ))
              )}
            </div>
          ) : null}
        </div>
      )}
    </div>
  )
}
