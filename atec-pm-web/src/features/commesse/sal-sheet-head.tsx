// ── Intestazione della tabella SAL (header a due righe, colonne opzionali) ──

import { CompactTableHead } from "@/components/shared/compact-table-head"
import { TableRow } from "@/components/ui/table"
import { cn } from "@/lib/utils"

import { SAL_COL, type SalColId } from "./sal-sheet-shared"

export function SalSheetHead({
  showCol,
  canSeeEconomics,
}: {
  showCol: (id: SalColId) => boolean
  canSeeEconomics: boolean
}) {
  return (
    <TableRow className="border-0 hover:bg-transparent">
      <CompactTableHead label="#" className={SAL_COL.num} align="center" />
      {canSeeEconomics && showCol("iva") && (
        <CompactTableHead label="IVA" className={cn(SAL_COL.iva, "pr-4")} align="right" />
      )}
      {showCol("ivaPerc") && (
        <CompactTableHead label={["%", "IVA"]} className={SAL_COL.ivaPerc} align="center" />
      )}
      {canSeeEconomics && showCol("totIva") && (
        <CompactTableHead
          label={["Tot.", "+ IVA"]}
          className={cn(SAL_COL.totIva, "pr-4")}
          align="right"
          title="Totale + IVA"
        />
      )}
      {showCol("dataSaldo") && (
        <CompactTableHead
          label={["Data Prev.", "Saldo"]}
          className={SAL_COL.dataSaldo}
          align="center"
          title="Data Prevista Saldo"
        />
      )}
      {showCol("ggSaldo") && (
        <CompactTableHead
          label={["GG.", "Saldo"]}
          className={SAL_COL.ggSaldo}
          align="center"
          title="GG. Saldo"
        />
      )}
      {showCol("step") && <CompactTableHead label="Step SAL" className={SAL_COL.step} />}
      {showCol("nFatt") && (
        <CompactTableHead
          label={["N°", "Fattura"]}
          className={SAL_COL.nFatt}
          title="N° Fattura"
        />
      )}
      {showCol("contoSap") && (
        <CompactTableHead
          label={["Conto", "SAP"]}
          className={SAL_COL.contoSap}
          title="Conto SAP"
        />
      )}
      {showCol("perc") && (
        <CompactTableHead label={["%", "SAL"]} className={SAL_COL.perc} align="center" />
      )}
      {showCol("condizione") && (
        <CompactTableHead
          label={["Condizioni", "Pagamento"]}
          className={SAL_COL.condizione}
          title="Condizioni Pagamento"
        />
      )}
      {canSeeEconomics && showCol("importo") && (
        <CompactTableHead
          label={["Importo", "Fattura"]}
          className={cn(SAL_COL.importo, "pr-4")}
          align="right"
          title="Importo Fattura"
        />
      )}
      {showCol("dataFatt") && (
        <CompactTableHead
          label={["Ipotesi", "Fatturazione"]}
          className={SAL_COL.dataFatt}
          align="center"
          title="Ipotesi Fatturazione"
        />
      )}
      {showCol("stato") && (
        <CompactTableHead
          label={["Stato", "Fatturazione"]}
          className={SAL_COL.stato}
          title="Stato Fatturazione"
        />
      )}
      {showCol("pagamento") && (
        <CompactTableHead label="Pagamento" className={SAL_COL.pagamento} />
      )}
      {showCol("dataIncasso") && (
        <CompactTableHead
          label={["Data", "Incasso"]}
          className={SAL_COL.dataIncasso}
          align="center"
          title="Data Incasso"
        />
      )}
      {showCol("note") && <CompactTableHead label="Note" className={SAL_COL.note} />}
      <CompactTableHead label="" className={SAL_COL.actions} />
    </TableRow>
  )
}
