import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import {
  ChevronLeft,
  ChevronRight,
  Download,
  DownloadCloud,
  KeyRound,
  Link2,
  Mail,
  MailCheck,
  RotateCw,
  Search,
  User,
} from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
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
import { fetchPunchingEmployees } from "@/lib/api/employees"
import type { HrDayStage } from "@/lib/api/types"
import {
  downloadHrTimesheetExcel,
  fetchHrTimesheet,
  fetchHrStatus,
  resyncHrDay,
} from "@/lib/api/hr"
import { canWriteFeature } from "@/lib/auth/permissions"
import { formatDateShort, formatDateTimeShort } from "@/lib/date-iso"
import { formatDateWithWeekday } from "@/components/shared/date-field"
import { notifyError, notifySuccess } from "@/lib/toast"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { GiornataDialog } from "./GiornataDialog"
import { MappaturaEcosDialog } from "./MappaturaEcosDialog"
import { CalendarioPresenzeView } from "./CalendarioPresenzeView"
import { CredenzialiEcosDialog } from "./CredenzialiEcosDialog"
import { CronologiaMailView } from "./CronologiaMailView"
import { QuadraturaPresenzeView } from "./QuadraturaPresenzeView"
import { SincronizzaEcosDialog } from "./SincronizzaEcosDialog"
import { SollecitoGiornataDialog } from "./SollecitoGiornataDialog"

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
  { id: "azioni", label: "📧 Sollecito / 🔄 Risincronizza" },
]
const COLUMNS_DEFAULT = Object.fromEntries(COLUMNS.map((c) => [c.id, true]))
// v2: sono nati i due blocchi 🔸/🔷 - con la chiave vecchia resterebbero spenti.
// v3: e nata la colonna azioni (sollecito della giornata + risincronizzazione).
const COLUMNS_STORAGE_KEY = "hr-timbrature-columns-v3"

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

type Vista = "cartellino" | "calendario" | "quadratura" | "cronologia"

