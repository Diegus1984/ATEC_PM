import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import {
  ArrowDownToLine,
  ArrowUpToLine,
  Flag,
  GripVertical,
  Plus,
  Power,
  Printer,
  Trash2,
} from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { DateField } from "@/components/shared/date-field"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
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
  createMilestone,
  deleteMilestone,
  fetchActiveActivityCatalog,
  reorderMilestones,
  updateMilestone,
} from "@/lib/api/milestones"
import { printMilestoneTable } from "@/features/milestones/milestone-print"
import type { Milestone, MilestoneSaveRequest } from "@/lib/api/types"
import { formatDateShort, isoToDate } from "@/lib/date-iso"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"
import {
  buildMilestoneSave,
  msStatus,
  statusRowClass,
  weekLabel,
  weekTot,
} from "@/features/milestones/milestone-utils"

/**
 * Colonne opzionali del menu «Colonne» (standard: vedi BLOCKS-RULES.md).
 * «#», «Attività / Descrizione» e «Azioni» sono la struttura della riga e restano fisse.
 */
const MILESTONE_COLUMNS: { id: string; label: string }[] = [
  { id: "weekStart", label: "W.In" },
  { id: "dateStart", label: "Data Inizio" },
  { id: "weekEnd", label: "W.Fine" },
  { id: "dateEnd", label: "Data Fine" },
  { id: "weekTot", label: "W.Tot" },
  { id: "progress", label: "Avanzamento" },
  { id: "notes", label: "Note" },
]
const MILESTONE_COLUMNS_DEFAULT = Object.fromEntries(
  MILESTONE_COLUMNS.map((column) => [column.id, true])
)

/** Milestone vuota, per creazione in coda o inserimento in posizione. */
const EMPTY_MILESTONE: MilestoneSaveRequest = {
  descrizione: "",
  dataInizio: null,
  dataFine: null,
  avanzamento: null,
  note: "",
  evidenza: false,
  spento: false,
  rowVersion: null,
}

type DropHint = { id: number; after: boolean }

/** Chip della settimana ISO derivata (sola lettura). Sfondo viola tenue, distinto dalle tinte
 *  di stato riga (teal/blu/rosso) e dal campo data, così non si confonde. */
function WeekChip({ label }: { label: string }) {
  return (
    <span className="inline-flex h-6 min-w-[2.75rem] items-center justify-center rounded-md border border-violet-200 bg-violet-100 px-1.5 font-mono text-xs text-violet-700">
      {label || "—"}
    </span>
  )
}

/** Textarea che cresce con il contenuto: la riga si adatta all'altezza del testo (Attività/Note).
 *  Invio = conferma, Shift+Invio = a capo. */
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

/**
 * Testo di una cella in sola lettura. Tiene la stessa metrica della textarea editabile
 * (min-h-8, px-2/py-1, leading-5) così la riga non cambia altezza fra chi può scrivere
 * e chi legge soltanto, e manda a capo come faceva il campo.
 */
function ReadOnlyCellText({
  value,
  className,
}: {
  value: string
  className?: string
}) {
  return (
    <div
      className={cn(
        "min-h-8 whitespace-pre-wrap px-2 py-1 text-sm leading-5",
        className
      )}
    >
      {value || "—"}
    </div>
  )
}

function AvanzCell({
  value,
  onCommit,
  readOnly,
}: {
  value: number | null
  onCommit: (v: number | null) => void
  readOnly: boolean
}) {
  const [text, setText] = React.useState(value == null ? "" : String(value))
  React.useEffect(() => {
    setText(value == null ? "" : String(value))
  }, [value])

  function commit() {
    const trimmed = text.trim()
    if (trimmed === "") {
      if (value !== null) onCommit(null)
      return
    }
    let n = Math.round(Number(trimmed))
    if (Number.isNaN(n)) {
      setText(value == null ? "" : String(value))
      return
    }
    n = Math.max(0, Math.min(100, n))
    if (n !== value) onCommit(n)
    else setText(String(n))
  }

  const pct = value ?? 0
  return (
    <div className="flex items-center gap-2">
      {readOnly ? (
        // Stessa larghezza del campo: la barra resta allineata con le altre righe.
        <span className="flex h-8 w-14 items-center justify-center text-sm tabular-nums">
          {value == null ? "—" : `${value}%`}
        </span>
      ) : (
        <Input
          value={text}
          onChange={(e) => setText(e.target.value)}
          onBlur={commit}
          onKeyDown={(e) => {
            if (e.key === "Enter") e.currentTarget.blur()
          }}
          inputMode="numeric"
          placeholder="—"
          // Campo piatto a riposo, aspetto da input solo all'interazione (come le textarea).
          className="h-8 w-14 border-transparent bg-transparent px-1 text-center tabular-nums shadow-none placeholder:text-current placeholder:opacity-60 hover:border-input focus-visible:border-input focus-visible:bg-background focus-visible:placeholder:text-muted-foreground focus-visible:placeholder:opacity-100"
        />
      )}
      <div className="h-1.5 w-full min-w-10 overflow-hidden rounded bg-muted">
        <div
          className={cn("h-full", pct >= 100 ? "bg-teal-500" : "bg-primary")}
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  )
}

