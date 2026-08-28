import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ChevronLeft, ChevronRight, DownloadCloud, KeyRound, Link2 } from "lucide-react"

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
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { fetchRealEmployees } from "@/lib/api/employees"
import type { HrDayStage } from "@/lib/api/types"
import { fetchHrTimesheet, fetchHrStatus, importHrPunches } from "@/lib/api/hr"
import { canWriteFeature } from "@/lib/auth/permissions"
import { formatDateShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { GiornataDialog } from "./GiornataDialog"
import { MappaturaEcosDialog } from "./MappaturaEcosDialog"
import { CalendarioPresenzeView } from "./CalendarioPresenzeView"
import { CredenzialiEcosDialog } from "./CredenzialiEcosDialog"
import { QuadraturaPresenzeView } from "./QuadraturaPresenzeView"

// Le tre letture della stessa giornata, come il ReportPage del programma «Timbrature»:
// 🔸 come e' arrivata dal rilevatore, 🔷 dopo l'arrotondamento, ✅ come vale. Affiancate si
// vede DOVE la giornata e' cambiata: quanto ha spostato lo scatto, quanto la pausa dedotta.
const COLUMNS: { id: string; label: string }[] = [
  { id: "grezzo", label: "🔸 Grezzo (come timbrato)" },
  { id: "normalizzato", label: "🔷 Normalizzato (arrotondato)" },
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
// v2: sono nati i due blocchi 🔸/🔷 - con la chiave vecchia resterebbero spenti.
const COLUMNS_STORAGE_KEY = "hr-timbrature-columns-v2"

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

/**
 * Le sei celle di uno stadio (grezzo o normalizzato): quattro orari, pausa e totale.
 * In grigio, perché non sono il dato che vale — servono a spiegare quello che vale.
 */
function StadioCelle({ stadio }: { stadio: HrDayStage }) {
  return (
    <>
      <TableCell className="border-l font-mono text-xs text-muted-foreground">
        {stadio.clockIn1}
      </TableCell>
      <TableCell className="font-mono text-xs text-muted-foreground">
        {stadio.clockOut1}
      </TableCell>
      <TableCell className="font-mono text-xs text-muted-foreground">
        {stadio.clockIn2}
      </TableCell>
      <TableCell className="font-mono text-xs text-muted-foreground">
        {stadio.clockOut2}
      </TableCell>
      <TableCell className="text-right font-mono text-xs text-muted-foreground">
        {stadio.breakTime}
      </TableCell>
      <TableCell className="text-right font-mono text-xs text-muted-foreground">
        {stadio.totalHours}
      </TableCell>
    </>
  )
}

function minutiDa(durata: string): number {
  const m = /^(\d+)h (\d+)m$/.exec(durata)
  return m ? Number(m[1]) * 60 + Number(m[2]) : 0
}

function durata(minuti: number): string {
  return `${Math.floor(minuti / 60)}h ${minuti % 60}m`
}

export function TimbraturePage() {
  const queryClient = useQueryClient()
  const canWrite = canWriteFeature("nav.hr_timbrature")

  const [vista, setVista] = React.useState<"cartellino" | "calendario" | "quadratura">(
    "cartellino"
  )
  const [periodo, setPeriodo] = React.useState(() => {
    const oggi = new Date()
    return { anno: oggi.getFullYear(), mese: oggi.getMonth() + 1 }
  })
  const [employeeId, setEmployeeId] = React.useState<number | null>(null)
  const [giornoAperto, setGiornoAperto] = React.useState<string | null>(null)
  const [mappaturaAperta, setMappaturaAperta] = React.useState(false)
  const [credenzialiAperte, setCredenzialiAperte] = React.useState(false)

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
    queryKey: ["hr-timesheet", periodo.anno, periodo.mese, employeeId],
    queryFn: () => fetchHrTimesheet(periodo.anno, periodo.mese, employeeId),
    enabled: vista === "cartellino",
  })
  const statoQuery = useQuery({
    queryKey: ["hr-status"],
    queryFn: fetchHrStatus,
    enabled: canWrite,
  })
  const dipendentiQuery = useQuery({
    queryKey: ["employees-real"],
    queryFn: fetchRealEmployees,
    enabled: canWrite,
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["hr-timesheet"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-calendar"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-quadratura"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-status"] })
  }

  const importa = useMutation({
    mutationFn: () => importHrPunches(false),
    onSuccess: (esito) => {
      notifySuccess(esito.message)
      invalidate()
    },
    onError: (e) => notifyError((e as Error).message),
  })

  const cartellino = cartellinoQuery.data
  const stato = statoQuery.data
  const nonAbbinati = importa.data?.unmatched ?? []

  const giornataAperta = React.useMemo(
    () => cartellino?.days.find((g) => g.workDate.slice(0, 10) === giornoAperto) ?? null,
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
    const giornate = cartellino?.days ?? []
    let ordinarie = 0
    let straordinario = 0
    let anomalie = 0
    for (const g of giornate) {
      if (!g.hasData) continue
      ordinarie += minutiDa(g.regularHours)
      straordinario += minutiDa(g.overtime)
      if (g.hasAnomaly) anomalie++
    }
    return { ordinarie, straordinario, anomalie }
  }, [cartellino])

  // I blocchi 🔸 e 🔷 valgono sei colonne ciascuno; le altre voci una a testa.
  const colonneFinali = COLUMNS.filter(
    (c) => c.id !== "grezzo" && c.id !== "normalizzato" && show(c.id)
  ).length
  const visibleCount =
    1 + colonneFinali + (show("grezzo") ? 6 : 0) + (show("normalizzato") ? 6 : 0)

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <div>
          <h1 className="text-lg font-semibold">Timbrature</h1>
          <p className="text-sm text-muted-foreground">
            Cartellino presenze da EcosAgile: ore, pausa, straordinari e quadratura con commesse.
          </p>
        </div>
        <div className="ml-auto flex flex-wrap items-center gap-2">
          {canWrite && vista === "cartellino" && (
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
                disabled={importa.isPending || stato?.importInProgress}
                onClick={() => importa.mutate()}
              >
                <DownloadCloud className="mr-1 size-3.5" />
                {importa.isPending ? "Import in corso…" : "Importa da Ecos"}
              </Button>
            </>
          )}
          {vista === "cartellino" && <ColumnsMenu columns={columnToggles} />}
        </div>
      </div>

      {/* Tabs Switcher: Cartellino vs Calendario vs Quadratura */}
      {canWrite && (
        <Tabs
          value={vista}
          onValueChange={(v) =>
            setVista(v as "cartellino" | "calendario" | "quadratura")
          }
        >
          <TabsList>
            <TabsTrigger value="cartellino">Cartellino individuale</TabsTrigger>
            <TabsTrigger value="calendario">Calendario mensile</TabsTrigger>
            <TabsTrigger value="quadratura">Quadratura commesse</TabsTrigger>
          </TabsList>
        </Tabs>
      )}

      {/* Toolbar mese */}
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

        {vista === "cartellino" && cartellino && (
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
        {canWrite && stato && !stato.configured && (
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
          <Button
            variant="outline"
            size="sm"
            onClick={() => setCredenzialiAperte(true)}
            title="Utente, password e Client ID con cui il server entra in Ecos"
          >
            <KeyRound className="mr-1 size-3.5" />
            Credenziali Ecos
          </Button>
        </div>
      )}

      {vista === "calendario" ? (
        <CalendarioPresenzeView anno={periodo.anno} mese={periodo.mese} />
      ) : vista === "quadratura" ? (
        <QuadraturaPresenzeView anno={periodo.anno} mese={periodo.mese} />
      ) : (
        <>
          {cartellino && !cartellino.ecosLinked && (
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
                  {(show("grezzo") || show("normalizzato")) && (
                    <TableRow className="hover:bg-transparent">
                      <TableHead className="w-28" />
                      {show("grezzo") && (
                        <TableHead
                          colSpan={6}
                          className="border-l text-center text-[11px] font-semibold"
                        >
                          🔸 Grezzo
                        </TableHead>
                      )}
                      {show("normalizzato") && (
                        <TableHead
                          colSpan={6}
                          className="border-l text-center text-[11px] font-semibold"
                        >
                          🔷 Normalizzato
                        </TableHead>
                      )}
                      {colonneFinali > 0 && (
                        <TableHead
                          colSpan={colonneFinali}
                          className="border-l text-center text-[11px] font-semibold"
                        >
                          ✅ Finale
                        </TableHead>
                      )}
                    </TableRow>
                  )}
                  <TableRow>
                    <TableHead className="w-28">Giorno</TableHead>
                    {show("grezzo") && (
                      <>
                        <TableHead className="border-l">E1</TableHead>
                        <TableHead>U1</TableHead>
                        <TableHead>E2</TableHead>
                        <TableHead>U2</TableHead>
                        <TableHead className="text-right">Pausa</TableHead>
                        <TableHead className="text-right">Ore</TableHead>
                      </>
                    )}
                    {show("normalizzato") && (
                      <>
                        <TableHead className="border-l">E1</TableHead>
                        <TableHead>U1</TableHead>
                        <TableHead>E2</TableHead>
                        <TableHead>U2</TableHead>
                        <TableHead className="text-right">Pausa</TableHead>
                        <TableHead className="text-right">Ore</TableHead>
                      </>
                    )}
                    {show("entrata1") && <TableHead className="border-l">E1</TableHead>}
                    {show("uscita1") && <TableHead>U1</TableHead>}
                    {show("entrata2") && <TableHead>E2</TableHead>}
                    {show("uscita2") && <TableHead>U2</TableHead>}
                    {show("ordinarie") && (
                      <TableHead className="text-right">Ordinarie</TableHead>
                    )}
                    {show("straordinario") && (
                      <TableHead className="text-right">Straord.</TableHead>
                    )}
                    {show("pausa") && (
                      <TableHead className="text-right">Pausa</TableHead>
                    )}
                    {show("nota") && <TableHead className="w-60">Nota</TableHead>}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {cartellino.days.map((g) => {
                    const dataIso = g.workDate.slice(0, 10)
                    const cliccabile = g.hasData || g.punches.length > 0 || canWrite
                    const fasceEntries = Object.entries(g.bands)

                    return (
                      <TableRow
                        key={g.workDate}
                        className={cn(
                          g.isHoliday && "bg-muted/40",
                          g.hasAnomaly && "bg-destructive/10 text-destructive",
                          cliccabile && "cursor-pointer hover:bg-muted/60"
                        )}
                        onClick={() => {
                          if (cliccabile) setGiornoAperto(dataIso)
                        }}
                      >
                        <TableCell className="font-mono text-xs">
                          {formatDateShort(g.workDate)}
                        </TableCell>
                        {show("grezzo") && <StadioCelle stadio={g.raw} />}
                        {show("normalizzato") && <StadioCelle stadio={g.normalized} />}
                        {show("entrata1") && (
                          <TableCell className="border-l font-mono text-xs">
                            {g.clockIn1 || "—"}
                          </TableCell>
                        )}
                        {show("uscita1") && (
                          <TableCell className="font-mono text-xs">
                            {g.clockOut1 || "—"}
                          </TableCell>
                        )}
                        {show("entrata2") && (
                          <TableCell className="font-mono text-xs">
                            {g.clockIn2 || "—"}
                          </TableCell>
                        )}
                        {show("uscita2") && (
                          <TableCell className="font-mono text-xs">
                            {g.clockOut2 || "—"}
                          </TableCell>
                        )}
                        {show("ordinarie") && (
                          <TableCell className="text-right font-mono text-xs">
                            {g.regularHours || "—"}
                          </TableCell>
                        )}
                        {show("straordinario") && (
                          <TableCell className="text-right font-mono text-xs">
                            {fasceEntries.length > 0 ? (
                              <Tooltip>
                                <TooltipTrigger asChild>
                                  <span className="underline decoration-dotted cursor-help">
                                    {g.overtime || "—"}
                                  </span>
                                </TooltipTrigger>
                                <TooltipContent className="text-xs">
                                  <p className="font-semibold mb-1">
                                    Dettaglio fasce CCNL:
                                  </p>
                                  {fasceEntries.map(([k, v]) => (
                                    <div key={k}>
                                      <b>{FASCE_LABELS[k] ?? `Fascia ${k}`}:</b> {v}
                                    </div>
                                  ))}
                                </TooltipContent>
                              </Tooltip>
                            ) : (
                              g.overtime || "—"
                            )}
                          </TableCell>
                        )}
                        {show("pausa") && (
                          <TableCell className="text-right font-mono text-xs text-muted-foreground">
                            {g.breakTime || "—"}
                          </TableCell>
                        )}
                        {show("nota") && (
                          <TableCell
                            className="max-w-60 truncate text-xs text-muted-foreground"
                            title={g.note}
                          >
                            {g.note || "—"}
                          </TableCell>
                        )}
                      </TableRow>
                    )
                  })}
                  {cartellino.days.length === 0 && (
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

          <CredenzialiEcosDialog
            open={credenzialiAperte}
            onOpenChange={setCredenzialiAperte}
          />

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
        </>
      )}

      <MappaturaEcosDialog
        open={mappaturaAperta}
        onOpenChange={setMappaturaAperta}
      />
    </div>
  )
}
