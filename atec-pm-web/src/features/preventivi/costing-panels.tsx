// ── Scheda prezzi e tabella di distribuzione del prezzo ────────────────────

import * as React from "react"
import { Wand2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { saveDistributionsBatch, updatePricing } from "@/lib/api/quote-costing"
import type {
  ProjectCostSectionDto,
  ProjectMaterialItemDto,
  ProjectPricingDto,
} from "@/lib/api/types"
import { notifyError } from "@/lib/toast"

import type { DistComputed, DistRow } from "./costing-distribution"
import { computeDist, distKey } from "./costing-distribution"
import { fmt2, parseDecimal } from "@/lib/format"

export function PricingPanel({
  quoteId,
  readOnly,
  net,
  resourceSale,
  materialSale,
  travelSale,
  pricing,
  onChanged,
}: {
  quoteId: number
  readOnly: boolean
  net: number
  resourceSale: number
  materialSale: number
  travelSale: number
  pricing: ProjectPricingDto
  onChanged: () => void
}) {
  const [cont, setCont] = React.useState((pricing.contingencyPct * 100).toFixed(1))
  const [margin, setMargin] = React.useState((pricing.negotiationMarginPct * 100).toFixed(1))

  const contFrac = parseDecimal(cont) / 100
  const marginFrac = parseDecimal(margin) / 100
  const contingencyAmount = net * contFrac
  const offer = net + contingencyAmount
  const marginAmount = offer * marginFrac
  const final = offer + marginAmount

  function savePricing() {
    if (readOnly) return
    void updatePricing(quoteId, {
      ...pricing,
      contingencyPct: contFrac,
      negotiationMarginPct: marginFrac,
    }).then(onChanged)
  }

  const Row = ({ label, value, strong, color }: { label: string; value: string; strong?: boolean; color?: string }) => (
    <div className="flex items-center justify-between gap-6">
      <span className={strong ? "font-bold" : "text-muted-foreground"} style={color ? { color } : undefined}>{label}</span>
      <span className={strong ? "font-bold tabular-nums" : "tabular-nums"} style={color ? { color } : undefined}>{value}</span>
    </div>
  )

  return (
    <div className="rounded-md border p-4">
      <div className="flex flex-wrap items-start justify-between gap-6">
        <div className="space-y-3">
          <h3 className="font-bold">Scheda prezzi</h3>
          <div className="flex items-center gap-2 text-sm">
            <Label className="w-40">Contingency %</Label>
            <Input className="h-8 w-24 text-right" value={cont} readOnly={readOnly} onChange={(e) => setCont(e.target.value)} onBlur={savePricing} />
          </div>
          <div className="flex items-center gap-2 text-sm">
            <Label className="w-40">Margine trattativa %</Label>
            <Input className="h-8 w-24 text-right" value={margin} readOnly={readOnly} onChange={(e) => setMargin(e.target.value)} onBlur={savePricing} />
          </div>
        </div>

        <div className="w-80 space-y-1 text-sm">
          <Row label="Vendita risorse" value={`${fmt2(resourceSale)} €`} />
          <Row label="Vendita materiali" value={`${fmt2(materialSale)} €`} />
          <Row label="Trasferte" value={`${fmt2(travelSale)} €`} />
          <div className="border-t pt-1">
            <Row label="PREZZO NETTO" value={`${fmt2(net)} €`} strong />
          </div>
          <Row label={`Contingency (${cont}%)`} value={`${fmt2(contingencyAmount)} €`} />
          <Row label="PREZZO OFFERTA" value={`${fmt2(offer)} €`} strong color="#2563EB" />
          <Row label={`Margine (${margin}%)`} value={`${fmt2(marginAmount)} €`} />
          <div className="border-t pt-1">
            <Row label="OFFERTA FINALE" value={`${fmt2(final)} €`} strong color="#059669" />
          </div>
        </div>
      </div>
    </div>
  )
}

export function DistributionPanel({
  quoteId,
  readOnly,
  sections,
  materialItems,
  contPool,
  margPool,
}: {
  quoteId: number
  readOnly: boolean
  sections: ProjectCostSectionDto[]
  materialItems: ProjectMaterialItemDto[]
  contPool: number
  margPool: number
}) {
  const [rows, setRows] = React.useState<DistRow[]>([])

  // Firma dei dati sorgente: re-init quando cambiano vendite/pin/shadow.
  const sig = React.useMemo(() => {
    const part = (
      p: string,
      id: number,
      sale: number,
      cp: number,
      mp: number,
      cpin: boolean,
      mpin: boolean,
      sh: boolean
    ) => `${p}${id}:${sale}:${cp}:${mp}:${cpin ? 1 : 0}:${mpin ? 1 : 0}:${sh ? 1 : 0}`
    const secs = sections
      .filter((s) => s.isEnabled && s.totalSale > 0)
      .map((s) => part("R", s.id, s.totalSale, s.contingencyPct, s.marginPct, s.contingencyPinned, s.marginPinned, s.isShadowed))
    const mats = materialItems
      .filter((i) => i.totalSale > 0)
      .map((i) => part("M", i.id, i.totalSale, i.contingencyPct, i.marginPct, i.contingencyPinned, i.marginPinned, i.isShadowed))
    return [...secs, ...mats].join("|")
  }, [sections, materialItems])

  React.useEffect(() => {
    const raw: DistRow[] = []
    const seen = new Set<string>()
    for (const s of sections) {
      if (!s.isEnabled || s.totalSale <= 0) continue
      const nameKey = (s.name || "").trim().toLowerCase()
      if (seen.has(nameKey)) continue
      seen.add(nameKey)
      raw.push({
        rowType: "R", sectionId: s.id, itemId: 0, name: s.name, sale: s.totalSale,
        contingencyPct: s.contingencyPct, marginPct: s.marginPct,
        contingencyPinned: s.contingencyPinned, marginPinned: s.marginPinned, isShadowed: s.isShadowed,
      })
    }
    for (const i of materialItems) {
      if (i.totalSale <= 0) continue
      raw.push({
        rowType: "M", sectionId: 0, itemId: i.id, name: i.description, sale: i.totalSale,
        contingencyPct: i.contingencyPct, marginPct: i.marginPct,
        contingencyPinned: i.contingencyPinned, marginPinned: i.marginPinned, isShadowed: i.isShadowed,
      })
    }
    setRows(raw)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sig])

  const computed = React.useMemo(
    () => computeDist(rows, contPool, margPool),
    [rows, contPool, margPool]
  )

  function persist(next: DistRow[]) {
    setRows(next)
    const c = computeDist(next, contPool, margPool)
    const toReq = (r: DistComputed, id: number) => ({
      id,
      contingencyPct: r.contingencyPct,
      marginPct: r.marginPct,
      contingencyPinned: r.contingencyPinned,
      marginPinned: r.marginPinned,
      isShadowed: r.isShadowed,
    })
    void saveDistributionsBatch(quoteId, {
      sections: c.filter((r) => r.rowType === "R").map((r) => toReq(r, r.sectionId)),
      materialItems: c.filter((r) => r.rowType === "M").map((r) => toReq(r, r.itemId)),
    }).catch((err: Error) => notifyError(err))
  }

  function editPct(row: DistComputed, isCont: boolean, percentStr: string) {
    if (readOnly) return
    let val = parseDecimal(percentStr) / 100
    const otherPinned = rows
      .filter((r) => distKey(r) !== distKey(row) && (isCont ? r.contingencyPinned : r.marginPinned))
      .reduce((a, r) => a + (isCont ? r.contingencyPct : r.marginPct), 0)
    val = Math.min(val, Math.max(0, 1 - otherPinned))
    const next = rows.map((r) => {
      if (distKey(r) !== distKey(row)) return r
      return isCont
        ? { ...r, contingencyPct: val, contingencyPinned: true }
        : { ...r, marginPct: val, marginPinned: true }
    })
    persist(next)
  }

  function toggleShadow(row: DistComputed) {
    if (readOnly) return
    persist(rows.map((r) => (distKey(r) === distKey(row) ? { ...r, isShadowed: !r.isShadowed } : r)))
  }

  function redistribute() {
    if (readOnly) return
    persist(rows.map((r) => ({ ...r, contingencyPinned: false, marginPinned: false })))
  }

  if (computed.length === 0) return null

  const totalCont = computed.reduce((a, r) => a + r.contingencyAmount, 0)
  const totalMarg = computed.reduce((a, r) => a + r.marginAmount, 0)
  const totalClient = computed.reduce((a, r) => a + r.sectionTotal, 0)

  const PctCell = ({ row, isCont }: { row: DistComputed; isCont: boolean }) => {
    const pct = isCont ? row.contingencyPct : row.marginPct
    const pinned = isCont ? row.contingencyPinned : row.marginPinned
    if (row.isShadowed) return <span>—</span>
    return (
      <span className="inline-flex items-center justify-end gap-1">
        {pinned ? <span title="Bloccato">🔒</span> : null}
        <input
          key={`${isCont ? "c" : "m"}${distKey(row)}:${pct}`}
          className="h-6 w-14 rounded border bg-background px-1 text-right"
          defaultValue={(pct * 100).toFixed(2)}
          readOnly={readOnly}
          onBlur={(e) => editPct(row, isCont, e.target.value)}
        />
      </span>
    )
  }

  return (
    <div className="rounded-md border">
      <div className="flex items-center justify-between border-b bg-muted/40 px-3 py-2">
        <h3 className="font-bold">Distribuzione prezzo</h3>
        {!readOnly ? (
          <Button size="sm" variant="outline" onClick={redistribute}>
            <Wand2 className="size-4" /> Ridistribuisci
          </Button>
        ) : null}
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead>
            <tr className="border-b bg-muted/20 text-[10px] font-bold text-muted-foreground">
              <th className="px-2 py-1 text-left">SEZIONE</th>
              <th className="px-2 py-1 text-right">VENDITA</th>
              <th className="px-2 py-1 text-right">CONT. %</th>
              <th className="px-2 py-1 text-right">CONT. €</th>
              <th className="px-2 py-1 text-right">MARG. %</th>
              <th className="px-2 py-1 text-right">MARG. €</th>
              <th className="px-2 py-1 text-right">PREZZO CL.</th>
              <th className="w-8" />
            </tr>
          </thead>
          <tbody>
            {computed.map((r) => (
              <tr
                key={distKey(r)}
                className="border-b last:border-b-0"
                style={r.isShadowed ? { backgroundColor: "#FEF2F2", opacity: 0.6 } : undefined}
              >
                <td className="px-2 py-1">
                  <span
                    className="mr-1 rounded px-1 py-0.5 text-[9px] font-bold text-white"
                    style={{ backgroundColor: r.rowType === "M" ? "#7C3AED" : "#2563EB" }}
                  >
                    {r.rowType}
                  </span>
                  {r.name}
                </td>
                <td className="px-2 py-1 text-right tabular-nums">{r.isShadowed ? "—" : `${fmt2(r.sale)}€`}</td>
                <td className="px-2 py-1 text-right"><PctCell row={r} isCont /></td>
                <td className="px-2 py-1 text-right tabular-nums">{r.isShadowed ? "—" : `${fmt2(r.contingencyAmount)}€`}</td>
                <td className="px-2 py-1 text-right"><PctCell row={r} isCont={false} /></td>
                <td className="px-2 py-1 text-right tabular-nums">{r.isShadowed ? "—" : `${fmt2(r.marginAmount)}€`}</td>
                <td className="px-2 py-1 text-right font-semibold tabular-nums">{r.isShadowed ? "—" : `${fmt2(r.sectionTotal)}€`}</td>
                <td className="px-1 py-1 text-center">
                  {!readOnly ? (
                    <button type="button" title={r.isShadowed ? "Includi" : "Escludi"} onClick={() => toggleShadow(r)}>
                      {r.isShadowed ? "🙈" : "👁"}
                    </button>
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="border-t font-bold">
              <td className="px-2 py-1">TOTALE</td>
              <td />
              <td />
              <td className="px-2 py-1 text-right tabular-nums">{fmt2(totalCont)}€</td>
              <td />
              <td className="px-2 py-1 text-right tabular-nums">{fmt2(totalMarg)}€</td>
              <td className="px-2 py-1 text-right tabular-nums text-[#059669]">{fmt2(totalClient)}€</td>
              <td />
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  )
}
