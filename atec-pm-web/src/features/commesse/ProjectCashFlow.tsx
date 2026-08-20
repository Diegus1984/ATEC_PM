import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts"
import { Plus, RefreshCw, Trash2 } from "lucide-react"

import { GridScroller } from "@/components/shared/grid-scroller"
import { MoneyInput } from "@/components/shared/money-input"
import { useConfirm } from "@/components/shared/confirm"
import { euro, parseDecimal } from "@/lib/format"
import { notifyError } from "@/lib/toast"
import { DateField } from "@/components/shared/date-field"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import { ApiError } from "@/lib/api/client"
import {
  addCashFlowCategory,
  deleteCashFlowCategory,
  fetchCashFlow,
  initCashFlow,
  saveCashFlowData,
  updateCashFlowCategory,
  updateCashFlowHeader,
} from "@/lib/api/project-cashflow"
import type { CashFlowCategory, CashFlowData } from "@/lib/api/types"

const YELLOW = "#FFE699"
const GREEN = "#92D050"
/**
 * Importo compatto per le celle mensili: intero, senza simbolo. La griglia ha 13
 * colonne di mesi e con `4.000,00 €` in ognuna diventa illeggibile — il € completo
 * resta sulla colonna «Totale» e sulle righe calcolate di riepilogo.
 */
const n0 = (v: number) => Math.round(v).toLocaleString("it-IT")
/** MoneyInput dentro una cella: niente bordo, altezza e padding del campo standard. */
const cellInput =
  "h-5 rounded-none border-0 bg-transparent px-0 py-0 text-[11px] shadow-none focus-visible:ring-0 md:text-[11px]"

function monthLabel(startDate: string | null, monthNumber: number): string {
  if (!startDate) return `M${monthNumber}`
  const base = new Date(startDate)
  if (Number.isNaN(base.getTime())) return `M${monthNumber}`
  const d = new Date(base.getFullYear(), base.getMonth() + monthNumber + 1, 0)
  return d.toLocaleDateString("it-IT", { month: "short", year: "2-digit" })
}

/** Indicizza dataItems per tipo → mappa monthNumber→valore (e per CAT_PCT, catId→mappa). */
function indexData(cf: CashFlowData) {
  const byType = (t: string) => cf.dataItems.filter((x) => x.dataType === t)
  const toMonthMap = (t: string) => {
    const m: Record<number, number> = {}
    byType(t).forEach((x) => (m[x.monthNumber] = x.numValue))
    return m
  }
  const catPct: Record<number, Record<number, number>> = {}
  byType("CAT_PCT").forEach((x) => {
    catPct[x.refId] = catPct[x.refId] ?? {}
    catPct[x.refId][x.monthNumber] = x.numValue
  })
  return {
    income: toMonthMap("INCOME_PCT"),
    adjust: toMonthMap("ADJUSTMENT"),
    bank: toMonthMap("BANK"),
    catPct,
  }
}

