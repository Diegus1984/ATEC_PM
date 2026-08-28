import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { X } from "lucide-react"

import { Button } from "@/components/ui/button"
import { notifyInfo } from "@/lib/toast"
import { Card, CardContent } from "@/components/ui/card"
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
  addCodexReference,
  confirmCodexReservation,
  fetchCodexPrefixes,
  releaseCodexReservation,
  reserveCodexCode,
} from "@/lib/api/codex"

import { CodexRefSearch, type RefItem } from "./CodexRefSearch"

/**
 * Pannello inline «Genera Codice» (admin): scelta prefisso → prenotazione
 * automatica del prossimo codice, descrizione, e (solo per prefisso 101)
 * riferimenti opzionali 201/401. Conferma = crea l'articolo. Replica il flusso
 * reserve/confirm/release della CodexPage WPF.
 */
export function CodexGeneratePanel({
  onClose,
  onGenerated,
}: {
  onClose: () => void
  onGenerated: (codice: string) => Promise<void>
}) {
  const prefixesQuery = useQuery({
    queryKey: ["codex-prefixes"],
    queryFn: fetchCodexPrefixes,
  })

  const [selectedPrefix, setSelectedPrefix] = React.useState("")
  const [descr, setDescr] = React.useState("")
  const [reservation, setReservation] = React.useState<{
    id: number
    code: string
  } | null>(null)
  const [ref201, setRef201] = React.useState<RefItem | null>(null)
  const [reserving, setReserving] = React.useState(false)
  const [confirming, setConfirming] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)

  const reservationRef = React.useRef<number | null>(null)
  const is101 = selectedPrefix === "101"

  // Rilascia la prenotazione pendente se il pannello viene smontato.
  React.useEffect(() => {
    return () => {
      const id = reservationRef.current
      if (id != null) {
        void releaseCodexReservation(id).catch(() => undefined)
      }
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
        // best-effort: la prenotazione scade comunque lato server
      }
    }
  }

  async function handlePrefixChange(value: string) {
    setError(null)
    await releaseCurrent()
    setSelectedPrefix(value)
    setRef201(null)
    if (!value) {
      return
    }
    setReserving(true)
    try {
      const res = await reserveCodexCode(value)
      reservationRef.current = res.reservationId
      setReservation({ id: res.reservationId, code: res.codice })
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setReserving(false)
    }
  }

  async function handleConfirm() {
    setError(null)
    if (!reservation) {
      setError("Nessun codice prenotato.")
      return
    }
    if (!descr.trim()) {
      setError("Inserisci una descrizione.")
      return
    }
    setConfirming(true)
    try {
      const generated = await confirmCodexReservation(
        reservation.id,
        descr.trim()
      )
      reservationRef.current = null
      // I riferimenti sono opzionali: un loro errore non deve annullare la
      // creazione dell'articolo (già avvenuta) — come nella CodexPage WPF.
      // Vincolo 401 liberato (21/07/2026): nella creazione dei 101 resta solo il
      // riferimento commerciale opzionale; il rif. Materia Prima non si compila più da qui
      // (l'API dei riferimenti lo supporta ancora, il campo è solo nascosto).
      if (is101 && ref201) {
        try {
          await addCodexReference({
            sourceCodexId: generated.id,
            refCodexId: ref201.id,
            refType: "201",
          })
        } catch (err) {
          notifyInfo(
            `Articolo ${generated.codice} creato, ma il riferimento non è stato salvato:\n\nRif. 201: ${(err as Error).message}`
          )
        }
      }
      await onGenerated(generated.codice)
    } catch (err) {
      setError((err as Error).message)
      setConfirming(false)
    }
  }

  async function handleCancel() {
    await releaseCurrent()
    onClose()
  }

  return (
    // overflow-visible: la Card base ha overflow-hidden, che taglierebbe la tendina
    // dei suggerimenti 201/401 al bordo del pannello invece di lasciarla sopra la griglia.
    <Card className="overflow-visible border-primary/40 bg-primary/5">
      <CardContent className="space-y-4 pt-6">
        <div className="flex items-center justify-between">
          <p className="text-sm font-semibold">Genera un nuovo articolo Codex</p>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            onClick={() => void handleCancel()}
          >
            <X />
            <span className="sr-only">Chiudi</span>
          </Button>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Prefisso</Label>
            <Select
              value={selectedPrefix}
              onValueChange={(value) => void handlePrefixChange(value)}
            >
              <SelectTrigger className="w-full bg-background">
                <SelectValue
                  placeholder={
                    prefixesQuery.isLoading
                      ? "Caricamento…"
                      : "Seleziona prefisso"
                  }
                />
              </SelectTrigger>
              <SelectContent>
                {(prefixesQuery.data ?? []).map((prefix) => (
                  <SelectItem key={prefix.codice} value={prefix.codice}>
                    {prefix.codice} — {prefix.descrizione}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="grid gap-1.5">
            <Label className="text-xs text-muted-foreground">Descrizione</Label>
            <Input
              value={descr}
              placeholder="Descrizione articolo"
              className="bg-background"
              onChange={(event) => setDescr(event.target.value)}
            />
          </div>
        </div>

        {is101 ? (
          <div className="grid gap-4 sm:grid-cols-2">
            <CodexRefSearch
              prefix="2"
              label="Rif. Commerciale (201) — opzionale"
              placeholder="Digita per cercare un 2xx…"
              value={ref201}
              onSelect={setRef201}
            />
            {/* Rif. Materia Prima (401): vincolo liberato il 21/07/2026 — campo nascosto,
                l'API dei riferimenti resta disponibile se servirà ripristinarlo. */}
          </div>
        ) : null}

        {reserving ? (
          <p className="text-sm text-muted-foreground">
            Prenotazione codice…
          </p>
        ) : reservation ? (
          <div className="flex items-center gap-2 text-sm">
            <span className="text-muted-foreground">Codice assegnato:</span>
            <span className="font-bold text-primary tabular-nums">
              {reservation.code}
            </span>
          </div>
        ) : null}

        {error ? <p className="text-sm text-destructive">{error}</p> : null}

        <div className="flex justify-end gap-2">
          <Button
            type="button"
            variant="outline"
            onClick={() => void handleCancel()}
            disabled={confirming}
          >
            Annulla
          </Button>
          <Button
            type="button"
            onClick={() => void handleConfirm()}
            disabled={!reservation || !descr.trim() || confirming}
          >
            {confirming ? "Generazione…" : "Genera"}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
