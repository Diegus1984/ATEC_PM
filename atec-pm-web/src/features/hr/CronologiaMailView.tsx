import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { RotateCcw } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useCopyText } from "@/components/shared/copy-text"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  LookupCombobox,
  type LookupComboboxOption,
} from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Textarea } from "@/components/ui/textarea"
import { fetchPunchingEmployees } from "@/lib/api/employees"
import { fetchHrReminderLog } from "@/lib/api/hr"
import type { HrReminderLogRow } from "@/lib/api/types"
import { formatDateShort, formatDateTimeShort } from "@/lib/date-iso"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"

interface CronologiaMailViewProps {
  anno: number
  mese: number
}

/**
 * La Cronologia Email (PIANO-HR-PORT-ORIGINALE.md, voce 6): port di `MailLogPage` del
 * programma «Timbrature» — data invio · dipendente · indirizzo · giorno di riferimento ·
 * oggetto · origine · inviata da, e il testo rileggibile aprendo la riga.
 *
 * <p>🪤 Il mese è quello del <b>giorno di riferimento</b>, non della spedizione: come
 * nell'originale, una mail mandata a settembre per un buco di agosto si cerca sotto
 * agosto. È la domanda che si fa chi guarda: «per quel giorno, che cosa gli ho scritto?».</p>
 *
 * <p>🪤 Le righe scritte prima della migrazione M117 non hanno il testo: si dice «testo non
 * conservato», non si finge una mail vuota.</p>
 */
const COLUMNS: { id: string; label: string }[] = [
  { id: "inviata", label: "📅 Inviata" },
  { id: "dipendente", label: "👤 Dipendente" },
  { id: "email", label: "📧 Email" },
  { id: "giorno", label: "📆 Giorno rif." },
  { id: "oggetto", label: "📝 Oggetto" },
  { id: "origine", label: "📌 Origine" },
  { id: "da", label: "👤 Inviata da" },
]
const COLUMNS_DEFAULT = Object.fromEntries(COLUMNS.map((c) => [c.id, true]))
const COLUMNS_STORAGE_KEY = "hr-cronologia-mail-columns-v1"