export function ProjectCashFlow({ projectId }: { projectId: number }) {
  const confirm = useConfirm()
  const query = useQuery({
    queryKey: ["project-cashflow", projectId],
    queryFn: () => fetchCashFlow(projectId),
    enabled: projectId > 0,
  })
  const cf = query.data

  const mc = cf?.monthCount ?? 13
  const [pay, setPay] = React.useState(0)
  const [income, setIncome] = React.useState<number[]>([])
  const [adjust, setAdjust] = React.useState<number[]>([])
  const [bank, setBank] = React.useState<number[]>([])
  const [catPct, setCatPct] = React.useState<Record<number, number[]>>({})
  const [catAmount, setCatAmount] = React.useState<Record<number, number>>({})
  const [busy, setBusy] = React.useState(false)
  // Testo del campo «Incasso totale»: MoneyInput è controllato, quindi serve una stringa
  // che segua `pay` (che cambia anche quando arrivano i dati dal server).
  const [payText, setPayText] = React.useState("0")
  React.useEffect(() => {
    setPayText(String(pay))
  }, [pay])

  // (Re)inizializza lo stato editabile quando arrivano nuovi dati dal server.
  React.useEffect(() => {
    if (!cf || !cf.isInitialized) return
    const idx = indexData(cf)
    const col = (m: Record<number, number>) =>
      Array.from({ length: cf.monthCount }, (_, i) => m[i + 1] ?? 0)
    setPay(cf.paymentAmount)
    setIncome(col(idx.income))
    setAdjust(col(idx.adjust))
    setBank(col(idx.bank))
    setCatAmount(Object.fromEntries(cf.categories.map((c) => [c.id, c.totalAmount])))
    setCatPct(
      Object.fromEntries(
        cf.categories.map((c) => [c.id, col(idx.catPct[c.id] ?? {})])
      )
    )
  }, [cf])

  // Ricalcolo (replica CashFlowViewModel.Recalculate).
  const calc = React.useMemo(() => {
    const cats = cf?.categories ?? []
    const payment: number[] = []
    const entrate: number[] = []
    const uscite: number[] = []
    const cumulative: number[] = []
    const catAmt: Record<number, number[]> = {}
    cats.forEach((c) => (catAmt[c.id] = []))
    let pctSum = 0
    let cum = 0
    for (let m = 0; m < mc; m++) {
      let pct = income[m] ?? 0
      if (pctSum + pct > 100) pct = 100 - pctSum
      const p = (pay * pct) / 100
      payment[m] = p
      pctSum += pct
      const ent = p + (adjust[m] ?? 0)
      entrate[m] = ent
      let out = 0
      cats.forEach((c) => {
        const pcts = catPct[c.id] ?? []
        let cp = pcts[m] ?? 0
        let before = 0
        for (let k = 0; k < m; k++) before += pcts[k] ?? 0
        if (before + cp > 100) cp = 100 - before
        const amt = ((catAmount[c.id] ?? c.totalAmount) * cp) / 100
        catAmt[c.id][m] = amt
        out += amt
      })
      uscite[m] = out
      cum += ent - out
      cumulative[m] = cum
    }
    return { payment, entrate, uscite, cumulative, catAmt }
  }, [cf, mc, pay, income, adjust, catPct, catAmount])

  async function save(fn: () => Promise<void>) {
    setBusy(true)
    try {
      await fn()
    } catch (err) {
      notifyError(err)
    } finally {
      setBusy(false)
    }
  }

  function saveCell(
    dataType: string,
    refId: number,
    monthIndex: number,
    value: number
  ) {
    void saveCashFlowData(projectId, {
      dataType,
      refId,
      monthNumber: monthIndex + 1,
      numValue: value,
    }).catch((err) => notifyError(err))
  }

  if (query.isLoading) {
    return <Skeleton className="h-64 w-full" />
  }
  if (query.isError) {
    return (
      <p className="text-sm text-destructive">
        {query.error instanceof ApiError && query.error.status === 403
          ? "Flusso di cassa riservato ai ruoli PM/ADMIN."
          : (query.error as Error).message || "Errore di caricamento."}
      </p>
    )
  }
  if (!cf) return null

  // Non inizializzato → schermata di setup.
  if (!cf.isInitialized) {
    return (
      <div className="mx-auto max-w-md rounded-lg border p-6 text-center">
        <p className="text-base font-semibold">Flusso di cassa non inizializzato</p>
        <p className="mt-2 text-sm text-muted-foreground">
          Crea il piano a {cf.monthCount} mesi con incasso pari al ricavo commessa
          ({euro(cf.projectRevenue)}).
        </p>
        <Button
          className="mt-4"
          disabled={busy}
          onClick={() =>
            save(async () => {
              await initCashFlow(projectId, {
                paymentAmount: cf.projectRevenue,
                monthCount: 13,
              })
              await query.refetch()
            })
          }
        >
          Inizializza
        </Button>
      </div>
    )
  }

  const months = Array.from({ length: mc }, (_, i) => i)
  const chartData = months.map((m) => ({
    label: monthLabel(cf.startDate, m + 1),
    entrate: Math.round(calc.entrate[m]),
    uscite: -Math.round(calc.uscite[m]),
    saldo: Math.round(calc.cumulative[m]),
  }))

  const cell = "border px-1 py-0.5 text-right tabular-nums"
  const numInput =
    "w-full bg-transparent text-right text-[11px] tabular-nums outline-none"

  return (
    <div className="space-y-3">
      {/* Header */}
      <div className="flex flex-wrap items-end gap-3">
        <label className="text-xs">
          <span className="block text-muted-foreground">Incasso totale (€)</span>
          <MoneyInput
            className="h-8 w-36"
            value={payText}
            onChange={setPayText}
            onCommit={() => {
              const v = parseDecimal(payText)
              if (v === pay) return
              setPay(v)
              void save(async () => {
                await updateCashFlowHeader(projectId, {
                  paymentAmount: v,
                  monthCount: mc,
                  startDate: cf.startDate,
                })
              })
            }}
          />
        </label>
        <label className="text-xs">
          <span className="block text-muted-foreground">Data inizio</span>
          <DateField
            value={cf.startDate}
            onChange={(v) =>
              save(async () => {
                await updateCashFlowHeader(projectId, {
                  paymentAmount: pay,
                  monthCount: mc,
                  startDate: v,
                })
                await query.refetch()
              })
            }
            size="sm"
            className="w-52"
          />
        </label>
        <Button
          variant="outline"
          size="sm"
          className="ml-auto"
          disabled={busy}
          onClick={() =>
            save(async () => {
              await addCashFlowCategory(projectId, {
                name: "Nuova categoria",
                totalAmount: 0,
                notes: "",
              })
              await query.refetch()
            })
          }
        >
          <Plus />
          Categoria
        </Button>
        <Button
          variant="outline"
          size="sm"
          onClick={() => query.refetch()}
          disabled={query.isFetching}
        >
          <RefreshCw className={query.isFetching ? "animate-spin" : ""} />
          Aggiorna
        </Button>
      </div>

      {/* Griglia */}
      <p className="text-xs text-muted-foreground">
        Importi in €: le celle mensili sono arrotondate all'unità, i totali sono
        esatti. Le righe «%» sono percentuali di ripartizione.
      </p>
      <GridScroller className="rounded-lg border">
        <table className="w-full border-collapse text-[11px]">
          <thead>
            <tr className="bg-muted/50">
              <th className="border px-2 py-1 text-left">Voce</th>
              <th className="border px-2 py-1 text-right">Totale (€)</th>
              {months.map((m) => (
                <th key={m} className="border px-1 py-1 text-center font-normal">
                  {m + 1}
                  <br />
                  {monthLabel(cf.startDate, m + 1)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            <CalcRow label="PAGAMENTO" total={pay} values={calc.payment} color={YELLOW} />
            <EditRow
              label="%"
              color={GREEN}
              total={income.reduce((a, b) => a + b, 0)}
              values={income}
              onCommit={(m, v) => {
                setIncome((prev) => prev.map((x, i) => (i === m ? v : x)))
                saveCell("INCOME_PCT", 0, m, v)
              }}
              cellCls={cell}
              inputCls={numInput}
            />
            <CalcRow label="ENTRATE MESE" total={calc.entrate.reduce((a, b) => a + b, 0)} values={calc.entrate} color={YELLOW} />
            <EditRow
              label="Aggiustam. Manu"
              kind="money"
              total={adjust.reduce((a, b) => a + b, 0)}
              values={adjust}
              onCommit={(m, v) => {
                setAdjust((prev) => prev.map((x, i) => (i === m ? v : x)))
                saveCell("ADJUSTMENT", 0, m, v)
              }}
              cellCls={cell}
              inputCls={numInput}
            />
            <tr>
              <td className="border px-2 py-1" colSpan={mc + 2} />
            </tr>
            {cf.categories.map((c) => (
              <React.Fragment key={c.id}>
                <CategoryAmountRow
                  category={c}
                  values={calc.catAmt[c.id] ?? []}
                  amount={catAmount[c.id] ?? c.totalAmount}
                  onAmount={(v) => {
                    setCatAmount((prev) => ({ ...prev, [c.id]: v }))
                    void save(async () => {
                      await updateCashFlowCategory(projectId, c.id, {
                        name: c.name,
                        totalAmount: v,
                        notes: c.notes,
                      })
                    })
                  }}
                  onLabel={(name) =>
                    save(async () => {
                      await updateCashFlowCategory(projectId, c.id, {
                        name,
                        totalAmount: catAmount[c.id] ?? c.totalAmount,
                        notes: c.notes,
                      })
                      await query.refetch()
                    })
                  }
                  onDelete={async () => {
                    const ok = await confirm({
                      title: "Elimina categoria",
                      description: `Eliminare "${c.name}"?`,
                      confirmLabel: "Elimina",
                    })
                    if (ok)
                      void save(async () => {
                        await deleteCashFlowCategory(projectId, c.id)
                        await query.refetch()
                      })
                  }}
                  cellCls={cell}
                />
                <EditRow
                  label="%"
                  color={GREEN}
                  total={(catPct[c.id] ?? []).reduce((a, b) => a + b, 0)}
                  values={catPct[c.id] ?? []}
                  onCommit={(m, v) => {
                    setCatPct((prev) => ({
                      ...prev,
                      [c.id]: (prev[c.id] ?? []).map((x, i) => (i === m ? v : x)),
                    }))
                    saveCell("CAT_PCT", c.id, m, v)
                  }}
                  cellCls={cell}
                  inputCls={numInput}
                />
              </React.Fragment>
            ))}
            <tr>
              <td className="border px-2 py-1" colSpan={mc + 2} />
            </tr>
            <CalcRow label="USCITE MESE" total={calc.uscite.reduce((a, b) => a + b, 0)} values={calc.uscite} color={YELLOW} />
            <CalcRow label="DIFFERENZA" total={calc.cumulative[mc - 1] ?? 0} values={calc.cumulative} color={YELLOW} />
            <EditRow
              label="BANCA"
              kind="money"
              total={bank.reduce((a, b) => a + b, 0)}
              values={bank}
              onCommit={(m, v) => {
                setBank((prev) => prev.map((x, i) => (i === m ? v : x)))
                saveCell("BANK", 0, m, v)
              }}
              cellCls={cell}
              inputCls={numInput}
            />
          </tbody>
        </table>
      </GridScroller>

      {/* Grafico */}
      <div className="rounded-lg border p-3">
        <ResponsiveContainer width="100%" height={240}>
          <ComposedChart data={chartData}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} />
            <XAxis dataKey="label" fontSize={9} angle={-45} textAnchor="end" height={50} />
            <YAxis fontSize={9} />
            <Tooltip formatter={(value) => euro(Number(value))} />
            <Bar dataKey="entrate" name="Entrate" fill="#059669" />
            <Bar dataKey="uscite" name="Uscite" fill="#DC2626" />
            <Line type="monotone" dataKey="saldo" name="Saldo cumulativo" stroke="#4F6EF7" strokeWidth={2} />
          </ComposedChart>
        </ResponsiveContainer>
      </div>
    </div>
  )
}

function CalcRow({
  label,
  total,
  values,
  color,
}: {
  label: string
  total: number
  values: number[]
  color: string
}) {
  return (
    <tr style={{ backgroundColor: color }}>
      <td className="border px-2 py-1 text-left font-medium">{label}</td>
      <td className="border px-2 py-1 text-right font-semibold tabular-nums">
        {euro(total)}
      </td>
      {values.map((v, i) => (
        <td key={i} className="border px-1 py-0.5 text-right tabular-nums">
          {n0(v)}
        </td>
      ))}
    </tr>
  )
}

function EditRow({
  label,
  total,
  values,
  onCommit,
  color,
  cellCls,
  inputCls,
  kind = "percent",
}: {
  label: string
  total: number
  values: number[]
  onCommit: (monthIndex: number, value: number) => void
  color?: string
  cellCls: string
  inputCls: string
  /** `money`: celle con `MoneyInput` e totale in €. `percent`: input semplice, mai il €. */
  kind?: "money" | "percent"
}) {
  // `MoneyInput` è controllato: serve una stringa per cella che segua `values`
  // (che cambia anche quando arrivano i dati dal server). Tiene il numero grezzo,
  // non il testo formattato: è `MoneyInput` a mostrarlo compatto quando è a riposo.
  const [text, setText] = React.useState<string[]>(() => values.map(String))
  React.useEffect(() => setText(values.map(String)), [values])

  return (
    <tr style={color ? { backgroundColor: color } : undefined}>
      <td className="border px-2 py-1 text-left text-muted-foreground">{label}</td>
      <td className="border px-2 py-1 text-right tabular-nums">
        {kind === "money" ? euro(total) : n0(total)}
      </td>
      {values.map((v, i) =>
        kind === "money" ? (
          <td key={i} className={cellCls}>
            <MoneyInput
              className={cellInput}
              format={n0}
              value={text[i] ?? ""}
              onChange={(s) =>
                setText((prev) => prev.map((x, k) => (k === i ? s : x)))
              }
              onCommit={() => {
                // Solo al blur, e solo se il valore è davvero cambiato: `saveCell`
                // parte a ogni commit.
                const next = parseDecimal(text[i] ?? "")
                if (next !== v) onCommit(i, next)
              }}
            />
          </td>
        ) : (
          <td key={i} className={cellCls}>
            <input
              className={inputCls}
              defaultValue={n0(v)}
              key={`${i}-${v}`}
              onBlur={(e) => onCommit(i, parseDecimal(e.target.value))}
              onKeyDown={(e) => {
                if (e.key === "Enter") (e.target as HTMLInputElement).blur()
              }}
            />
          </td>
        )
      )}
    </tr>
  )
}

function CategoryAmountRow({
  category,
  values,
  amount,
  onAmount,
  onLabel,
  onDelete,
  cellCls,
}: {
  category: CashFlowCategory
  values: number[]
  amount: number
  onAmount: (value: number) => void
  onLabel: (name: string) => void
  onDelete: () => void
  cellCls: string
}) {
  // Come in `EditRow`: il totale editabile passa a `MoneyInput`, che è controllato.
  const [amountText, setAmountText] = React.useState(() => String(amount))
  React.useEffect(() => setAmountText(String(amount)), [amount])

  return (
    <tr style={{ backgroundColor: YELLOW }}>
      <td className="border px-2 py-1 text-left">
        <div className="flex items-center gap-1">
          {category.isLinked ? (
            <span className="font-medium" title="Collegata a un robot">
              [M] {category.name}
            </span>
          ) : (
            <input
              className="flex-1 bg-transparent text-[11px] font-medium outline-none"
              defaultValue={category.name}
              key={category.name}
              onBlur={(e) => {
                if (e.target.value && e.target.value !== category.name)
                  onLabel(e.target.value)
              }}
            />
          )}
          {!category.isLinked ? (
            <button
              type="button"
              className="text-muted-foreground hover:text-destructive"
              onClick={onDelete}
              aria-label="Elimina"
            >
              <Trash2 className="size-3" />
            </button>
          ) : null}
        </div>
      </td>
      <td className="border px-1 py-0.5 text-right">
        {category.isLinked ? (
          // Categoria collegata a un robot: il totale arriva da lì, non si edita.
          <span className="font-semibold tabular-nums">{euro(amount)}</span>
        ) : (
          <MoneyInput
            className={cellInput}
            value={amountText}
            onChange={setAmountText}
            onCommit={() => {
              const next = parseDecimal(amountText)
              if (next !== amount) onAmount(next)
            }}
          />
        )}
      </td>
      {values.map((v, i) => (
        <td key={i} className={cellCls}>
          {n0(v)}
        </td>
      ))}
    </tr>
  )
}