export function TimbraturePage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const canWrite = canWriteFeature("nav.hr_timbrature")

  const [vista, setVista] = React.useState<Vista>("cartellino")
  const [periodo, setPeriodo] = React.useState(() => {
    const oggi = new Date()
    return { anno: oggi.getFullYear(), mese: oggi.getMonth() + 1 }
  })
  const [employeeId, setEmployeeId] = React.useState<number | null>(null)
  const [searchEmployee, setSearchEmployee] = React.useState("")
  const [giornoAperto, setGiornoAperto] = React.useState<string | null>(null)
  const [mappaturaAperta, setMappaturaAperta] = React.useState(false)
  const [credenzialiAperte, setCredenzialiAperte] = React.useState(false)
  const [sincronizzaAperto, setSincronizzaAperto] = React.useState(false)
  // Voce 3 del port: l'interruttore «📧 Da segnalare» del ReportPage originale.
  const [soloDaSegnalare, setSoloDaSegnalare] = React.useState(false)
  const [sollecito, setSollecito] = React.useState<{
    employeeId: number
    date: string
  } | null>(null)
  const [nonAbbinati, setNonAbbinati] = React.useState<string[]>([])
  const [esportando, setEsportando] = React.useState(false)
  const [risincronizzando, setRisincronizzando] = React.useState<string | null>(null)

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
    queryKey: ["employees-punching"],
    queryFn: fetchPunchingEmployees,
    enabled: canWrite,
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["hr-timesheet"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-calendar"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-quadratura"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-status"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-reminder-log"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-day-reminder"] })
  }

  const cartellino = cartellinoQuery.data
  const stato = statoQuery.data

  const giornataAperta = React.useMemo(
    () => cartellino?.days.find((g) => g.workDate.slice(0, 10) === giornoAperto) ?? null,
    [cartellino, giornoAperto]
  )

  const dipendentiFiltrati = React.useMemo(() => {
    const list = dipendentiQuery.data ?? []
    if (!searchEmployee.trim()) return list
    const q = searchEmployee.toLowerCase()
    return list.filter((e) => e.name.toLowerCase().includes(q))
  }, [dipendentiQuery.data, searchEmployee])

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

  // Voce 3 del port: il filtro mostra le stesse giornate che hanno il pulsante 📧 —
  // la regola la decide il server (canRemind), qui non se ne fa una seconda copia.
  const giornate = React.useMemo(() => {
    const tutte = cartellino?.days ?? []
    return soloDaSegnalare ? tutte.filter((g) => g.canRemind) : tutte
  }, [cartellino, soloDaSegnalare])

  const daSegnalare = React.useMemo(
    () => (cartellino?.days ?? []).filter((g) => g.canRemind).length,
    [cartellino]
  )

  // La colonna azioni vive solo con la scrittura: sono due comandi, non due informazioni.
  const mostraAzioni = canWrite && show("azioni")

  // I blocchi 🔸 e 🔷 valgono sei colonne ciascuno; le altre voci una a testa.
  const colonneFinali = COLUMNS.filter(
    (c) =>
      c.id !== "grezzo" &&
      c.id !== "normalizzato" &&
      (c.id !== "azioni" || mostraAzioni) &&
      show(c.id)
  ).length
  const visibleCount =
    1 + colonneFinali + (show("grezzo") ? 6 : 0) + (show("normalizzato") ? 6 : 0)

  async function esportaExcel() {
    if (!cartellino) return
    setEsportando(true)
    try {
      await downloadHrTimesheetExcel(
        periodo.anno,
        periodo.mese,
        cartellino.employeeId,
        cartellino.employeeName
      )
    } catch (e) {
      notifyError(e instanceof Error ? e.message : "Esportazione non riuscita.")
    } finally {
      setEsportando(false)
    }
  }

  async function risincronizzaGiorno(dataIso: string) {
    if (!cartellino) return
    const ok = await confirm({
      title: `Risincronizzare il ${formatDateShort(dataIso)}?`,
      description:
        `Si riscaricano da Ecos le timbrature di ${cartellino.employeeName} per quel ` +
        "giorno e si ricalcola la giornata. Le timbrature cancellate su Ecos spariscono " +
        "anche qui; le rettifiche inserite a mano restano.",
      confirmLabel: "Risincronizza",
      destructive: false,
    })
    if (!ok) return

    setRisincronizzando(dataIso)
    try {
      const esito = await resyncHrDay(cartellino.employeeId, dataIso)
      notifySuccess(esito.message)
      invalidate()
    } catch (e) {
      notifyError(e instanceof Error ? e.message : "Risincronizzazione non riuscita.")
    } finally {
      setRisincronizzando(null)
    }
  }

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
          {canWrite && (
            <>
              {/* Le credenziali stanno QUI e non nel banner dei codici non abbinati: quel
                  banner compare solo dopo un import RIUSCITO, e le credenziali si devono
                  poter correggere proprio quando l'import non riesce più. */}
              <Button
                variant="outline"
                size="sm"
                onClick={() => setCredenzialiAperte(true)}
                title="Utente, password e Client ID con cui il server entra in Ecos"
              >
                <KeyRound className="mr-1 size-3.5" />
                Credenziali Ecos
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setMappaturaAperta(true)}
              >
                <Link2 className="mr-1 size-3.5" />
                Collega Ecos
              </Button>
              {/* Mentre l'import gira il pulsante NON si spegne: è da lì che si guarda
                  l'avanzamento — spegnerlo lascerebbe l'utente fuori dalla porta. */}
              <Button
                size="sm"
                onClick={() => setSincronizzaAperto(true)}
                title="Import da Ecos, sincronizzazione di un mese e avanzamento a video"
              >
                <DownloadCloud className="mr-1 size-3.5" />
                {stato?.importInProgress ? "Import in corso…" : "Sincronizza Ecos"}
              </Button>
            </>
          )}
          {vista === "cartellino" && <ColumnsMenu columns={columnToggles} />}
        </div>
      </div>

      {/* Tabs Switcher: Cartellino vs Calendario vs Quadratura vs Cronologia email */}
      {canWrite && (
        <Tabs value={vista} onValueChange={(v) => setVista(v as Vista)}>
          <TabsList>
            <TabsTrigger value="cartellino">Cartellino individuale</TabsTrigger>
            <TabsTrigger value="calendario">Calendario mensile</TabsTrigger>
            <TabsTrigger value="quadratura">Quadratura commesse</TabsTrigger>
            <TabsTrigger value="cronologia">Cronologia email</TabsTrigger>
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

        {vista === "cartellino" && cartellino && (
          <div className="ml-auto flex flex-wrap items-center gap-2">
            {/* Voce 3: «📧 Da segnalare», l'interruttore del ReportPage originale. */}
            <Button
              variant={soloDaSegnalare ? "default" : "outline"}
              size="sm"
              onClick={() => setSoloDaSegnalare((v) => !v)}
              title="Mostra solo le giornate per cui c'è una segnalazione da mandare"
            >
              <Mail className="mr-1 size-3.5" />
              Da segnalare{daSegnalare > 0 ? ` (${daSegnalare})` : ""}
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => void esportaExcel()}
              disabled={esportando}
            >
              <Download className="mr-1 size-3.5" />
              {esportando ? "Esporto…" : "Esporta Excel"}
            </Button>
          </div>
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

      {vista === "calendario" ? (
        <CalendarioPresenzeView anno={periodo.anno} mese={periodo.mese} />
      ) : vista === "quadratura" ? (
        <QuadraturaPresenzeView anno={periodo.anno} mese={periodo.mese} />
      ) : vista === "cronologia" ? (
        <CronologiaMailView anno={periodo.anno} mese={periodo.mese} />
      ) : (
        <div className={cn("flex flex-col gap-4", canWrite && "md:flex-row md:items-start")}>
          {/* Elenco dipendenti lato sinistro */}
          {canWrite && (
            <div className="w-full md:w-64 shrink-0 rounded-lg border bg-card p-2.5 space-y-2">
              <div className="flex items-center justify-between px-1">
                <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  Dipendenti ({dipendentiQuery.data?.length ?? 0})
                </span>
              </div>
              <div className="relative">
                <Search className="absolute left-2.5 top-2.5 size-3.5 text-muted-foreground" />
                <Input
                  placeholder="Cerca dipendente…"
                  value={searchEmployee}
                  onChange={(e) => setSearchEmployee(e.target.value)}
                  className="pl-8 h-8 text-xs"
                />
              </div>
              <div className="max-h-[calc(100vh-20rem)] min-h-[300px] overflow-y-auto space-y-0.5 pr-1">
                <button
                  type="button"
                  onClick={() => setEmployeeId(null)}
                  className={cn(
                    "flex w-full items-center gap-2 rounded-md px-2.5 py-1.5 text-xs text-left font-medium transition-colors cursor-pointer",
                    employeeId === null
                      ? "bg-primary text-primary-foreground font-semibold shadow-xs"
                      : "hover:bg-muted text-foreground"
                  )}
                >
                  <User className="size-3.5 shrink-0" />
                  <span className="truncate flex-1">Il mio cartellino</span>
                </button>
                <div className="my-1.5 border-t" />
                {dipendentiQuery.isLoading ? (
                  <p className="p-2 text-xs text-muted-foreground">Caricamento…</p>
                ) : dipendentiFiltrati.length === 0 ? (
                  <p className="p-2 text-xs text-muted-foreground">Nessun dipendente trovato.</p>
                ) : (
                  dipendentiFiltrati.map((emp) => (
                    <button
                      key={emp.id}
                      type="button"
                      onClick={() => setEmployeeId(emp.id)}
                      className={cn(
                        "flex w-full items-center gap-2 rounded-md px-2.5 py-1.5 text-xs text-left transition-colors cursor-pointer",
                        employeeId === emp.id
                          ? "bg-primary text-primary-foreground font-semibold shadow-xs"
                          : "hover:bg-muted text-foreground"
                      )}
                    >
                      <span
                        className={cn(
                          "size-1.5 rounded-full shrink-0",
                          employeeId === emp.id
                            ? "bg-primary-foreground"
                            : "bg-muted-foreground/60"
                        )}
                      />
                      <span className="truncate flex-1">{emp.name}</span>
                    </button>
                  ))
                )}
              </div>
            </div>
          )}

          {/* Area principale Cartellino */}
          <div className="min-w-0 flex-1 space-y-3">
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
                        <TableHead className="w-32" />
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
                      <TableHead className="w-32">Giorno</TableHead>
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
                      {mostraAzioni && <TableHead className="w-20 text-center">📧 🔄</TableHead>}
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {giornate.map((g) => {
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
                          <TableCell className="font-mono text-xs whitespace-nowrap">
                            {formatDateWithWeekday(g.workDate)}
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
                          {mostraAzioni && (
                            <TableCell
                              className="text-center whitespace-nowrap"
                              // I due comandi non aprono il dettaglio della giornata.
                              onClick={(e) => e.stopPropagation()}
                            >
                              {/* Voce 1: il 📧 dell'originale, solo dove serve davvero.
                                  Già mandato = busta chiusa e opaca, col giorno nel tooltip. */}
                              {g.canRemind && (
                                <Button
                                  variant="ghost"
                                  size="icon-sm"
                                  className={cn(g.lastReminderAt && "opacity-50")}
                                  onClick={() =>
                                    setSollecito({
                                      employeeId: cartellino.employeeId,
                                      date: dataIso,
                                    })
                                  }
                                  title={
                                    g.lastReminderAt
                                      ? `Sollecito già inviato il ${formatDateTimeShort(g.lastReminderAt)}`
                                      : "Invia segnalazione anomalia al dipendente"
                                  }
                                >
                                  {g.lastReminderAt ? (
                                    <MailCheck className="size-3.5" />
                                  ) : (
                                    <Mail className="size-3.5 text-amber-600 dark:text-amber-500" />
                                  )}
                                </Button>
                              )}
                              {/* Voce 2: risincronizza questo giorno da Ecos. */}
                              <Button
                                variant="ghost"
                                size="icon-sm"
                                disabled={risincronizzando != null}
                                onClick={() => void risincronizzaGiorno(dataIso)}
                                title="Risincronizza questo giorno da Ecos"
                              >
                                <RotateCw
                                  className={cn(
                                    "size-3.5",
                                    risincronizzando === dataIso && "animate-spin"
                                  )}
                                />
                              </Button>
                            </TableCell>
                          )}
                        </TableRow>
                      )
                    })}
                    {giornate.length === 0 && (
                      <TableRow>
                        <TableCell
                          colSpan={visibleCount}
                          className="text-center text-sm text-muted-foreground"
                        >
                          {soloDaSegnalare
                            ? "Nessuna giornata da segnalare in questo mese."
                            : "Nessuna giornata nel mese."}
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
          </div>
        </div>
      )}

      <MappaturaEcosDialog
        open={mappaturaAperta}
        onOpenChange={setMappaturaAperta}
      />

      {/* Fuori dal ramo del cartellino: le credenziali si devono poter aprire da qualunque
          scheda, e prima si montava solo quando era a video la griglia del cartellino. */}
      <CredenzialiEcosDialog
        open={credenzialiAperte}
        onOpenChange={setCredenzialiAperte}
      />

      <SincronizzaEcosDialog
        open={sincronizzaAperto}
        onOpenChange={setSincronizzaAperto}
        anno={periodo.anno}
        mese={periodo.mese}
        onImported={(esito) => {
          setNonAbbinati(esito?.unmatched ?? [])
          invalidate()
        }}
      />

      <SollecitoGiornataDialog
        target={sollecito}
        onOpenChange={(open) => {
          if (!open) setSollecito(null)
        }}
        onSent={invalidate}
      />
    </div>
  )
}