export function CronologiaMailView({ anno, mese }: CronologiaMailViewProps) {
  const [employeeId, setEmployeeId] = React.useState<number | null>(null)
  const [aperta, setAperta] = React.useState<HrReminderLogRow | null>(null)

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
  const visibleCount = COLUMNS.filter((c) => show(c.id)).length || 1

  const dipendentiQuery = useQuery({
    queryKey: ["employees-punching"],
    queryFn: fetchPunchingEmployees,
  })

  const logQuery = useQuery({
    queryKey: ["hr-reminder-log", anno, mese, employeeId],
    queryFn: () => fetchHrReminderLog(anno, mese, employeeId),
  })

  const opzioniDipendenti: LookupComboboxOption<number>[] = React.useMemo(
    () => (dipendentiQuery.data ?? []).map((e) => ({ id: e.id, name: e.name })),
    [dipendentiQuery.data]
  )

  const righe = logQuery.data?.rows ?? []

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <LookupCombobox<number>
          options={opzioniDipendenti}
          value={employeeId}
          onValueChange={setEmployeeId}
          placeholder="Tutti i dipendenti"
          noneLabel="— tutti i dipendenti —"
          loading={dipendentiQuery.isLoading}
          className="w-56"
        />
        {employeeId != null && (
          <Button variant="ghost" size="sm" onClick={() => setEmployeeId(null)}>
            <RotateCcw className="mr-1 size-3.5" />
            Reset
          </Button>
        )}
        <span className="text-sm text-muted-foreground">
          {righe.length} {righe.length === 1 ? "email trovata" : "email trovate"} — il mese è
          quello del giorno di riferimento
        </span>
        <div className="ml-auto">
          <ColumnsMenu columns={columnToggles} />
        </div>
      </div>

      {logQuery.isLoading ? (
        <p className="text-sm text-muted-foreground">Caricamento…</p>
      ) : logQuery.error ? (
        <p className="text-sm text-destructive">{(logQuery.error as Error).message}</p>
      ) : (
        <GridScroller className="rounded-lg border">
          <Table className="text-xs">
            <TableHeader>
              <TableRow>
                {show("inviata") && <TableHead className="w-32">Inviata</TableHead>}
                {show("dipendente") && <TableHead className="w-44">Dipendente</TableHead>}
                {show("email") && <TableHead className="w-56">Email</TableHead>}
                {show("giorno") && <TableHead className="w-28">Giorno rif.</TableHead>}
                {show("oggetto") && <TableHead>Oggetto</TableHead>}
                {show("origine") && <TableHead className="w-24">Origine</TableHead>}
                {show("da") && <TableHead className="w-40">Inviata da</TableHead>}
              </TableRow>
            </TableHeader>
            <TableBody>
              {righe.map((r) => (
                <TableRow
                  key={r.id}
                  className="cursor-pointer hover:bg-muted/60"
                  onDoubleClick={() => setAperta(r)}
                  title="Doppio clic per rileggere il testo"
                >
                  {show("inviata") && (
                    <TableCell className="font-mono whitespace-nowrap">
                      {formatDateTimeShort(r.sentAt)}
                    </TableCell>
                  )}
                  {show("dipendente") && <TableCell>{r.employeeName}</TableCell>}
                  {show("email") && (
                    <TableCell className="text-muted-foreground">
                      {r.email || "—"}
                    </TableCell>
                  )}
                  {show("giorno") && (
                    <TableCell className="font-mono whitespace-nowrap">
                      {formatDateShort(r.workDate)}
                    </TableCell>
                  )}
                  {show("oggetto") && (
                    <TableCell className="max-w-80 truncate" title={r.subject ?? ""}>
                      {r.subject || "—"}
                    </TableCell>
                  )}
                  {show("origine") && (
                    <TableCell className="text-muted-foreground">
                      {r.channel === "MAILTO" ? "Client posta" : "Server"}
                    </TableCell>
                  )}
                  {show("da") && (
                    <TableCell className="text-muted-foreground">
                      {r.sentByName || "—"}
                    </TableCell>
                  )}
                </TableRow>
              ))}
              {righe.length === 0 && (
                <TableRow>
                  <TableCell
                    colSpan={visibleCount}
                    className="text-center text-sm text-muted-foreground"
                  >
                    Nessun sollecito inviato per questo mese.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </GridScroller>
      )}

      <DettaglioMailDialog riga={aperta} onOpenChange={() => setAperta(null)} />
    </div>
  )
}

/** Il testo della mail inviata, come `MailDetailDialog` dell'originale. */
function DettaglioMailDialog({
  riga,
  onOpenChange,
}: {
  riga: HrReminderLogRow | null
  onOpenChange: (open: boolean) => void
}) {
  const copia = useCopyText()

  // 🪤 In produzione ATEC PM gira su HTTP: `navigator.clipboard` non esiste e il ripiego
  // con execCommand dice «copiato» senza copiare. useCopyText apre il ripiego a mano.
  const testo = riga
    ? `A: ${riga.email ?? "—"}\nOggetto: ${riga.subject ?? "—"}\n` +
      `Data: ${formatDateTimeShort(riga.sentAt)}\n` +
      `${"─".repeat(40)}\n\n${riga.body ?? ""}`
    : ""

  return (
    <Dialog open={riga != null} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[85vh] flex-col sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Dettaglio email inviata</DialogTitle>
          <DialogDescription>
            {riga
              ? `${riga.employeeName} — giornata del ${formatDateShort(riga.workDate)}`
              : ""}
          </DialogDescription>
        </DialogHeader>

        {riga && (
          <div className="flex min-h-0 flex-1 flex-col gap-3">
            <div className="space-y-1 rounded-lg border bg-muted/30 p-3 text-sm">
              <p>
                <span className="font-semibold text-muted-foreground">A: </span>
                {riga.email || "—"}
              </p>
              <p>
                <span className="font-semibold text-muted-foreground">Inviata il: </span>
                {formatDateTimeShort(riga.sentAt)}
                {riga.sentByName ? ` da ${riga.sentByName}` : ""}
              </p>
              <p>
                <span className="font-semibold text-muted-foreground">Oggetto: </span>
                <span className="font-medium">{riga.subject || "—"}</span>
              </p>
            </div>

            {riga.body ? (
              <Textarea
                readOnly
                value={riga.body}
                className="min-h-64 flex-1 resize-none font-mono text-xs"
              />
            ) : (
              <p className="rounded-lg border border-amber-500/40 bg-amber-500/5 p-3 text-sm">
                Testo non conservato: questo sollecito è stato registrato prima che il
                gestionale iniziasse a tenere il corpo delle email.
              </p>
            )}
          </div>
        )}

        <DialogFooter>
          {riga?.body && (
            <Button
              variant="outline"
              onClick={() => void copia(testo, "Testo del sollecito")}
            >
              Copia testo
            </Button>
          )}
          <Button onClick={() => onOpenChange(false)}>Chiudi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
