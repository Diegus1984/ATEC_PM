import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowDownToLine, ArrowUpToLine, GripVertical, Plus, RefreshCw, Trash2 } from "lucide-react"
import { useNavigate } from "react-router-dom"

import { useConfirm } from "@/components/shared/confirm"
import { DateField } from "@/components/shared/date-field"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader } from "@/components/ui/card"
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import {
  fetchSal,
  saveSalHeader,
  createSalRow,
  updateSalRow,
  deleteSalRow,
  reorderSalRows,
  seedSalTemplate,
  fetchSalConditions,
} from "@/lib/api/sal"
import type { SalRow, SalHeaderSaveRequest, SalRowSaveRequest } from "@/lib/api/types"
import { useSalHub } from "@/lib/signalr/use-sal-hub"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"
import { salAlertState, salRowClass } from "./sal-utils"
import { toIso } from "@/features/risorse/planner-logic"

type DropHint = { id: number; after: boolean }

/** Percentuale con al massimo 1 decimale, virgola all'italiana (nessun decimale se intera). */
function fmtPct(n: number): string {
  const r = Math.round(n * 10) / 10
  return Number.isInteger(r) ? String(r) : r.toFixed(1).replace(".", ",")
}

function Stat({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs uppercase tracking-wide text-muted-foreground">
        {label}
      </span>
      <span className="text-sm font-medium">{children}</span>
    </div>
  )
}

function GrowTextarea({
  value,
  onChange,
  onCommit,
  placeholder,
  className,
}: {
  value: string
  onChange: (v: string) => void
  onCommit: () => void
  placeholder?: string
  className?: string
}) {
  return (
    <Textarea
      rows={1}
      value={value}
      placeholder={placeholder}
      spellCheck={false}
      className={cn(
        "field-sizing-content min-h-8 resize-none px-2 py-1 text-sm leading-5 shadow-none",
        className
      )}
      onChange={(e) => onChange(e.target.value)}
      onBlur={onCommit}
      onKeyDown={(e) => {
        if (e.key === "Enter" && !e.shiftKey) {
          e.preventDefault()
          onCommit()
          e.currentTarget.blur()
        }
      }}
    />
  )
}

