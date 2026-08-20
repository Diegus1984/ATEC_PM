import * as React from "react"
import { ArrowUpDown, Flag, Plus, Trash2 } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { DateField, ReadonlyDateField } from "@/components/shared/date-field"
import { useConfirm } from "@/components/shared/confirm"
import { ProjectStackedLabel } from "@/components/shared/project-stacked-label"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
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
import { GridScroller } from "@/components/shared/grid-scroller"
import { Textarea } from "@/components/ui/textarea"
import {
  addChecklistItem,
  deleteChecklistItem,
  updateChecklistItem,
} from "@/lib/api/checklist"
import type {
  ChecklistItem,
  ChecklistItemSaveRequest,
  ChecklistStatus,
} from "@/lib/api/types"
import { dateToIso } from "@/lib/date-iso"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"
import {
  addDaysIso,
  buildSave,
  daysFromToday,
  type ItemContainer,
  PRIORITY_META,
  priorityRowClass,
  sortChecklistItems,
} from "@/features/checklist/checklist-utils"
import { useDeferredItemOrder } from "@/lib/use-deferred-item-order"

// ── Menu «Colonne» (standard: vedi BLOCKS-RULES.md) ─────────────
// Colonne opzionali: «Attività», la spunta di selezione e «Azioni» sono la struttura
// della riga e restano fisse. Lo stato sta in un context di pagina così tutte le
// tabelle (una card per commessa/gruppo) restano allineate a un solo menu.

export const CHECKLIST_COLUMNS: { id: string; label: string }[] = [
  { id: "createdAt", label: "Inserimento" },
  { id: "priority", label: "Priorità" },
  { id: "dueDate", label: "Scadenza" },
  { id: "timeLeft", label: "Tempo residuo" },
  { id: "status", label: "Stato" },
]
const CHECKLIST_COLUMNS_DEFAULT = Object.fromEntries(
  CHECKLIST_COLUMNS.map((column) => [column.id, true])
)

interface ChecklistColumnsValue {
  visible: Record<string, boolean>
  setVisible: React.Dispatch<React.SetStateAction<Record<string, boolean>>>
}

const ChecklistColumnsContext =
  React.createContext<ChecklistColumnsValue | null>(null)

/** Visibilità colonne condivisa dalle tabelle attività della pagina. */
export function ChecklistColumnsProvider({
  children,
}: {
  children: React.ReactNode
}) {
  const [visible, setVisible] = usePersistedColumnVisibility(
    "checklist-columns-v1",
    CHECKLIST_COLUMNS_DEFAULT
  )
  const value = React.useMemo(() => ({ visible, setVisible }), [visible, setVisible])
  return (
    <ChecklistColumnsContext.Provider value={value}>
      {children}
    </ChecklistColumnsContext.Provider>
  )
}

/** Colonne accese; fuori dal provider sono tutte visibili. */
export function useChecklistColumns(): Record<string, boolean> {
  return React.useContext(ChecklistColumnsContext)?.visible ?? CHECKLIST_COLUMNS_DEFAULT
}

/** Menu «Colonne» delle attività: da mettere nella toolbar della pagina. */
export function ChecklistColumnsMenu() {
  const ctx = React.useContext(ChecklistColumnsContext)
  if (!ctx) return null
  return (
    <ColumnsMenu
      columns={CHECKLIST_COLUMNS.map(({ id, label }) => ({
        id,
        label,
        checked: ctx.visible[id] ?? true,
        onToggle: (value: boolean) =>
          ctx.setVisible((prev) => ({ ...prev, [id]: value })),
      }))}
    />
  )
}

/** Stati attività, allineati al MoM. */
const STATUS_META = [
  { value: "OPEN", label: "Aperta", dot: "bg-blue-500" },
  { value: "STANDBY", label: "Standby", dot: "bg-amber-500" },
  { value: "CLOSED", label: "Chiusa", dot: "bg-emerald-500" },
] as const

function visibleIdsKey(ids: number[]) {
  return ids.join(",")
}

function parseVisibleIds(key: string): number[] {
  if (!key) return []
  return key.split(",").map((s) => Number(s))
}

