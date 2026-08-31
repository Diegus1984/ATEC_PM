import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Plus, X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  confirmCodexReservation,
  fetchCodex,
  fetchCodexPrefixes,
  releaseCodexReservation,
  reserveCodexCode,
} from "@/lib/api/codex"
import { canWriteFeature } from "@/lib/auth/permissions"
import { useDebounced } from "@/lib/use-debounced"

/** Articolo scelto come riferimento: `id` è l'id Codex dell'articolo, non della derivazione. */
export interface RefItem {
  id: number
  codice: string
  descr: string
}

/**
 * #142 — «Nuovo 2xx…»: il progettista non sempre trova il commerciale già codificato,
 * e a metà della derivazione non deve cambiare pagina per crearlo. Stessa meccanica
 * reserve/confirm del CodexGeneratePanel, in piccolo: famiglia (i prefissi 2xx
 * dell'anagrafica — se un giorno resterà solo il 201, basta l'anagrafica), descrizione,
 * conferma. Il codice appena nato viene selezionato SUBITO come riferimento.
 * Il 201 nasce senza articoli Danea: i suoi grezzi saranno «da associare» (bloccati)
 * finché qualcuno non fa l'associazione — è il giro voluto, non un difetto.
 */
function QuickCreate2xx({ onCreated }: { onCreated: (item: RefItem) => void }) {
  const [aperto, setAperto] = React.useState(false)
  const [famiglia, setFamiglia] = React.useState("")
  const [descr, setDescr] = React.useState("")
  const [reservation, setReservation] = React.useState<{
    id: number
    code: string
  } | null>(null)
  const [busy, setBusy] = React.useState(false)
  const [errore, setErrore] = React.useState<string | null>(null)
  const reservationRef = React.useRef<number | null>(null)

  const prefixesQuery = useQuery({
    queryKey: ["codex-prefixes"],
    queryFn: fetchCodexPrefixes,
    enabled: aperto,
  })
  const famiglie = (prefixesQuery.data ?? []).filter((p) =>
    p.codice.startsWith("2")
  )

  // Prenotazione pendente rilasciata se il blocchetto viene smontato (best-effort:
  // lato server scade comunque) — stessa cura del CodexGeneratePanel.
  React.useEffect(() => {
    return () => {
      const id = reservationRef.current
      if (id != null) void releaseCodexReservation(id).catch(() => undefined)
    }
  }, [])

  async function releaseCurrent() {
    const id = reservationRef.current
    reservationRef.current = null
    setReservation(null)
    if (id != null) {
      try {
        await releaseCodexReservation(id)
      } catch {
        // best-effort
      }
    }
  }

  async function handleFamiglia(value: string) {
    setErrore(null)
    await releaseCurrent()
    setFamiglia(value)
    if (!value) return
    setBusy(true)
    try {
      const res = await reserveCodexCode(value)
      reservationRef.current = res.reservationId
      setReservation({ id: res.reservationId, code: res.codice })
    } catch (err) {
      setErrore((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleCrea() {
    if (!reservation || !descr.trim()) return
    setErrore(null)
    setBusy(true)
    try {
      const generated = await confirmCodexReservation(
        reservation.id,
        descr.trim()
      )
      reservationRef.current = null
      onCreated({ id: generated.id, codice: generated.codice, descr: descr.trim() })
      setAperto(false)
      setFamiglia("")
      setDescr("")
      setReservation(null)
    } catch (err) {
      setErrore((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleAnnulla() {
    await releaseCurrent()
    setErrore(null)
    setFamiglia("")
    setDescr("")
    setAperto(false)
  }

  if (!aperto) {
    return (
      <button
        type="button"
        className="mt-1 inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
        onClick={() => setAperto(true)}
      >
        <Plus className="size-3" />
        Il commerciale non esiste ancora? Crea un nuovo 2xx
      </button>
    )
  }

  return (
    <div className="mt-1 space-y-2 rounded-md border border-primary/40 bg-primary/5 p-2">
      <div className="grid gap-2 sm:grid-cols-2">
        <Select value={famiglia} onValueChange={(v) => void handleFamiglia(v)}>
          <SelectTrigger size="sm" className="w-full bg-background">
            <SelectValue
              placeholder={
                prefixesQuery.isLoading ? "Caricamento…" : "Famiglia (2xx)"
              }
            />
          </SelectTrigger>
          <SelectContent>
            {famiglie.map((p) => (
              <SelectItem key={p.codice} value={p.codice}>
                {p.codice} — {p.descrizione}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Input
          value={descr}
          placeholder="Descrizione articolo"
          className="h-8 bg-background text-sm"
          onChange={(e) => setDescr(e.target.value)}
        />
      </div>
      {reservation ? (
        <p className="text-xs">
          <span className="text-muted-foreground">Codice assegnato: </span>
          <span className="font-bold tabular-nums text-primary">
            {reservation.code}
          </span>
        </p>
      ) : null}
      {errore ? <p className="text-xs text-destructive">{errore}</p> : null}
      <div className="flex justify-end gap-2">
        <Button
          type="button"
          size="sm"
          variant="outline"
          disabled={busy}
          onClick={() => void handleAnnulla()}
        >
          Annulla
        </Button>
        <Button
          type="button"
          size="sm"
          disabled={!reservation || !descr.trim() || busy}
          onClick={() => void handleCrea()}
        >
          {busy ? "…" : "Crea e collega"}
        </Button>
      </div>
    </div>
  )
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

  // #142: creazione al volo solo per il riferimento commerciale (2xx), con gli stessi
  // permessi del «Nuovo codice Codex» del picker (il server li fa rispettare comunque).
  const puoCreare2xx =
    prefix.startsWith("2") &&
    (canWriteFeature("action.manage_codex") ||
      canWriteFeature("action.assign_atec_code") ||
      canWriteFeature("project.ddp_officina"))

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
          {puoCreare2xx ? (
            <QuickCreate2xx
              onCreated={(item) => {
                onSelect(item)
                setText("")
                setFocused(false)
              }}
            />
          ) : null}
        </div>
      )}
    </div>
  )
}
