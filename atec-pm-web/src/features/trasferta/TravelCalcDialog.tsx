// ── Le 4 calcolatrici della riga-persona (blocco 6) ────────────────────────
//
// Sono la stessa finestra del blocco 5 con quattro configurazioni diverse — nel prototipo
// era il `CALC_CFG`. Quello che ATEC PM aggiunge rispetto al prototipo è che i valori
// (diaria, indennità, €/km) arrivano dall'anagrafica tariffe invece che da una lista
// salvata nel browser.
//
// La calcolatrice Ore NON è in euro: somma ore. È il motivo per cui il componente ha una
// modalità numerica — mostrare «6,00 €» al posto di «6» sarebbe sbagliato, non solo brutto.

import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import {
  CalcSheetDialog,
  type CalcSheetSection,
} from "@/components/shared/calc-sheet"
import { fetchTravelRowCalc, saveTravelRowCalc } from "@/lib/api/travel"
import { fetchTariffOptions } from "@/lib/api/tariffs"
import type { ProjectCalcRowDto, TravelCalcKind } from "@/lib/api/types"
import { notifyError, notifySuccess } from "@/lib/toast"

type CalcConfig = {
  title: string
  /** Etichette delle colonne, alla lettera dal prototipo V32. */
  quantityLabel: string
  unitLabel: string
  amountLabel: string
  totalLabel: string
  money: boolean
  /** Tipo di `tariff_options` che alimenta il picker della colonna valore. */
  tariffType?: string
  /** Terza colonna moltiplicatore (solo Auto: Km × Rimborso × Numero Tratte). */
  multiplierLabel?: string
  /** La somma dei Giorni si confronta con i Giorni trasferta della riga. */
  checksDays: boolean
}

const CONFIGS: Record<TravelCalcKind, CalcConfig> = {
  ore: {
    title: "Calcolo Ore Trasferta",
    quantityLabel: "Giorni",
    unitLabel: "Ore Lav.",
    amountLabel: "Ore Tot.",
    totalLabel: "Totale ore",
    money: false,
    checksDays: true,
  },
  vitto: {
    title: "Calcolo Vitto / Diaria",
    quantityLabel: "Giorni",
    unitLabel: "Diaria",
    amountLabel: "Tot. Diaria",
    totalLabel: "Totale diaria",
    money: true,
    tariffType: "DAILY_FOOD",
    checksDays: true,
  },
  indennita: {
    title: "Calcolo Indennità",
    quantityLabel: "Giorni",
    unitLabel: "Indennità",
    amountLabel: "Tot. Indennità",
    totalLabel: "Totale indennità",
    money: true,
    tariffType: "DAILY_ALLOWANCE",
    checksDays: true,
  },
  auto: {
    title: "Calcolo Auto",
    quantityLabel: "Km Tratta",
    unitLabel: "Rimborso Km",
    amountLabel: "Costo",
    totalLabel: "Totale auto",
    money: true,
    tariffType: "COST_PER_KM",
    multiplierLabel: "Numero Tratte",
    checksDays: false,
  },
}

export function TravelCalcDialog({
  projectId,
  rowId,
  personName,
  kind,
  expectedDays,
  onClose,
  onSaved,
}: {
  projectId: number
  /** null = finestra chiusa. */
  rowId: number | null
  personName: string
  kind: TravelCalcKind
  /** Giorni trasferta della riga, per il controllo di coerenza. */
  expectedDays: number | null
  onClose: () => void
  onSaved: () => void
}) {
  const open = rowId != null
  const config = CONFIGS[kind]

  const sheetQuery = useQuery({
    queryKey: ["travel-calc", projectId, rowId, kind],
    queryFn: () => fetchTravelRowCalc(projectId, rowId!, kind),
    enabled: open,
  })

  const tariffsQuery = useQuery({
    queryKey: ["tariff-options", config.tariffType],
    queryFn: () => fetchTariffOptions(config.tariffType),
    enabled: open && !!config.tariffType,
  })

  const saveMutation = useMutation({
    mutationFn: (rows: ProjectCalcRowDto[]) =>
      saveTravelRowCalc(projectId, rowId!, kind, {
        rowVersion: sheetQuery.data?.rowVersion ?? null,
        rows,
      }),
    onSuccess: () => {
      notifySuccess("Calcolo salvato")
      onSaved()
    },
    onError: (err: Error) => notifyError(err),
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
        valueKind: config.money ? "money" : "plain",
        columns,
        labels: {
          quantity: config.quantityLabel,
          unitCost: config.unitLabel,
          multiplier: config.multiplierLabel,
          amount: config.amountLabel,
        },
        unitCostOptions: config.tariffType
          ? { values: (tariffsQuery.data ?? []).map((t) => t.value) }
          : undefined,
      },
    ]
  }, [config, tariffsQuery.data])

  // Finché il foglio non è arrivato la finestra non si apre: aprirla vuota e poi
  // riempirla farebbe perdere la copia di lavoro (che nasce all'apertura).
  if (!open || !sheetQuery.data) return null

  return (
    <CalcSheetDialog
      open
      title={config.title}
      intro={personName ? `${config.title} · ${personName}` : undefined}
      sections={sections}
      rows={sheetQuery.data.rows}
      grandTotalLabel={config.totalLabel}
      // Il ricarico non c'entra nulla qui: le colonne non lo mostrano e 1,000 lo lascia
      // fuori dai conti.
      defaultMarkup={1}
      check={
        config.checksDays
          ? {
              column: "quantity",
              expected: expectedDays,
              subject: "Giorni del calcolo",
              reference: "i Giorni trasferta della riga",
            }
          : undefined
      }
      saving={saveMutation.isPending}
      onClose={onClose}
      onConfirm={(rows) => saveMutation.mutate(rows)}
    />
  )
}
