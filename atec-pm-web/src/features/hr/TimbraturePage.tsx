import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ChevronLeft, ChevronRight, DownloadCloud, Link2 } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  LookupCombobox,
  type LookupComboboxOption,
} from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { fetchRealEmployees } from "@/lib/api/employees"
import { fetchHrCartellino, fetchHrStato, importaTimbrature } from "@/lib/api/hr"
import { canWriteFeature } from "@/lib/auth/permissions"
import { formatDateShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { GiornataDialog } from "./GiornataDialog"
import { MappaturaEcosDialog } from "./MappaturaEcosDialog"

const COLUMNS: { id: string; label: string }[] = [
  { id: "entrata1", label: "Entrata 1" },
  { id: "uscita1", label: "Uscita 1" },
  { id: "entrata2", label: "Entrata 2" },
  { id: "uscita2", label: "Uscita 2" },
  { id: "ordinarie", label: "Ore ordinarie" },
  { id: "straordinario", label: "Straordinario" },
  { id: "pausa", label: "Pausa" },
  { id: "nota", label: "Nota" },
]
const COLUMNS_DEFAULT = Object.fromEntries(COLUMNS.map((c) => [c.id, true]))
const COLUMNS_STORAGE_KEY = "hr-timbrature-columns-v1"

/** Le fasce della Circolare n. 12 del 23.12.2024 (colonna «Non a turni»). */
const FASCE_LABELS: Record<string, string> = {
  A: "Straordinario diurno (20%)",
  C: "Festivo (55%)",
  D: "Festivo con riposo comp. (10%)",
  E: "Straord. festivo (55%)",
  F: "Straord. festivo con riposo comp. (35%)",
  G: "Straordinario notturno (50/60%)",
  H: "Notturno festivo (35%)",
  L: "Straord. notturno festivo (75%)",
  M: "Straord. nott. festivo con riposo comp. (55%)",
}

/** «8h 30m» → minuti; «---» e simili → 0 (il totale non deve inventare). */
function minutiDa(durata: string): number {
  const m = /^(\d+)h (\d+)m$/.exec(durata)
  return m ? Number(m[1]) * 60 + Number(m[2]) : 0
}

function durata(minuti: number): string {
  return `${Math.floor(minuti / 60)}h ${minuti % 60}m`
}

/**
 * Cartellino presenze (PIANO-HR-PRESENZE.md, Fase 1): le timbrature importate da
 * EcosAgile, elaborate dal motore CCNL. Con la sola lettura si vede il PROPRIO
 * cartellino; la scrittura apre i colleghi, l'import, la mappatura e le rettifiche.
 */
export function TimbraturePage() {
  const queryClient = useQueryClient()
  const canWrite = canWriteFeature("nav.hr_timbrature")

  const [periodo, setPeriodo] = React.useState(() => {
    const oggi = new Date()
    return { anno: oggi.getFullYear(), mese: oggi.getMonth() + 1 }
  })
  const [employeeId, setEmployeeId] = React.useState<number | null>(null)
  // 🪤 Si tiene la DATA, non l'oggetto giornata: con lo snapshot in state il dialogo
  // restava congelato dopo una rettifica (e su un ambiente condiviso anche dopo quella
  // di un collega), e si finiva per registrare due volte la stessa correzione.
  const [giornoAperto, setGiornoAperto] = React.useState<string | null>(null)
  const [mappaturaAperta, setMappaturaAperta] = React.useState(false)

  const [visible, setVisible] = usePersistedColumnVisibility(
    COLUMNS_STORAGE_KEY,
    COLUMNS_DEFAULT
  )
  const columnToggles = COLUMNS.map(({ id, label }) => ({
    id,
    label,
    checked: visible[id] ?? true,
    onToggle: (value: boolean) => setVisible((prev) => ({ ...prev, [id]: value })),
  }))
  const show = (id: string) => visible[id] ?? true

  const cartellinoQuery = useQuery({
    queryKey: ["hr-cartellino", periodo.anno, periodo.mese, employeeId],
    queryFn: () => fetchHrCartellino(periodo.anno, periodo.mese, employeeId),
  })
  // Lo stato dell'import è dietro la scrittura (riporta l'errore grezzo di Ecos): chi ha
  // la sola lettura non lo chiede nemmeno, altrimenti prenderebbe un 403 a ogni apertura.
  const statoQuery = useQuery({
    queryKey: ["hr-stato"],
    queryFn: fetchHrStato,
    enabled: canWrite,
  })
  const dipendentiQuery = useQuery({
    queryKey: ["employees-real"],
    queryFn: fetchRealEmployees,
    enabled: canWrite,
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["hr-cartellino"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-stato"] })
  }

  const importa = useMutation({
    mutationFn: () => importaTimbrature(false),
    onSuccess: (esito) => {
      notifySuccess(esito.messaggio)
      invalidate()
    },
    onError: (e) => notifyError((e as Error).message),
  })

  const cartellino = cartellinoQuery.data
  const stato = statoQuery.data
  // I codici non abbinati restano in pagina finché non si risolvono: le loro timbrature
  // sono state SCARTATE, e una notifica che sparisce in quattro secondi non basta a dirlo.
  const nonAbbinati = importa.data?.nonAbbinati ?? []

  const giornataAperta = React.useMemo(
    () => cartellino?.giornate.find((g) => g.giorno === giornoAperto) ?? null,
    [cartellino, giornoAperto]
  )

  const opzioniDipendenti: LookupComboboxOption<number>[] = React.useMemo(
    () => (dipendentiQuery.data ?? []).map((d) => ({ id: d.id, name: d.name })),
    [dipendentiQuery.data]
  )

  const meseLabel = new Date(periodo.anno, periodo.mese - 1, 1).toLocaleDateString(
    "it-IT",
    { month: "long", year: "numeric" }
  )

  function cambiaMese(delta: number) {
    setPeriodo((p) => {
      const d = new Date(p.anno, p.mese - 1 + delta, 1)
      return { anno: d.getFullYear(), mese: d.getMonth() + 1 }
    })
  }

  const totali = React.useMemo(() => {
    const giornate = cartellino?.giornate ?? []
    let ordinarie = 0
    let straordinario = 0
    let anomalie = 0
    for (const g of giornate) {
      if (!g.haDati) continue
      ordinarie += minutiDa(g.oreOrdinarie)
      straordinario += minutiDa(g.straordinario)
      if (g.anomalia) anomalie++
    }
    return { ordinarie, straordinario, anomalie }
  }, [cartellino])

  const visibleCount = COLUMNS.filter((c) => show(c.id)).length + 1

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <div>
          <h1 className="text-lg font-semibold">Timbrature</h1>
          <p className="text-sm text-muted-foreground">
            Cartellino presenze da EcosAgile: ore, pausa e straordinari per fascia CCNL.
          </p>
        </div>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          {canWrite && (
            <LookupCombobox<number>
              options={opzioniDipendenti}
              value={employeeId}
              onValueChange={setEmployeeId}
              placeholder="Il mio cartellino"
              noneLabel="— il mio cartellino —"
              loading={dipendentiQuery.isLoading}
              className="w-56"
            />
          )}
          {canWrite && (
            <>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setMappaturaAperta(true)}
              >
                <Link2 className="mr-1 size-3.5" />
                Collega Ecos
              </Button>
              <Button
                size="sm"
                disabled={importa.isPending || stato?.importInCorso}
                onClick={() => importa.mutate()}
              >
                <DownloadCloud className="mr-1 size-3.5" />
                {importa.isPending ? "Import in corso…" : "Importa da Ecos"}
              </Button>
            </>
          )}
          <ColumnsMenu columns={columnToggles} />
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Button variant="outline" size="icon-sm" onClick={() => cambiaMese(-1)}>
          <ChevronLeft className="size-4" />
        </Button>
        <span className="min-w-36 text-center text-sm font-medium capitalize">
          {meseLabel}
        </span>
        <Button variant="outline" size="icon-sm" onClick={() => cambiaMese(1)}>
          <ChevronRight className="size-4" />
        </Button>
        {cartellino && (
          <span className="text-sm text-muted-foreground">
            {cartellino.employeeName} · ordinarie {durata(totali.ordinarie)} ·
            straordinario {durata(totali.straordinario)}
            {totali.anomalie > 0 && (
              <span className="text-destructive">
                {" "}· {totali.anomalie}{" "}
                {totali.anomalie === 1 ? "anomalia" : "anomalie"}
              </span>
            )}
          </span>
        )}
        {canWrite && stato && !stato.configurato && (
          <span className="text-sm text-amber-600 dark:text-amber-500">
            Credenziali Ecos non configurate sul server: l'import è fermo.
          </span>
        )}
      </div>

      {canWrite && nonAbbinati.length > 0 && (
        <div className="rounded-lg border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
          <p className="font-medium">
            {nonAbbinati.length} codici Ecos non sono collegati a nessun dipendente: le
            loro timbrature sono state scartate.
          </p>
          <p className="mt-1 text-muted-foreground">{nonAbbinati.join(" · ")}</p>
          <Button
            variant="outline"
            size="sm"
            className="mt-2"
            onClick={() => setMappaturaAperta(true)}
          >
            <Link2 className="mr-1 size-3.5" />
            Collega e reimporta
          </Button>
        </div>
      )}

      {cartellino && !cartellino.ecosCollegato && (
        <p className="text-sm text-muted-foreground">
          {employeeId == null
            ? "Il tuo utente non è ancora collegato a Ecos: il cartellino si riempie quando l'amministratore collega il tuo codice badge."
            : "Dipendente non collegato a Ecos: nessuna timbratura può arrivare."}
        </p>
      )}

      {cartellinoQuery.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : cartellinoQuery.error ? (
        <p className="text-sm text-destructive">
          {(cartellinoQuery.error as Error).message}
        </p>
      ) : cartellino ? (
        <GridScroller className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-28">Giorno</TableHead>
                {show("entrata1") && <TableHead>E1</TableHead>}
                {show("uscita1") && <TableHead>U1</TableHead>}
                {show("entrata2") && <TableHead>E2</TableHead>}
                {show("uscita2") && <TableHead>U2</TableHead>}
                {show("ordinarie") && (
                  <TableHead className="text-right">Ordinarie</TableHead>
                )}
                {show("straordinario") && (
                  <TableHead className="text-right">Straord.</TableHead>
                )}
                {show("pausa") && <TableHead className="text-right">Pausa</TableHead>}
                {show("nota") && <TableHead>Nota</TableHead>}
              </TableRow>
            </TableHeader>
            <TableBody>
              {cartellino.giornate.map((g) => {
                const data = new Date(g.giorno)
                const settimana = data.toLocaleDateString("it-IT", {
                  weekday: "short",
                })
                return (
                  <TableRow
                    key={g.giorno}
                    className={cn(
                      "cursor-pointer",
                      g.festivo && "bg-muted/50 text-muted-foreground",
                      g.anomalia && "bg-destructive/5"
                    )}
                    onDoubleClick={() => setGiornoAperto(g.giorno)}
                  >
                    <TableCell className="whitespace-nowrap">
                      <span className="capitalize">{settimana}</span>{" "}
                      {formatDateShort(g.giorno)}
                    </TableCell>
                    {show("entrata1") && (
                      <TableCell className="tabular-nums">{g.entrata1 || "—"}</TableCell>
                    )}
                    {show("uscita1") && (
                      <TableCell className="tabular-nums">{g.uscita1 || "—"}</TableCell>
                    )}
                    {show("entrata2") && (
                      <TableCell className="tabular-nums">{g.entrata2 || "—"}</TableCell>
                    )}
                    {show("uscita2") && (
                      <TableCell className="tabular-nums">{g.uscita2 || "—"}</TableCell>
                    )}
                    {show("ordinarie") && (
                      <TableCell className="text-right tabular-nums">
                        {g.haDati ? g.oreOrdinarie : "—"}
                      </TableCell>
                    )}
                    {show("straordinario") && (
                      <TableCell className="text-right tabular-nums">
                        {g.haDati && Object.keys(g.fasce).length > 0 ? (
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <span className="underline decoration-dotted underline-offset-2">
                                {g.straordinario}
                              </span>
                            </TooltipTrigger>
                            <TooltipContent>
                              <div className="space-y-0.5">
                                {Object.entries(g.fasce).map(([fascia, valore]) => (
                                  <p key={fascia}>
                                    {FASCE_LABELS[fascia] ?? fascia}: {valore}
                                  </p>
                                ))}
                              </div>
                            </TooltipContent>
                          </Tooltip>
                        ) : g.haDati ? (
                          g.straordinario
                        ) : (
                          "—"
                        )}
                      </TableCell>
                    )}
                    {show("pausa") && (
                      <TableCell className="text-right tabular-nums">
                        {g.haDati ? g.pausa : "—"}
                      </TableCell>
                    )}
                    {show("nota") && (
                      <TableCell
                        className={cn(
                          "max-w-[280px] truncate",
                          g.anomalia && "text-destructive"
                        )}
                        title={g.nota}
                      >
                        {g.nota || "—"}
                      </TableCell>
                    )}
                  </TableRow>
                )
              })}
              {cartellino.giornate.length === 0 && (
                <TableRow>
                  <TableCell
                    colSpan={visibleCount}
                    className="text-center text-sm text-muted-foreground"
                  >
                    Nessuna giornata nel mese.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </GridScroller>
      ) : null}

      <GiornataDialog
        open={giornataAperta != null}
        onOpenChange={(open) => {
          if (!open) setGiornoAperto(null)
        }}
        giornata={giornataAperta}
        employeeId={cartellino?.employeeId ?? 0}
        canWrite={canWrite}
        onChanged={invalidate}
      />
      <MappaturaEcosDialog
        open={mappaturaAperta}
        onOpenChange={setMappaturaAperta}
      />
    </div>
  )
}
