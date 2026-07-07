import * as React from "react"
import { createPortal } from "react-dom"
import {
  ArrowUpDown,
  CircleCheck,
  Flag,
  GripVertical,
  Pause,
  Plus,
  Trash2,
} from "lucide-react"

import { DateField } from "@/components/shared/date-field"
import {
  MultiSelectCombo,
  type MultiSelectOption,
} from "@/components/shared/multi-select"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Textarea } from "@/components/ui/textarea"
import { isoToDate } from "@/lib/date-iso"
import type { MoMActionItem } from "@/lib/api/types"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

import {
  MOM_PRIORITY_META,
  momRowClass,
  canReorderRows,
  type SheetSort,
  type SheetSortField,
} from "./mom-detail-shared"

// ─────────────────────────────────────────────────────────────
// Foglio MoM editabile (prototipo Gestione_MoM_v9): tabella a
// celle inline con autosave, autocomplete, riordino drag&drop.
// Stile datagrid allineato alla check list (bordi inset, pill, spacing).
// ─────────────────────────────────────────────────────────────

type TextField = "attivita" | "descrizione" | "azione"
type AcField = "attivita" | "descrizione"

interface SheetColumn {
  key: SheetSortField
  label: string
  className: string
}

const SHEET_COLS: SheetColumn[] = [
  { key: "attivita", label: "Attività", className: "min-w-[8rem]" },
  { key: "descrizione", label: "Descrizione", className: "min-w-[8rem]" },
  { key: "azione", label: "Azione", className: "min-w-[10rem]" },
  { key: "priorita", label: "Priorità", className: "w-36" },
  { key: "responsabili", label: "Responsabile", className: "w-px" },
  { key: "dataCheck", label: "Data check avanz.", className: "w-52" },
  { key: "dataClose", label: "Data close", className: "w-52" },
]

const TEXT_CELL_CLS = "whitespace-normal py-1.5 align-top"
const RESP_CELL_CLS = "whitespace-nowrap py-1.5 align-top"
/** grip + selezione + # + azioni */
const SHEET_FIXED_COLS = 4

function SortableHead({
  active,
  label,
  onClick,
}: {
  active: boolean
  label: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      className={cn(
        "inline-flex items-center gap-1 font-medium text-foreground transition-colors hover:text-foreground/70",
        active && "text-primary hover:text-primary"
      )}
      title={`Ordina per ${label.toLowerCase()}`}
      onClick={onClick}
    >
      {label}
      <ArrowUpDown className="size-3 opacity-60" />
    </button>
  )
}

