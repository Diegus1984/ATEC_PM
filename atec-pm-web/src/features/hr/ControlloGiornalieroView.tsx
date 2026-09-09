import * as React from "react"
import { keepPreviousData, useQuery } from "@tanstack/react-query"
import { ChevronLeft, ChevronRight, Mail, MailCheck, TriangleAlert } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { DateField } from "@/components/shared/date-field"
import { GridScroller } from "@/components/shared/grid-scroller"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { fetchHrDailyCheck } from "@/lib/api/hr"
import { dateToIso, formatDateTimeShort } from "@/lib/date-iso"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { AzioniGiornata } from "./AzioniGiornata"
import { CellaOra, CellaOre, CellaStraordinario, Riquadro } from "./celle-cartellino"
import {
  daSistemare,
  descriviBlocco,
  etichettaGiorno,
  riassuntoControllo,
  righeControllo,
  spiegaBlocco,
  type RigaControllo,
} from "./controllo-giornaliero"
import { GiornataDialog } from "./GiornataDialog"
import { isZero, oreLeggibili } from "./ore"
import { SollecitoGiornataDialog, type SollecitoTarget } from "./SollecitoGiornataDialog"
import { StatoGiornata } from "./stato-giornata"

// «Controllo di ieri» (09/09/2026, richiesta di Diego): la pagina che l'ufficio HR apre al
// mattino — le timbrature di ieri di TUTTI i dipendenti, una riga a testa, così si vede in un
// colpo chi manca e chi ha un orario da sistemare. Il blocco di giorni lo decide il server:
// di lunedì arrivano venerdì, sabato e domenica (nei riposi compare solo chi ha timbrato).
// Le celle sono quelle del cartellino di una persona: stessa regola, stesse parole.
// Il sollecito sta SULLA RIGA (📧) e si manda anche a più giornate insieme: casella di
// selezione e «Sollecita i selezionati» (Diego, 09/09/2026 pomeriggio).

const COLUMNS: { id: string; label: string }[] = [
  { id: "entrata1", label: "Entrata (mattina)" },
  { id: "uscita1", label: "Uscita (mattina)" },
  { id: "entrata2", label: "Entrata (pomeriggio)" },
  { id: "uscita2", label: "Uscita (pomeriggio)" },
  { id: "ore", label: "Ore" },
  { id: "straordinario", label: "Straordinario" },
  { id: "stato", label: "Com'è la giornata" },
  { id: "pausa", label: "Pausa" },
  { id: "nota", label: "Nota del motore" },
]
const COLUMNS_DEFAULT: Record<string, boolean> = {
  ...Object.fromEntries(COLUMNS.map((c) => [c.id, true])),
  pausa: false,
  nota: false,
}
const COLUMNS_STORAGE_KEY = "hr-controllo-ieri-columns-v1"

/** Si sollecita quello che il server dice sollecitabile (`canRemind`): la regola è una sola. */
function sollecitabile(r: RigaControllo): boolean {
  return r.giorno.canRemind
}

