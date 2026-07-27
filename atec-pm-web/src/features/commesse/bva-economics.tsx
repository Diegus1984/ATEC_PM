// ── Conto economico e scheda prezzi del Preventivo vs Consuntivo ───────────

import * as React from "react"
import { useMutation } from "@tanstack/react-query"

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
import { updateActualTravelCost, updateProjectRevenue } from "@/lib/api/project-bva"
import { updateProjectPricing } from "@/lib/api/project-costing"
import type {
  BvaEconomicSummary,
  BvaPricingDto,
  ProjectPricingDto,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { notifyError } from "@/lib/toast"

import { Kpi } from "./bva-shared"

function pct(value: number): string {
  return `${value.toFixed(1)}%`
}

/** Dialog di modifica di un singolo valore economico (order price / trasferta). */
function EconomicEditDialog({
  open,
  title,
  label,
  initialValue,
  onClose,
  onSave,
}: {
  open: boolean
  title: string
  label: string
  initialValue: number
  onClose: () => void
  onSave: (value: number) => Promise<void>
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
    const parsed = Number(text.replace(",", "."))
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
          <Input
            inputMode="decimal"
            value={text}
            autoFocus
            disabled={saving}
            onChange={(event) => setText(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !saving) void handleSave()
            }}
          />
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
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

export function EconomicSummary({
  economic,
  projectId,
  onSaved,
}: {
  economic: BvaEconomicSummary
  projectId: number
  onSaved: () => void
}) {
  const [editing, setEditing] = React.useState<"order" | "travel" | null>(null)
  const profitAccent =
    economic.profitabilityPct < 0 ? "text-destructive" : "text-emerald-600"
  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Kpi
          label="Prezzo offerta finale"
          value={euro(economic.finalOfferPrice)}
        />
        <Kpi
          label="Order price"
          value={euro(economic.orderPrice)}
          onEdit={() => setEditing("order")}
        />
        <Kpi
          label="Budget costi"
          value={euro(economic.budgetCost)}
          hint={`Risorse ${euro(economic.budgetResourceCost)} · Mat. ${euro(
            economic.budgetMaterialCost
          )} · Trasf. ${euro(economic.budgetTravelCost)}`}
        />
        <Kpi
          label="Consuntivo costi"
          value={euro(economic.actualTotalCost)}
          hint={`Risorse ${euro(economic.actualResourceCost)} · Mat. ${euro(
            economic.actualMaterialCost
          )} · Trasf. ${euro(economic.actualTravelCost)}`}
          onEdit={() => setEditing("travel")}
        />
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Kpi
          label="Redditività"
          value={pct(economic.profitabilityPct)}
          accent={profitAccent}
        />
        <Kpi label="Avanzamento" value={`${economic.progressPct}%`} />
        <Kpi label="Tecnici attivi" value={String(economic.activeTechnicians)} />
        <Kpi
          label="Fasi completate"
          value={`${economic.completedPhases}/${economic.totalPhases}`}
        />
      </div>

      <EconomicEditDialog
        open={editing === "order"}
        title="Order price"
        label="Prezzo d'ordine (€)"
        initialValue={economic.orderPrice}
        onClose={() => setEditing(null)}
        onSave={async (value) => {
          await updateProjectRevenue(projectId, value)
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
          await updateActualTravelCost(projectId, value)
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
    mutationFn: (patch: { contingencyPct?: number; negotiationMarginPct?: number }) => {
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
          <span>Costo netto {euro(pricing.netCost)}</span>
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

      {canEditBudget && (
        <div className="grid gap-4 sm:grid-cols-2 max-w-xl rounded-md border p-3 bg-muted/10">
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
