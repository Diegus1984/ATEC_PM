// ── Conto economico e scheda prezzi del Preventivo vs Consuntivo ───────────

import * as React from "react"
import { useMutation } from "@tanstack/react-query"

import { MoneyInput } from "@/components/shared/money-input"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { num } from "@/features/commesse/preventivo-dialogs"
import {
  updateActualTravelCost,
  updateFinalPriceOverride,
} from "@/lib/api/project-bva"
import { updateProjectPricing } from "@/lib/api/project-costing"
import type {
  BvaEconomicSummary,
  BvaPricingDto,
  ProjectPricingDto,
} from "@/lib/api/types"
import { euro, percent as pct } from "@/lib/format"
import { notifyError } from "@/lib/toast"

import { Kpi } from "./bva-shared"

/** Dialog di modifica di un singolo valore economico (order price / trasferta). */
function EconomicEditDialog({
  open,
  title,
  label,
  initialValue,
  emptyHint,
  onClose,
  onSave,
}: {
  open: boolean
  title: string
  label: string
  initialValue: number
  /**
   * Se c'è, il campo può essere svuotato e `onSave` riceve `null`. Serve al «Prezzo offerta
   * finale» (#35), dove svuotare significa «torna al valore calcolato dalla Scheda Prezzi»:
   * senza questa via d'uscita, chi imputa un prezzo a mano una volta non può più tornare
   * indietro se non scrivendo a memoria il numero che c'era prima.
   */
  emptyHint?: string
  onClose: () => void
  onSave: (value: number | null) => Promise<void>
}) {
  const [text, setText] = React.useState(String(initialValue))
  const [error, setError] = React.useState<string | null>(null)
  const [saving, setSaving] = React.useState(false)
  const wasOpen = React.useRef(false)

  React.useEffect(() => {
    if (open && !wasOpen.current) {
      setText(String(initialValue))
      setError(null)
      setSaving(false)
    }
    wasOpen.current = open
  }, [open, initialValue])

  async function handleSave() {
    if (saving) return
    const trimmed = text.trim()
    if (trimmed === "" && emptyHint) {
      setSaving(true)
      try {
        await onSave(null)
      } catch (err) {
        setError((err as Error).message)
      } finally {
        setSaving(false)
      }
      return
    }
    const parsed = Number(trimmed.replace(",", "."))
    if (!Number.isFinite(parsed) || parsed < 0) {
      setError("Inserisci un importo valido (≥ 0).")
      return
    }
    setSaving(true)
    try {
      await onSave(parsed)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>
        <div className="grid gap-2">
          <Label>{label}</Label>
          <MoneyInput
            value={text}
            autoFocus
            disabled={saving}
            onChange={setText}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !saving) void handleSave()
            }}
          />
          {error ? (
            <p className="text-sm text-destructive">{error}</p>
          ) : emptyHint ? (
            <p className="text-xs text-muted-foreground">{emptyHint}</p>
          ) : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button onClick={() => void handleSave()} disabled={saving}>
            {saving ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/**
 * I sei riquadri del Conto Economico.
 *
 * Segnalazione #35 (riquadri + tooltip) e #45 (tooltip sintetici: solo titolo, formula
 * e cifre — niente commenti). Una sola funzione perché Dashboard e Bilancio devono
 * dire la stessa cosa sugli stessi numeri.
 */
export interface EconomicKpiSource {
  finalOfferPrice: number
  /** true = imputato a mano dal PM, non derivato dalla Scheda Prezzi. */
  finalOfferPriceIsManual?: boolean
  orderPrice: number
  saleTotal: number | null
  orderDelta: number | null
  budgetCost: number
  budgetResourceCost: number
  budgetMaterialCost: number
  budgetWorkshopCost: number
  budgetTravelCost: number
  actualTotalCost: number
  actualResourceCost: number
  actualMaterialCost: number
  actualWorkshopCost: number
  actualTravelCost: number
  actualTravelFromPlan: boolean
  budgetProfitability: number
  budgetProfitabilityPct: number
  profitability: number
  profitabilityPct: number
}

interface EconomicKpi {
  label: string
  value: string
  hint?: string
  accent?: string
  explain: React.ReactNode
}

/** Rosso sotto zero, verde sopra: vale per entrambe le redditività. */
function profitAccentOf(pctValue: number): string {
  return pctValue < 0 ? "text-destructive" : "text-emerald-600"
}

/**
 * Popup del Conto Economico (#45): titolo · formula · cifre. Niente commenti,
 * domande o ipotesi — solo da dove esce il numero (stile degli allegati di Paolo).
 */
function CalcExplain({
  title,
  formula,
  calc,
}: {
  title: string
  formula: string
  calc: React.ReactNode
}) {
  return (
    <div className="space-y-1 text-left">
      <p className="text-[10px] font-semibold uppercase tracking-wide text-blue-600">
        {title}
      </p>
      <p className="text-muted-foreground">{formula}</p>
      <p className="font-medium tabular-nums text-blue-700">{calc}</p>
    </div>
  )
}

export function economicKpis(e: EconomicKpiSource): EconomicKpi[] {
  const hasOrder = e.orderPrice > 0

  const costiPrevParts = [
    `risorse ${euro(e.budgetResourceCost)}`,
    `materiali ${euro(e.budgetMaterialCost)}`,
    ...(e.budgetWorkshopCost !== 0
      ? [`officine ${euro(e.budgetWorkshopCost)}`]
      : []),
    `trasferta ${euro(e.budgetTravelCost)}`,
  ]
  const costiConsParts = [
    `risorse ${euro(e.actualResourceCost)}`,
    `materiali ${euro(e.actualMaterialCost)}`,
    `officine ${euro(e.actualWorkshopCost)}`,
    `trasferta ${euro(e.actualTravelCost)}`,
  ]

  return [
    {
      label: "Prezzo offerta finale",
      value: euro(e.finalOfferPrice),
      hint: e.finalOfferPriceIsManual ? "imputato a mano" : "dalla Scheda Prezzi",
      explain: e.finalOfferPriceIsManual ? (
        <CalcExplain
          title="Prezzo offerta finale"
          formula="Imputato a mano"
          calc={euro(e.finalOfferPrice)}
        />
      ) : (
        <CalcExplain
          title="Prezzo offerta finale"
          formula="Scheda Prezzi · ultima riga"
          calc={euro(e.finalOfferPrice)}
        />
      ),
    },
    {
      label: "Totale Ordine",
      value: euro(e.orderPrice),
      hint:
        e.saleTotal != null
          ? `Vendita ${euro(e.saleTotal)} · Margine ${euro(e.orderDelta)}`
          : "dalla tabella Ordine Commessa",
      explain: (
        <CalcExplain
          title="Totale Ordine"
          formula="Somma righe · Ordine Commessa"
          calc={euro(e.orderPrice)}
        />
      ),
    },
    {
      label: "Totale Costi",
      value: euro(e.budgetCost),
      explain: (
        <CalcExplain
          title="Calcolo Totale Costi · preventivati"
          formula={costiPrevParts.join(" + ")}
          calc={`= ${euro(e.budgetCost)}`}
        />
      ),
    },
    {
      label: "Consuntivo Costi",
      value: euro(e.actualTotalCost),
      explain: (
        <CalcExplain
          title="Calcolo Consuntivo Costi"
          formula={costiConsParts.join(" + ")}
          calc={`= ${euro(e.actualTotalCost)}`}
        />
      ),
    },
    {
      label: "Redditività Teorica Commessa",
      value: hasOrder
        ? `${euro(e.budgetProfitability)} · ${pct(e.budgetProfitabilityPct)}`
        : "—",
      accent: hasOrder ? profitAccentOf(e.budgetProfitabilityPct) : undefined,
      explain: hasOrder ? (
        <div className="space-y-3">
          <CalcExplain
            title="Calcolo Redditività · costi preventivati"
            formula="Totale Ordine – Totale Costi"
            calc={`${euro(e.orderPrice)} − ${euro(e.budgetCost)} = ${euro(e.budgetProfitability)}`}
          />
          <CalcExplain
            title="Calcolo % Redditività · costi preventivati"
            formula="(Totale Ordine − Totale Costi) ÷ Totale Ordine × 100"
            calc={`(${euro(e.orderPrice)} − ${euro(e.budgetCost)}) ÷ ${euro(e.orderPrice)} × 100 = ${pct(e.budgetProfitabilityPct)}`}
          />
        </div>
      ) : (
        <CalcExplain
          title="Redditività teorica"
          formula="Totale Ordine mancante"
          calc="—"
        />
      ),
    },
    {
      label: "Redditività Effettiva Commessa",
      value: hasOrder ? `${euro(e.profitability)} · ${pct(e.profitabilityPct)}` : "—",
      accent: hasOrder ? profitAccentOf(e.profitabilityPct) : undefined,
      explain: hasOrder ? (
        <div className="space-y-3">
          <CalcExplain
            title="Calcolo Redditività · costi consuntivati"
            formula="Totale Ordine – Totale Costi"
            calc={`${euro(e.orderPrice)} − ${euro(e.actualTotalCost)} = ${euro(e.profitability)}`}
          />
          <CalcExplain
            title="Calcolo % Redditività · costi consuntivati"
            formula="(Totale Ordine − Totale Costi) ÷ Totale Ordine × 100"
            calc={`(${euro(e.orderPrice)} − ${euro(e.actualTotalCost)}) ÷ ${euro(e.orderPrice)} × 100 = ${pct(e.profitabilityPct)}`}
          />
        </div>
      ) : (
        <CalcExplain
          title="Redditività effettiva"
          formula="Totale Ordine mancante"
          calc="—"
        />
      ),
    },
  ]
}

export function EconomicSummary({
  economic,
  projectId,
  onSaved,
}: {
  economic: BvaEconomicSummary
  projectId: number
  onSaved: () => void
}) {
  const [editing, setEditing] = React.useState<"travel" | "offer" | null>(null)
  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {economicKpis(economic).map((kpi) => (
          <Kpi
            key={kpi.label}
            label={kpi.label}
            value={kpi.value}
            hint={kpi.hint}
            accent={kpi.accent}
            explain={kpi.explain}
            onEdit={
              kpi.label === "Prezzo offerta finale"
                ? () => setEditing("offer")
                : kpi.label === "Consuntivo Costi" && !economic.actualTravelFromPlan
                  ? () => setEditing("travel")
                  : undefined
            }
          />
        ))}
      </div>

      <EconomicEditDialog
        open={editing === "offer"}
        title="Prezzo offerta finale"
        label="Prezzo concordato con il cliente (€)"
        initialValue={economic.finalOfferPrice}
        emptyHint="Lascia il campo vuoto per tornare al prezzo calcolato dalla Scheda Prezzi."
        onClose={() => setEditing(null)}
        onSave={async (value) => {
          await updateFinalPriceOverride(projectId, value)
          setEditing(null)
          onSaved()
        }}
      />

      <EconomicEditDialog
        open={editing === "travel"}
        title="Trasferta a consuntivo"
        label="Costo trasferta consuntivo (€)"
        initialValue={economic.actualTravelCost}
        onClose={() => setEditing(null)}
        onSave={async (value) => {
          // Questo dialogo non ha `emptyHint`, quindi `value` è sempre un numero: il ?? 0
          // è solo per il tipo, non un caso raggiungibile.
          await updateActualTravelCost(projectId, value ?? 0)
          setEditing(null)
          onSaved()
        }}
      />
    </div>
  )
}

export function PricingBlock({
  projectId,
  pricing,
  costingPricing,
  canEditBudget,
  onChanged,
}: {
  projectId: number
  pricing: BvaPricingDto
  costingPricing?: ProjectPricingDto | null
  canEditBudget: boolean
  onChanged: () => void
}) {
  const [contingency, setContingency] = React.useState(String(pricing.contingencyPct * 100))
  const [negotiation, setNegotiation] = React.useState(String(pricing.negotiationPct * 100))

  React.useEffect(() => {
    setContingency(String(pricing.contingencyPct * 100))
    setNegotiation(String(pricing.negotiationPct * 100))
  }, [pricing.contingencyPct, pricing.negotiationPct])

  const saveMutation = useMutation({
    mutationFn: (patch: {
      contingencyPct?: number
      negotiationMarginPct?: number
    }) => {
      if (!costingPricing) return Promise.resolve()
      return updateProjectPricing(projectId, {
        ...costingPricing,
        ...patch,
      })
    },
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  return (
    <div className="space-y-4">
      <div className="text-sm tabular-nums">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-muted-foreground">
          {/* Segnalazione #36: «Costo netto» si chiama «Totale Costi di Vendita».
              È lo stesso importo della voce omonima dell'Ordine Commessa (somma
              delle colonne Vendita), non un costo netto: il nome vecchio si leggeva
              come il contrario di quello che è. */}
          <span>Totale Costi di Vendita {euro(pricing.netCost)}</span>
          <span>→ + Contingency {euro(pricing.contingencyAmount)}</span>
          <span className="text-foreground">
            = Offerta {euro(pricing.offerPrice)}
          </span>
          <span>→ + Margine {euro(pricing.negotiationAmount)}</span>
          <span className="font-semibold text-foreground">
            = Finale {euro(pricing.finalPrice)}
          </span>
        </div>
      </div>

      {/* Il campo «K trasferta» stava qui, come terza colonna. Rimosso il 06/08/2026 su
          decisione di Paolo (#34): le trasferte non si ricaricano più, restano a costo
          netto. La colonna `project_pricing.travel_markup` esiste ancora, forzata a 1: se
          il ricarico serve di nuovo, si rimette questo campo e si toglie il vincolo. */}
      {canEditBudget && (
        <div className="grid gap-4 sm:grid-cols-2 max-w-2xl rounded-md border p-3 bg-muted/10">
          <div className="space-y-1.5">
            <Label className="text-xs">Imprevisti / contingency (%)</Label>
            <Input
              value={contingency}
              inputMode="decimal"
              className="h-9 text-right font-mono tabular-nums bg-background"
              onChange={(e) => setContingency(e.target.value)}
              onBlur={() =>
                saveMutation.mutate({ contingencyPct: num(contingency) / 100 })
              }
              disabled={saveMutation.isPending || !costingPricing}
            />
          </div>
          <div className="space-y-1.5">
            <Label className="text-xs">Margine di trattativa (%)</Label>
            <Input
              value={negotiation}
              inputMode="decimal"
              className="h-9 text-right font-mono tabular-nums bg-background"
              onChange={(e) => setNegotiation(e.target.value)}
              onBlur={() =>
                saveMutation.mutate({ negotiationMarginPct: num(negotiation) / 100 })
              }
              disabled={saveMutation.isPending || !costingPricing}
            />
          </div>
        </div>
      )}
    </div>
  )
}
