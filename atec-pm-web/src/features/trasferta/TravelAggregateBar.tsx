// ── Aggrega costi Trasferta (#67) ──────────────────────────────────────────
// Barra sticky sopra le tabelle step: stessa grafica Alloggio/Vitto + Altri costi
// della griglia riga-persona. Deseleziona → Aggrega → Azzera, poi i campi.

import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Calculator } from "lucide-react"

import {
  CalcSheetDialog,
  type CalcSheetSection,
} from "@/components/shared/calc-sheet"
import { MoneyInput } from "@/components/shared/money-input"
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
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { applyTravelCosts } from "@/lib/api/travel"
import { fetchTariffOptions } from "@/lib/api/tariffs"
import type { ProjectCalcRowDto, TravelCalcKind } from "@/lib/api/types"
import { euro, parseDecimal } from "@/lib/format"
import { notifyError, notifySuccess } from "@/lib/toast"

type AggregateCalcKind = Extract<TravelCalcKind, "vitto" | "indennita" | "auto">

type CalcConfig = {
  title: string
  quantityLabel: string
  unitLabel: string
  amountLabel: string
  totalLabel: string
  tariffType: string
  multiplierLabel?: string
}

const CALC_CONFIGS: Record<AggregateCalcKind, CalcConfig> = {
  vitto: {
    title: "Calcolo Vitto / Diaria",
    quantityLabel: "Giorni",
    unitLabel: "Diaria",
    amountLabel: "Tot. Diaria",
    totalLabel: "Totale diaria",
    tariffType: "DAILY_FOOD",
  },
  indennita: {
    title: "Calcolo Indennità",
    quantityLabel: "Giorni",
    unitLabel: "Indennità",
    amountLabel: "Tot. Indennità",
    totalLabel: "Totale indennità",
    tariffType: "DAILY_ALLOWANCE",
  },
  auto: {
    title: "Calcolo Auto",
    quantityLabel: "Km Tratta",
    unitLabel: "Rimborso Km",
    amountLabel: "Costo",
    totalLabel: "Totale auto",
    tariffType: "COST_PER_KM",
    multiplierLabel: "Numero Tratte",
  },
}

function numOrNull(text: string): number | null {
  return text.trim() === "" ? null : parseDecimal(text)
}

function sumAmount(rows: ProjectCalcRowDto[]): number | null {
  let sum = 0
  let any = false
  for (const row of rows) {
    if (row.amount == null) continue
    sum += row.amount
    any = true
  }
  return any ? sum : null
}

/** Pulsante calcolatrice locale (non salva finché non si preme Aggrega). */
function AggregateCalcCell({
  value,
  title,
  onOpen,
}: {
  value: number | null
  title: string
  onOpen: () => void
}) {
  return (
    <TableCell className="text-right">
      <Tooltip>
        <TooltipTrigger asChild>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-8 w-full justify-end gap-1 px-2 font-normal tabular-nums"
            onClick={onOpen}
          >
            <Calculator className="size-3.5 shrink-0 text-muted-foreground" />
            {euro(value)}
          </Button>
        </TooltipTrigger>
        <TooltipContent>{title}</TooltipContent>
      </Tooltip>
    </TableCell>
  )
}