/** Selezione multipla righe (stesso pattern del foglio MoM). */
export function useChecklistRowSelection(visibleIds: number[]) {
  const visibleKey = visibleIdsKey(visibleIds)
  const [selectedRowIds, setSelectedRowIds] = React.useState<Set<number>>(
    () => new Set()
  )

  React.useEffect(() => {
    const allowed = new Set(parseVisibleIds(visibleKey))
    setSelectedRowIds((prev) => {
      const next = new Set<number>()
      let changed = false
      for (const id of prev) {
        if (allowed.has(id)) next.add(id)
        else changed = true
      }
      if (!changed && next.size === prev.size) return prev
      return next
    })
  }, [visibleKey])

  const toggleRowSelected = React.useCallback((id: number, selected: boolean) => {
    setSelectedRowIds((prev) => {
      const next = new Set(prev)
      if (selected) next.add(id)
      else next.delete(id)
      return next
    })
  }, [])

  const toggleAllRowsSelected = React.useCallback(
    (selected: boolean) => {
      const ids = parseVisibleIds(visibleKey)
      setSelectedRowIds((prev) => {
        const next = new Set(prev)
        for (const id of ids) {
          if (selected) next.add(id)
          else next.delete(id)
        }
        return next
      })
    },
    [visibleKey]
  )

  const ids = parseVisibleIds(visibleKey)
  const allVisibleSelected =
    ids.length > 0 && ids.every((id) => selectedRowIds.has(id))
  const someVisibleSelected = ids.some((id) => selectedRowIds.has(id))

  const clearSelection = React.useCallback(() => setSelectedRowIds(new Set()), [])

  return {
    selectedRowIds,
    toggleRowSelected,
    toggleAllRowsSelected,
    allVisibleSelected,
    someVisibleSelected,
    clearSelection,
  }
}

export function ChecklistBulkDeleteBar({
  count,
  onDelete,
}: {
  count: number
  onDelete: () => void
}) {
  if (count === 0) return null
  return (
    <div className="mb-2 flex justify-end">
      <Button variant="destructive" size="sm" onClick={onDelete}>
        <Trash2 />
        Elimina ({count})
      </Button>
    </div>
  )
}

export async function deleteSelectedChecklistItems(
  ids: number[],
  confirm: (opts: {
    title: string
    description: string
    confirmLabel: string
  }) => Promise<boolean>,
  onDone: () => void
) {
  if (ids.length === 0) return
  const ok = await confirm({
    title: ids.length === 1 ? "Eliminare l'attività?" : `Eliminare ${ids.length} attività?`,
    description:
      ids.length === 1
        ? "L'attività selezionata verrà rimossa definitivamente."
        : `Verranno rimosse ${ids.length} attività.`,
    confirmLabel: "Elimina",
  })
  if (!ok) return
  try {
    await Promise.all(ids.map((id) => deleteChecklistItem(id)))
    onDone()
  } catch (err) {
    notifyError(err as Error)
    onDone()
  }
}

/** Tempo residuo alla scadenza — pill bianca, solo il testo cambia colore. */
export function TempoResiduoBadge({ dueDate }: { dueDate: string | null }) {
  const n = daysFromToday(dueDate)
  const base =
    "inline-flex h-8 min-w-[5rem] items-center justify-center rounded-full border border-border bg-background px-2.5 text-sm leading-5 tabular-nums shadow-xs"

  if (n === null) {
    return <span className={cn(base, "text-muted-foreground")}>—</span>
  }

  let textTone = "text-foreground"
  let label = `+${n} gg`
  if (n < 0) {
    textTone = "text-red-700"
    label = `${n} gg`
  } else if (n === 0) {
    textTone = "text-orange-700"
    label = "Oggi"
  } else if (n <= 3) {
    textTone = "text-amber-700"
    label = `+${n} gg`
  }

  return <span className={cn(base, textTone)}>{label}</span>
}

