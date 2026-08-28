import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Printer, RefreshCw, Save } from "lucide-react"

import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { MoneyInput } from "@/components/shared/money-input"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { ApiError } from "@/lib/api/client"
import { fetchSalSapAcconti, saveSalSapAcconti } from "@/lib/api/sal"
import type { SalSapAcconti } from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { formatDateShort } from "@/lib/date-iso"
import { euro, parseDecimal } from "@/lib/format"
import { printHtml } from "@/lib/print-template"
import { notifyError, notifySuccess } from "@/lib/toast"
import { cn } from "@/lib/utils"

/**
 * #131 «SAL / SAP Acconti» — la quadratura fra gli acconti che risultano dal gestionale e
 * il saldo del conto acconti in SAP.
 *
 * <p>Tre tabelle, come le ha disegnate la segnalazione: quella del SAL si calcola, quella
 * del conto SAP si scrive a mano (in SAP quel numero c'è, qui no), la terza è la
 * differenza. Se la terza è a zero i due mondi si parlano; se non lo è, quello è lo scarto
 * da andare a cercare.</p>
 *
 * <p>L'aggiornamento in tempo reale lo governa <c>SalPage</c> con SignalR: il salvataggio
 * manda l'evento globale SAL e le altre postazioni ricaricano da sole.</p>
 */
export const SAL_SAP_ACCONTI_QUERY_KEY = ["sal", "sap-acconti"] as const

/** Numero intero di fatture, all'italiana. */
function fmtCount(n: number | null): string {
  return n == null ? "—" : n.toLocaleString("it-IT")
}

function fmtEuro(n: number | null): string {
  return n == null ? "—" : euro(n)
}

/** Le due colonne di ogni tabella, con lo stesso ordine del disegno della segnalazione. */
function Coppia({
  totFatture,
  importo,
  className,
}: {
  totFatture: React.ReactNode
  importo: React.ReactNode
  className?: string
}) {
  return (
    <div className={cn("grid grid-cols-2 divide-x rounded-md border", className)}>
      <div className="flex flex-col gap-1 p-3">
        <span className="text-xs font-medium text-muted-foreground">Tot. Fatture</span>
        {totFatture}
      </div>
      <div className="flex flex-col gap-1 p-3">
        <span className="text-xs font-medium text-muted-foreground">Importo Acconti</span>
        {importo}
      </div>
    </div>
  )
}

function Valore({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <span className={cn("font-mono text-lg font-semibold tabular-nums", className)}>
      {children}
    </span>
  )
}

/** Stampa le tre tabelle, nello stesso formato delle altre viste SAL. */
function printSapAcconti(data: SalSapAcconti, diff: { fatture: number | null; importo: number | null }): void {
  const riga = (titolo: string, fatture: string, importo: string) =>
    `<tr><td>${titolo}</td><td style="text-align:right">${fatture}</td><td style="text-align:right">${importo}</td></tr>`

  const contentHtml = `
    <table>
      <thead>
        <tr>
          <th>Tabella</th>
          <th style="text-align:right">Tot. Fatture</th>
          <th style="text-align:right">Importo Acconti</th>
        </tr>
      </thead>
      <tbody>
        ${riga("Da SAL Gestionale — Totale Acconti", fmtCount(data.salTotFatture), euro(data.salImportoAcconti))}
        ${riga(`Conto SAP ${data.contoSap} — Totale Acconti`, fmtCount(data.sapTotFatture), fmtEuro(data.sapImportoAcconti))}
        ${riga("Differenza SAP − SAL", fmtCount(diff.fatture), fmtEuro(diff.importo))}
      </tbody>
    </table>
  `

  printHtml({
    title: `SAP Acconti — conto ${data.contoSap}`,
    subtitle: `Situazione al ${formatDateShort(new Date())}`,
    contentHtml,
    orientation: "portrait",
    paperSize: "A4",
    customStyles: `
      table{border-collapse:collapse;width:100%;font-size:11px}
      th,td{border:1px solid #ccc;padding:5px 8px;text-align:left}th{background:#f3f4f6}
    `,
  })
}