export function TravelAggregateBar({
  projectId,
  selectedIds,
  onDeselect,
  onApplied,
}: {
  projectId: number
  selectedIds: number[]
  onDeselect: () => void
  onApplied: () => void
}) {
  const [nights, setNights] = React.useState("")
  const [nightPrice, setNightPrice] = React.useState("")
  const [transportCost, setTransportCost] = React.useState("")
  const [mealCost, setMealCost] = React.useState<number | null>(null)
  const [allowanceCost, setAllowanceCost] = React.useState<number | null>(null)
  const [carCost, setCarCost] = React.useState<number | null>(null)
  const [mealRows, setMealRows] = React.useState<ProjectCalcRowDto[]>([])
  const [allowanceRows, setAllowanceRows] = React.useState<ProjectCalcRowDto[]>(
    []
  )
  const [carRows, setCarRows] = React.useState<ProjectCalcRowDto[]>([])
  const [calcKind, setCalcKind] = React.useState<AggregateCalcKind | null>(null)

  const nightsNum = nights.trim() === "" ? null : Math.round(parseDecimal(nights))
  const nightPriceNum = numOrNull(nightPrice)
  const transportNum = numOrNull(transportCost)
  const lodgingCost =
    nightsNum != null && nightPriceNum != null
      ? nightsNum * nightPriceNum
      : null

  function clearFields() {
    setNights("")
    setNightPrice("")
    setTransportCost("")
    setMealCost(null)
    setAllowanceCost(null)
    setCarCost(null)
    setMealRows([])
    setAllowanceRows([])
    setCarRows([])
    setCalcKind(null)
  }

  const applyMutation = useMutation({
    mutationFn: () => {
      const fields: string[] = []
      if (nights.trim() !== "") fields.push("nights")
      if (nightPrice.trim() !== "") fields.push("nightPrice")
      if (transportCost.trim() !== "") fields.push("transportCost")
      if (mealCost != null) fields.push("mealCost")
      if (allowanceCost != null) fields.push("allowanceCost")
      if (carCost != null) fields.push("carCost")

      return applyTravelCosts(projectId, {
        rowIds: selectedIds,
        nights: nightsNum,
        nightPrice: nightPriceNum,
        mealCost,
        allowanceCost,
        carCost,
        transportCost: transportNum,
        fields,
      })
    },
    onSuccess: (count) => {
      notifySuccess(
        count === 1
          ? "Costi aggregati su 1 riga"
          : `Costi aggregati su ${count} righe`
      )
      onApplied()
    },
    onError: (err: Error) => notifyError(err),
  })

  function handleAggrega() {
    if (selectedIds.length === 0) {
      notifyError(new Error("Seleziona almeno una riga di trasferta."))
      return
    }
    const hasAny =
      nights.trim() !== "" ||
      nightPrice.trim() !== "" ||
      transportCost.trim() !== "" ||
      mealCost != null ||
      allowanceCost != null ||
      carCost != null
    if (!hasAny) {
      notifyError(new Error("Imposta almeno un costo nella tabella Aggrega."))
      return
    }
    applyMutation.mutate()
  }

  // z-30, non z-20 (#100): le intestazioni fisse delle griglie step (grid-sticky-head)
  // stanno a z-20 e vengono DOPO nel DOM — a pari livello scorrerebbero sopra la barra.
  return (
    <div className="sticky top-0 z-30 space-y-2 rounded-lg border bg-card p-3 shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <p className="text-sm font-semibold">Aggrega costi commessa</p>
          <p className="text-xs text-muted-foreground">
            Compila i costi e applicali alle righe selezionate
            {selectedIds.length > 0
              ? ` (${selectedIds.length} selezionat${selectedIds.length === 1 ? "a" : "e"})`
              : ""}
            .
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={selectedIds.length === 0 || applyMutation.isPending}
            onClick={onDeselect}
          >
            Deseleziona
          </Button>
          <Button
            type="button"
            size="sm"
            disabled={applyMutation.isPending}
            onClick={handleAggrega}
          >
            Aggrega
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={applyMutation.isPending}
            onClick={clearFields}
          >
            Azzera
          </Button>
        </div>
      </div>

      <div className="overflow-x-auto rounded-md border">
        <Table className="w-max min-w-full">
          <TableHeader className="bg-muted/40">
            <TableRow className="hover:bg-transparent">
              <TableHead
                colSpan={4}
                className="border-x text-center text-xs font-semibold"
              >
                Alloggio / Vitto
              </TableHead>
              <TableHead
                colSpan={3}
                className="border-x text-center text-xs font-semibold"
              >
                Altri costi
              </TableHead>
            </TableRow>
            <TableRow className="hover:bg-transparent">
              <TableHead className="w-16 text-right text-xs">Notti</TableHead>
              <TableHead className="w-28 text-right text-xs">Prezzo</TableHead>
              <TableHead className="w-28 text-right text-xs">Costo</TableHead>
              <TableHead className="w-28 text-right text-xs">Vitto</TableHead>
              <TableHead className="w-28 text-right text-xs">Indennità</TableHead>
              <TableHead className="w-28 text-right text-xs">Auto</TableHead>
              <TableHead className="w-28 text-right text-xs">Treno/aereo</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow className="hover:bg-transparent">
              <TableCell>
                <Input
                  inputMode="numeric"
                  value={nights}
                  placeholder="0"
                  className="h-8 w-14 bg-background text-right"
                  onChange={(e) => setNights(e.target.value)}
                />
              </TableCell>
              <TableCell>
                <MoneyInput
                  value={nightPrice}
                  placeholder="0,00 €"
                  className="h-8 w-24 bg-background"
                  onChange={setNightPrice}
                />
              </TableCell>
              <TableCell className="text-right text-sm tabular-nums">
                {lodgingCost == null ? "—" : euro(lodgingCost)}
              </TableCell>
              <AggregateCalcCell
                value={mealCost}
                title="Calcolo della diaria (Giorni × Diaria)"
                onOpen={() => setCalcKind("vitto")}
              />
              <AggregateCalcCell
                value={allowanceCost}
                title="Calcolo dell'indennità (Giorni × Indennità)"
                onOpen={() => setCalcKind("indennita")}
              />
              <AggregateCalcCell
                value={carCost}
                title="Calcolo auto (Km × Rimborso × Numero Tratte)"
                onOpen={() => setCalcKind("auto")}
              />
              <TableCell>
                <MoneyInput
                  value={transportCost}
                  placeholder="0,00 €"
                  className="h-8 w-24 bg-background"
                  onChange={setTransportCost}
                />
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>

      {calcKind ? (
        <AggregateLocalCalcDialog
          kind={calcKind}
          rows={
            calcKind === "vitto"
              ? mealRows
              : calcKind === "indennita"
                ? allowanceRows
                : carRows
          }
          onClose={() => setCalcKind(null)}
          onConfirm={(rows) => {
            const total = sumAmount(rows)
            if (calcKind === "vitto") {
              setMealRows(rows)
              setMealCost(total)
            } else if (calcKind === "indennita") {
              setAllowanceRows(rows)
              setAllowanceCost(total)
            } else {
              setCarRows(rows)
              setCarCost(total)
            }
            setCalcKind(null)
          }}
        />
      ) : null}
    </div>
  )
}

