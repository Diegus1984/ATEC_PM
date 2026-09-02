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
} from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
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
import type { HrDay } from "@/lib/api/types"
import {
  downloadHrTimesheetExcel,
  fetchHrTimesheet,
  fetchHrStatus,
  resyncHrDay,
} from "@/lib/api/hr"
import { canWriteFeature } from "@/lib/auth/permissions"
import { formatDateShort, formatDateTimeShort } from "@/lib/date-iso"
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
import { StatoGiornata, statoGiornata, type ToneStato } from "./stato-giornata"

// Il cartellino letto da chi non usa il computer tutti i giorni (02/09/2026): una riga per
// giorno, l'ora che vale in grande e quella timbrata in piccolo sotto, una colonna che dice
// in parole com'è la giornata. Le due letture «grezzo» e «normalizzato» del ReportPage
// originale non sono sparite: gli orari grezzi stanno sotto ogni ora, pausa e totale grezzi
// dietro la colonna «Prima dell'arrotondamento», spenta di default.
const COLUMNS: { id: string; label: string }[] = [
  { id: "entrata1", label: "Entrata (mattina)" },
  { id: "uscita1", label: "Uscita (mattina)" },
  { id: "entrata2", label: "Entrata (pomeriggio)" },
  { id: "uscita2", label: "Uscita (pomeriggio)" },
  { id: "ore", label: "Ore" },
  { id: "straordinario", label: "Straordinario" },
  { id: "stato", label: "Com'è la giornata" },
  { id: "pausa", label: "Pausa" },
  { id: "calcolo", label: "Prima dell'arrotondamento (pausa e ore)" },
  { id: "nota", label: "Nota del motore" },
]
const COLUMNS_DEFAULT: Record<string, boolean> = {
  ...Object.fromEntries(COLUMNS.map((c) => [c.id, true])),
  pausa: false,
  calcolo: false,
  nota: false,
}
// v2: sono nati i due blocchi 🔸/🔷 - con la chiave vecchia resterebbero spenti.
// v3: e nata la colonna azioni (sollecito della giornata + risincronizzazione).
// v4: griglia leggibile — via i blocchi, arriva «Com'è la giornata»; azioni nel dettaglio.
const COLUMNS_STORAGE_KEY = "hr-timbrature-columns-v4"

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

/** «Il mio cartellino» nella tendina dei dipendenti: 0 non è l'id di nessuno. */
const MIO_CARTELLINO = 0

function minutiDa(durata: string): number {
  const m = /^(\d+)h (\d+)m$/.exec(durata)
  return m ? Number(m[1]) * 60 + Number(m[2]) : 0
}

function durata(minuti: number): string {
  return `${Math.floor(minuti / 60)}h ${String(minuti % 60).padStart(2, "0")}m`
}

/** «8h 0m» → «8h 00m»; «---» e vuoto restano com'erano. */
function oreLeggibili(valore: string): string {
  const m = /^(\d+)h (\d+)m$/.exec(valore)
  return m ? `${m[1]}h ${m[2].padStart(2, "0")}m` : valore
}

function isZero(valore: string): boolean {
  return !valore || valore === "---" || minutiDa(valore) === 0
}

/**
 * Una cella di orario: in grande l'ora che vale, in piccolo l'ora timbrata davvero quando
 * è diversa. «??:??» del motore (uscita mai timbrata) diventa una parola.
 */
function CellaOra({
  valore,
  timbrato,
  spenta,
}: {
  valore: string
  timbrato: string
  spenta: boolean
}) {
  if (valore === "??:??") {
    return (
      <TableCell className="leading-tight">
        <span className="text-destructive font-semibold">Non timbrata</span>
      </TableCell>
    )
  }
  if (!valore) {
    return (
      <TableCell className="leading-tight">
        <span className="text-muted-foreground">—</span>
        {timbrato && (
          <span className="block text-[11px] text-muted-foreground">timbrato {timbrato}</span>
        )}
      </TableCell>
    )
  }
  const diversa = timbrato && timbrato !== valore
  return (
    <TableCell className="leading-tight">
      <span className={cn("tabular-nums font-semibold", spenta && "text-muted-foreground")}>
        {valore}
      </span>
      <span
        className={cn(
          "block text-[11px] tabular-nums text-muted-foreground",
          !diversa && "invisible"
        )}
        aria-hidden={!diversa}
      >
        timbrato {diversa ? timbrato : "—"}
      </span>
    </TableCell>
  )
}