export function ProjectSal({
  projectId,
}: {
  projectId: number
  projectCode?: string
  projectTitle?: string
}) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const confirm = useConfirm()

  const queryKey = React.useMemo(() => ["sal", "project", projectId], [projectId])
  const query = useQuery({
    queryKey,
    queryFn: () => fetchSal(projectId),
    enabled: projectId > 0,
  })

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey })
  }, [queryClient, queryKey])

  useSalHub(projectId > 0, invalidate, projectId)

  // Condizioni di pagamento attive
  const conditionsQuery = useQuery({
    queryKey: ["sal", "conditions", "active"],
    queryFn: () => fetchSalConditions(true),
  })

  const bundle = query.data
  const header = bundle?.header
  const rows = React.useMemo(() => bundle?.rows ?? [], [bundle])

  // Stato interno per l'editing dell'header
  const [cliente, setCliente] = React.useState("")
  const [valore, setValore] = React.useState("")

  React.useEffect(() => {
    if (header) {
      setCliente(header.cliente || "")
      setValore(header.valore === null || header.valore === undefined ? "" : String(header.valore))
    }
  }, [header])

  const handleSaveHeader = async (updatedFields: Partial<SalHeaderSaveRequest>) => {
    if (!header) return
    try {
      const payload: SalHeaderSaveRequest = {
        cliente: updatedFields.cliente !== undefined ? updatedFields.cliente : cliente,
        valore: updatedFields.valore !== undefined ? updatedFields.valore : (valore === "" ? null : Number(valore)),
        rowVersion: header.rowVersion,
      }
      await saveSalHeader(projectId, payload)
      invalidate()
    } catch (err) {
      notifyError(err as Error)
      invalidate()
    }
  }

  const handleSeedTemplate = async () => {
    try {
      await seedSalTemplate(projectId)
      invalidate()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  // Avanzamento Incasso
  const { totalPerc, paidPerc, advancedIncasso } = React.useMemo(() => {
    const tot = rows.reduce((acc, r) => acc + Number(r.perc ?? 0), 0)
    const paid = rows.reduce((acc, r) => acc + (r.stato === "pagata" ? Number(r.perc ?? 0) : 0), 0)
    const adv = tot > 0 ? (paid / tot) * 100 : 0
    return { totalPerc: tot, paidPerc: paid, advancedIncasso: adv }
  }, [rows])

  // Drag & Drop
  const dragIdRef = React.useRef<number | null>(null)
  const [grabId, setGrabId] = React.useState<number | null>(null)
  const [draggingId, setDraggingId] = React.useState<number | null>(null)
  const [dropHint, setDropHint] = React.useState<DropHint | null>(null)

  const clearDrag = () => {
    dragIdRef.current = null
    setGrabId(null)
    setDraggingId(null)
    setDropHint(null)
  }

  const handleReorder = async (dragId: number, targetId: number, after: boolean) => {
    if (dragId === targetId) return
    const ids = rows.map((r) => r.id)
    const fromIdx = ids.indexOf(dragId)
    if (fromIdx === -1) return
    ids.splice(fromIdx, 1)
    const targetIdx = ids.indexOf(targetId)
    if (targetIdx === -1) return
    ids.splice(after ? targetIdx + 1 : targetIdx, 0, dragId)

    try {
      await reorderSalRows(projectId, ids)
      invalidate()
    } catch (err) {
      notifyError(err as Error)
      invalidate()
    }
  }

  const handleInsertRow = async (index: number, where: "above" | "below") => {
    try {
      const newId = await createSalRow(projectId, {
        step: "",
        perc: null,
        condizione: "",
        dataFatt: null,
        stato: "",
        rowVersion: null,
      })
      const ids = rows.map((r) => r.id)
      ids.splice(where === "below" ? index + 1 : index, 0, newId)
      await reorderSalRows(projectId, ids)
      invalidate()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  const todayIso = React.useMemo(() => toIso(new Date()), [])

  if (query.isLoading) {
    return (
      <Card className="py-8 text-center text-sm text-muted-foreground">
        Caricamento dati SAL…
      </Card>
    )
  }

  if (query.isError) {
    return (
      <Card className="p-4 border-destructive/20 bg-destructive/10 text-destructive text-sm font-medium">
        {(query.error as Error).message}
      </Card>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      {/* Testata SAL */}
      <Card className="overflow-hidden py-0">
        <CardHeader className="flex flex-row flex-wrap items-center gap-6 border-b bg-muted/30 py-3">
          <div className="flex flex-wrap items-center gap-4">
            <div className="flex flex-col gap-1">
              <label className="text-[10px] uppercase font-bold tracking-wider text-muted-foreground">Cliente SAL</label>
              <Input
                value={cliente}
                onChange={(e) => setCliente(e.target.value)}
                onBlur={() => void handleSaveHeader({ cliente })}
                placeholder="Inserisci cliente..."
                className="h-8 w-64 shadow-none"
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[10px] uppercase font-bold tracking-wider text-muted-foreground">Valore Commessa (€)</label>
              <Input
                type="number"
                value={valore}
                onChange={(e) => setValore(e.target.value)}
                onBlur={() => void handleSaveHeader({ valore: valore === "" ? null : Number(valore) })}
                placeholder="Valore (€)..."
                className="h-8 w-44 font-mono text-right shadow-none"
              />
            </div>
          </div>

          <div className="flex flex-row gap-6 items-center">
            <Stat label="Avanzamento Incasso SAL">
              <span className="flex items-center gap-2 mt-1">
                <span className="tabular-nums font-semibold text-xs">{fmtPct(advancedIncasso)}%</span>
                <span className="h-2 w-24 overflow-hidden rounded bg-zinc-200">
                  <span
                    className="block h-full bg-emerald-500 transition-all duration-300"
                    style={{ width: `${Math.min(100, advancedIncasso)}%` }}
                  />
                </span>
                <span className="text-[10px] text-muted-foreground font-mono">
                  ({fmtPct(paidPerc)}% di {fmtPct(totalPerc)}%)
                </span>
              </span>
            </Stat>
          </div>

          <div className="ml-auto flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={handleSeedTemplate}
              disabled={rows.length > 0}
              title="Precarica i 6 step SAL standard (15/15/10/20/20/20)"
            >
              Precarica modello standard
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => void query.refetch()}
              disabled={query.isFetching}
            >
              <RefreshCw className={cn("size-3.5 mr-1.5", query.isFetching && "animate-spin")} />
              Aggiorna
            </Button>
          </div>
        </CardHeader>

        <CardContent className="p-3">
          {rows.length === 0 ? (
            <p className="mb-3 text-sm text-muted-foreground text-center py-6">
              Nessun pagamento SAL pianificato per questa commessa. Precarica il modello standard o aggiungi uno step qui sotto.
            </p>
          ) : null}

          <div className="overflow-x-auto rounded-lg border">
            <Table className="border-separate border-spacing-y-1">
              <TableHeader className="bg-muted/40">
                <TableRow>
                  <TableHead className="w-16">#</TableHead>
                  <TableHead className="min-w-[18rem]">Descrizione / Step SAL</TableHead>
                  <TableHead className="w-24 text-center">%</TableHead>
                  <TableHead className="w-56">Condizione Pagamento</TableHead>
                  <TableHead className="w-36 text-right">Importo (€)</TableHead>
                  <TableHead className="w-48 text-center">Ipotesi Fatturazione</TableHead>
                  <TableHead className="w-48">Stato</TableHead>
                  <TableHead className="w-12"></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row, index) => {
                  const alertState = salAlertState(row, todayIso)
                  const rowBg = salRowClass(alertState)
                  const isDragging = draggingId === row.id
                  const isDropOver = dropHint?.id === row.id

                  const importo = header && header.valore != null && row.perc != null
                    ? header.valore * (row.perc / 100)
                    : null;

                  return (
                    <SalRowComponent
                      key={row.id}
                      row={row}
                      index={index}
                      rowBg={rowBg}
                      isDragging={isDragging}
                      isDropOver={isDropOver}
                      dropHint={dropHint}
                      importo={importo}
                      activeConditions={conditionsQuery.data ?? []}
                      onMutated={invalidate}
                      onInsert={(where) => void handleInsertRow(index, where)}
                      onConfirm={confirm}
                      grabId={grabId}
                      setGrabId={setGrabId}
                      setDraggingId={setDraggingId}
                      setDropHint={setDropHint}
                      dragIdRef={dragIdRef}
                      handleReorder={handleReorder}
                      clearDrag={clearDrag}
                      navigate={navigate}
                    />
                  )
                })}

                {/* Riga aggiunta in coda */}
                <NewSalRowComponent
                  projectId={projectId}
                  onMutated={invalidate}
                />
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

interface SalRowComponentProps {
  row: SalRow
  index: number
  rowBg: string
  isDragging: boolean
  isDropOver: boolean
  dropHint: DropHint | null
  importo: number | null
  activeConditions: { id: number; label: string; isActive: boolean }[]
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
  navigate: ReturnType<typeof useNavigate>
}

function SalRowComponent({
  row,
  index,
  rowBg,
  isDragging,
  isDropOver,
  dropHint,
  importo,
  activeConditions,
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
  navigate,
}: SalRowComponentProps) {
  const [stepText, setStepText] = React.useState(row.step)
  const [percText, setPercText] = React.useState(row.perc === null ? "" : String(row.perc))

  React.useEffect(() => {
    setStepText(row.step)
  }, [row.step])

  React.useEffect(() => {
    setPercText(row.perc === null ? "" : String(row.perc))
  }, [row.perc])

  const patch = async (fields: Partial<SalRowSaveRequest>) => {
    try {
      const payload: SalRowSaveRequest = {
        step: fields.step !== undefined ? fields.step : row.step,
        perc: fields.perc !== undefined ? fields.perc : row.perc,
        condizione: fields.condizione !== undefined ? fields.condizione : row.condizione,
        dataFatt: fields.dataFatt !== undefined ? fields.dataFatt : row.dataFatt,
        stato: fields.stato !== undefined ? fields.stato : row.stato,
        rowVersion: row.rowVersion,
      }
      await updateSalRow(row.id, payload)
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
    let n = parseFloat(val)
    if (isNaN(n)) {
      setPercText(row.perc === null ? "" : String(row.perc))
      return
    }
    n = Math.max(0, Math.min(100, n))
    if (n !== row.perc) void patch({ perc: n })
    else setPercText(String(n))
  }

  const removeRow = async () => {
    const hasData = row.step.trim() !== "" || row.perc !== null || row.condizione !== "" || row.dataFatt !== null || row.stato !== ""
    if (hasData) {
      const ok = await onConfirm({
        title: "Eliminare lo step SAL?",
        description: row.step.trim() || `Step ${index + 1}`,
        confirmLabel: "Elimina",
      })
      if (!ok) return
    }

    try {
      await deleteSalRow(row.id)
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  // Costruisce la lista di condizioni, includendo quella corrente anche se inattiva.
  const selectOptions = React.useMemo(() => {
    const opts = [...activeConditions]
    if (row.condizione && !opts.some((o) => o.label === row.condizione)) {
      opts.unshift({ id: -1, label: row.condizione, isActive: false })
    }
    return opts
  }, [activeConditions, row.condizione])

  // Drag and Drop handlers
  const onDragStart = (e: React.DragEvent) => {
    dragIdRef.current = row.id
    setDraggingId(row.id)
    e.dataTransfer.effectAllowed = "move"
    // HTML5 requirement
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

  const formattedImporto = importo !== null
    ? importo.toLocaleString("it-IT", { style: "currency", currency: "EUR" })
    : "—";

  return (
    <TableRow
      draggable={grabId === row.id}
      onDragStart={onDragStart}
      onDragEnd={clearDrag}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      className={cn(
        "border-0 transition-colors",
        rowBg,
        isDragging && "opacity-50",
        isDropOver && !dropHint?.after && "[&>td]:shadow-[inset_0_2px_0_0_var(--primary)]",
        isDropOver && dropHint?.after && "[&>td]:shadow-[inset_0_-2px_0_0_var(--primary)]"
      )}
    >
      <TableCell className="w-16 py-1.5 align-top">
        <div className="flex items-center gap-1">
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
          <span className="text-xs tabular-nums text-muted-foreground">
            {String(index + 1).padStart(2, "0")}
          </span>
        </div>
      </TableCell>

      <TableCell className="min-w-[18rem] py-1.5 align-top">
        <GrowTextarea
          value={stepText}
          onChange={setStepText}
          onCommit={commitStep}
          placeholder="Descrizione step di pagamento (es. Acconto all'ordine...)"
          className="border-transparent bg-transparent focus-visible:border-input focus-visible:bg-background"
        />
      </TableCell>

      <TableCell className="w-24 py-1.5 align-top">
        <div className="flex items-center gap-1.5 h-8">
          <Input
            value={percText}
            onChange={(e) => setPercText(e.target.value)}
            onBlur={commitPerc}
            onKeyDown={(e) => {
              if (e.key === "Enter") e.currentTarget.blur()
            }}
            placeholder="0.0"
            className="h-8 w-16 px-1 text-center font-mono tabular-nums shadow-none"
          />
          <span className="text-xs text-muted-foreground font-semibold">%</span>
        </div>
      </TableCell>

      <TableCell className="w-56 py-1.5 align-top">
        <Select
          value={row.condizione || "__empty__"}
          onValueChange={(val) => {
            if (val === "__new_condition__") {
              navigate("/admin/sal-conditions")
              return
            }
            void patch({ condizione: val === "__empty__" ? "" : val })
          }}
        >
          <SelectTrigger className="h-8 shadow-none bg-transparent border-zinc-200">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="__empty__">—</SelectItem>
            {selectOptions.map((o) => (
              <SelectItem key={o.id} value={o.label}>
                {o.label} {!o.isActive && row.condizione === o.label ? "(inattiva)" : ""}
              </SelectItem>
            ))}
            <SelectItem value="__new_condition__" className="text-primary font-semibold">
              ➕ Nuova condizione…
            </SelectItem>
          </SelectContent>
        </Select>
      </TableCell>

      <TableCell className="w-36 py-1.5 align-top text-right pr-4">
        <div className="flex h-8 items-center justify-end font-mono text-xs font-semibold tabular-nums text-foreground">
          {formattedImporto}
        </div>
      </TableCell>

      <TableCell className="w-48 py-1.5 align-top">
        <DateField
          value={row.dataFatt}
          onChange={(v) => void patch({ dataFatt: v })}
          size="sm"
          placeholder="—"
          className="h-8 w-full min-w-0 shadow-none"
        />
      </TableCell>

      <TableCell className="w-48 py-1.5 align-top">
        <Select
          value={row.stato || "__empty__"}
          onValueChange={(val) => {
            void patch({ stato: val === "__empty__" ? "" : val })
          }}
        >
          <SelectTrigger className="h-8 shadow-none bg-transparent border-zinc-200">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="__empty__">—</SelectItem>
            <SelectItem value="emessa">Fattura emessa</SelectItem>
            <SelectItem value="pagata">Fattura pagata</SelectItem>
          </SelectContent>
        </Select>
      </TableCell>

      <TableCell className="w-12 py-1.5 align-top">
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
              {
                label: "Elimina step SAL",
                icon: Trash2,
                destructive: true,
                separatorBefore: true,
                onClick: () => void removeRow(),
              },
            ]}
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

function NewSalRowComponent({
  projectId,
  onMutated,
}: {
  projectId: number
  onMutated: () => void
}) {
  const [step, setStep] = React.useState("")
  const committing = React.useRef(false)

  const commit = async () => {
    const val = step.trim()
    if (!val || committing.current) return
    committing.current = true
    try {
      await createSalRow(projectId, {
        step: val,
        perc: null,
        condizione: "",
        dataFatt: null,
        stato: "",
        rowVersion: null,
      })
      setStep("")
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    } finally {
      committing.current = false
    }
  }

  return (
    <TableRow className="border-0 bg-muted/40 hover:bg-muted/50">
      <TableCell className="w-16" />
      <TableCell colSpan={7} className="py-1.5">
        <div className="flex items-center gap-2">
          <Plus className="size-4 text-muted-foreground" />
          <Input
            value={step}
            onChange={(e) => setStep(e.target.value)}
            onBlur={() => void commit()}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault()
                void commit()
              }
            }}
            placeholder="Aggiungi step SAL…"
            className="h-8 max-w-md border-dashed"
          />
        </div>
      </TableCell>
    </TableRow>
  )
}
