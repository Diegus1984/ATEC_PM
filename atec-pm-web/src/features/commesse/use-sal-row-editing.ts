// ── Editing di una riga SAL: bozze locali, commit con clamp, eliminazione ──
// Ogni campo testuale ha una bozza locale (si scrive senza round-trip) che al
// blur viene normalizzata e inviata; il valore dal server la risincronizza.

import * as React from "react"

import type { useConfirm } from "@/components/shared/confirm"
import { deleteSalRow, updateSalRow } from "@/lib/api/sal"
import type { SalRow, SalRowSaveRequest } from "@/lib/api/types"
import { notifyError } from "@/lib/toast"

import { salGgSaldoValue } from "./sal-utils"
import { salRowPayload } from "./sal-sheet-shared"

/**
 * #130 — la % SAL tiene fino a 10 decimali (colonna `sal_rows.perc` DECIMAL(13,10)).
 *
 * Non è precisione per sport: la percentuale è quello che serve per far uscire
 * l'importo di fattura concordato col cliente (8,13278177% di 142.625,00 € =
 * 11.599,38 €). Prima qui si arrotondava a 1 decimale e la fattura ballava di euro.
 *
 * `String()` toglie da solo gli zeri in coda che MySQL restituisce (8,1327817700 →
 * 8.13278177) e non passa mai alla notazione esponenziale in questo intervallo.
 */
export const SAL_PERC_DECIMALI = 10

function formatSalPercInput(n: number | null | undefined): string {
  if (n == null || isNaN(n)) return ""
  // Nessun clamp qui: quello che c'è scritto sulla riga si mostra com'è, anche se
  // fuori scala — altrimenti un dato sbagliato diventa invisibile invece che evidente.
  return String(troncaSalPerc(n))
}

/** Arrotondamento alle 10 cifre decimali che la colonna sa tenere. */
function troncaSalPerc(n: number): number {
  const factor = 10 ** SAL_PERC_DECIMALI
  return Math.round(n * factor) / factor
}

