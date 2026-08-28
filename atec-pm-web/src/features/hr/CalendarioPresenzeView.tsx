import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { CalendarCheck, Download, Mail, MailCheck, Printer, RotateCcw } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  LookupCombobox,
  type LookupComboboxOption,
} from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuTrigger,
} from "@/components/ui/context-menu"
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
import { GiustificaCausaleDialog } from "./GiustificaCausaleDialog"
import { fetchDepartmentsLookup } from "@/lib/api/departments"
import {
  downloadHrCalendarExcel,
  fetchHrCalendar,
  fetchHrReminders,
  markHrReminders,
  sendHrReminders,
} from "@/lib/api/hr"
import type { HrCalendarCell, HrCalendarRow, HrReminderTarget } from "@/lib/api/types"
import { dateToIso } from "@/lib/date-iso"
import { printHtml } from "@/lib/print-template"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

interface CalendarioPresenzeViewProps {
  anno: number
  mese: number
}

/**
 * Calendario mensile: una riga per VOCE — ore ordinarie, le fasce di straordinario che
 * hanno ore, presenza, ferie, permessi, malattia, infortunio — come nel programma
 * «Timbrature» che l'ufficio usa da prima di ATEC PM. Le celle (testo, colore, tooltip)
 * arrivano già decise dal server, che con gli stessi dati compone il file Excel: la
 * griglia sullo schermo e quella nel foglio sono la stessa cosa.
 */

/** I colori dell'originale, tradotti nel tema (e leggibili anche in scuro). */
const COLORI: Record<string, string> = {
  GRAY: "bg-muted/70 text-muted-foreground",
  GREEN: "bg-emerald-100 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-200",
  RED: "bg-rose-200 text-rose-900 font-semibold dark:bg-rose-950 dark:text-rose-200",
  ORANGE: "bg-amber-100 text-amber-900 dark:bg-amber-950 dark:text-amber-200",
  BLUE: "bg-sky-100 text-sky-900 dark:bg-sky-950 dark:text-sky-200",
  PURPLE: "bg-violet-100 text-violet-900 dark:bg-violet-950 dark:text-violet-200",
  YELLOW: "bg-yellow-100 text-yellow-900 dark:bg-yellow-900/50 dark:text-yellow-100",
  TEAL: "bg-teal-100 text-teal-900 dark:bg-teal-950 dark:text-teal-200",
}

/** Colori del foglio Excel, per la stampa su carta. */
const COLORI_STAMPA: Record<string, string> = {
  GRAY: "#e6e6e6",
  GREEN: "#c8f0c8",
  RED: "#ffc8c8",
  ORANGE: "#ffe6b4",
  BLUE: "#c8d7ff",
  PURPLE: "#e6c8ff",
  YELLOW: "#ffffc8",
  TEAL: "#b4ebe6",
}