function MilestoneRow({
  m,
  index,
  visible,
  readOnly,
  onMutated,
  onInsert,
  grabId,
  setGrabId,
  draggingId,
  dropHint,
  onDragStart,
  onDragEnd,
  onDragOver,
  onDragLeave,
  onDrop,
}: {
  m: Milestone
  index: number
  visible: Record<string, boolean>
  /** Sola lettura: niente editing inline, niente riordino, niente menu riga. */
  readOnly: boolean
  onMutated: () => void
  onInsert: (where: "above" | "below") => void
  grabId: number | null
  setGrabId: (id: number | null) => void
  draggingId: number | null
  dropHint: DropHint | null
  onDragStart: (e: React.DragEvent) => void
  onDragEnd: () => void
  onDragOver: (e: React.DragEvent) => void
  onDragLeave: () => void
  onDrop: (e: React.DragEvent) => void
}) {
  const confirm = useConfirm()
  const [desc, setDesc] = React.useState(m.descrizione)
  const [note, setNote] = React.useState(m.note)
  React.useEffect(() => setDesc(m.descrizione), [m.descrizione])
  React.useEffect(() => setNote(m.note), [m.note])

  async function patch(p: Partial<MilestoneSaveRequest>) {
    // Guardia in testa alla callback come nelle altre pagine in sola lettura: i
    // controlli editabili non esistono più, ma la scrittura non deve poter partire.
    if (readOnly) return
    try {
      await updateMilestone(m.id, buildMilestoneSave(m, p))
      onMutated()
    } catch (err) {
      notifyError(err as Error)
      onMutated()
    }
  }

  function commitDesc() {
    const v = desc.trim()
    if (v === m.descrizione) return
    void patch({ descrizione: v })
  }
  function commitNote() {
    if (note === m.note) return
    void patch({ note })
  }

  // Regola date range: se la fine resta precedente al nuovo inizio, la si allinea all'inizio.
  function changeInizio(v: string | null) {
    let fine = m.dataFine
    if (v && fine) {
      const s = isoToDate(v)
      const f = isoToDate(fine)
      if (s && f && f < s) fine = v
    }
    void patch({ dataInizio: v, dataFine: fine })
  }

  async function remove() {
    if (readOnly) return
    const ok = await confirm({
      title: "Eliminare la milestone?",
      description: m.descrizione.trim() || "(milestone senza descrizione)",
      confirmLabel: "Elimina",
    })
    if (!ok) return
    try {
      await deleteMilestone(m.id)
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  const status = msStatus(m)

  return (
    <TableRow
      // Il riordino è una scrittura (`reorderMilestones`): in sola lettura la riga non
      // parte nemmeno, oltre a non avere più la maniglia da afferrare.
      draggable={!readOnly && grabId === m.id}
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      className={cn(
        "border-0 transition-colors",
        // L'urgenza vince sul colore di stato: campisce tutta la riga in rosa con la
        // barretta rossa a sinistra (prima si tingeva di rosso la sola descrizione,
        // che in una tabella lunga non si notava).
        // Nessun caso «spento»: le righe spente non arrivano più qui (vedi SpenteDialog).
        m.evidenza
          ? "bg-rose-50/70 hover:bg-rose-100/70 shadow-[inset_0_0_0_1px_theme(colors.rose.200),inset_3px_0_0_0_theme(colors.red.500)] dark:bg-red-950/25"
          : statusRowClass(status),
        draggingId === m.id && "opacity-50",
        dropHint?.id === m.id &&
          !dropHint.after &&
          "[&>td]:shadow-[inset_0_2px_0_0_var(--primary)]",
        dropHint?.id === m.id &&
          dropHint.after &&
          "[&>td]:shadow-[inset_0_-2px_0_0_var(--primary)]"
      )}
    >
      <TableCell className="w-16 py-1.5 align-top">
        <div className="flex items-center gap-1">
          {!readOnly && (
            <span
              role="button"
              aria-label="Trascina per riordinare"
              title="Trascina per riordinare"
              className="inline-flex size-6 cursor-grab items-center justify-center rounded text-muted-foreground/60 hover:bg-muted hover:text-foreground active:cursor-grabbing"
              onMouseDown={() => setGrabId(m.id)}
              onMouseUp={() => setGrabId(null)}
            >
              <GripVertical className="size-3.5" />
            </span>
          )}
          <span className="text-xs tabular-nums text-muted-foreground">
            {String(index + 1).padStart(2, "0")}
          </span>
        </div>
      </TableCell>

      <TableCell className="min-w-[16rem] py-1.5 align-top">
        {readOnly ? (
          <ReadOnlyCellText
            value={m.descrizione}
            className={cn(m.evidenza && "font-semibold text-red-800")}
          />
        ) : (
          <GrowTextarea
            value={desc}
            onChange={setDesc}
            onCommit={commitDesc}
            placeholder="Descrizione attività"
            className={cn(
              "border-transparent bg-transparent focus-visible:border-input focus-visible:bg-background",
              m.evidenza && "font-semibold text-red-800"
            )}
          />
        )}
      </TableCell>

      {visible.weekStart && (
        <TableCell className="w-14 py-1.5 align-top">
          <div className="flex h-8 items-center justify-center">
            <WeekChip label={weekLabel(m.dataInizio)} />
          </div>
        </TableCell>
      )}
      {visible.dateStart && (
        <TableCell className="w-48 py-1.5 align-top">
          {readOnly ? (
            <div className="flex h-8 items-center px-1 text-sm tabular-nums">
              {formatDateShort(m.dataInizio) || "—"}
            </div>
          ) : (
            <DateField
              value={m.dataInizio}
              onChange={changeInizio}
              size="sm"
              segmented
              flat
              placeholder="—"
              className="h-8 w-full min-w-0 shadow-none"
            />
          )}
        </TableCell>
      )}

      {visible.weekEnd && (
        <TableCell className="w-14 py-1.5 align-top">
          <div className="flex h-8 items-center justify-center">
            <WeekChip label={weekLabel(m.dataFine)} />
          </div>
        </TableCell>
      )}
      {visible.dateEnd && (
        <TableCell className="w-48 py-1.5 align-top">
          {readOnly ? (
            <div className="flex h-8 items-center px-1 text-sm tabular-nums">
              {formatDateShort(m.dataFine) || "—"}
            </div>
          ) : (
            <DateField
              value={m.dataFine}
              onChange={(v) => void patch({ dataFine: v })}
              size="sm"
              segmented
              flat
              placeholder="—"
              disabled={!m.dataInizio}
              disableBefore={isoToDate(m.dataInizio)}
              className="h-8 w-full min-w-0 shadow-none"
            />
          )}
        </TableCell>
      )}

      {visible.weekTot && (
        <TableCell className="w-12 py-1.5 align-top">
          <div className="flex h-8 items-center justify-center">
            <span className="text-base font-medium tabular-nums text-foreground">
              {weekTot(m.dataInizio, m.dataFine) || "—"}
            </span>
          </div>
        </TableCell>
      )}

      {visible.progress && (
        <TableCell className="w-40 py-1.5 align-top">
          <AvanzCell
            value={m.avanzamento}
            onCommit={(v) => void patch({ avanzamento: v })}
            readOnly={readOnly}
          />
        </TableCell>
      )}

      {visible.notes && (
        <TableCell className="min-w-[10rem] py-1.5 align-top">
          {readOnly ? (
            <ReadOnlyCellText value={note} />
          ) : (
            <GrowTextarea
              value={note}
              onChange={setNote}
              onCommit={commitNote}
              placeholder="Note"
              className="border-transparent bg-transparent focus-visible:border-input focus-visible:bg-background"
            />
          )}
        </TableCell>
      )}

      {/* La colonna resta (larghezza e allineamento della griglia invariati), ma in sola
          lettura non ha comandi da offrire: tutte le voci del menu sono scritture. */}
      <TableCell className="w-12 py-1.5 align-top">
        <div className="flex justify-end">
          {!readOnly && (
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
                  label: m.evidenza ? "Togli evidenza" : "Evidenzia (urgenza)",
                  icon: Flag,
                  separatorBefore: true,
                  onClick: () => void patch({ evidenza: !m.evidenza }),
                },
                {
                  // Solo «spegni»: qui arrivano le sole righe attive. Per riaccenderle
                  // c'è il dialogo «Spente» in testata alla tabella.
                  label: "Spegni riga",
                  icon: Power,
                  onClick: () => void patch({ spento: true }),
                },
                {
                  label: "Elimina milestone",
                  icon: Trash2,
                  destructive: true,
                  separatorBefore: true,
                  onClick: () => void remove(),
                },
              ]}
            />
          )}
        </div>
      </TableCell>
    </TableRow>
  )
}

