// ── Riga del foglio SAL: celle editabili, drag handle, lock, azioni ────────

import * as React from "react"
import { useQueryClient } from "@tanstack/react-query"
import {
  ArrowDownToLine,
  ArrowUpToLine,
  GripVertical,
  Lock,
  Trash2,
} from "lucide-react"

import type { useConfirm } from "@/components/shared/confirm"
import { DateField, ReadonlyDateField } from "@/components/shared/date-field"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { TableCell, TableRow } from "@/components/ui/table"
import {
  createPaymentState,
  createSalCondition,
  createSapCausale,
} from "@/lib/api/sal"
import type { SalCondition, SalRow } from "@/lib/api/types"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"

import { AnagraficaSelect, GrowTextarea } from "./sal-sheet-fields"
import {
  focusNextRowField,
  SAL_COL,
  type DropHint,
  type SalColId,
} from "./sal-sheet-shared"
import {
  salDataSaldo,
  salIncassoScaduto,
  salIsPagata,
} from "./sal-utils"
import { useSalRowEditing } from "./use-sal-row-editing"

export interface SalRowComponentProps {
  row: SalRow
  index: number
  rowBg: string
  /** Tinta inline dal colore configurato dello stato pagamento (vince su rowBg). */
  rowStyle: React.CSSProperties | undefined
  isDragging: boolean
  isDropOver: boolean
  dropHint: DropHint | null
  importo: number | null
  todayIso: string
  activeConditions: SalCondition[]
  sapCausali: SalCondition[]
  paymentStates: SalCondition[]
  onMutated: () => void
  onInsert: (where: "above" | "below") => void
  onConfirm: ReturnType<typeof useConfirm>
  grabId: number | null
  setGrabId: (id: number | null) => void
  setDraggingId: (id: number | null) => void
  setDropHint: (hint: DropHint | null) => void
  dragIdRef: React.MutableRefObject<number | null>
  handleReorder: (dragId: number, targetId: number, after: boolean) => Promise<void>
  clearDrag: () => void
  canSeeEconomics: boolean
  isAdmin: boolean
  /** Visibilità dal menu «Colonne» del foglio. */
  showCol: (id: SalColId) => boolean
}

