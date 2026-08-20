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
import { DateField } from "@/components/shared/date-field"
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

import {
  AnagraficaSelect,
  GrowTextarea,
  SalViewBox,
  SalViewChip,
  SalViewDate,
  SalViewPercent,
  SalViewSelect,
  salStatoFattLabel,
} from "./sal-sheet-fields"
import {
  focusNextRowField,
  SAL_COL,
  type DropHint,
  type SalColId,
} from "./sal-sheet-shared"
import { salDataSaldo } from "./sal-utils"
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
  /** Commessa chiusa: foglio in sola lettura per chi non è ADMIN (unico lock del SAL). */
  isProjectClosed: boolean
  /** Funzione concessa in sola consultazione (es. tecnico del reparto Contabilità). */
  readOnly?: boolean
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
  isProjectClosed,
  readOnly = false,
  showCol,
}: SalRowComponentProps) {
  // Query client locale: serve alle onAdd delle tendine anagrafica per
  // invalidare le liste attive dopo la creazione inline di una voce.
  const queryClient = useQueryClient()

  // Due lock del foglio: la COMMESSA CHIUSA (per i non-ADMIN) e la funzione concessa in
  // sola lettura. Lo stato «Pagata» non blocca più la riga — un incasso segnato per errore
  // si deve poter correggere.
  const isLocked = (isProjectClosed && !isAdmin) || readOnly
  const [editing, setEditing] = React.useState(false)
  const isEditing = editing && !isLocked
  const rowRef = React.useRef<HTMLTableRowElement>(null)
  const pendingFocusCol = React.useRef<string | null>(null)

  const enterEdit = React.useCallback(
    (col?: string | null) => {
      if (isLocked) return
      pendingFocusCol.current = col ?? null
      setEditing(true)
    },
    [isLocked]
  )

  React.useEffect(() => {
    if (!isEditing) return
    const col = pendingFocusCol.current
    pendingFocusCol.current = null
    const root = rowRef.current
    if (!root) return
    const selector = col
      ? `[data-sal-col="${col}"]`
      : "input, textarea, button[role=combobox], button[data-slot=select-trigger]"
    const el = root.querySelector(selector) as HTMLElement | null
    el?.focus()
  }, [isEditing])

  React.useEffect(() => {
    if (!isEditing) return
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as HTMLElement | null
      if (!target) return
      if (rowRef.current?.contains(target)) return
      if (
        target.closest(
          "[data-radix-popper-content-wrapper], [data-slot='popover-content'], [data-slot='select-content'], [role='listbox']"
        )
      ) {
        return
      }
      setEditing(false)
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setEditing(false)
    }
    document.addEventListener("pointerdown", onPointerDown)
    document.addEventListener("keydown", onKeyDown)
    return () => {
      document.removeEventListener("pointerdown", onPointerDown)
      document.removeEventListener("keydown", onKeyDown)
    }
  }, [isEditing])

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

  // Handler Enter per gli input a riempimento verticale (Excel v10)
  const enterDown = (col: string) => (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      e.preventDefault()
      focusNextRowField(e.currentTarget, col, index)
    }
  }

  return (
    <TableRow
      ref={rowRef}
      draggable={!isLocked && grabId === row.id}
      onDragStart={onDragStart}
      onDragEnd={clearDrag}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      onClick={(event) => {
        if (isLocked || isEditing) return
        const target = event.target as HTMLElement
        if (target.closest("[data-sal-no-edit]")) return
        const col = target.closest("[data-sal-view-col]")?.getAttribute("data-sal-view-col")
        enterEdit(col)
      }}
      // Colore configurato dello stato pagamento (se presente): pura estetica,
      // la semantica (lock, incasso, warning) resta cablata su «Pagata»/«Parz.»
      style={rowStyle}
      className={cn(
        "border-0 transition-colors [&>td:first-child]:rounded-l-md [&>td:last-child]:rounded-r-md",
        !isLocked && !isEditing && "cursor-pointer",
        rowBg,
        !isEditing && "shadow-none",
        isDragging && "opacity-50",
        isDropOver && !dropHint?.after && "[&>td]:shadow-[inset_0_2px_0_0_var(--primary)]",
        isDropOver && dropHint?.after && "[&>td]:shadow-[inset_0_-2px_0_0_var(--primary)]"
      )}
    >
      {/* # — drag handle / lock */}
      <TableCell className={cn(SAL_COL.num, "py-2 align-middle")} data-sal-no-edit="">
        <div className="flex items-center gap-1">
          {isLocked ? (
            <span
              className="inline-flex size-6 items-center justify-center rounded text-muted-foreground/40"
              title={
                readOnly
                  ? "Sola consultazione: il tuo ruolo non modifica il SAL"
                  : "Commessa chiusa: sola lettura per chi non è ADMIN"
              }
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
          <div className="flex h-10 items-center justify-end text-sm">
            {euro(iva)}
          </div>
        </TableCell>
      )}

      {/* % IVA */}
      {showCol("ivaPerc") && (
        <TableCell
          className={cn(SAL_COL.ivaPerc, "py-2 align-top")}
          data-sal-view-col="ivaPerc"
        >
          {isEditing ? (
            <div className="flex h-10 items-center gap-1.5">
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
              <span className="text-sm">%</span>
            </div>
          ) : (
            <SalViewPercent value={ivaText} />
          )}
        </TableCell>
      )}

      {/* Tot. + IVA (calcolato) */}
      {canSeeEconomics && showCol("totIva") && (
        <TableCell className={cn(SAL_COL.totIva, "py-2 align-top text-right pr-4")}>
          <div className="flex h-10 items-center justify-end text-sm">
            {euro(totIva)}
          </div>
        </TableCell>
      )}

      {/* Data Prevista Saldo (calcolata: data fattura + gg saldo) */}
      {showCol("dataSaldo") && (
        <TableCell className={cn(SAL_COL.dataSaldo, "py-2 align-top text-center")}>
          {/* Niente tinta dedicata sull'incasso scaduto: il testo della riga è
              uniforme, l'allarme lo danno il colore riga e il Prospetto. */}
          <SalViewDate value={dataSaldo} />
        </TableCell>
      )}

      {/* GG. Saldo */}
      {showCol("ggSaldo") && (
        <TableCell
          className={cn(SAL_COL.ggSaldo, "py-2 align-top")}
          data-sal-view-col="ggSaldo"
        >
          {isEditing ? (
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
              className="h-10 w-full pl-2 pr-5 text-center text-sm shadow-none bg-white dark:bg-zinc-950 border-zinc-200 [appearance:auto] [&::-webkit-inner-spin-button]:opacity-100 [&::-webkit-outer-spin-button]:opacity-100 min-w-[4.5rem]"
              disabled={isLocked}
            />
          ) : (
            <SalViewChip className="w-full min-w-[4.5rem]">{ggText || "0"}</SalViewChip>
          )}
        </TableCell>
      )}

      {/* Step SAL */}
      {showCol("step") && (
        <TableCell
          className={cn(SAL_COL.step, "py-2 align-top whitespace-normal")}
          data-sal-view-col="step"
        >
          {isEditing ? (
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
          ) : (
            <SalViewBox empty={!stepText.trim()} wrap>
              {stepText}
            </SalViewBox>
          )}
        </TableCell>
      )}

      {/* N° Fattura (solo cifre) */}
      {showCol("nFatt") && (
        <TableCell
          className={cn(SAL_COL.nFatt, "py-2 align-top")}
          data-sal-view-col="nFatt"
        >
          {isEditing ? (
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
          ) : (
            <SalViewBox empty={!nFattText.trim()}>{nFattText}</SalViewBox>
          )}
        </TableCell>
      )}

      {/* Conto SAP (anagrafica causali) */}
      {showCol("contoSap") && (
        <TableCell
          className={cn(SAL_COL.contoSap, "py-2 align-top")}
          data-sal-view-col="contoSap"
        >
          {isEditing ? (
            <AnagraficaSelect
              value={row.contoSap ?? ""}
              options={sapCausali}
              emptyLabel="—"
              dataCol="contoSap"
              onChange={(v) => void patch({ contoSap: v })}
              showManage={isAdmin}
              disabled={isLocked}
              onAdd={async (label) => {
                await createSapCausale(label)
                await queryClient.invalidateQueries({ queryKey: ["sal", "sap-causali"] })
                await queryClient.invalidateQueries({ queryKey: ["sal-sap-causali"] })
                return label
              }}
            />
          ) : (
            <SalViewSelect label={row.contoSap ?? ""} />
          )}
        </TableCell>
      )}

      {/* % SAL */}
      {showCol("perc") && (
        <TableCell
          className={cn(SAL_COL.perc, "py-2 align-top")}
          data-sal-view-col="perc"
        >
          {isEditing ? (
            <div className="flex h-10 items-center gap-1.5">
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
              <span className="text-sm">%</span>
            </div>
          ) : (
            <SalViewPercent value={percText} />
          )}
        </TableCell>
      )}

      {/* Condizioni pagamento */}
      {showCol("condizione") && (
        <TableCell
          className={cn(SAL_COL.condizione, "py-2 align-top")}
          data-sal-view-col="condizione"
        >
          {isEditing ? (
            <AnagraficaSelect
              value={row.condizione ?? ""}
              options={activeConditions}
              emptyLabel="—"
              dataCol="condizione"
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
          ) : (
            <SalViewSelect label={row.condizione ?? ""} />
          )}
        </TableCell>
      )}

      {/* Importo Fattura (calcolato) */}
      {canSeeEconomics && showCol("importo") && (
        <TableCell className={cn(SAL_COL.importo, "py-2 align-top text-right pr-4")}>
          <div className="flex h-10 items-center justify-end text-sm">
            {euro(importo)}
          </div>
        </TableCell>
      )}

      {/* Ipotesi Fatturazione */}
      {showCol("dataFatt") && (
        <TableCell
          className={cn(SAL_COL.dataFatt, "py-2 align-top")}
          data-sal-view-col="dataFatt"
        >
          {isEditing ? (
            <DateField
              value={row.dataFatt}
              onChange={(v) => void patch({ dataFatt: v })}
              size="sm"
              placeholder="—"
              className="w-full min-w-0 shadow-none bg-white dark:bg-zinc-950 border-zinc-200 text-sm"
              disabled={isLocked}
              stackedWeekday
            />
          ) : (
            <SalViewDate value={row.dataFatt} />
          )}
        </TableCell>
      )}

      {/* Stato Fatturazione ('' | daEmettere | emessa — 'pagata' legacy mappata dal server) */}
      {showCol("stato") && (
        <TableCell
          className={cn(SAL_COL.stato, "py-2 align-top")}
          data-sal-view-col="stato"
        >
          {isEditing ? (
            <Select
              value={row.stato || "__empty__"}
              onValueChange={(val) => {
                void patch({ stato: val === "__empty__" ? "" : val })
              }}
              disabled={isLocked}
            >
              <SelectTrigger
                className="h-10 w-full shadow-none bg-white dark:bg-zinc-950 border-zinc-200 text-sm"
                data-sal-col="stato"
              >
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
          ) : (
            <SalViewSelect label={salStatoFattLabel(row.stato ?? "")} />
          )}
        </TableCell>
      )}

      {/* Pagamento (anagrafica stati pagamento) */}
      {showCol("pagamento") && (
        <TableCell
          className={cn(SAL_COL.pagamento, "py-2 align-top")}
          data-sal-view-col="pagamento"
        >
          {isEditing ? (
            <AnagraficaSelect
              value={row.pagamento ?? ""}
              options={paymentStates}
              emptyLabel="Nessuna"
              dataCol="pagamento"
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
          ) : (
            <SalViewSelect label={row.pagamento ?? ""} emptyLabel="Nessuna" />
          )}
        </TableCell>
      )}

      {/* Data Incasso */}
      {showCol("dataIncasso") && (
        <TableCell
          className={cn(SAL_COL.dataIncasso, "py-2 align-top")}
          data-sal-view-col="dataIncasso"
        >
          {isEditing ? (
            <DateField
              value={row.dataPagamento}
              onChange={(v) => void patch({ dataPagamento: v })}
              size="sm"
              placeholder="—"
              className="w-full min-w-0 shadow-none bg-white dark:bg-zinc-950 border-zinc-200 text-sm"
              disabled={isLocked}
              stackedWeekday
            />
          ) : (
            <SalViewDate value={row.dataPagamento} />
          )}
        </TableCell>
      )}

      {/* Note */}
      {showCol("note") && (
        <TableCell
          className={cn(SAL_COL.note, "py-2 align-top whitespace-normal")}
          data-sal-view-col="note"
        >
          {isEditing ? (
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
          ) : (
            <SalViewBox empty={!noteText.trim()} wrap>
              {noteText}
            </SalViewBox>
          )}
        </TableCell>
      )}

      {/* Azioni riga — tutte scritture: con il foglio bloccato il menu sparisce.
          (Prima «Inserisci sopra/sotto» restavano attive anche a commessa chiusa.) */}
      <TableCell className={cn(SAL_COL.actions, "py-2 align-top")} data-sal-no-edit="">
        <div className="flex justify-end">
          {!isLocked && (
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
                {
                  label: "Elimina step SAL",
                  icon: Trash2,
                  destructive: true,
                  separatorBefore: true,
                  onClick: () => void removeRow(),
                },
              ]}
            />
          )}
        </div>
      </TableCell>
    </TableRow>
  )
}