/**
 * Barra sottile fra due righe: un clic inserisce una milestone vuota in QUELLA posizione.
 * Resta invisibile finché non ci si passa sopra, così non appesantisce la tabella.
 * Il menu riga «Inserisci sopra/sotto» continua a esistere: questa è la scorciatoia
 * del prototipo, che rende ovvio dove finirà la riga nuova.
 */
function InsertBar({
  colSpan,
  onInsert,
}: {
  colSpan: number
  onInsert: () => void
}) {
  return (
    <TableRow className="group/ins border-0 hover:bg-transparent">
      <TableCell colSpan={colSpan} className="h-0 p-0">
        <button
          type="button"
          aria-label="Inserisci attività qui"
          title="Inserisci attività qui"
          className="flex h-2 w-full items-center justify-center opacity-0 transition-opacity focus-visible:opacity-100 group-hover/ins:opacity-100"
          onClick={onInsert}
        >
          <span className="h-px flex-1 bg-primary/40" />
          <span className="mx-2 inline-flex items-center gap-1 whitespace-nowrap rounded-full border border-primary/40 bg-background px-1.5 py-0.5 text-[10px] font-medium text-primary">
            <Plus className="size-2.5" />
            Inserisci attività
          </span>
          <span className="h-px flex-1 bg-primary/40" />
        </button>
      </TableCell>
    </TableRow>
  )
}