export function ControlloGiornalieroView({
  canWrite,
  onChanged,
}: {
  canWrite: boolean
  /** Dopo una modifica dal dettaglio (rettifica, sollecito, rilettura): la pagina rilegge. */
  onChanged: () => void
}) {
  // null = il giorno lo decide il server (ieri): riaprendo la pagina domani è già sul giorno giusto.
  const [scelto, setScelto] = React.useState<string | null>(null)
  const [soloDaSistemare, setSoloDaSistemare] = React.useState(false)
  const [aperta, setAperta] = React.useState<{ employeeId: number; dataIso: string } | null>(null)
  const [sollecito, setSollecito] = React.useState<SollecitoTarget[] | null>(null)
  // Le righe scelte per il sollecito in blocco (chiave = persona|giorno).
  const [selezionate, setSelezionate] = React.useState<Set<string>>(() => new Set())

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

  const query = useQuery({
    queryKey: ["hr-daily-check", scelto],
    queryFn: () => fetchHrDailyCheck(scelto),
    // Cambiando giorno la griglia di prima resta a video finché non arriva la nuova: niente lampeggio.
    placeholderData: keepPreviousData,
  })
  const controllo = query.data

  const righe = React.useMemo(() => (controllo ? righeControllo(controllo) : []), [controllo])
  const riassunto = React.useMemo(
    () => (controllo ? riassuntoControllo(controllo, righe) : null),
    [controllo, righe]
  )
  const visibili = React.useMemo(
    () => (soloDaSistemare ? righe.filter(daSistemare) : righe),
    [righe, soloDaSistemare]
  )

  // Giorno per giorno, nell'ordine del blocco; l'intestazione del giorno compare solo
  // quando i giorni sono più d'uno.
  const perGiorno = React.useMemo(() => {
    if (!controllo) return []
    return controllo.days.map((d) => {
      const dataIso = d.date.slice(0, 10)
      return {
        dataIso,
        lavorativo: d.isWorkingDay,
        righe: visibili.filter((r) => r.dataIso === dataIso),
      }
    })
  }, [controllo, visibili])

  // La selezione vale solo sulle righe che ci sono ancora e si possono ancora sollecitare:
  // dopo un ricalcolo o un cambio di giorno le chiavi sparite cadono da sole.
  const sollecitabili = React.useMemo(() => visibili.filter(sollecitabile), [visibili])
  const selezione = React.useMemo(
    () => sollecitabili.filter((r) => selezionate.has(r.chiave)),
    [sollecitabili, selezionate]
  )
  const tutteSelezionate = sollecitabili.length > 0 && selezione.length === sollecitabili.length

  function toggleRiga(chiave: string, on: boolean) {
    setSelezionate((prev) => {
      const next = new Set(prev)
      if (on) next.add(chiave)
      else next.delete(chiave)
      return next
    })
  }

  function toggleTutte(on: boolean) {
    setSelezionate(on ? new Set(sollecitabili.map((r) => r.chiave)) : new Set())
  }

  const dipendenteAperto = React.useMemo(
    () => controllo?.employees.find((e) => e.employeeId === aperta?.employeeId) ?? null,
    [controllo, aperta]
  )
  // La giornata del dettaglio si rilegge dai dati: dopo una rettifica il dialogo mostra il nuovo.
  const giornataAperta = React.useMemo(
    () =>
      dipendenteAperto?.days.find((g) => g.workDate.slice(0, 10) === aperta?.dataIso) ?? null,
    [dipendenteAperto, aperta]
  )

  const oggiIso = dateToIso(new Date())
  // Colonne fisse: la casella di selezione (solo con la scrittura), il nome, il pulsante 📧.
  const visibleCount = (canWrite ? 2 : 0) + 1 + COLUMNS.filter((c) => show(c.id)).length
  const quantiDaSistemare = riassunto?.daSistemare ?? 0

  if (query.isLoading) {
    return <p className="text-sm text-muted-foreground">Caricamento…</p>
  }
  if (query.error) {
    return <p className="text-sm text-destructive">{(query.error as Error).message}</p>
  }
  if (!controllo || !riassunto) return null

  const spiegazione = spiegaBlocco(controllo)

  return (
    <div className="space-y-3">
      {/* Il blocco di giorni: frecce per quello prima e quello dopo, «Torna a ieri», un
          calendario per saltare a un giorno qualsiasi. */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="inline-flex items-center gap-1 rounded-lg border p-1">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => setScelto(controllo.previousDate.slice(0, 10))}
            aria-label="Giorni precedenti"
            title="Giorni precedenti"
          >
            <ChevronLeft className="size-5" />
          </Button>
          <span className="min-w-64 text-center text-base font-semibold">
            {descriviBlocco(controllo)}
          </span>
          <Button
            variant="ghost"
            size="icon"
            disabled={!controllo.nextDate}
            onClick={() => {
              if (controllo.nextDate) setScelto(controllo.nextDate.slice(0, 10))
            }}
            aria-label="Giorni successivi"
            title={controllo.nextDate ? "Giorni successivi" : "Più avanti di oggi non c'è niente"}
          >
            <ChevronRight className="size-5" />
          </Button>
        </div>
        {scelto != null && (
          <Button variant="outline" size="sm" onClick={() => setScelto(null)}>
            Torna a ieri
          </Button>
        )}
        <div className="flex items-center gap-2">
          <Label className="text-xs text-muted-foreground">Vai al giorno</Label>
          <DateField
            value={controllo.date.slice(0, 10)}
            onChange={(v) => {
              if (v) setScelto(v)
            }}
            clearable={false}
            size="sm"
            disableAfter={new Date()}
            className="w-52"
          />
        </div>
        {query.isFetching && <span className="text-xs text-muted-foreground">Aggiorno…</span>}
        <div className="ml-auto flex flex-wrap items-center gap-2">
          {canWrite && (
            <Button
              size="sm"
              disabled={selezione.length === 0}
              onClick={() =>
                setSollecito(selezione.map((r) => ({ employeeId: r.dipendente.employeeId, date: r.dataIso })))
              }
              title={
                sollecitabili.length === 0
                  ? "Nessuna giornata da sollecitare in questo blocco"
                  : "Manda un'email a ognuna delle persone selezionate, per la sua giornata"
              }
            >
              <Mail className="mr-1 size-3.5" />
              Sollecita i selezionati{selezione.length > 0 ? ` (${selezione.length})` : ""}
            </Button>
          )}
          <Button
            variant={soloDaSistemare ? "default" : "outline"}
            size="sm"
            onClick={() => setSoloDaSistemare((v) => !v)}
            title="Mostra solo le giornate in rosso o in ambra"
          >
            <TriangleAlert className="mr-1 size-3.5" />
            Solo da sistemare{quantiDaSistemare > 0 ? ` (${quantiDaSistemare})` : ""}
          </Button>
          <ColumnsMenu columns={columnToggles} />
        </div>
      </div>
      {spiegazione && <p className="text-sm text-muted-foreground">{spiegazione}</p>}

      {/* I quattro numeri: si capisce com'è andata prima di leggere le righe. */}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Riquadro
          etichetta="Dipendenti"
          valore={String(riassunto.dipendenti)}
          dettaglio="che timbrano"
        />
        <Riquadro
          etichetta="Tutto regolare"
          valore={String(riassunto.regolari)}
          dettaglio={riassunto.regolari === 1 ? "giornata a posto" : "giornate a posto"}
        />
        <Riquadro
          etichetta="Da sistemare"
          valore={String(riassunto.daSistemare)}
          dettaglio={
            riassunto.daSistemare === 0
              ? "tutto in ordine"
              : riassunto.senzaTimbrature > 0
                ? `di cui ${riassunto.senzaTimbrature} senza timbrature`
                : "orari da verificare"
          }
          tone={riassunto.daSistemare > 0 ? "bad" : undefined}
        />
        <Riquadro
          etichetta="Assenti"
          valore={String(riassunto.assenti)}
          dettaglio="ferie, permessi, malattia"
        />
      </div>

      <div className="flex flex-wrap items-center gap-4 text-sm text-muted-foreground">
        <span className="inline-flex items-center gap-1.5">
          <span className="size-3.5 rounded border bg-destructive/10" />
          Da sistemare
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span className="size-3.5 rounded border bg-muted" />
          Riposo, non collegato a Ecos
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span className="size-3.5 rounded border border-amber-400 bg-amber-500/10" />
          Oggi
        </span>
        <span>
          In grande l'ora che vale, in piccolo l'ora timbrata. Clic sulla riga per il dettaglio,
          {canWrite ? " 📧 per il sollecito, la casella per sollecitarne più d'uno." : ""}
        </span>
      </div>

      <GridScroller className="rounded-lg border">
        <Table className="text-sm">
          <TableHeader>
            <TableRow>
              {canWrite && (
                <TableHead className="w-10">
                  <Checkbox
                    checked={tutteSelezionate ? true : selezione.length > 0 ? "indeterminate" : false}
                    onCheckedChange={(on) => toggleTutte(on === true)}
                    disabled={sollecitabili.length === 0}
                    aria-label="Seleziona tutte le giornate da sollecitare"
                    title="Seleziona tutte le giornate da sollecitare"
                  />
                </TableHead>
              )}
              <TableHead className="w-56">Dipendente</TableHead>
              {show("entrata1") && <TableHead>Entrata</TableHead>}
              {show("uscita1") && <TableHead>Uscita</TableHead>}
              {show("entrata2") && <TableHead>Entrata</TableHead>}
              {show("uscita2") && <TableHead>Uscita</TableHead>}
              {show("ore") && <TableHead className="text-right">Ore</TableHead>}
              {show("straordinario") && <TableHead className="text-right">Straord.</TableHead>}
              {show("stato") && <TableHead>Com'è la giornata</TableHead>}
              {show("pausa") && <TableHead className="text-right">Pausa</TableHead>}
              {show("nota") && <TableHead className="w-60">Nota</TableHead>}
              {canWrite && <TableHead className="w-12 text-center">Sollecito</TableHead>}
            </TableRow>
          </TableHeader>
          <TableBody>
            {perGiorno.map((giorno) => (
              <React.Fragment key={giorno.dataIso}>
                {perGiorno.length > 1 && (
                  <TableRow className="bg-muted/60 hover:bg-muted/60">
                    <TableCell colSpan={visibleCount} className="py-1.5 font-semibold">
                      {etichettaGiorno(giorno.dataIso)}
                      {!giorno.lavorativo && (
                        <span className="ml-2 text-xs font-normal text-muted-foreground">
                          riposo: solo chi ha timbrato
                        </span>
                      )}
                    </TableCell>
                  </TableRow>
                )}
                {giorno.righe.map((r) => (
                  <RigaDipendente
                    key={r.chiave}
                    riga={r}
                    show={show}
                    oggi={r.dataIso === oggiIso}
                    canWrite={canWrite}
                    selezionata={selezionate.has(r.chiave)}
                    onToggle={(on) => toggleRiga(r.chiave, on)}
                    onOpen={() =>
                      setAperta({ employeeId: r.dipendente.employeeId, dataIso: r.dataIso })
                    }
                    onSollecito={() =>
                      setSollecito([{ employeeId: r.dipendente.employeeId, date: r.dataIso }])
                    }
                  />
                ))}
                {giorno.righe.length === 0 && (
                  <TableRow>
                    <TableCell
                      colSpan={visibleCount}
                      className="text-center text-sm text-muted-foreground"
                    >
                      {soloDaSistemare
                        ? "Niente da sistemare."
                        : giorno.lavorativo
                          ? "Nessun dipendente da controllare."
                          : "Nessuno ha timbrato."}
                    </TableCell>
                  </TableRow>
                )}
              </React.Fragment>
            ))}
          </TableBody>
        </Table>
      </GridScroller>

      <GiornataDialog
        open={giornataAperta != null}
        onOpenChange={(open) => {
          if (!open) setAperta(null)
        }}
        giornata={giornataAperta}
        employeeId={dipendenteAperto?.employeeId ?? 0}
        employeeName={dipendenteAperto?.employeeName ?? ""}
        canWrite={canWrite}
        onChanged={onChanged}
        azioni={
          giornataAperta && dipendenteAperto && canWrite ? (
            <AzioniGiornata
              employeeId={dipendenteAperto.employeeId}
              employeeName={dipendenteAperto.employeeName}
              giornata={giornataAperta}
              onSollecito={() =>
                setSollecito([
                  {
                    employeeId: dipendenteAperto.employeeId,
                    date: giornataAperta.workDate.slice(0, 10),
                  },
                ])
              }
              onChanged={onChanged}
            />
          ) : null
        }
      />

      <SollecitoGiornataDialog
        targets={sollecito}
        onOpenChange={(open) => {
          if (!open) setSollecito(null)
        }}
        onSent={() => {
          // Spedite: le caselle si svuotano, così un secondo clic non le rimanda.
          setSelezionate(new Set())
          onChanged()
        }}
      />
    </div>
  )
}

/**
 * Una persona in un giorno: la casella per il sollecito in blocco, nome e reparto, le stesse
 * celle del cartellino, il 📧 in fondo (solo sulle giornate che il server dice da segnalare).
 */
function RigaDipendente({
  riga,
  show,
  oggi,
  canWrite,
  selezionata,
  onToggle,
  onOpen,
  onSollecito,
}: {
  riga: RigaControllo
  show: (id: string) => boolean
  oggi: boolean
  canWrite: boolean
  selezionata: boolean
  onToggle: (on: boolean) => void
  onOpen: () => void
  onSollecito: () => void
}) {
  const { giorno: g, stato: st, dipendente } = riga
  const spenta = st.tone === "dim"
  const daSollecitare = canWrite && sollecitabile(riga)
  return (
    <TableRow
      className={cn(
        "h-11 cursor-pointer hover:bg-muted/60",
        spenta && "bg-muted/40 text-muted-foreground",
        st.tone === "bad" && "bg-destructive/10",
        oggi && "bg-amber-500/10 shadow-[inset_3px_0_0_0_theme(colors.amber.400)]",
        selezionata && "bg-primary/10"
      )}
      onClick={onOpen}
    >
      {canWrite && (
        // La casella non apre il dettaglio: il clic si ferma qui.
        <TableCell className="w-10" onClick={(e) => e.stopPropagation()}>
          {daSollecitare && (
            <Checkbox
              checked={selezionata}
              onCheckedChange={(on) => onToggle(on === true)}
              aria-label={`Seleziona ${dipendente.employeeName} per il sollecito`}
            />
          )}
        </TableCell>
      )}
      <TableCell className="whitespace-nowrap leading-tight">
        <span className="font-semibold">{dipendente.employeeName}</span>
        <span
          className={cn(
            "block text-[11px] text-muted-foreground",
            !dipendente.departmentName && "invisible"
          )}
          aria-hidden={!dipendente.departmentName}
        >
          {dipendente.departmentName || "—"}
        </span>
      </TableCell>
      {show("entrata1") && <CellaOra valore={g.clockIn1} timbrato={g.raw.clockIn1} spenta={spenta} />}
      {show("uscita1") && <CellaOra valore={g.clockOut1} timbrato={g.raw.clockOut1} spenta={spenta} />}
      {show("entrata2") && <CellaOra valore={g.clockIn2} timbrato={g.raw.clockIn2} spenta={spenta} />}
      {show("uscita2") && <CellaOra valore={g.clockOut2} timbrato={g.raw.clockOut2} spenta={spenta} />}
      {show("ore") && <CellaOre g={g} />}
      {show("straordinario") && <CellaStraordinario g={g} />}
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
      {show("nota") && (
        <TableCell className="max-w-60 truncate text-xs text-muted-foreground" title={g.note}>
          {g.note || "—"}
        </TableCell>
      )}
      {canWrite && (
        <TableCell className="w-12 text-center" onClick={(e) => e.stopPropagation()}>
          {daSollecitare && (
            <Button
              variant="ghost"
              size="icon"
              className="size-8"
              onClick={onSollecito}
              aria-label={`Sollecita ${dipendente.employeeName}`}
              title={
                g.lastReminderAt
                  ? `Sollecito già inviato il ${formatDateTimeShort(g.lastReminderAt)}: manda di nuovo l'email`
                  : "Manda al dipendente un'email con la giornata da verificare"
              }
            >
              {g.lastReminderAt ? (
                <MailCheck className="size-4 text-muted-foreground" />
              ) : (
                <Mail className="size-4 text-amber-600 dark:text-amber-500" />
              )}
            </Button>
          )}
        </TableCell>
      )}
    </TableRow>
  )
}