/** Etichetta del giorno nella griglia: «12 agosto» sopra, «mercoledì» sotto. */
function EtichettaGiorno({ g }: { g: HrDay }) {
  const d = new Date(g.workDate)
  return (
    <TableCell className="whitespace-nowrap leading-tight">
      <span className="font-semibold tabular-nums">
        {d.getDate()} {d.toLocaleDateString("it-IT", { month: "long" })}
      </span>
      <span className="block text-[11px] capitalize text-muted-foreground">
        {d.toLocaleDateString("it-IT", { weekday: "long" })}
      </span>
    </TableCell>
  )
}

function Riquadro({
  etichetta,
  valore,
  dettaglio,
  tone,
}: {
  etichetta: string
  valore: string
  dettaglio: string
  tone?: ToneStato
}) {
  return (
    <div
      className={cn(
        "rounded-lg border bg-card px-4 py-3 shadow-xs",
        tone === "bad" && "border-destructive/40"
      )}
    >
      <p className="text-sm text-muted-foreground">{etichetta}</p>
      <p
        className={cn(
          "text-2xl font-bold leading-tight tabular-nums",
          tone === "bad" && "text-destructive",
          tone === "warn" && "text-amber-600 dark:text-amber-500"
        )}
      >
        {valore}
      </p>
      <p className="text-sm text-muted-foreground">{dettaglio}</p>
    </div>
  )
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
    checked: visible[id] ?? COLUMNS_DEFAULT[id] ?? true,
    onToggle: (value: boolean) => setVisible((prev) => ({ ...prev, [id]: value })),
  }))
  const show = (id: string) => visible[id] ?? COLUMNS_DEFAULT[id] ?? true

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

  const opzioniDipendenti = React.useMemo(
    () => [
      { id: MIO_CARTELLINO, name: "Il mio cartellino" },
      ...(dipendentiQuery.data ?? []).map((e) => ({ id: e.id, name: e.name })),
    ],
    [dipendentiQuery.data]
  )

  const meseLabel = new Date(periodo.anno, periodo.mese - 1, 1).toLocaleDateString(
    "it-IT",
    { month: "long", year: "numeric" }
  )
  const oggiIso = new Date().toISOString().slice(0, 10)
  const meseCorrente =
    periodo.anno === Number(oggiIso.slice(0, 4)) && periodo.mese === Number(oggiIso.slice(5, 7))

  function cambiaMese(delta: number) {
    setPeriodo((p) => {
      const d = new Date(p.anno, p.mese - 1 + delta, 1)
      return { anno: d.getFullYear(), mese: d.getMonth() + 1 }
    })
  }

  function vaiAOggi() {
    const oggi = new Date()
    setPeriodo({ anno: oggi.getFullYear(), mese: oggi.getMonth() + 1 })
  }

  // I quattro numeri del mese. «Da sistemare» = le giornate con anomalia (la regola del ⚠
  // la decide il motore); «di cui segnalate» conta quelle con un sollecito già partito.
  const totali = React.useMemo(() => {
    const giornate = cartellino?.days ?? []
    let ordinarie = 0
    let straordinario = 0
    let giorniLavorati = 0
    let giorniStraordinario = 0
    let anomalie = 0
    let segnalate = 0
    let assenzeIntere = 0
    let assenzeParziali = 0
    const fasce = new Set<string>()
    for (const g of giornate) {
      if (!g.hasData) continue
      const st = statoGiornata(g)
      if (st.assenza) {
        if (st.assenzaParziale) assenzeParziali++
        else assenzeIntere++
      }
      ordinarie += minutiDa(g.regularHours)
      straordinario += minutiDa(g.overtime)
      if (!isZero(g.regularHours) && !st.assenza) giorniLavorati++
      if (!isZero(g.overtime)) {
        giorniStraordinario++
        for (const k of Object.keys(g.bands)) fasce.add(k)
      }
      if (g.hasAnomaly) {
        anomalie++
        if (g.lastReminderAt) segnalate++
      }
    }
    return {
      ordinarie,
      straordinario,
      giorniLavorati,
      giorniStraordinario,
      anomalie,
      segnalate,
      assenzeIntere,
      assenzeParziali,
      fasce: [...fasce],
    }
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

  const visibleCount =
    1 +
    COLUMNS.filter((c) => c.id !== "calcolo" && show(c.id)).length +
    (show("calcolo") ? 2 : 0)

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
      title: `Rileggere da Ecos il ${formatDateShort(dataIso)}?`,
      description:
        `Si riscaricano da Ecos le timbrature di ${cartellino.employeeName} per quel ` +
        "giorno e si ricalcola la giornata. Le timbrature cancellate su Ecos spariscono " +
        "anche qui; le rettifiche inserite a mano restano.",
      confirmLabel: "Rileggi",
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

  const azioniGiornata = (g: HrDay) => {
    if (!canWrite || !cartellino) return null
    const dataIso = g.workDate.slice(0, 10)
    return (
      <>
        {g.canRemind && (
          <Button
            variant="outline"
            size="sm"
            onClick={() => setSollecito({ employeeId: cartellino.employeeId, date: dataIso })}
            title={
              g.lastReminderAt
                ? `Sollecito già inviato il ${formatDateTimeShort(g.lastReminderAt)}`
                : "Manda al dipendente un'email con la giornata da verificare"
            }
          >
            {g.lastReminderAt ? (
              <MailCheck className="mr-1 size-3.5" />
            ) : (
              <Mail className="mr-1 size-3.5 text-amber-600 dark:text-amber-500" />
            )}
            {g.lastReminderAt ? "Manda di nuovo l'email" : "Manda un'email al dipendente"}
          </Button>
        )}
        <Button
          variant="outline"
          size="sm"
          disabled={risincronizzando != null}
          onClick={() => void risincronizzaGiorno(dataIso)}
          title="Riscarica da Ecos le timbrature di questo giorno e ricalcola"
        >
          <RotateCw
            className={cn("mr-1 size-3.5", risincronizzando === dataIso && "animate-spin")}
          />
          Rileggi da Ecos
        </Button>
      </>
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <div>
          <h1 className="text-lg font-semibold">Timbrature</h1>
          <p className="text-sm text-muted-foreground">
            Ore lavorate, pause e straordinari letti dai terminali Ecos.
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
                {stato?.importInProgress ? "Aggiornamento in corso…" : "Aggiorna da Ecos"}
              </Button>
            </>
          )}
          {vista === "cartellino" && cartellino && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => void esportaExcel()}
              disabled={esportando}
            >
              <Download className="mr-1 size-3.5" />
              {esportando ? "Scarico…" : "Scarica Excel"}
            </Button>
          )}
          {vista === "cartellino" && <ColumnsMenu columns={columnToggles} />}
        </div>
      </div>

      {/* Schede: le quattro letture delle presenze, coi nomi di chi le legge. */}
      {canWrite && (
        <Tabs value={vista} onValueChange={(v) => setVista(v as Vista)}>
          <TabsList>
            <TabsTrigger value="cartellino">Cartellino di una persona</TabsTrigger>
            <TabsTrigger value="calendario">Tutti, mese per mese</TabsTrigger>
            <TabsTrigger value="quadratura">Ore sulle commesse</TabsTrigger>
            <TabsTrigger value="cronologia">Email inviate</TabsTrigger>
          </TabsList>
        </Tabs>
      )}

      {/* Persona e mese: i due comandi con cui si sceglie cosa guardare. */}
      <div className="flex flex-wrap items-center gap-3">
        {canWrite && vista === "cartellino" && (
          <LookupCombobox
            options={opzioniDipendenti}
            value={employeeId ?? MIO_CARTELLINO}
            onValueChange={(id) =>
              setEmployeeId(id == null || id === MIO_CARTELLINO ? null : id)
            }
            placeholder="Scegli il dipendente…"
            searchPlaceholder="Cerca per nome…"
            emptyText="Nessun dipendente con questo nome"
            loading={dipendentiQuery.isLoading}
            className="h-10 w-72 text-base"
          />
        )}
        <div className="inline-flex items-center gap-1 rounded-lg border p-1">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => cambiaMese(-1)}
            aria-label="Mese precedente"
            title="Mese precedente"
          >
            <ChevronLeft className="size-5" />
          </Button>
          <span className="min-w-40 text-center text-base font-semibold capitalize">
            {meseLabel}
          </span>
          <Button
            variant="ghost"
            size="icon"
            onClick={() => cambiaMese(1)}
            aria-label="Mese successivo"
            title="Mese successivo"
          >
            <ChevronRight className="size-5" />
          </Button>
        </div>
        {!meseCorrente && (
          <Button variant="outline" size="sm" onClick={vaiAOggi}>
            Torna a oggi
          </Button>
        )}
        {canWrite && stato && !stato.configured && (
          <span className="text-sm text-amber-600 dark:text-amber-500">
            Credenziali Ecos non configurate sul server: l'aggiornamento è fermo.
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

      {vista === "calendario" ? (
        <CalendarioPresenzeView anno={periodo.anno} mese={periodo.mese} />
      ) : vista === "quadratura" ? (
        <QuadraturaPresenzeView anno={periodo.anno} mese={periodo.mese} />
      ) : vista === "cronologia" ? (
        <CronologiaMailView anno={periodo.anno} mese={periodo.mese} />
      ) : (
        <div className="space-y-3">
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
            <>
              {/* I quattro numeri del mese: si capisce com'è andato prima di leggere le righe. */}
              <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                <Riquadro
                  etichetta="Ore ordinarie"
                  valore={durata(totali.ordinarie)}
                  dettaglio={
                    totali.giorniLavorati === 1
                      ? "in 1 giorno lavorato"
                      : `in ${totali.giorniLavorati} giorni lavorati`
                  }
                />
                <Riquadro
                  etichetta="Straordinario"
                  valore={durata(totali.straordinario)}
                  dettaglio={
                    totali.giorniStraordinario === 0
                      ? "nessuna giornata"
                      : `${totali.giorniStraordinario} ${
                          totali.giorniStraordinario === 1 ? "giornata" : "giornate"
                        }${
                          totali.fasce.length > 0
                            ? " · " +
                              totali.fasce
                                .map((k) => (FASCE_LABELS[k] ?? `fascia ${k}`).toLowerCase())
                                .join(", ")
                            : ""
                        }`
                  }
                  tone={totali.straordinario > 0 ? "warn" : undefined}
                />
                <Riquadro
                  etichetta="Ferie e assenze"
                  valore={`${totali.assenzeIntere} ${totali.assenzeIntere === 1 ? "giorno" : "giorni"}`}
                  dettaglio={
                    totali.assenzeParziali > 0
                      ? `più ${totali.assenzeParziali} ${
                          totali.assenzeParziali === 1 ? "permesso parziale" : "permessi parziali"
                        }`
                      : "interi, da Ecos"
                  }
                />
                <Riquadro
                  etichetta="Giornate da sistemare"
                  valore={String(totali.anomalie)}
                  dettaglio={
                    totali.anomalie === 0
                      ? "tutto in ordine"
                      : totali.segnalate > 0
                        ? `di cui ${totali.segnalate} già ${
                            totali.segnalate === 1 ? "segnalata" : "segnalate"
                          } via email`
                        : "nessuna ancora segnalata"
                  }
                  tone={totali.anomalie > 0 ? "bad" : undefined}
                />
              </div>

              <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
                {canWrite && (
                  <Button
                    variant={soloDaSegnalare ? "default" : "outline"}
                    size="sm"
                    onClick={() => setSoloDaSegnalare((v) => !v)}
                    title="Mostra solo le giornate per cui c'è una segnalazione da mandare"
                  >
                    <Mail className="mr-1 size-3.5" />
                    Solo le giornate da segnalare{daSegnalare > 0 ? ` (${daSegnalare})` : ""}
                  </Button>
                )}
                <div className="flex flex-wrap items-center gap-4 text-sm text-muted-foreground">
                  <span className="inline-flex items-center gap-1.5">
                    <span className="size-3.5 rounded border bg-destructive/10" />
                    Da sistemare
                  </span>
                  <span className="inline-flex items-center gap-1.5">
                    <span className="size-3.5 rounded border bg-muted" />
                    Sabato, domenica, festivi
                  </span>
                  <span className="inline-flex items-center gap-1.5">
                    <span className="size-3.5 rounded border border-amber-400 bg-amber-500/10" />
                    Oggi
                  </span>
                  <span>In grande l'ora che vale, in piccolo l'ora timbrata.</span>
                </div>
              </div>

              <GridScroller className="rounded-lg border">
                <Table className="text-sm">
                  <TableHeader>
                    <TableRow>
                      <TableHead className="w-36">Giorno</TableHead>
                      {show("entrata1") && <TableHead>Entrata</TableHead>}
                      {show("uscita1") && <TableHead>Uscita</TableHead>}
                      {show("entrata2") && <TableHead>Entrata</TableHead>}
                      {show("uscita2") && <TableHead>Uscita</TableHead>}
                      {show("ore") && <TableHead className="text-right">Ore</TableHead>}
                      {show("straordinario") && (
                        <TableHead className="text-right">Straord.</TableHead>
                      )}
                      {show("stato") && <TableHead>Com'è la giornata</TableHead>}
                      {show("pausa") && <TableHead className="text-right">Pausa</TableHead>}
                      {show("calcolo") && (
                        <>
                          <TableHead className="border-l text-right text-muted-foreground">
                            Pausa timbrata
                          </TableHead>
                          <TableHead className="text-right text-muted-foreground">
                            Ore timbrate
                          </TableHead>
                        </>
                      )}
                      {show("nota") && <TableHead className="w-60">Nota</TableHead>}
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {giornate.map((g) => {
                      const dataIso = g.workDate.slice(0, 10)
                      const cliccabile = g.hasData || g.punches.length > 0 || canWrite
                      const fasceEntries = Object.entries(g.bands)
                      const st = statoGiornata(g)
                      const riposo = st.riposo
                      const oggi = dataIso === oggiIso

                      return (
                        <TableRow
                          key={g.workDate}
                          className={cn(
                            "h-11",
                            riposo && "bg-muted/40 text-muted-foreground",
                            g.hasAnomaly && "bg-destructive/10",
                            oggi && "bg-amber-500/10 shadow-[inset_3px_0_0_0_theme(colors.amber.400)]",
                            cliccabile && "cursor-pointer hover:bg-muted/60"
                          )}
                          onClick={() => {
                            if (cliccabile) setGiornoAperto(dataIso)
                          }}
                        >
                          <EtichettaGiorno g={g} />
                          {show("entrata1") && (
                            <CellaOra valore={g.clockIn1} timbrato={g.raw.clockIn1} spenta={riposo} />
                          )}
                          {show("uscita1") && (
                            <CellaOra valore={g.clockOut1} timbrato={g.raw.clockOut1} spenta={riposo} />
                          )}
                          {show("entrata2") && (
                            <CellaOra valore={g.clockIn2} timbrato={g.raw.clockIn2} spenta={riposo} />
                          )}
                          {show("uscita2") && (
                            <CellaOra valore={g.clockOut2} timbrato={g.raw.clockOut2} spenta={riposo} />
                          )}
                          {show("ore") && (
                            <TableCell className="text-right tabular-nums font-medium">
                              {isZero(g.regularHours) && g.regularHours !== "---" ? (
                                <span className="text-muted-foreground">—</span>
                              ) : (
                                oreLeggibili(g.regularHours)
                              )}
                            </TableCell>
                          )}
                          {show("straordinario") && (
                            <TableCell className="text-right tabular-nums">
                              {isZero(g.overtime) ? (
                                <span className="text-muted-foreground">—</span>
                              ) : fasceEntries.length > 0 ? (
                                <Tooltip>
                                  <TooltipTrigger asChild>
                                    <span className="cursor-help underline decoration-dotted">
                                      {oreLeggibili(g.overtime)}
                                    </span>
                                  </TooltipTrigger>
                                  <TooltipContent className="text-xs">
                                    <p className="mb-1 font-semibold">Dettaglio fasce CCNL:</p>
                                    {fasceEntries.map(([k, v]) => (
                                      <div key={k}>
                                        <b>{FASCE_LABELS[k] ?? `Fascia ${k}`}:</b> {v}
                                      </div>
                                    ))}
                                  </TooltipContent>
                                </Tooltip>
                              ) : (
                                oreLeggibili(g.overtime)
                              )}
                            </TableCell>
                          )}
                          {show("stato") && (
                            <TableCell className="whitespace-nowrap">
                              <StatoGiornata stato={st} />
                            </TableCell>
                          )}
                          {show("pausa") && (
                            <TableCell className="text-right tabular-nums text-muted-foreground">
                              {isZero(g.breakTime) ? "—" : oreLeggibili(g.breakTime)}
                            </TableCell>
                          )}
                          {show("calcolo") && (
                            <>
                              <TableCell className="border-l text-right tabular-nums text-muted-foreground">
                                {g.raw.breakTime || "—"}
                              </TableCell>
                              <TableCell className="text-right tabular-nums text-muted-foreground">
                                {g.raw.totalHours || "—"}
                              </TableCell>
                            </>
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
                  {giornate.length > 0 && !soloDaSegnalare && (
                    <TableFooter>
                      <TableRow className="h-11 font-semibold">
                        <TableCell
                          colSpan={
                            1 +
                            ["entrata1", "uscita1", "entrata2", "uscita2"].filter(show).length
                          }
                        >
                          Totale del mese
                        </TableCell>
                        {show("ore") && (
                          <TableCell className="text-right tabular-nums">
                            {durata(totali.ordinarie)}
                          </TableCell>
                        )}
                        {show("straordinario") && (
                          <TableCell className="text-right tabular-nums">
                            {totali.straordinario > 0 ? durata(totali.straordinario) : "—"}
                          </TableCell>
                        )}
                        <TableCell
                          colSpan={
                            (show("stato") ? 1 : 0) +
                            (show("pausa") ? 1 : 0) +
                            (show("calcolo") ? 2 : 0) +
                            (show("nota") ? 1 : 0)
                          }
                        />
                      </TableRow>
                    </TableFooter>
                  )}
                </Table>
              </GridScroller>
            </>
          ) : null}

          <GiornataDialog
            open={giornataAperta != null}
            onOpenChange={(open) => {
              if (!open) setGiornoAperto(null)
            }}
            giornata={giornataAperta}
            employeeId={cartellino?.employeeId ?? 0}
            employeeName={cartellino?.employeeName ?? ""}
            canWrite={canWrite}
            onChanged={invalidate}
            azioni={giornataAperta ? azioniGiornata(giornataAperta) : null}
          />
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
