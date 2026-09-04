// ── Le 3 calcolatrici della trasferta di preventivo (segnalazione #33) ─────
//
// Stessa finestra riusabile della Gestione Trasferta (`CalcSheetDialog`), con tre
// configurazioni invece di quattro: le Ore non ci sono, perché nel preventivo ore, €/h e
// K stanno già nella griglia «Risorse pianificate».
//
// D33-A: il K di trasferta NON entra qui dentro. `defaultMarkup={1}` lascia il ricarico
// fuori dal calcolo, che resta un COSTO; il K si applica dopo, sul totale di sezione,
// e solo sulle spese ricaricabili (mai sull'indennità).
//
// Rispetto alla Trasferta c'è in più «Gestisci tariffe…» sotto la tendina dei valori,
// come nel calcolo Lavorazioni Officine: la diaria giusta si aggiunge senza uscire
// dalla finestra e senza andare in Configurazione.

import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import {
  CalcSheetDialog,
  type CalcSheetSection,
} from "@/components/shared/calc-sheet"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { TariffOptionsPanel } from "@/features/admin/config-sections/TariffOptionsPanel"
import {
  fetchCostTravelCalc,
  saveCostTravelCalc,
  type CostTravelCalcKind,
} from "@/lib/api/project-costing-travel"
import { fetchTariffOptions } from "@/lib/api/tariffs"
import type { ProjectCalcRowDto } from "@/lib/api/types"
import { notifyError, notifySuccess } from "@/lib/toast"

type CalcConfig = {
  title: string
  /** Etichette delle colonne, le stesse della Gestione Trasferta. */
  quantityLabel: string
  unitLabel: string
  amountLabel: string
  totalLabel: string
  /** Tipo di `tariff_options` che alimenta il picker della colonna valore. */
  tariffType: string
  tariffTitle: string
  tariffHint: string
  /** Terza colonna moltiplicatore (solo Auto: Km × Rimborso × Numero Tratte). */
  multiplierLabel?: string
  /** La somma dei Giorni si confronta con i giorni della risorsa collegata. */
  checksDays: boolean
}

const CONFIGS: Record<CostTravelCalcKind, CalcConfig> = {
  vitto: {
    title: "Calcolo Vitto / Diaria",
    quantityLabel: "Giorni",
    unitLabel: "Diaria",
    amountLabel: "Tot. Diaria",
    totalLabel: "Totale diaria",
    tariffType: "DAILY_FOOD",
    tariffTitle: "Diarie vitto",
    tariffHint: "Importo giornaliero proposto per il vitto",
    checksDays: true,
  },
  indennita: {
    title: "Calcolo Indennità",
    quantityLabel: "Giorni",
    unitLabel: "Indennità",
    amountLabel: "Tot. Indennità",
    totalLabel: "Totale indennità",
    tariffType: "DAILY_ALLOWANCE",
    tariffTitle: "Indennità giornaliere",
    tariffHint: "Importo giornaliero proposto per l'indennità",
    checksDays: true,
  },
  auto: {
    title: "Calcolo Auto",
    quantityLabel: "Km Tratta",
    unitLabel: "Rimborso Km",
    amountLabel: "Costo",
    totalLabel: "Totale auto",
    tariffType: "COST_PER_KM",
    tariffTitle: "Rimborsi chilometrici",
    tariffHint: "Importo al km proposto per l'auto",
    multiplierLabel: "Numero Tratte",
    checksDays: false,
  },
}

export function PreventivoTravelCalcDialog({
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
  kind: CostTravelCalcKind
  /**
   * Giorni della risorsa pianificata collegata, per il controllo di coerenza.
   * null quando la riga è a testo libero: il controllo si spegne invece di mentire.
   */
  expectedDays: number | null
  onClose: () => void
  onSaved: () => void
}) {
  const open = rowId != null
  const config = CONFIGS[kind]
  const [tariffsOpen, setTariffsOpen] = React.useState(false)

  const sheetQuery = useQuery({
    queryKey: ["cost-travel-calc", projectId, rowId, kind],
    queryFn: () => fetchCostTravelCalc(projectId, rowId!, kind),
    enabled: open,
  })

  const tariffsQuery = useQuery({
    queryKey: ["tariff-options", config.tariffType],
    queryFn: () => fetchTariffOptions(config.tariffType),
    enabled: open,
  })

  const saveMutation = useMutation({
    mutationFn: (rows: ProjectCalcRowDto[]) =>
      saveCostTravelCalc(projectId, rowId!, kind, {
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
          manageLabel: "Gestisci tariffe…",
          onManage: () => setTariffsOpen(true),
        },
      },
    ]
  }, [config, tariffsQuery.data])

  // Finché il foglio non è arrivato la finestra non si apre: aprirla vuota e poi
  // riempirla farebbe perdere la copia di lavoro (che nasce all'apertura).
  if (!open || !sheetQuery.data) return null

  return (
    <>
      <CalcSheetDialog
        open
        title={config.title}
        intro={
          personName
            ? `${config.title} · ${personName} · costo di preventivo, senza ricarico`
            : `${config.title} · costo di preventivo, senza ricarico`
        }
        sections={sections}
        rows={sheetQuery.data.rows}
        grandTotalLabel={config.totalLabel}
        // Il K di trasferta si applica dopo, sul totale di sezione: qui dentro
        // non deve comparire né incidere (D33-A).
        defaultMarkup={1}
        check={
          config.checksDays
            ? {
                column: "quantity",
                expected: expectedDays,
                subject: "Giorni del calcolo",
                reference: "i giorni della risorsa collegata",
              }
            : undefined
        }
        saving={saveMutation.isPending}
        onClose={onClose}
        onConfirm={(rows) => saveMutation.mutate(rows)}
      />

      <Dialog open={tariffsOpen} onOpenChange={(next) => !next && setTariffsOpen(false)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>{config.tariffTitle}</DialogTitle>
            <DialogDescription>{config.tariffHint}</DialogDescription>
          </DialogHeader>
          <TariffOptionsPanel types={[config.tariffType]} />
          <DialogFooter>
            <Button onClick={() => setTariffsOpen(false)}>Chiudi</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