export function SalSapAccontiView() {
  const queryClient = useQueryClient()
  const canWrite = canWriteFeature("sal.economics")

  const query = useQuery({
    queryKey: SAL_SAP_ACCONTI_QUERY_KEY,
    queryFn: fetchSalSapAcconti,
    refetchOnWindowFocus: true,
  })

  const data = query.data

  // Bozze locali dei due campi a mano: si scrivono senza round-trip e si risincronizzano
  // quando il server manda un valore diverso (anche per SignalR, da un'altra postazione).
  const [fattureText, setFattureText] = React.useState("")
  const [importoText, setImportoText] = React.useState("")
  const [sporco, setSporco] = React.useState(false)

  const remoteFatture = data?.sapTotFatture ?? null
  const remoteImporto = data?.sapImportoAcconti ?? null

  React.useEffect(() => {
    // Non si sovrascrive quello che l'utente sta scrivendo: solo l'allineamento iniziale
    // e quello dopo un salvataggio riuscito (che azzera `sporco`).
    if (sporco) return
    setFattureText(remoteFatture == null ? "" : String(remoteFatture))
    setImportoText(remoteImporto == null ? "" : String(remoteImporto))
  }, [remoteFatture, remoteImporto, sporco])

  const salva = useMutation({
    mutationFn: () =>
      saveSalSapAcconti({
        totFatture: fattureText.trim() === "" ? null : Math.max(0, Math.trunc(Number(fattureText))),
        importoAcconti: importoText.trim() === "" ? null : parseDecimal(importoText),
        rowVersion: data?.rowVersion ?? null,
      }),
    onSuccess: async () => {
      setSporco(false)
      notifySuccess("Totali conto SAP aggiornati")
      await queryClient.invalidateQueries({ queryKey: SAL_SAP_ACCONTI_QUERY_KEY })
    },
    onError: (err) => notifyError(err as Error),
  })

  const fattureNonValide =
    fattureText.trim() !== "" && !Number.isFinite(Number(fattureText))

  // Differenza = SAP − SAL, ma solo dove il valore SAP c'è: senza, il «differenziale»
  // sarebbe l'intero importo del SAL cambiato di segno, cioè un allarme inventato.
  const diff = React.useMemo(() => {
    if (!data) return { fatture: null, importo: null }
    return {
      fatture: data.sapTotFatture == null ? null : data.sapTotFatture - data.salTotFatture,
      importo:
        data.sapImportoAcconti == null ? null : data.sapImportoAcconti - data.salImportoAcconti,
    }
  }, [data])

  const quadra =
    diff.fatture !== null && diff.importo !== null && diff.fatture === 0 && Math.abs(diff.importo) < 0.005

  if (query.isLoading) {
    return (
      <div className="flex items-center justify-center gap-2 py-12 text-sm text-muted-foreground">
        <RefreshCw className="size-4 animate-spin" />
        Caricamento acconti SAP...
      </div>
    )
  }

  if (query.isError) {
    const message =
      query.error instanceof ApiError && query.error.status === 403
        ? "Dati economici riservati ai ruoli PM/ADMIN."
        : (query.error as Error).message
    return <PageErrorAlert message={message} />
  }

  if (!data) return null

  const segnoDiff = (n: number | null) =>
    n == null || n === 0 ? "" : n > 0 ? "+" : "−"

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-end">
        <Button
          variant="outline"
          size="sm"
          className="h-8"
          onClick={() => printSapAcconti(data, diff)}
        >
          <Printer className="size-3.5 mr-1.5" />
          Stampa PDF
        </Button>
      </div>

      <div className="grid gap-3 lg:grid-cols-3">
        {/* ── 1. Quello che dice il gestionale ─────────────────────────────── */}
        <Card size="sm" className="border-l-4 border-l-amber-500">
          <CardHeader className="pb-0">
            <CardTitle className="text-[13px] leading-snug">
              Da SAL Gestionale — Totale Acconti
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            <Coppia
              totFatture={<Valore>{fmtCount(data.salTotFatture)}</Valore>}
              importo={<Valore>{euro(data.salImportoAcconti)}</Valore>}
            />
            <p className="text-[11px] leading-snug text-muted-foreground">
              Righe SAL con Conto SAP «Acconto», di tutte le commesse (chiuse comprese).
              Una riga portata a «Ricavo» esce dal conteggio.
            </p>
          </CardContent>
        </Card>

        {/* ── 2. Quello che dice SAP: lo scrive una persona ─────────────────── */}
        <Card size="sm" className="border-l-4 border-l-emerald-600">
          <CardHeader className="pb-0">
            <CardTitle className="text-[13px] leading-snug">
              Conto SAP {data.contoSap} — Totale Acconti
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            <Coppia
              totFatture={
                <Input
                  value={fattureText}
                  onChange={(e) => {
                    setSporco(true)
                    setFattureText(e.target.value)
                  }}
                  inputMode="numeric"
                  placeholder="—"
                  disabled={!canWrite || salva.isPending}
                  aria-invalid={fattureNonValide}
                  className="h-9 text-right tabular-nums"
                />
              }
              importo={
                <MoneyInput
                  value={importoText}
                  onChange={(v) => {
                    setSporco(true)
                    setImportoText(v)
                  }}
                  placeholder="—"
                  disabled={!canWrite || salva.isPending}
                  className="h-9"
                />
              }
            />
            <div className="flex items-center justify-between gap-2">
              <p className="text-[11px] leading-snug text-muted-foreground">
                {data.updatedAt
                  ? `Ultimo aggiornamento: ${formatDateShort(data.updatedAt)}${
                      data.updatedByName ? ` — ${data.updatedByName}` : ""
                    }`
                  : "Mai compilato: i valori si leggono in SAP e si scrivono qui."}
              </p>
              {canWrite && (
                <Button
                  size="sm"
                  className="h-8 shrink-0"
                  disabled={!sporco || fattureNonValide || salva.isPending}
                  onClick={() => salva.mutate()}
                >
                  <Save className="size-3.5 mr-1.5" />
                  Salva
                </Button>
              )}
            </div>
          </CardContent>
        </Card>

        {/* ── 3. Lo scarto fra i due ───────────────────────────────────────── */}
        <Card
          size="sm"
          className={cn("border-l-4", quadra ? "border-l-emerald-600" : "border-l-rose-600")}
        >
          <CardHeader className="pb-0">
            <CardTitle className="text-[13px] leading-snug">Differenza SAP − SAL</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-2">
            <Coppia
              totFatture={
                <Valore className={cn(diff.fatture !== 0 && diff.fatture !== null && "text-rose-600")}>
                  {diff.fatture == null
                    ? "—"
                    : `${segnoDiff(diff.fatture)}${fmtCount(Math.abs(diff.fatture))}`}
                </Valore>
              }
              importo={
                <Valore className={cn(!quadra && diff.importo !== null && "text-rose-600")}>
                  {diff.importo == null
                    ? "—"
                    : `${segnoDiff(diff.importo)}${euro(Math.abs(diff.importo))}`}
                </Valore>
              }
            />
            <p className="text-[11px] leading-snug text-muted-foreground">
              {diff.importo == null
                ? "Compila i totali del conto SAP per vedere lo scarto."
                : quadra
                  ? "Il conto SAP quadra con il SAL gestionale."
                  : "Scarto da verificare fra SAP e SAL gestionale."}
            </p>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