/** Calcolatrice solo locale: conferma scrive il totale nella barra, non sul server. */
function AggregateLocalCalcDialog({
  kind,
  rows,
  onClose,
  onConfirm,
}: {
  kind: AggregateCalcKind
  rows: ProjectCalcRowDto[]
  onClose: () => void
  onConfirm: (rows: ProjectCalcRowDto[]) => void
}) {
  const config = CALC_CONFIGS[kind]
  const tariffsQuery = useQuery({
    queryKey: ["tariff-options", config.tariffType],
    queryFn: () => fetchTariffOptions(config.tariffType),
  })

  const sections: CalcSheetSection[] = React.useMemo(() => {
    const columns: CalcSheetSection["columns"] = config.multiplierLabel
      ? ["quantity", "unitCost", "multiplier", "amount"]
      : ["quantity", "unitCost", "amount"]
    return [
      {
        key: "",
        heading: "",
        totalLabel: config.totalLabel,
        addLabel: "Aggiungi riga",
        hideDescription: true,
        valueKind: "money",
        columns,
        labels: {
          quantity: config.quantityLabel,
          unitCost: config.unitLabel,
          multiplier: config.multiplierLabel,
          amount: config.amountLabel,
        },
        unitCostOptions: {
          values: (tariffsQuery.data ?? []).map((t) => t.value),
        },
      },
    ]
  }, [config, tariffsQuery.data])

  return (
    <CalcSheetDialog
      open
      title={config.title}
      intro="Aggrega costi commessa · il totale si applica con Aggrega"
      sections={sections}
      rows={rows}
      grandTotalLabel={config.totalLabel}
      defaultMarkup={1}
      saving={false}
      onClose={onClose}
      onConfirm={onConfirm}
    />
  )
}
