import * as React from "react"
import {
  ArrowDownToLine,
  ArrowUpToLine,
  Flag,
  GripVertical,
  Plus,
  Power,
  Trash2,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { DateField } from "@/components/shared/date-field"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Input } from "@/components/ui/input"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Textarea } from "@/components/ui/textarea"
import {
  createMilestone,
  deleteMilestone,
  reorderMilestones,
  updateMilestone,
} from "@/lib/api/milestones"
import type { Milestone, MilestoneSaveRequest } from "@/lib/api/types"
import { isoToDate } from "@/lib/date-iso"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"
import {
  buildMilestoneSave,
  msStatus,
  statusRowClass,
  weekLabel,
  weekTot,
} from "@/features/milestones/milestone-utils"

const MILESTONE_COLS = 10

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

function AvanzCell({
  value,
  onCommit,
}: {
  value: number | null
  onCommit: (v: number | null) => void
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
      <Input
        value={text}
        onChange={(e) => setText(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === "Enter") e.currentTarget.blur()
        }}
        inputMode="numeric"
        placeholder="—"
        className="h-8 w-14 px-1 text-center tabular-nums"
      />
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
      draggable={grabId === m.id}
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      className={cn(
        "border-0 transition-colors",
        m.spento ? "bg-muted/30 opacity-70" : statusRowClass(status),
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
          <span className="text-xs tabular-nums text-muted-foreground">
            {String(index + 1).padStart(2, "0")}
          </span>
        </div>
      </TableCell>

      <TableCell className="min-w-[16rem] py-1.5 align-top">
        <GrowTextarea
          value={desc}
          onChange={setDesc}
          onCommit={commitDesc}
          placeholder="Descrizione attività"
          className={cn(
            "border-transparent bg-transparent focus-visible:border-input focus-visible:bg-background",
            m.evidenza && "font-semibold text-red-800",
            m.spento && "line-through"
          )}
        />
      </TableCell>

      <TableCell className="w-14 py-1.5 align-top">
        <div className="flex h-8 items-center justify-center">
          <WeekChip label={weekLabel(m.dataInizio)} />
        </div>
      </TableCell>
      <TableCell className="w-48 py-1.5 align-top">
        <DateField
          value={m.dataInizio}
          onChange={changeInizio}
          size="sm"
          placeholder="—"
          className="h-8 w-full min-w-0 shadow-none"
        />
      </TableCell>

      <TableCell className="w-14 py-1.5 align-top">
        <div className="flex h-8 items-center justify-center">
          <WeekChip label={weekLabel(m.dataFine)} />
        </div>
      </TableCell>
      <TableCell className="w-48 py-1.5 align-top">
        <DateField
          value={m.dataFine}
          onChange={(v) => void patch({ dataFine: v })}
          size="sm"
          placeholder="—"
          disabled={!m.dataInizio}
          disableBefore={isoToDate(m.dataInizio)}
          className="h-8 w-full min-w-0 shadow-none"
        />
      </TableCell>

      <TableCell className="w-12 py-1.5 align-top">
        <div className="flex h-8 items-center justify-center">
          <span className="text-base font-medium tabular-nums text-foreground">
            {weekTot(m.dataInizio, m.dataFine) || "—"}
          </span>
        </div>
      </TableCell>

      <TableCell className="w-40 py-1.5 align-top">
        <AvanzCell
          value={m.avanzamento}
          onCommit={(v) => void patch({ avanzamento: v })}
        />
      </TableCell>

      <TableCell className="min-w-[10rem] py-1.5 align-top">
        <GrowTextarea
          value={note}
          onChange={setNote}
          onCommit={commitNote}
          placeholder="Note"
          className="border-transparent bg-transparent focus-visible:border-input focus-visible:bg-background"
        />
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
                label: m.evidenza ? "Togli evidenza" : "Evidenzia (urgenza)",
                icon: Flag,
                separatorBefore: true,
                onClick: () => void patch({ evidenza: !m.evidenza }),
              },
              {
                label: m.spento ? "Riattiva riga" : "Spegni riga",
                icon: Power,
                onClick: () => void patch({ spento: !m.spento }),
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
        </div>
      </TableCell>
    </TableRow>
  )
}

function NewMilestoneRow({
  projectId,
  onMutated,
}: {
  projectId: number
  onMutated: () => void
}) {
  const [desc, setDesc] = React.useState("")
  const committing = React.useRef(false)

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
      <TableCell colSpan={MILESTONE_COLS - 1} className="py-1.5">
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
            placeholder="Aggiungi attività…"
            className="h-8 max-w-md border-dashed"
          />
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
}: {
  projectId: number
  items: Milestone[]
  onMutated: () => void
}) {
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
    if (dragId === targetId) return
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

  return (
    <div className="overflow-x-auto rounded-lg border">
      <Table className="border-separate border-spacing-y-1.5">
        <TableHeader className="bg-muted/40">
          <TableRow>
            <TableHead className="w-16">#</TableHead>
            <TableHead className="min-w-[16rem]">Attività / Descrizione</TableHead>
            <TableHead className="w-14 text-center">W.In</TableHead>
            <TableHead className="w-48">Data Inizio</TableHead>
            <TableHead className="w-14 text-center">W.Fine</TableHead>
            <TableHead className="w-48">Data Fine</TableHead>
            <TableHead className="w-12 text-center">W.Tot</TableHead>
            <TableHead className="w-40">Avanzamento</TableHead>
            <TableHead className="min-w-[10rem]">Note</TableHead>
            <TableHead className="w-12 text-right">Azioni</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((m, index) => (
            <MilestoneRow
              key={m.id}
              m={m}
              index={index}
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
          ))}
          <NewMilestoneRow projectId={projectId} onMutated={onMutated} />
        </TableBody>
      </Table>
    </div>
  )
}
