// ── Calcolo «Lavorazioni Officine» (lato Costi Preventivati) ───────────────
//
// È l'unica delle 4 voci del Riepilogo Costi senza una struttura che la alimenti: le
// altre tre vengono da risorse pianificate, materiali e trasferta del preventivo, mentre
// le lavorazioni a preventivo erano semplicemente vuote («—», blocco 4).
//
// Due sezioni, come nel prototipo V32 — «Officine esterne» e «Officine interne» — che nel
// Riepilogo restano una voce sola, pari alla loro somma.
//
// D4 (decisa il 04/08/2026): il «k correzione» del prototipo NON si adotta. In ATEC PM il
// 45% è un ricarico di VENDITA, scritto come moltiplicatore 1,450 come per le risorse:
//  - la colonna che alimenta il Riepilogo Costi è il COSTO, senza maggiorazione;
//  - la vendita è una colonna a parte e finisce nel costo netto della Scheda Prezzi.
// Chi confronta i numeri con il prototipo li troverà diversi: è voluto.

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
import { saveProjectCalcSheet } from "@/lib/api/project-bva"
import { fetchTariffOptions } from "@/lib/api/tariffs"
import {
  CALC_GROUP_EXTERNAL,
  CALC_GROUP_INTERNAL,
  CALC_KEY_WORKSHOP_BUDGET,
  type ProjectCalcRowDto,
  type ProjectCalcSheetDto,
} from "@/lib/api/types"
import { notifyError, notifySuccess } from "@/lib/toast"

const HOURLY_RATE = "HOURLY_RATE"

export function WorkshopCalcDialog({
  open,
  projectId,
  sheet,
  onClose,
  onSaved,
}: {
  open: boolean
  projectId: number
  sheet: ProjectCalcSheetDto
  onClose: () => void
  onSaved: () => void
}) {
  const [tariffsOpen, setTariffsOpen] = React.useState(false)

  // Tariffe orarie dedicate alle Officine interne: nel prototipo erano una lista a parte
  // (di default 40 e 50 €/ora), qui sono il tipo HOURLY_RATE dell'anagrafica tariffe.
  const tariffsQuery = useQuery({
    queryKey: ["tariff-options", HOURLY_RATE],
    queryFn: () => fetchTariffOptions(HOURLY_RATE),
    enabled: open,
  })

  const saveMutation = useMutation({
    mutationFn: (rows: ProjectCalcRowDto[]) =>
      saveProjectCalcSheet(projectId, CALC_KEY_WORKSHOP_BUDGET, {
        rowVersion: sheet.rowVersion,
        rows,
      }),
    onSuccess: () => {
      notifySuccess("Calcolo salvato")
      onSaved()
    },
    onError: (err: Error) => notifyError(err),
  })

  const sections: CalcSheetSection[] = React.useMemo(
    () => [
      {
        key: CALC_GROUP_EXTERNAL,
        heading: "Officine esterne",
        totalLabel: "Totale officine esterne",
        addLabel: "Riga esterna",
        // Il costo è già l'importo della riga: niente ore, niente colonna calcolata.
        columns: ["unitCost", "markup", "sale"],
        labels: { unitCost: "Costo" },
      },
      {
        key: CALC_GROUP_INTERNAL,
        heading: "Officine interne",
        totalLabel: "Totale officine interne",
        addLabel: "Riga interna",
        columns: ["quantity", "unitCost", "amount", "markup", "sale"],
        labels: { quantity: "Ore", unitCost: "Costo orario", amount: "Costo" },
        unitCostOptions: {
          values: (tariffsQuery.data ?? []).map((t) => t.value),
          // Dal #87 le tariffe orarie sono tre (meccanica, carpenteria, stampa 3D): senza
          // il nome, in tendina resterebbero tre importi da indovinare.
          labelOf: (value) =>
            (tariffsQuery.data ?? []).find((t) => t.value === value)?.label || undefined,
          manageLabel: "Gestisci tariffe orarie…",
          onManage: () => setTariffsOpen(true),
        },
      },
    ],
    [tariffsQuery.data]
  )

  return (
    <>
      <CalcSheetDialog
        open={open}
        title="Calcolo Lavorazioni Officine"
        intro="Lavorazioni Officine · Costi Preventivati. Nelle interne il costo è Ore × Costo orario; lasciando vuote le Ore, il costo vale come importo della riga."
        sections={sections}
        rows={sheet.rows}
        grandTotalLabel="Totale Lavorazioni Officine"
        saving={saveMutation.isPending}
        onClose={onClose}
        onConfirm={(rows) => saveMutation.mutate(rows)}
      />

      <Dialog open={tariffsOpen} onOpenChange={(next) => !next && setTariffsOpen(false)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Tariffe orarie</DialogTitle>
            <DialogDescription>
              Costo orario proposto per le Officine interne
            </DialogDescription>
          </DialogHeader>
          <TariffOptionsPanel types={[HOURLY_RATE]} />
          <DialogFooter>
            <Button onClick={() => setTariffsOpen(false)}>Chiudi</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