export function useSalRowEditing({
  row,
  index,
  onMutated,
  onConfirm,
}: {
  row: SalRow
  index: number
  onMutated: () => void
  onConfirm: ReturnType<typeof useConfirm>
}) {
  const [stepText, setStepText] = React.useState(row.step)
  // == null (e non ===): se il server omette il campo, row.perc è undefined.
  const [percText, setPercText] = React.useState(formatSalPercInput(row.perc))
  const [ivaText, setIvaText] = React.useState(
    row.ivaPerc === null ? "" : String(row.ivaPerc)
  )
  const [ggText, setGgText] = React.useState(String(salGgSaldoValue(row.ggSaldo)))
  const [nFattText, setNFattText] = React.useState(row.nFatt ?? "")
  const [noteText, setNoteText] = React.useState(row.note ?? "")

  React.useEffect(() => {
    setStepText(row.step)
  }, [row.step])

  React.useEffect(() => {
    setPercText(formatSalPercInput(row.perc))
  }, [row.perc])

  React.useEffect(() => {
    setIvaText(row.ivaPerc === null ? "" : String(row.ivaPerc))
  }, [row.ivaPerc])

  React.useEffect(() => {
    setGgText(String(salGgSaldoValue(row.ggSaldo)))
  }, [row.ggSaldo])

  React.useEffect(() => {
    setNFattText(row.nFatt ?? "")
  }, [row.nFatt])

  React.useEffect(() => {
    setNoteText(row.note ?? "")
  }, [row.note])

  const patch = async (fields: Partial<SalRowSaveRequest>) => {
    try {
      await updateSalRow(row.id, salRowPayload(row, fields))
      onMutated()
    } catch (err) {
      notifyError(err as Error)
      onMutated()
    }
  }

  const commitStep = () => {
    const val = stepText.trim()
    if (val === row.step) return
    void patch({ step: val })
  }

  const commitPerc = () => {
    const val = percText.trim()
    if (val === "") {
      if (row.perc !== null) void patch({ perc: null })
      return
    }
    let n = parseFloat(val.replace(",", "."))
    if (isNaN(n)) {
      setPercText(formatSalPercInput(row.perc))
      return
    }
    n = troncaSalPerc(Math.max(0, Math.min(100, n)))
    if (n !== row.perc) void patch({ perc: n })
    else setPercText(formatSalPercInput(n))
  }

  const commitIva = () => {
    const val = ivaText.trim()
    if (val === "") {
      if (row.ivaPerc !== null) void patch({ ivaPerc: null })
      return
    }
    let n = parseInt(val, 10)
    if (isNaN(n)) {
      setIvaText(row.ivaPerc === null ? "" : String(row.ivaPerc))
      return
    }
    n = Math.max(0, Math.min(100, n))
    if (n !== row.ivaPerc) void patch({ ivaPerc: n })
    else setIvaText(String(n))
  }

  const commitGgSaldo = () => {
    const val = ggText.trim()
    let n = val === "" ? 0 : parseInt(val, 10)
    if (isNaN(n)) {
      setGgText(String(salGgSaldoValue(row.ggSaldo)))
      return
    }
    n = Math.max(0, Math.min(3650, n))
    if (n !== salGgSaldoValue(row.ggSaldo)) void patch({ ggSaldo: n })
    else setGgText(String(n))
  }

  /** GG saldo effettivo in UI: testo in editing → anteprima live di Data prev. saldo. */
  const ggSaldoEffective = React.useMemo(() => {
    const val = ggText.trim()
    if (val === "") return 0
    const n = parseInt(val, 10)
    if (isNaN(n)) return salGgSaldoValue(row.ggSaldo)
    return Math.max(0, Math.min(3650, n))
  }, [ggText, row.ggSaldo])

  const handleGgChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const v = e.target.value
    const prevParsed = parseInt(ggText, 10)
    const prev = Number.isNaN(prevParsed) ? salGgSaldoValue(row.ggSaldo) : prevParsed
    const next = v === "" ? 0 : parseInt(v, 10)
    setGgText(v)
    if (!Number.isNaN(next)) {
      const clamped = Math.max(0, Math.min(3650, next))
      // Spinner / frecce: incremento unitario → salva subito come la modifica manuale al blur
      if (Math.abs(clamped - prev) === 1 && clamped !== salGgSaldoValue(row.ggSaldo)) {
        void patch({ ggSaldo: clamped })
      }
    }
  }

  const commitNFatt = () => {
    const val = nFattText.trim()
    if (val === (row.nFatt ?? "")) return
    void patch({ nFatt: val })
  }

  const commitNote = () => {
    const val = noteText
    if (val === (row.note ?? "")) return
    void patch({ note: val })
  }

  /** Elimina lo step; chiede conferma solo se la riga ha davvero dei dati dentro. */
  const removeRow = async () => {
    const hasData =
      row.step.trim() !== "" ||
      row.perc !== null ||
      row.condizione !== "" ||
      row.dataFatt !== null ||
      row.stato !== "" ||
      salGgSaldoValue(row.ggSaldo) !== 0 ||
      (row.nFatt ?? "") !== "" ||
      (row.contoSap ?? "") !== "" ||
      (row.pagamento ?? "") !== "" ||
      row.dataPagamento !== null ||
      (row.note ?? "") !== ""
    if (hasData) {
      const ok = await onConfirm({
        title: "Eliminare lo step SAL?",
        description: row.step.trim() || `Step ${index + 1}`,
        confirmLabel: "Elimina",
      })
      if (!ok) return
    }

    try {
      await deleteSalRow(row.id, row.rowVersion)
      onMutated()
    } catch (err) {
      notifyError(err as Error)
      onMutated()
    }
  }

  return {
    stepText,
    setStepText,
    percText,
    setPercText,
    ivaText,
    setIvaText,
    ggText,
    nFattText,
    setNFattText,
    noteText,
    setNoteText,
    ggSaldoEffective,
    patch,
    commitStep,
    commitPerc,
    commitIva,
    commitGgSaldo,
    handleGgChange,
    commitNFatt,
    commitNote,
    removeRow,
  }
}