export function CalendarioPresenzeView({ anno, mese }: CalendarioPresenzeViewProps) {
  const confirm = useConfirm()
  const [departmentId, setDepartmentId] = React.useState<number | null>(null)
  const [employeeId, setEmployeeId] = React.useState<number | null>(null)
  const [scaricando, setScaricando] = React.useState(false)
  const [sollecitando, setSollecitando] = React.useState(false)
  // #132: giornata su cui si e fatto doppio clic, in attesa della causale.
  const [giustifica, setGiustifica] = React.useState<{
    employeeId: number
    date: string
  } | null>(null)

  const repartiQuery = useQuery({
    queryKey: ["departments-lookup"],
    queryFn: fetchDepartmentsLookup,
  })

  const calendarioQuery = useQuery({
    queryKey: ["hr-calendar", anno, mese, departmentId],
    queryFn: () => fetchHrCalendar(anno, mese, departmentId),
  })

  const opzioniReparti: LookupComboboxOption<number>[] = React.useMemo(
    () => (repartiQuery.data ?? []).map((d) => ({ id: d.id, name: d.name })),
    [repartiQuery.data]
  )

  const calendario = calendarioQuery.data
  const giorni = calendario?.daysInMonth ?? 31

  const opzioniDipendenti: LookupComboboxOption<number>[] = React.useMemo(
    () => (calendario?.employees ?? []).map((e) => ({ id: e.id, name: e.name })),
    [calendario]
  )

  // Il filtro dipendente lavora sui dati già scaricati, come faceva ApplyFilter nel VB.
  const righe: HrCalendarRow[] = React.useMemo(() => {
    const tutte = calendario?.rows ?? []
    return employeeId == null ? tutte : tutte.filter((r) => r.employeeId === employeeId)
  }, [calendario, employeeId])

  const nomeDipendente = React.useMemo(
    () => calendario?.employees.find((e) => e.id === employeeId)?.name,
    [calendario, employeeId]
  )

  const meseLabel = new Date(anno, mese - 1, 1).toLocaleDateString("it-IT", {
    month: "long",
    year: "numeric",
  })

  /**
   * #132 — su quali celle ha senso il doppio clic. È lo stesso filtro delle due prime
   * porte dell'originale (solo giorni passati, mai i non lavorativi): serve a non far
   * sembrare cliccabile una cella che poi risponderebbe «non si può». Il vero controllo
   * resta del server, che rifà tutto anche al salvataggio.
   */
  const giornoGiustificabile = React.useCallback(
    (giorno: number) => {
      if (calendario?.nonWorkingDays[giorno]) return false
      const oggi = new Date()
      oggi.setHours(0, 0, 0, 0)
      return new Date(anno, mese - 1, giorno) < oggi
    },
    [calendario, anno, mese]
  )

  async function handleEsportaExcel() {
    if (!calendario) return
    setScaricando(true)
    try {
      await downloadHrCalendarExcel(anno, mese, departmentId, employeeId, nomeDipendente)
    } catch (e) {
      notifyError(e instanceof Error ? e.message : "Esportazione non riuscita.")
    } finally {
      setScaricando(false)
    }
  }

  // ── SOLLECITI ───────────────────────────────────────────────────────────
  //
  // Due strade, come nel programma originale: il client di posta (si legge e si modifica
  // prima di spedire) e l'invio diretto dal server (per farne trenta in fila). In
  // entrambi i casi si mostra PRIMA chi verrà scritto: un sollecito sbagliato lo legge
  // una persona.

  function riepilogo(destinatari: HrReminderTarget[]): string {
    const senzaEmail = destinatari.filter((t) => !t.email)
    const giaChiesti = destinatari.filter((t) => t.lastReminderAt)
    const elenco = destinatari
      .slice(0, 8)
      .map((t) => `${t.employeeName} (${t.missingDays.length} gg)`)
      .join(", ")
    const resto = destinatari.length > 8 ? ` e altri ${destinatari.length - 8}` : ""

    return (
      `${elenco}${resto}.` +
      (senzaEmail.length > 0 ? ` Senza email: ${senzaEmail.length}.` : "") +
      (giaChiesti.length > 0 ? ` Già sollecitati in questo mese: ${giaChiesti.length}.` : "")
    )
  }

  async function handleSollecita() {
    setSollecitando(true)
    try {
      const solleciti = await fetchHrReminders(anno, mese, departmentId, employeeId)
      const conEmail = solleciti.targets.filter((t) => t.email)

      if (solleciti.targets.length === 0) {
        notifySuccess("Nessun sollecito da inviare: nessuna giornata scoperta.")
        return
      }
      if (conEmail.length === 0) {
        notifyError("Nessuno dei dipendenti da sollecitare ha un indirizzo email.")
        return
      }

      const ok = await confirm({
        title: `Aprire ${conEmail.length} email di sollecito?`,
        description: `Si apre una finestra del client di posta per ciascuno: ${riepilogo(solleciti.targets)}`,
        confirmLabel: "Apri le email",
        destructive: false,
      })
      if (!ok) return

      // Una finestra per volta: aprirle tutte insieme le fa bloccare dal browser.
      conEmail.forEach((t, indice) => {
        const url = `mailto:${t.email}?subject=${encodeURIComponent(t.subject)}&body=${encodeURIComponent(t.mailtoBody)}`
        if (indice === 0) window.open(url, "_self")
        else setTimeout(() => window.open(url, "_self"), indice * 900)
      })

      await markHrReminders(anno, mese, conEmail.map((t) => t.employeeId))
      notifySuccess(`${conEmail.length} solleciti aperti nel client di posta.`)
    } catch (e) {
      notifyError(e instanceof Error ? e.message : "Solleciti non riusciti.")
    } finally {
      setSollecitando(false)
    }
  }

  async function handleInviaSollecito() {
    setSollecitando(true)
    try {
      const solleciti = await fetchHrReminders(anno, mese, departmentId, employeeId)
      const conEmail = solleciti.targets.filter((t) => t.email)

      if (solleciti.targets.length === 0) {
        notifySuccess("Nessun sollecito da inviare: nessuna giornata scoperta.")
        return
      }
      if (!solleciti.smtpEnabled) {
        notifyError(
          "SMTP non configurato: usa «Sollecita» per aprire le email nel client di posta."
        )
        return
      }
      if (conEmail.length === 0) {
        notifyError("Nessuno dei dipendenti da sollecitare ha un indirizzo email.")
        return
      }

      const ok = await confirm({
        title: `Inviare ${conEmail.length} solleciti via email?`,
        description: `Le email partono dal server, senza altra conferma: ${riepilogo(solleciti.targets)}`,
        confirmLabel: "Invia",
        destructive: false,
      })
      if (!ok) return

      const esito = await sendHrReminders(anno, mese, departmentId, employeeId)
      if (esito.failed > 0 || esito.withoutEmail.length > 0) notifyError(esito.message)
      else notifySuccess(esito.message)
    } catch (e) {
      notifyError(e instanceof Error ? e.message : "Invio dei solleciti non riuscito.")
    } finally {
      setSollecitando(false)
    }
  }

  function handleStampa() {
    if (!calendario) return

    const intestazioni = Array.from({ length: giorni }, (_, i) => {
      const g = i + 1
      const festivo = calendario.nonWorkingDays[g]
      const sfondo = festivo ? "#ffc8c8" : "#c8e6ff"
      const testo = festivo ? "#8b0000" : "#00008b"
      return `<th style="padding:2px;font-size:8px;border:1px solid #ccc;text-align:center;background:${sfondo};color:${testo};">${g}<br/>${calendario.dayLabels[g] ?? ""}</th>`
    }).join("")

    const corpo = righe
      .map((r) => {
        const celle = Array.from({ length: giorni }, (_, i) => {
          const cella = r.days[i + 1]
          const sfondo = cella?.color ? COLORI_STAMPA[cella.color] ?? "#ffffff" : "#ffffff"
          return `<td style="padding:2px;font-size:8px;border:1px solid #ccc;text-align:center;background:${sfondo};">${cella?.text ?? ""}</td>`
        }).join("")

        const chiude = r.voceType === "INFORTUNIO" ? "border-bottom:2px solid #666;" : ""
        return `<tr>
          <td style="padding:3px;font-size:9px;border:1px solid #ccc;font-weight:bold;white-space:pre-line;${chiude}">${r.employee}</td>
          <td style="padding:3px;font-size:8px;border:1px solid #ccc;font-weight:bold;${chiude}">${r.voce}</td>
          ${celle}
          <td style="padding:3px;font-size:8px;border:1px solid #ccc;text-align:center;font-weight:bold;${chiude}">${r.total}</td>
        </tr>`
      })
      .join("")

    printHtml({
      title: `Calendario Presenze — ${meseLabel}`,
      subtitle: nomeDipendente ?? `Dipendenti: ${calendario.employees.length}`,
      contentHtml: `
        <table style="width:100%;border-collapse:collapse;margin-top:10px;">
          <thead>
            <tr style="background:#e9ecef;">
              <th style="padding:4px;font-size:9px;border:1px solid #ccc;text-align:left;">Dipendente</th>
              <th style="padding:4px;font-size:8px;border:1px solid #ccc;text-align:left;">Voce</th>
              ${intestazioni}
              <th style="padding:3px;font-size:8px;border:1px solid #ccc;">TOTALE</th>
            </tr>
          </thead>
          <tbody>${corpo}</tbody>
        </table>`,
      orientation: "landscape",
      paperSize: "A3",
    })
  }

  return (
    <div className="space-y-3">
      {/* Toolbar: gli stessi comandi del programma originale */}
      <div className="flex flex-wrap items-center gap-2">
        <LookupCombobox<number>
          options={opzioniReparti}
          value={departmentId}
          onValueChange={setDepartmentId}
          placeholder="Tutti i reparti"
          noneLabel="— tutti i reparti —"
          loading={repartiQuery.isLoading}
          className="w-56"
        />
        <LookupCombobox<number>
          options={opzioniDipendenti}
          value={employeeId}
          onValueChange={setEmployeeId}
          placeholder="Tutti i dipendenti"
          noneLabel="— tutti i dipendenti —"
          loading={calendarioQuery.isLoading}
          className="w-56"
        />
        {(departmentId != null || employeeId != null) && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setDepartmentId(null)
              setEmployeeId(null)
            }}
          >
            <RotateCcw className="mr-1 size-3.5" />
            Reset
          </Button>
        )}

        <div className="ml-auto flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={handleSollecita}
            disabled={!calendario || sollecitando}
            title="Apre nel client di posta un sollecito per ogni dipendente con giornate scoperte"
          >
            <Mail className="mr-1 size-3.5" />
            Sollecita
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={handleInviaSollecito}
            disabled={!calendario || sollecitando}
            title="Invia i solleciti direttamente dal server, senza passare dal client di posta"
          >
            <MailCheck className="mr-1 size-3.5" />
            {sollecitando ? "Attendi…" : "Invia sollecito"}
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={handleEsportaExcel}
            disabled={!calendario || scaricando}
          >
            <Download className="mr-1 size-3.5" />
            {scaricando ? "Esporto…" : "Esporta Excel"}
          </Button>
          <Button variant="outline" size="sm" onClick={handleStampa} disabled={!calendario}>
            <Printer className="mr-1 size-3.5" />
            Stampa
          </Button>
        </div>
      </div>

      <GridScroller>
        <Table className="text-xs">
          <TableHeader>
            <TableRow>
              <TableHead className="w-44 sticky left-0 z-20 bg-background font-semibold">
                Dipendente
              </TableHead>
              <TableHead className="w-40 sticky left-44 z-20 bg-background font-semibold">
                Voce
              </TableHead>
              {Array.from({ length: giorni }, (_, i) => {
                const g = i + 1
                const festivo = calendario?.nonWorkingDays[g] ?? false
                return (
                  <TableHead
                    key={g}
                    className={cn(
                      "min-w-[30px] max-w-[36px] p-1 text-center font-mono",
                      festivo
                        ? "bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-200"
                        : "bg-sky-100 text-sky-900 dark:bg-sky-950 dark:text-sky-200"
                    )}
                  >
                    <div>{g}</div>
                    <div className="text-[10px] font-normal opacity-80">
                      {calendario?.dayLabels[g] ?? ""}
                    </div>
                  </TableHead>
                )
              })}
              <TableHead className="text-center font-semibold">TOTALE</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {calendarioQuery.isLoading ? (
              <TableRow>
                <TableCell
                  colSpan={giorni + 3}
                  className="h-32 text-center text-muted-foreground"
                >
                  Caricamento calendario…
                </TableCell>
              </TableRow>
            ) : righe.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={giorni + 3}
                  className="h-32 text-center text-muted-foreground"
                >
                  Nessun dato presenze per il periodo selezionato.
                </TableCell>
              </TableRow>
            ) : (
              righe.map((riga, indice) => {
                // L'infortunio chiude il dipendente: sotto ci va la riga di separazione.
                const chiudeDipendente = riga.voceType === "INFORTUNIO"

                return (
                  <TableRow
                    key={`${riga.employeeId}-${riga.voceType}-${indice}`}
                    className={cn(
                      "hover:bg-muted/30",
                      chiudeDipendente && "border-b-2 border-b-foreground/30"
                    )}
                  >
                    <TableCell className="sticky left-0 z-10 whitespace-pre-line bg-background font-semibold">
                      {riga.employee}
                    </TableCell>
                    <TableCell className="sticky left-44 z-10 whitespace-nowrap bg-background text-[11px] font-semibold">
                      {riga.voce}
                    </TableCell>

                    {Array.from({ length: giorni }, (_, i) => {
                      const g = i + 1
                      const cella: HrCalendarCell | undefined = riga.days[g]
                      const cliccabile = giornoGiustificabile(g)
                      const apriGiustifica = () =>
                        setGiustifica({
                          employeeId: riga.employeeId,
                          date: dateToIso(new Date(anno, mese - 1, g)),
                        })

                      const contenuto = (
                        <div className="flex h-6 w-full items-center justify-center">
                          {cella?.text ?? ""}
                        </div>
                      )

                      // Il tooltip col dettaglio della giornata, quando il server ne manda uno.
                      const corpo = cella?.tooltip ? (
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <div className="cursor-default">{contenuto}</div>
                          </TooltipTrigger>
                          <TooltipContent className="whitespace-pre-line text-xs">
                            {cella.tooltip}
                          </TooltipContent>
                        </Tooltip>
                      ) : (
                        contenuto
                      )

                      return (
                        <TableCell
                          key={g}
                          // Doppio clic = causale, come nel programma originale; il tasto
                          // destro apre lo stesso dialogo dal menu contestuale.
                          onDoubleClick={cliccabile ? apriGiustifica : undefined}
                          // 🪤 Il `title` nativo solo dove NON c'e' gia' il tooltip del
                          // server: due suggerimenti sovrapposti sulla stessa cella si
                          // coprono a vicenda e non si legge piu' nessuno dei due.
                          title={
                            cliccabile && !cella?.tooltip
                              ? "Doppio clic o tasto destro: giustifica le ore mancanti"
                              : undefined
                          }
                          className={cn(
                            "border-r p-0.5 text-center font-mono text-[10px]",
                            cella?.color ? COLORI[cella.color] : "",
                            cliccabile && "cursor-pointer"
                          )}
                        >
                          {cliccabile ? (
                            // Il menu sta DENTRO la cella: un <div> fra <tr> e <td> non e'
                            // markup valido e il browser lo sposterebbe fuori dalla tabella.
                            <ContextMenu>
                              <ContextMenuTrigger asChild>
                                <div>{corpo}</div>
                              </ContextMenuTrigger>
                              <ContextMenuContent>
                                <ContextMenuItem onSelect={apriGiustifica}>
                                  <CalendarCheck className="size-3.5" />
                                  Giustifica ore mancanti…
                                </ContextMenuItem>
                              </ContextMenuContent>
                            </ContextMenu>
                          ) : (
                            corpo
                          )}
                        </TableCell>
                      )
                    })}

                    <TableCell className="text-center font-mono font-semibold">
                      {riga.total}
                    </TableCell>
                  </TableRow>
                )
              })
            )}
          </TableBody>
        </Table>
      </GridScroller>

      <GiustificaCausaleDialog
        target={giustifica}
        onOpenChange={(open) => {
          if (!open) setGiustifica(null)
        }}
        onSaved={() => void calendarioQuery.refetch()}
      />
    </div>
  )
}