/**
 * Anagrafica spegnimenti: le righe spente spariscono dalla tabella (scelta di prodotto,
 * come nel prototipo), quindi serve un posto da cui rivederle e riattivarle.
 */
function SpenteDialog({
  open,
  onOpenChange,
  items,
  readOnly,
  onMutated,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Elenco completo: serve per mostrare il numero di riga originale. */
  items: Milestone[]
  /** Sola lettura: l'elenco si consulta, ma «Riattiva» (una scrittura) sparisce. */
  readOnly: boolean
  onMutated: () => void
}) {
  const spente = items
    .map((m, i) => ({ m, nr: i + 1 }))
    .filter(({ m }) => m.spento)

  async function riattiva(m: Milestone) {
    if (readOnly) return
    try {
      await updateMilestone(m.id, buildMilestoneSave(m, { spento: false }))
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Righe spente</DialogTitle>
          <DialogDescription>
            Sono escluse dalla tabella, dal Gantt, dalla stampa e dall'avanzamento medio.
            Il numero è la posizione che avevano nell'elenco.
          </DialogDescription>
        </DialogHeader>
        {spente.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            Nessuna riga spenta.
          </p>
        ) : (
          <ul className="max-h-80 space-y-1 overflow-y-auto">
            {spente.map(({ m, nr }) => (
              <li
                key={m.id}
                className="flex items-center gap-2 rounded-md border px-2 py-1.5"
              >
                <span className="w-8 shrink-0 text-xs tabular-nums text-muted-foreground">
                  {String(nr).padStart(2, "0")}
                </span>
                <span className="min-w-0 flex-1 truncate text-sm">
                  {m.descrizione || "(Nessuna descrizione)"}
                </span>
                {!readOnly && (
                  <Button
                    variant="outline"
                    size="sm"
                    className="h-7 shrink-0 gap-1 text-xs"
                    onClick={() => void riattiva(m)}
                  >
                    <Power className="size-3" />
                    Riattiva
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </DialogContent>
    </Dialog>
  )
}

function NewMilestoneRow({
  projectId,
  colSpan,
  onMutated,
}: {
  projectId: number
  colSpan: number
  onMutated: () => void
}) {
  const [desc, setDesc] = React.useState("")
  const committing = React.useRef(false)

  const catalogQuery = useQuery({
    queryKey: ["activity-catalog", "active"],
    queryFn: fetchActiveActivityCatalog,
    staleTime: 60000,
  })

  const catalogItems = catalogQuery.data ?? []

  async function commit() {
    const v = desc.trim()
    if (!v || committing.current) return
    committing.current = true
    try {
      await createMilestone(projectId, { ...EMPTY_MILESTONE, descrizione: v })
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
      <TableCell className="w-16" />
      <TableCell colSpan={colSpan} className="py-1.5">
        <div className="flex items-center gap-2">
          <Plus className="size-4 text-muted-foreground" />
          <Input
            list="activity-catalog-list"
            value={desc}
            onChange={(e) => setDesc(e.target.value)}
            onBlur={() => void commit()}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault()
                void commit()
              }
            }}
            placeholder="Aggiungi attività (digita o scegli dal catalogo)…"
            className="h-8 max-w-md border-dashed"
          />
          <datalist id="activity-catalog-list">
            {catalogItems.map((item) => (
              <option key={item.id} value={item.label} />
            ))}
          </datalist>
        </div>
      </TableCell>
    </TableRow>
  )
}

/** Tabella milestone di una commessa: righe editabili inline, riordino drag&drop,
 *  «inserisci in posizione» dal menu riga, e riga «aggiungi» in coda. */
export function MilestoneTable({
  projectId,
  items,
  onMutated,
  projectCode,
  projectTitle,
  readOnly = false,
}: {
  projectId: number
  items: Milestone[]
  onMutated: () => void
  /** Codice commessa: testata della stampa. */
  projectCode?: string
  /** Descrizione commessa: testata della stampa. */
  projectTitle?: string
  /** Funzione concessa in sola lettura: restano griglia, colonne, stampa e dialogo
   *  «Spente»; spariscono editing inline, riordino, inserimenti e menu riga. */
  readOnly?: boolean
}) {
  const [visible, setVisible] = usePersistedColumnVisibility(
    "milestones-table-columns-v1",
    MILESTONE_COLUMNS_DEFAULT
  )
  const columnToggles = MILESTONE_COLUMNS.map(({ id, label }) => ({
    id,
    label,
    checked: visible[id] ?? true,
    onToggle: (value: boolean) =>
      setVisible((prev) => ({ ...prev, [id]: value })),
  }))
  // # + Attività + colonne accese (Azioni è fuori dallo span della riga «aggiungi»).
  const addRowColSpan =
    1 + MILESTONE_COLUMNS.filter((column) => visible[column.id] ?? true).length + 1

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

  function handleReorder(dragId: number, targetId: number, after: boolean) {
    // Il riordino scrive: in sola lettura le righe non sono trascinabili, ma la
    // guardia regge anche un drop arrivato da fuori la griglia.
    if (readOnly || dragId === targetId) return
    const ids = items.map((m) => m.id)
    const from = ids.indexOf(dragId)
    if (from === -1) return
    ids.splice(from, 1)
    const targetIndex = ids.indexOf(targetId)
    if (targetIndex === -1) return
    ids.splice(after ? targetIndex + 1 : targetIndex, 0, dragId)
    void reorderMilestones(projectId, ids)
      .then(onMutated)
      .catch((err) => notifyError(err as Error))
  }

  // Inserisce una milestone vuota sopra/sotto la riga index: la crea (in coda) e la riordina nello slot.
  async function insert(index: number, where: "above" | "below") {
    if (readOnly) return
    try {
      const newId = await createMilestone(projectId, EMPTY_MILESTONE)
      const ids = items.map((m) => m.id)
      ids.splice(where === "below" ? index + 1 : index, 0, newId)
      await reorderMilestones(projectId, ids)
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  const [spenteOpen, setSpenteOpen] = React.useState(false)

  // Le righe spente spariscono dalla tabella (D1, come nel prototipo): si rivedono e si
  // riattivano dal dialogo «Spente». La numerazione resta quella dell'elenco completo,
  // quindi al posto di una riga spenta si vede un buco — è il modo di accorgersene.
  const activeItems = items.filter((m) => !m.spento)
  const visibleRows = items
    .map((m, index) => ({ m, index }))
    .filter(({ m }) => !m.spento)
  const spenteCount = items.length - activeItems.length

  // Stampa dalla vista Tabella: prima esisteva solo dentro il menu del Gantt, quindi
  // da qui non si poteva stampare senza passare dall'altra scheda.
  function handlePrint() {
    printMilestoneTable({
      projectCode,
      projectTitle,
      milestones: activeItems,
      allMilestones: items,
      onError: (msg) => notifyError(msg, msg),
    })
  }

  return (
    <div className="space-y-2">
      <div className="flex justify-end gap-2">
        {spenteCount > 0 && (
          <Button
            variant="outline"
            size="sm"
            className="h-8 gap-1.5 border-dashed text-xs"
            title={
              readOnly
                ? "Rivedi le righe spente"
                : "Rivedi e riattiva le righe spente"
            }
            onClick={() => setSpenteOpen(true)}
          >
            <Power className="size-3.5" />
            Spente ({spenteCount})
          </Button>
        )}
        <Button
          variant="outline"
          size="sm"
          className="h-8 gap-1.5 text-xs"
          disabled={activeItems.length === 0}
          onClick={handlePrint}
        >
          <Printer className="size-3.5" />
          Stampa Tabella (A4)
        </Button>
        <ColumnsMenu columns={columnToggles} />
      </div>

      <SpenteDialog
        open={spenteOpen}
        onOpenChange={setSpenteOpen}
        items={items}
        readOnly={readOnly}
        onMutated={onMutated}
      />
      <GridScroller className="rounded-lg border">
      <Table className="border-separate border-spacing-y-1.5">
        <TableHeader className="bg-muted/40">
          <TableRow>
            <TableHead className="w-16">#</TableHead>
            <TableHead className="min-w-[16rem]">Attività / Descrizione</TableHead>
            {visible.weekStart && (
              <TableHead className="w-14 text-center">W.In</TableHead>
            )}
            {visible.dateStart && <TableHead className="w-48">Data Inizio</TableHead>}
            {visible.weekEnd && (
              <TableHead className="w-14 text-center">W.Fine</TableHead>
            )}
            {visible.dateEnd && <TableHead className="w-48">Data Fine</TableHead>}
            {visible.weekTot && (
              <TableHead className="w-12 text-center">W.Tot</TableHead>
            )}
            {visible.progress && <TableHead className="w-40">Avanzamento</TableHead>}
            {visible.notes && <TableHead className="min-w-[10rem]">Note</TableHead>}
            <TableHead className="w-12 text-right">Azioni</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {visibleRows.map(({ m, index }) => (
            <React.Fragment key={m.id}>
            {/* La barra «Inserisci attività» crea una riga: in sola lettura non c'è. */}
            {!readOnly && (
              <InsertBar
                colSpan={1 + addRowColSpan}
                onInsert={() => void insert(index, "above")}
              />
            )}
            <MilestoneRow
              m={m}
              index={index}
              visible={visible}
              readOnly={readOnly}
              onMutated={onMutated}
              onInsert={(where) => void insert(index, where)}
              grabId={grabId}
              setGrabId={setGrabId}
              draggingId={draggingId}
              dropHint={dropHint}
              onDragStart={(e) => {
                dragIdRef.current = m.id
                setDraggingId(m.id)
                e.dataTransfer.effectAllowed = "move"
                try {
                  e.dataTransfer.setData("text/plain", String(m.id))
                } catch {
                  // ignore
                }
              }}
              onDragEnd={clearDrag}
              onDragOver={(e) => {
                const dragId = dragIdRef.current
                if (dragId == null || dragId === m.id) return
                e.preventDefault()
                e.dataTransfer.dropEffect = "move"
                const rect = e.currentTarget.getBoundingClientRect()
                const after = e.clientY - rect.top > rect.height / 2
                setDropHint({ id: m.id, after })
              }}
              onDragLeave={() =>
                setDropHint((prev) => (prev?.id === m.id ? null : prev))
              }
              onDrop={(e) => {
                e.preventDefault()
                const dragId = dragIdRef.current
                const rect = e.currentTarget.getBoundingClientRect()
                const after = e.clientY - rect.top > rect.height / 2
                clearDrag()
                if (dragId != null) handleReorder(dragId, m.id, after)
              }}
            />
            </React.Fragment>
          ))}
          {!readOnly && (
            <NewMilestoneRow
              projectId={projectId}
              colSpan={addRowColSpan}
              onMutated={onMutated}
            />
          )}
        </TableBody>
      </Table>
      </GridScroller>
    </div>
  )
}