export function SalRowComponent({
  row,
  index,
  rowBg,
  rowStyle,
  isDragging,
  isDropOver,
  dropHint,
  importo,
  todayIso,
  activeConditions,
  sapCausali,
  paymentStates,
  onMutated,
  onInsert,
  onConfirm,
  grabId,
  setGrabId,
  setDraggingId,
  setDropHint,
  dragIdRef,
  handleReorder,
  clearDrag,
  canSeeEconomics,
  isAdmin,
  showCol,
}: SalRowComponentProps) {
  // Query client locale: serve alle onAdd delle tendine anagrafica per
  // invalidare le liste attive dopo la creazione inline di una voce.
  const queryClient = useQueryClient()

  // Lock v10 (D6): la riga si blocca per i non-ADMIN quando il PAGAMENTO è «Pagata»
  const isLocked = salIsPagata(row.pagamento) && !isAdmin

  const {
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
  } = useSalRowEditing({ row, index, onMutated, onConfirm })

  const onDragStart = (e: React.DragEvent) => {
    dragIdRef.current = row.id
    setDraggingId(row.id)
    e.dataTransfer.effectAllowed = "move"
    e.dataTransfer.setData("text/plain", "")
  }

  const onDragOver = (e: React.DragEvent) => {
    e.preventDefault()
    const dragId = dragIdRef.current
    if (dragId === null || dragId === row.id) return

    const rect = e.currentTarget.getBoundingClientRect()
    const relativeY = e.clientY - rect.top
    const after = relativeY > rect.height / 2
    setDropHint({ id: row.id, after })
  }

  const onDragLeave = () => {
    setDropHint(null)
  }

  const onDrop = (e: React.DragEvent) => {
    e.preventDefault()
    const dragId = dragIdRef.current
    if (dragId === null || dragId === row.id || !dropHint) return
    void handleReorder(dragId, row.id, dropHint.after)
    clearDrag()
  }

  // Derivate client v10 (D4): iva = importo × %IVA/100, totIva = importo + iva,
  // dataSaldo = data fattura + gg saldo (mai persistite)
  const iva = importo !== null ? importo * ((row.ivaPerc ?? 0) / 100) : null
  const totIva = importo !== null && iva !== null ? importo + iva : null
  const dataSaldo = salDataSaldo(row.dataFatt, ggSaldoEffective)
  const incassoScaduto = salIncassoScaduto(
    { pagamento: row.pagamento, dataFatt: row.dataFatt, ggSaldo: ggSaldoEffective },
    todayIso
  )

  // Handler Enter per gli input a riempimento verticale (Excel v10)
  const enterDown = (col: string) => (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      e.preventDefault()
      focusNextRowField(e.currentTarget, col, index)
    }
  }

  return (
    <TableRow
      draggable={!isLocked && grabId === row.id}
      onDragStart={onDragStart}
      onDragEnd={clearDrag}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      // Colore configurato dello stato pagamento (se presente): pura estetica,
      // la semantica (lock, incasso, warning) resta cablata su «Pagata»/«Parz.»
      style={rowStyle}
      className={cn(
        "border-0 transition-colors",
        rowBg,
        isDragging && "opacity-50",
        isDropOver && !dropHint?.after && "[&>td]:shadow-[inset_0_2px_0_0_var(--primary)]",
        isDropOver && dropHint?.after && "[&>td]:shadow-[inset_0_-2px_0_0_var(--primary)]"
      )}
    >
      {/* # — drag handle / lock */}
      <TableCell className={cn(SAL_COL.num, "py-2 align-middle")}>
        <div className="flex items-center gap-1">
          {isLocked ? (
            <span
              className="inline-flex size-6 items-center justify-center rounded text-muted-foreground/40"
              title="Fattura pagata (sola lettura per non-ADMIN)"
            >
              <Lock className="size-3.5" />
            </span>
          ) : (
            <span
              role="button"
              aria-label="Trascina per riordinare"
              title="Trascina per riordinare"
              className="inline-flex size-6 cursor-grab items-center justify-center rounded text-muted-foreground/60 hover:bg-muted hover:text-foreground active:cursor-grabbing"
              onMouseDown={() => setGrabId(row.id)}
              onMouseUp={() => setGrabId(null)}
            >
              <GripVertical className="size-3.5" />
            </span>
          )}
          <span className="text-sm text-muted-foreground">
            {String(index + 1).padStart(2, "0")}
          </span>
        </div>
      </TableCell>

      {/* IVA (calcolata) */}
      {canSeeEconomics && showCol("iva") && (
        <TableCell className={cn(SAL_COL.iva, "py-2 align-top text-right pr-4")}>
          <div className="flex h-10 items-center justify-end text-sm font-semibold text-foreground">
            {euro(iva)}
          </div>
        </TableCell>
      )}

      {/* % IVA (input int) */}
      {showCol("ivaPerc") && (
        <TableCell className={cn(SAL_COL.ivaPerc, "py-2 align-top")}>
          <div className="flex items-center gap-1.5 h-10">
            <Input
              value={ivaText}
              onChange={(e) => setIvaText(e.target.value)}
              onBlur={commitIva}
              onKeyDown={enterDown("ivaPerc")}
              data-sal-col="ivaPerc"
              data-sal-row={index}
              inputMode="numeric"
              placeholder="22"
              className="h-10 w-14 px-1 text-center text-sm shadow-none bg-white dark:bg-zinc-950 border-zinc-200"
              disabled={isLocked}
            />
            <span className="text-sm text-muted-foreground font-semibold">%</span>
          </div>
        </TableCell>
      )}

      {/* Tot. + IVA (calcolato) */}
      {canSeeEconomics && showCol("totIva") && (
        <TableCell className={cn(SAL_COL.totIva, "py-2 align-top text-right pr-4")}>
          <div className="flex h-10 items-center justify-end text-sm font-semibold text-foreground">
            {euro(totIva)}
          </div>
        </TableCell>
      )}

      {/* Data Prevista Saldo (calcolata: data fattura + gg saldo) */}
      {showCol("dataSaldo") && (
        <TableCell className={cn(SAL_COL.dataSaldo, "py-2 align-top text-center")}>
          <ReadonlyDateField
            value={dataSaldo}
            size="sm"
            stackedWeekday
            className={cn(
              "bg-muted/30 border-zinc-200 shadow-none",
              incassoScaduto && "border-rose-300 bg-rose-50/50 text-rose-700 font-semibold"
            )}
            placeholder="—"
          />
        </TableCell>
      )}

      {/* GG. Saldo (input int ≥ 0, default 0) */}
      {showCol("ggSaldo") && (
        <TableCell className={cn(SAL_COL.ggSaldo, "py-2 align-top")}>
          <Input
            type="number"
            value={ggText}
            onChange={handleGgChange}
            onBlur={commitGgSaldo}
            onKeyDown={enterDown("ggSaldo")}
            data-sal-col="ggSaldo"
            data-sal-row={index}
            min={0}
            max={3650}
            step={1}
            placeholder="0"
            className="h-10 w-full px-1 text-center text-sm shadow-none bg-white dark:bg-zinc-950 border-zinc-200 [appearance:auto] [&::-webkit-inner-spin-button]:opacity-100 [&::-webkit-outer-spin-button]:opacity-100"
            disabled={isLocked}
          />
        </TableCell>
      )}

      {/* Step SAL */}
      {showCol("step") && (
        <TableCell className={cn(SAL_COL.step, "py-2 align-top")}>
          <GrowTextarea
            value={stepText}
            onChange={setStepText}
            onCommit={commitStep}
            placeholder="Descrizione step di pagamento (es. Acconto all'ordine...)"
            className="bg-white dark:bg-zinc-950 border-zinc-200 text-sm"
            disabled={isLocked}
            dataCol="step"
            dataRow={index}
          />
        </TableCell>
      )}

      {/* N° Fattura (solo cifre) */}
      {showCol("nFatt") && (
        <TableCell className={cn(SAL_COL.nFatt, "py-2 align-top")}>
          <Input
            value={nFattText}
            onChange={(e) => setNFattText(e.target.value.replace(/\D/g, ""))}
            onBlur={commitNFatt}
            onKeyDown={enterDown("nFatt")}
            data-sal-col="nFatt"
            data-sal-row={index}
            inputMode="numeric"
            placeholder="—"
            className="h-10 w-full px-2 text-sm shadow-none bg-white dark:bg-zinc-950 border-zinc-200"
            disabled={isLocked}
          />
        </TableCell>
      )}

      {/* Conto SAP (anagrafica causali) */}
      {showCol("contoSap") && (
        <TableCell className={cn(SAL_COL.contoSap, "py-2 align-top")}>
          <AnagraficaSelect
            value={row.contoSap ?? ""}
            options={sapCausali}
            emptyLabel="—"
            onChange={(v) => void patch({ contoSap: v })}
            showManage={isAdmin}
            disabled={isLocked}
            onAdd={async (label) => {
              await createSapCausale(label)
              // Prefisso ["sal","sap-causali"] = tendine attive; "sal-sap-causali" = pagina Anagrafiche
              await queryClient.invalidateQueries({ queryKey: ["sal", "sap-causali"] })
              await queryClient.invalidateQueries({ queryKey: ["sal-sap-causali"] })
              return label
            }}
          />
        </TableCell>
      )}

      {/* % SAL */}
      {showCol("perc") && (
        <TableCell className={cn(SAL_COL.perc, "py-2 align-top")}>
          <div className="flex items-center gap-1.5 h-10">
            <Input
              value={percText}
              onChange={(e) => setPercText(e.target.value)}
              onBlur={commitPerc}
              onKeyDown={enterDown("perc")}
              data-sal-col="perc"
              data-sal-row={index}
              placeholder="0.0"
              className="h-10 w-16 px-1 text-center text-sm shadow-none bg-white dark:bg-zinc-950 border-zinc-200"
              disabled={isLocked}
            />
            <span className="text-sm text-muted-foreground font-semibold">%</span>
          </div>
        </TableCell>
      )}

      {/* Condizioni pagamento */}
      {showCol("condizione") && (
        <TableCell className={cn(SAL_COL.condizione, "py-2 align-top")}>
          <AnagraficaSelect
            value={row.condizione ?? ""}
            options={activeConditions}
            emptyLabel="—"
            onChange={(v) => void patch({ condizione: v })}
            showManage={isAdmin}
            disabled={isLocked}
            onAdd={async (label) => {
              await createSalCondition({ label })
              await queryClient.invalidateQueries({ queryKey: ["sal", "conditions"] })
              await queryClient.invalidateQueries({ queryKey: ["sal-conditions"] })
              return label
            }}
          />
        </TableCell>
      )}

      {/* Importo Fattura (calcolato) */}
      {canSeeEconomics && showCol("importo") && (
        <TableCell className={cn(SAL_COL.importo, "py-2 align-top text-right pr-4")}>
          <div className="flex h-10 items-center justify-end text-sm font-semibold text-foreground">
            {euro(importo)}
          </div>
        </TableCell>
      )}

      {/* Ipotesi Fatturazione */}
      {showCol("dataFatt") && (
        <TableCell className={cn(SAL_COL.dataFatt, "py-2 align-top")}>
          <DateField
            value={row.dataFatt}
            onChange={(v) => void patch({ dataFatt: v })}
            size="sm"
            placeholder="—"
            className="w-full min-w-0 shadow-none bg-white dark:bg-zinc-950 border-zinc-200 text-sm"
            disabled={isLocked}
            stackedWeekday
          />
        </TableCell>
      )}

      {/* Stato Fatturazione ('' | daEmettere | emessa — 'pagata' legacy mappata dal server) */}
      {showCol("stato") && (
        <TableCell className={cn(SAL_COL.stato, "py-2 align-top")}>
          <Select
            value={row.stato || "__empty__"}
            onValueChange={(val) => {
              void patch({ stato: val === "__empty__" ? "" : val })
            }}
            disabled={isLocked}
          >
            <SelectTrigger className="h-10 w-full shadow-none bg-white dark:bg-zinc-950 border-zinc-200 text-sm">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__empty__">—</SelectItem>
              <SelectItem value="daEmettere">Da emettere</SelectItem>
              <SelectItem value="emessa">Emessa</SelectItem>
              {row.stato && row.stato !== "daEmettere" && row.stato !== "emessa" && (
                <SelectItem value={row.stato}>{row.stato} (non valido)</SelectItem>
              )}
            </SelectContent>
          </Select>
        </TableCell>
      )}

      {/* Pagamento (anagrafica stati pagamento) */}
      {showCol("pagamento") && (
        <TableCell className={cn(SAL_COL.pagamento, "py-2 align-top")}>
          <AnagraficaSelect
            value={row.pagamento ?? ""}
            options={paymentStates}
            emptyLabel="Nessuna"
            onChange={(v) => void patch({ pagamento: v })}
            showManage={isAdmin}
            disabled={isLocked}
            onAdd={async (label) => {
              await createPaymentState({ label })
              await queryClient.invalidateQueries({ queryKey: ["sal", "payment-states"] })
              await queryClient.invalidateQueries({ queryKey: ["sal-payment-states"] })
              return label
            }}
          />
        </TableCell>
      )}

      {/* Data Incasso */}
      {showCol("dataIncasso") && (
        <TableCell className={cn(SAL_COL.dataIncasso, "py-2 align-top")}>
          <DateField
            value={row.dataPagamento}
            onChange={(v) => void patch({ dataPagamento: v })}
            size="sm"
            placeholder="—"
            className="w-full min-w-0 shadow-none bg-white dark:bg-zinc-950 border-zinc-200 text-sm"
            disabled={isLocked}
            stackedWeekday
          />
        </TableCell>
      )}

      {/* Note */}
      {showCol("note") && (
        <TableCell className={cn(SAL_COL.note, "py-2 align-top")}>
          <GrowTextarea
            value={noteText}
            onChange={setNoteText}
            onCommit={commitNote}
            placeholder="—"
            className="bg-white dark:bg-zinc-950 border-zinc-200 text-sm"
            disabled={isLocked}
            dataCol="note"
            dataRow={index}
          />
        </TableCell>
      )}

      {/* Azioni riga */}
      <TableCell className={cn(SAL_COL.actions, "py-2 align-top")}>
        <div className="flex justify-end">
          <RowActionsMenu
            size="icon-sm"
            triggerClassName="size-7"
            actions={[
              {
                label: "Inserisci sopra",
                icon: ArrowUpToLine,
                onClick: () => onInsert("above"),
              },
              {
                label: "Inserisci sotto",
                icon: ArrowDownToLine,
                onClick: () => onInsert("below"),
              },
              ...(!isLocked
                ? [
                    {
                      label: "Elimina step SAL",
                      icon: Trash2,
                      destructive: true,
                      separatorBefore: true,
                      onClick: () => void removeRow(),
                    },
                  ]
                : []),
            ]}
          />
        </div>
      </TableCell>
    </TableRow>
  )
}