function PrioritySelect({
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
        // Campo piatto a riposo, aspetto da input solo all'interazione (pattern foglio SAL/DDP).
        className="h-8 min-w-[8.5rem] gap-1.5 border-transparent bg-transparent px-2 font-normal shadow-none hover:border-input focus-visible:border-input focus-visible:bg-background"
      >
        <SelectValue />
      </SelectTrigger>
      <SelectContent align="start">
        {PRIORITY_META.map((p) => (
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

function StatusSelect({
  value,
  onChange,
}: {
  value: ChecklistStatus
  onChange: (status: ChecklistStatus) => void
}) {
  return (
    <Select value={value} onValueChange={(v) => onChange(v as ChecklistStatus)}>
      <SelectTrigger
        size="sm"
        aria-label="Stato"
        className="h-8 min-w-[7rem] gap-1.5 border-transparent bg-transparent px-2 font-normal shadow-none hover:border-input focus-visible:border-input focus-visible:bg-background"
      >
        <SelectValue />
      </SelectTrigger>
      <SelectContent align="start">
        {STATUS_META.map((s) => (
          <SelectItem key={s.value} value={s.value} textValue={s.label}>
            <span className="flex items-center gap-2">
              <span className={cn("size-2 shrink-0 rounded-full", s.dot)} />
              <span className="text-muted-foreground">{s.label}</span>
            </span>
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

function RescheduleInput({ onApply }: { onApply: (days: number) => void }) {
  const [rip, setRip] = React.useState("")

  function apply() {
    const n = parseInt(rip, 10)
    if (Number.isNaN(n) || n === 0) {
      setRip("")
      return
    }
    setRip("")
    onApply(n)
  }

  return (
    <Input
      value={rip}
      onChange={(e) => setRip(e.target.value)}
      onBlur={apply}
      onKeyDown={(e) => {
        if (e.key === "Enter") {
          e.preventDefault()
          apply()
        }
      }}
      inputMode="numeric"
      placeholder="±"
      className="h-7 w-10 shrink-0 px-1 text-center font-mono text-xs"
      title="Sposta la scadenza di N giorni (Invio per applicare)"
    />
  )
}

/**
 * Textarea che cresce con il contenuto: la riga si adatta all'altezza del testo, anche
 * quando non è in modifica (prima restava alta 32px e il testo a capo veniva tagliato).
 * Stesso comportamento della tabella Milestone (`field-sizing-content`).
 * Invio = conferma, Shift+Invio = a capo.
 */
export function AutoTextarea({
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
        "focus-visible:ring-1 focus-visible:ring-ring/40",
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

/**
 * Contenitore dell'attività mostrato nelle viste aggregate. Per le commesse i dati
 * stanno su righe separate (codice / cliente / titolo) invece che in un'unica riga
 * lunga: la colonna resta stretta e non serve tagliare il testo.
 */
export interface ChecklistRowContainer {
  /** Etichetta piatta: usata per i gruppi generici e come fallback. */
  label: string
  code?: string
  customer?: string
  title?: string
}

/**
 * Cella "Commessa / Gruppo": impila codice, cliente e titolo su righe distinte.
 * Prima era una riga sola troncata a metà parola e la colonna doveva restare larga.
 */
function ContainerCell({ container }: { container?: ChecklistRowContainer }) {
  if (!container) {
    return null
  }
  return (
    <ProjectStackedLabel
      code={container.code}
      customer={container.customer}
      title={container.title}
      fallback={container.label}
      className="w-full max-w-[190px] rounded-md border bg-muted px-2 py-1"
    />
  )
}

/** Riga attività editabile inline. Riusata sia nelle tabelle per-container sia nelle viste priorità. */
export function ChecklistRow({
  item,
  onMutated,
  showContainer = false,
  container,
  selected = false,
  onSelectedChange,
}: {
  item: ChecklistItem
  onMutated: () => void
  showContainer?: boolean
  container?: ChecklistRowContainer
  selected?: boolean
  onSelectedChange?: (selected: boolean) => void
}) {
  const confirm = useConfirm()
  const visible = useChecklistColumns()
  const [desc, setDesc] = React.useState(item.description)
  React.useEffect(() => setDesc(item.description), [item.description])

  async function patch(p: Partial<ChecklistItemSaveRequest>) {
    try {
      await updateChecklistItem(item.id, buildSave(item, p))
      onMutated()
    } catch (err) {
      notifyError(err as Error)
      onMutated()
    }
  }

  async function commitDesc() {
    const v = desc.trim()
    if (v === item.description) return
    await patch({ description: v })
  }

  async function remove() {
    const ok = await confirm({
      title: "Eliminare l'attività?",
      description: item.description.trim() || "(attività senza descrizione)",
      confirmLabel: "Elimina",
    })
    if (!ok) return
    try {
      await deleteChecklistItem(item.id)
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  function applyRip(days: number) {
    void patch({ dueDate: addDaysIso(item.dueDate, days) })
  }

  return (
    <TableRow
      className={cn(
        "border-0 transition-colors",
        item.status === "CLOSED"
          ? "bg-muted/30 opacity-70"
          : priorityRowClass(item.priority, item.isCritical)
      )}
    >
      <TableCell
        className="w-10 py-1.5 align-middle"
        onClick={(e) => e.stopPropagation()}
      >
        <Checkbox
          checked={selected}
          onCheckedChange={(value) => onSelectedChange?.(value === true)}
          aria-label="Seleziona attività"
        />
      </TableCell>
      {showContainer ? (
        <TableCell className="align-middle">
          <ContainerCell container={container} />
        </TableCell>
      ) : null}
      <TableCell className="whitespace-normal py-1.5 align-middle">
        <AutoTextarea
          value={desc}
          onChange={setDesc}
          onCommit={() => void commitDesc()}
          placeholder="Descrizione attività"
          className={cn(
            "border-transparent bg-transparent hover:border-input focus-visible:border-input focus-visible:bg-background",
            item.isCritical && "font-semibold text-red-800",
            item.status === "CLOSED" && "text-muted-foreground line-through"
          )}
        />
      </TableCell>
      {visible.createdAt && (
        <TableCell className="w-52 py-1.5 align-middle">
          <ReadonlyDateField
            value={item.createdAt}
            size="sm"
            className="min-w-0 border-transparent bg-transparent shadow-none"
          />
        </TableCell>
      )}
      {visible.priority && (
        <TableCell className="w-36 py-1.5 align-middle">
          <PrioritySelect
            value={item.priority}
            onChange={(priority) => void patch({ priority })}
          />
        </TableCell>
      )}
      {visible.dueDate && (
        <TableCell className="w-52 py-1.5 align-middle">
          <DateField
            value={item.dueDate}
            onChange={(v) => void patch({ dueDate: v })}
            size="sm"
            placeholder="—"
            className="h-8 w-full min-w-0 border-transparent bg-transparent shadow-none hover:border-input focus-visible:border-input focus-visible:bg-background"
          />
        </TableCell>
      )}
      {visible.timeLeft && (
        <TableCell className="w-28 py-1.5 text-center align-middle">
          <TempoResiduoBadge dueDate={item.dueDate} />
        </TableCell>
      )}
      {visible.status && (
        <TableCell className="w-32 py-1.5 align-middle">
          <StatusSelect
            value={item.status}
            onChange={(status) => void patch({ status })}
          />
        </TableCell>
      )}
      <TableCell className="w-28 py-1.5 align-middle">
        <div className="flex items-center justify-end gap-1">
          <RescheduleInput onApply={applyRip} />
          <RowActionsMenu
            size="icon-sm"
            triggerClassName="size-7"
            actions={[
              {
                label: item.isCritical
                  ? "Rimuovi ultra-critica"
                  : "Segna ultra-critica",
                icon: Flag,
                onClick: () => void patch({ isCritical: !item.isCritical }),
              },
              {
                label: "Elimina attività",
                icon: Trash2,
                destructive: true,
                separatorBefore: true,
                onClick: () => void remove(),
              },
            ]}
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

/** Riga "+ Nuova attività" in coda alla tabella di un container. */
function NewItemRow({
  container,
  onMutated,
  colSpanBefore = 0,
}: {
  container: ItemContainer
  onMutated: () => void
  colSpanBefore?: number
}) {
  const visible = useChecklistColumns()
  // Colonne dati effettive: Attività + opzionali accese + Azioni.
  const dataCols =
    2 + CHECKLIST_COLUMNS.filter((column) => visible[column.id] ?? true).length
  const [desc, setDesc] = React.useState("")
  const committing = React.useRef(false)

  async function commit() {
    const v = desc.trim()
    if (!v || committing.current) return
    committing.current = true
    try {
      await addChecklistItem({
        projectId: container.projectId ?? null,
        groupId: container.groupId ?? null,
        description: v,
        priority: 0,
        dueDate: dateToIso(new Date()),
        isCritical: false,
      })
      setDesc("")
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    } finally {
      committing.current = false
    }
  }

  return (
    <TableRow className="border-0 bg-muted/40 hover:bg-muted/50">
      <TableCell className="w-10" />
      <TableCell
        colSpan={colSpanBefore > 0 ? dataCols + 1 - colSpanBefore : dataCols}
        className="py-1.5"
      >
        <div className="flex items-center gap-2">
          <Plus className="size-4 text-muted-foreground" />
          <Input
            value={desc}
            onChange={(e) => setDesc(e.target.value)}
            onBlur={() => void commit()}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault()
                void commit()
              }
            }}
            placeholder="Nuova attività…"
            className="h-8 border-dashed"
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

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

/** Card contenitore commessa/gruppo — stesso pattern della board globale. */
export function ChecklistContainerCard({
  title,
  count,
  accent,
  headerExtra,
  children,
}: {
  title: React.ReactNode
  count: number
  accent: string
  headerExtra?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <Card className="overflow-hidden py-0">
      <CardHeader className="flex flex-row items-center gap-3 border-b bg-muted/40 py-3">
        <span className={cn("h-6 w-1.5 rounded", accent)} />
        <CardTitle className="text-base">{title}</CardTitle>
        <span className="rounded-md border bg-background px-2 py-0.5 font-mono text-xs text-muted-foreground">
          {count}
        </span>
        <div className="ml-auto flex items-center gap-1">{headerExtra}</div>
      </CardHeader>
      <CardContent className="p-3">{children}</CardContent>
    </Card>
  )
}

/** Tabella attività di un singolo container (commessa o gruppo) con nuova riga in coda. */
export function ChecklistTable({
  items,
  container,
  onMutated,
  layoutEpoch = 0,
  className,
}: {
  items: ChecklistItem[]
  container: ItemContainer
  onMutated: () => void
  /** Incrementato da «Aggiorna»: riapplica l'ordinamento priorità/data. */
  layoutEpoch?: number
  className?: string
}) {
  const confirm = useConfirm()
  const visible = useChecklistColumns()
  const dataCols =
    2 + CHECKLIST_COLUMNS.filter((column) => visible[column.id] ?? true).length
  const [sortKey, setSortKey] = React.useState<"priority" | "date">("priority")
  const sortItems = React.useCallback(
    (list: ChecklistItem[]) => sortChecklistItems(list, sortKey),
    [sortKey]
  )
  const sorted = useDeferredItemOrder(items, sortItems, layoutEpoch, sortKey)
  const visibleIds = React.useMemo(() => sorted.map((item) => item.id), [sorted])
  const {
    selectedRowIds,
    toggleRowSelected,
    toggleAllRowsSelected,
    allVisibleSelected,
    someVisibleSelected,
    clearSelection,
  } = useChecklistRowSelection(visibleIds)

  async function deleteSelected() {
    const ids = [...selectedRowIds]
    await deleteSelectedChecklistItems(ids, confirm, () => {
      clearSelection()
      onMutated()
    })
  }

  return (
    <div className={cn("space-y-0", className)}>
      <ChecklistBulkDeleteBar
        count={selectedRowIds.size}
        onDelete={() => void deleteSelected()}
      />
      <GridScroller className="rounded-lg border">
      <Table className="border-separate border-spacing-y-1.5">
        <TableHeader className="bg-muted/40">
          <TableRow>
            <TableHead className="w-10 px-0 text-center">
              <Checkbox
                checked={
                  allVisibleSelected
                    ? true
                    : someVisibleSelected
                      ? "indeterminate"
                      : false
                }
                onCheckedChange={(value) =>
                  toggleAllRowsSelected(value === true)
                }
                aria-label="Seleziona tutte le attività visibili"
                disabled={sorted.length === 0}
              />
            </TableHead>
            <TableHead>Attività</TableHead>
            {visible.createdAt && (
              <TableHead className="w-52">Inserimento</TableHead>
            )}
            {visible.priority && (
              <TableHead className="w-36">
                <SortableHead
                  active={sortKey === "priority"}
                  label="Priorità"
                  onClick={() => setSortKey("priority")}
                />
              </TableHead>
            )}
            {visible.dueDate && (
              <TableHead className="w-52">
                <SortableHead
                  active={sortKey === "date"}
                  label="Scadenza"
                  onClick={() => setSortKey("date")}
                />
              </TableHead>
            )}
            {visible.timeLeft && (
              <TableHead className="w-28 text-center">Tempo residuo</TableHead>
            )}
            {visible.status && <TableHead className="w-32">Stato</TableHead>}
            <TableHead className="w-28 text-right">Azioni</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {sorted.length === 0 ? (
            <TableRow>
              <TableCell
                colSpan={dataCols + 1}
                className="py-6 text-center text-sm text-muted-foreground"
              >
                Nessuna attività in questa vista.
              </TableCell>
            </TableRow>
          ) : (
            sorted.map((item) => (
              <ChecklistRow
                key={item.id}
                item={item}
                onMutated={onMutated}
                selected={selectedRowIds.has(item.id)}
                onSelectedChange={(value) => toggleRowSelected(item.id, value)}
              />
            ))
          )}
          <NewItemRow container={container} onMutated={onMutated} />
        </TableBody>
      </Table>
      </GridScroller>
    </div>
  )
}