function MomPrioritySelect({
  value,
  onChange,
}: {
  value: number
  onChange: (priority: number) => void
}) {
  return (
    <Select value={String(value)} onValueChange={(v) => onChange(Number(v))}>
      <SelectTrigger
        size="sm"
        aria-label="Priorità"
        className="h-8 min-w-[8.5rem] gap-1.5 border-input bg-background px-2 font-normal shadow-none"
      >
        <SelectValue />
      </SelectTrigger>
      <SelectContent align="start" position="popper">
        {MOM_PRIORITY_META.map((p) => (
          <SelectItem
            key={p.value}
            value={String(p.value)}
            textValue={`${p.code} · ${p.name}`}
          >
            <span className="flex items-center gap-2">
              <span className={cn("size-2 shrink-0 rounded-full", p.dot)} />
              <span className="font-mono text-xs font-semibold">{p.code}</span>
              <span className="text-muted-foreground">{p.name}</span>
            </span>
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/** Textarea auto-adattante in altezza e larghezza al contenuto. */
const GrowTextarea = React.forwardRef<
  HTMLTextAreaElement,
  React.ComponentProps<typeof Textarea>
>(function GrowTextarea({ className, value, onFocus, onBlur, ...props }, forwarded) {
  const inner = React.useRef<HTMLTextAreaElement | null>(null)
  const [focused, setFocused] = React.useState(false)

  const syncSize = React.useCallback(() => {
    const el = inner.current
    if (!el) return
    el.style.height = "0"
    el.style.height = `${Math.max(32, el.scrollHeight)}px`
  }, [])

  React.useLayoutEffect(() => {
    syncSize()
  }, [value, syncSize])

  return (
    <Textarea
      ref={(el) => {
        inner.current = el
        if (typeof forwarded === "function") forwarded(el)
        else if (forwarded) forwarded.current = el
      }}
      rows={1}
      value={value}
      spellCheck={false}
      className={cn(
        "field-sizing-content box-border min-h-8 w-auto min-w-[6rem] max-w-full resize-none overflow-hidden px-2 py-1 text-sm leading-5 shadow-none",
        "whitespace-pre-wrap break-words",
        focused
          ? "border-input bg-background focus-visible:ring-1 focus-visible:ring-ring/40"
          : "border-transparent bg-transparent",
        "hover:border-input focus-visible:border-input",
        className
      )}
      onFocus={(event) => {
        setFocused(true)
        onFocus?.(event)
      }}
      onBlur={(event) => {
        setFocused(false)
        onBlur?.(event)
      }}
      onInput={syncSize}
      {...props}
    />
  )
})

// ── Autocomplete «definizioni disponibili» (Attività/Descrizione) ──

interface AcState {
  rowId: number
  field: AcField
  rect: { left: number; top: number; width: number }
  items: string[]
  idx: number
}

export interface SheetFocusRequest {
  rowId: number
  field: TextField
}

export interface MoMSheetProps {
  /** Righe visibili, già filtrate e ordinate. */
  displayRows: MoMActionItem[]
  /** Tutte le righe (per le definizioni autocomplete). */
  allRows: MoMActionItem[]
  sort: SheetSort
  poolFor: (item: MoMActionItem) => MultiSelectOption[]
  focusRequest: SheetFocusRequest | null
  onFocusHandled: () => void
  onSortChange: (sort: SheetSort) => void
  onPatch: (
    item: MoMActionItem,
    patch: Partial<MoMActionItem>,
    opts?: { immediate?: boolean }
  ) => void
  onFlush: (item: MoMActionItem) => void
  onAddRow: (
    focusField: TextField,
    opts?: { attivita?: string }
  ) => void | Promise<void>
  onDelete: (item: MoMActionItem) => void
  onReorder: (dragId: number, targetId: number, after: boolean) => boolean
  emptyMessage?: string
  selectedRowIds: ReadonlySet<number>
  onToggleRowSelected: (id: number, selected: boolean) => void
  onToggleAllRowsSelected: (selected: boolean) => void
}

function SheetNewRow({
  onAddRow,
}: {
  onAddRow: (
    focusField: TextField,
    opts?: { attivita?: string }
  ) => void | Promise<void>
}) {
  const [text, setText] = React.useState("")
  const committing = React.useRef(false)
  const inputRef = React.useRef<HTMLInputElement>(null)

  async function commit() {
    const v = text.trim()
    if (!v || committing.current) return
    committing.current = true
    try {
      await onAddRow("attivita", { attivita: v })
      setText("")
      requestAnimationFrame(() => inputRef.current?.focus())
    } catch (err) {
      notifyError(err as Error)
    } finally {
      committing.current = false
    }
  }

  return (
    <TableRow className="border-0 bg-muted/40 hover:bg-muted/50">
      <TableCell className="print:hidden" />
      <TableCell className="print:hidden" />
      <TableCell colSpan={SHEET_COLS.length + 2} className="py-1.5">
        <div className="flex items-center gap-2">
          <Plus className="size-4 shrink-0 text-muted-foreground" />
          <Input
            ref={inputRef}
            value={text}
            onChange={(e) => setText(e.target.value)}
            onBlur={() => void commit()}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault()
                void commit()
              }
            }}
            placeholder="Nuova attività…"
            className="h-8 border-dashed shadow-none"
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

export function MoMSheet({
  displayRows,
  allRows,
  sort,
  poolFor,
  focusRequest,
  onFocusHandled,
  onSortChange,
  onPatch,
  onFlush,
  onAddRow,
  onDelete,
  onReorder,
  emptyMessage = "Nessuna azione in questa vista.",
  selectedRowIds,
  onToggleRowSelected,
  onToggleAllRowsSelected,
}: MoMSheetProps) {
  const wrapRef = React.useRef<HTMLDivElement | null>(null)
  const [ac, setAc] = React.useState<AcState | null>(null)
  const acRef = React.useRef<AcState | null>(null)
  acRef.current = ac

  const dragIdRef = React.useRef<number | null>(null)
  const [grabId, setGrabId] = React.useState<number | null>(null)
  const [draggingId, setDraggingId] = React.useState<number | null>(null)
  const [dropHint, setDropHint] = React.useState<{
    id: number
    after: boolean
    valid: boolean
  } | null>(null)

  const rowById = React.useCallback(
    (id: number) => allRows.find((r) => r.id === id),
    [allRows]
  )

  function dropAllowed(dragId: number, targetId: number): boolean {
    const drag = rowById(dragId)
    const target = rowById(targetId)
    if (!drag || !target) return false
    return canReorderRows(drag, target)
  }

  React.useEffect(() => {
    if (!focusRequest) return
    const el = wrapRef.current?.querySelector<HTMLTextAreaElement>(
      `textarea[data-row="${focusRequest.rowId}"][data-field="${focusRequest.field}"]`
    )
    if (el) {
      el.focus()
      el.scrollIntoView({ block: "nearest" })
    }
    onFocusHandled()
  }, [focusRequest, onFocusHandled])

  const acValues = React.useCallback(
    (field: AcField): string[] => {
      const seen = new Set<string>()
      const out: string[] = []
      for (const row of allRows) {
        const value = (row[field] ?? "").trim()
        if (!value) continue
        const key = value.toLowerCase()
        if (seen.has(key)) continue
        seen.add(key)
        out.push(value)
      }
      return out.sort((a, b) => a.localeCompare(b, "it", { sensitivity: "base" }))
    },
    [allRows]
  )

  const acOpen = React.useCallback(
    (target: HTMLTextAreaElement, item: MoMActionItem, field: AcField) => {
      const filter = target.value.trim().toLowerCase()
      let values = acValues(field)
      if (filter) {
        values = values.filter((value) => {
          const lower = value.toLowerCase()
          return lower.includes(filter) && lower !== filter
        })
      }
      const items = values.slice(0, 80)
      if (items.length === 0) {
        setAc(null)
        return
      }
      const rect = target.getBoundingClientRect()
      setAc({
        rowId: item.id,
        field,
        rect: { left: rect.left, top: rect.bottom + 2, width: rect.width },
        items,
        idx: -1,
      })
    },
    [acValues]
  )

  const acPick = React.useCallback(
    (index: number) => {
      const state = acRef.current
      if (!state || index < 0 || index >= state.items.length) return
      const item = allRows.find((row) => row.id === state.rowId)
      if (item) onPatch(item, { [state.field]: state.items[index] })
      setAc(null)
    },
    [allRows, onPatch]
  )

  function acKeyDown(event: React.KeyboardEvent<HTMLTextAreaElement>) {
    const state = acRef.current
    if (!state) return
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault()
      const delta = event.key === "ArrowDown" ? 1 : -1
      setAc({
        ...state,
        idx: (state.idx + delta + state.items.length) % state.items.length,
      })
    } else if (event.key === "Enter") {
      if (state.idx >= 0) {
        event.preventDefault()
        acPick(state.idx)
      }
    } else if (event.key === "Escape") {
      event.preventDefault()
      setAc(null)
    }
  }

  function handleWrapDragOver(event: React.DragEvent) {
    const wrap = wrapRef.current
    if (!wrap || dragIdRef.current == null) return
    const rect = wrap.getBoundingClientRect()
    const zone = 56
    const maxSpeed = 22
    const speed = (dist: number) =>
      Math.ceil(maxSpeed * Math.min(1, Math.max(0, dist) / zone))
    if (event.clientY < rect.top + zone) {
      wrap.scrollTop -= speed(rect.top + zone - event.clientY)
    } else if (event.clientY > rect.bottom - zone) {
      wrap.scrollTop += speed(event.clientY - (rect.bottom - zone))
    }
  }

  function clearDrag() {
    dragIdRef.current = null
    setGrabId(null)
    setDraggingId(null)
    setDropHint(null)
  }

  function toggleSort(field: SheetSortField) {
    onSortChange(
      sort.field === field
        ? { field, dir: sort.dir > 0 ? -1 : 1 }
        : { field, dir: 1 }
    )
  }

  const visibleIds = React.useMemo(
    () => displayRows.map((row) => row.id),
    [displayRows]
  )
  const allVisibleSelected =
    visibleIds.length > 0 && visibleIds.every((id) => selectedRowIds.has(id))
  const someVisibleSelected = visibleIds.some((id) => selectedRowIds.has(id))

  return (
    <div className="space-y-2">
      <div className="overflow-hidden rounded-lg border bg-card">
        <div
          ref={wrapRef}
          className="max-h-[72vh] overflow-auto"
          onDragOver={handleWrapDragOver}
        >
          <Table className="w-max min-w-full table-auto border-separate border-spacing-y-1.5">
            <TableHeader className="sticky top-0 z-10 bg-muted/40">
              <TableRow>
                <TableHead className="w-7 print:hidden" />
                <TableHead className="w-10 px-0 text-center print:hidden">
                  <Checkbox
                    checked={
                      allVisibleSelected
                        ? true
                        : someVisibleSelected
                          ? "indeterminate"
                          : false
                    }
                    onCheckedChange={(value) =>
                      onToggleAllRowsSelected(value === true)
                    }
                    aria-label="Seleziona tutte le righe visibili"
                    disabled={displayRows.length === 0}
                  />
                </TableHead>
                <TableHead className="w-10 text-center">#</TableHead>
                {SHEET_COLS.map((col) => (
                  <TableHead key={col.key} className={col.className}>
                    <SortableHead
                      active={sort.field === col.key}
                      label={col.label}
                      onClick={() => toggleSort(col.key)}
                    />
                  </TableHead>
                ))}
                <TableHead className="w-12 text-right print:hidden">Azioni</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {displayRows.length === 0 ? (
                <TableRow>
                  <TableCell
                    colSpan={SHEET_COLS.length + SHEET_FIXED_COLS}
                    className="py-6 text-center text-sm text-muted-foreground"
                  >
                    {emptyMessage}
                  </TableCell>
                </TableRow>
              ) : (
                displayRows.map((item, index) => (
                <TableRow
                  key={item.id}
                  data-row-id={item.id}
                  draggable={grabId === item.id}
                  className={cn(
                    "border-0 transition-colors",
                    momRowClass(item),
                    draggingId === item.id && "opacity-50",
                    dropHint?.id === item.id &&
                      dropHint.valid &&
                      !dropHint.after &&
                      "[&>td]:shadow-[inset_0_2px_0_0_var(--primary)]",
                    dropHint?.id === item.id &&
                      dropHint.valid &&
                      dropHint.after &&
                      "[&>td]:shadow-[inset_0_-2px_0_0_var(--primary)]",
                    dropHint?.id === item.id &&
                      !dropHint.valid &&
                      "[&>td]:shadow-[inset_0_2px_0_0_var(--destructive)]"
                  )}
                  onDragStart={(event) => {
                    dragIdRef.current = item.id
                    setDraggingId(item.id)
                    event.dataTransfer.effectAllowed = "move"
                    try {
                      event.dataTransfer.setData("text/plain", String(item.id))
                    } catch {
                      /* alcuni browser rifiutano setData su drag sintetici */
                    }
                  }}
                  onDragEnd={clearDrag}
                  onDragOver={(event) => {
                    const dragId = dragIdRef.current
                    if (dragId == null || dragId === item.id) return
                    const valid = dropAllowed(dragId, item.id)
                    event.preventDefault()
                    event.dataTransfer.dropEffect = valid ? "move" : "none"
                    const rect = event.currentTarget.getBoundingClientRect()
                    const after = event.clientY - rect.top > rect.height / 2
                    setDropHint({ id: item.id, after, valid })
                  }}
                  onDragLeave={() =>
                    setDropHint((prev) => (prev?.id === item.id ? null : prev))
                  }
                  onDrop={(event) => {
                    event.preventDefault()
                    const dragId = dragIdRef.current
                    const hint = dropHint
                    clearDrag()
                    if (dragId == null || dragId === item.id) return
                    if (!hint?.valid) return
                    const rect = event.currentTarget.getBoundingClientRect()
                    const after = event.clientY - rect.top > rect.height / 2
                    onReorder(dragId, item.id, after)
                  }}
                >
                  <TableCell className="py-1.5 align-top print:hidden">
                    <span
                      role="button"
                      aria-label="Trascina per riordinare la riga"
                      title="Trascina per riordinare la riga"
                      className="inline-flex size-7 cursor-grab items-center justify-center rounded text-muted-foreground/60 hover:bg-muted hover:text-foreground active:cursor-grabbing"
                      onMouseDown={() => setGrabId(item.id)}
                      onMouseUp={() => setGrabId(null)}
                    >
                      <GripVertical className="size-3.5" />
                    </span>
                  </TableCell>
                  <TableCell
                    className="py-1.5 align-top print:hidden"
                    onClick={(e) => e.stopPropagation()}
                  >
                    <Checkbox
                      checked={selectedRowIds.has(item.id)}
                      onCheckedChange={(value) =>
                        onToggleRowSelected(item.id, value === true)
                      }
                      aria-label={`Seleziona riga ${index + 1}`}
                    />
                  </TableCell>
                  <TableCell className="text-center align-top font-mono text-xs text-muted-foreground">
                    {index + 1}
                  </TableCell>

                  {(["attivita", "descrizione"] as const).map((field) => (
                    <TableCell key={field} className={TEXT_CELL_CLS}>
                      <GrowTextarea
                        data-row={item.id}
                        data-field={field}
                        value={item[field] ?? ""}
                        onChange={(event) => {
                          onPatch(item, { [field]: event.target.value })
                          acOpen(event.target, item, field)
                        }}
                        onFocus={(event) => acOpen(event.target, item, field)}
                        onBlur={() => {
                          window.setTimeout(() => {
                            if (acRef.current?.rowId === item.id) setAc(null)
                          }, 150)
                          onFlush(item)
                        }}
                        onKeyDown={acKeyDown}
                      />
                    </TableCell>
                  ))}

                  <TableCell className={TEXT_CELL_CLS}>
                    <GrowTextarea
                      data-row={item.id}
                      data-field="azione"
                      value={item.azione ?? ""}
                      onChange={(event) => onPatch(item, { azione: event.target.value })}
                      onBlur={() => onFlush(item)}
                    />
                  </TableCell>

                  <TableCell className="py-1.5 align-top">
                    <MomPrioritySelect
                      value={item.priorita}
                      onChange={(priorita) =>
                        onPatch(item, { priorita }, { immediate: true })
                      }
                    />
                  </TableCell>

                  <TableCell className={RESP_CELL_CLS}>
                    <MultiSelectCombo
                      options={poolFor(item)}
                      selectedIds={item.responsibleIds}
                      onChange={(ids) =>
                        onPatch(
                          item,
                          {
                            responsibleIds: ids,
                            resp1Id: ids[0] ?? null,
                            resp2Id: ids[1] ?? null,
                            resp3Id: ids[2] ?? null,
                          },
                          { immediate: true }
                        )
                      }
                      placeholder="—"
                      size="sm"
                      stackLabel
                      className="min-h-8 shadow-none"
                    />
                  </TableCell>

                  <TableCell className="py-1.5 align-top">
                    <DateField
                      value={item.dataCheck}
                      onChange={(value) => {
                        const patch: Partial<MoMActionItem> = { dataCheck: value }
                        if (!value) {
                          patch.dataClose = null
                        } else {
                          const end = item.dataClose?.slice(0, 10) ?? null
                          if (!end || end < value) patch.dataClose = value
                        }
                        onPatch(item, patch, { immediate: true })
                      }}
                      size="sm"
                      placeholder="—"
                      className="h-8 w-full min-w-0 shadow-none"
                    />
                  </TableCell>

                  <TableCell className="py-1.5 align-top">
                    <DateField
                      value={item.dataClose}
                      onChange={(value) =>
                        onPatch(item, { dataClose: value }, { immediate: true })
                      }
                      size="sm"
                      placeholder="—"
                      className="h-8 w-full min-w-0 shadow-none"
                      disabled={!item.dataCheck}
                      disableBefore={isoToDate(item.dataCheck)}
                    />
                  </TableCell>

                  <TableCell className="py-1.5 align-top print:hidden">
                    <div className="flex items-center justify-end">
                      <RowActionsMenu
                        size="icon-sm"
                        triggerClassName="size-7"
                        actions={[
                          {
                            label:
                              item.status === "CLOSED"
                                ? "Riapri riga"
                                : "Segna come chiusa",
                            icon: CircleCheck,
                            onClick: () =>
                              onPatch(
                                item,
                                {
                                  status:
                                    item.status === "CLOSED" ? "OPEN" : "CLOSED",
                                },
                                { immediate: true }
                              ),
                          },
                          {
                            label: item.isCritical
                              ? "Rimuovi massima priorità"
                              : "Massima priorità",
                            icon: Flag,
                            onClick: () =>
                              onPatch(
                                item,
                                { isCritical: !item.isCritical },
                                { immediate: true }
                              ),
                          },
                          {
                            label:
                              item.status === "STANDBY"
                                ? "Togli stand by"
                                : "Stand by",
                            icon: Pause,
                            onClick: () =>
                              onPatch(
                                item,
                                {
                                  status:
                                    item.status === "STANDBY" ? "OPEN" : "STANDBY",
                                },
                                { immediate: true }
                              ),
                          },
                          {
                            label: "Elimina riga",
                            icon: Trash2,
                            destructive: true,
                            separatorBefore: true,
                            onClick: () => onDelete(item),
                          },
                        ]}
                      />
                    </div>
                  </TableCell>
                </TableRow>
                ))
              )}
              <SheetNewRow onAddRow={onAddRow} />
            </TableBody>
          </Table>
        </div>
      </div>

      <p className="text-xs text-muted-foreground">
        {displayRows.length} {displayRows.length === 1 ? "riga" : "righe"}
      </p>

      {ac && ac.items.length > 0
        ? createPortal(
            <div
              role="listbox"
              aria-label="Definizioni disponibili"
              className="fixed z-50 max-h-60 overflow-auto rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
              style={{
                left: ac.rect.left,
                top: ac.rect.top,
                width: Math.max(ac.rect.width, 180),
              }}
            >
              <div className="px-2 pb-0.5 pt-1 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
                Definizioni disponibili
              </div>
              {ac.items.map((value, index) => (
                <div
                  key={value}
                  role="option"
                  aria-selected={index === ac.idx}
                  className={cn(
                    "cursor-pointer whitespace-pre-wrap break-words rounded-sm px-2 py-1.5 text-sm",
                    index === ac.idx && "bg-accent text-accent-foreground"
                  )}
                  onMouseDown={(event) => {
                    event.preventDefault()
                    acPick(index)
                  }}
                >
                  {value}
                </div>
              ))}
            </div>,
            document.body
          )
        : null}
    </div>
  )
}
