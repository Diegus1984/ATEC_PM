import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { AutoTextarea } from "@/features/checklist/checklist-shared"
import { notifyError, notifySuccess } from "@/lib/toast"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
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
import {
  addMoMNote,
  assignMoMNote,
  deleteMoMNote,
  fetchMoMList,
  fetchMoMNotes,
  updateMoMNote,
} from "@/lib/api/mom"
import { useMoMHub } from "@/lib/signalr/use-mom-hub"
import type { MoMListItem, MoMNote } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { MoMVerbaleDialog } from "./MoMVerbaleDialog"

// ─────────────────────────────────────────────────────────────
// Note MoM — Acquisizione rapida (prototipo Gestione_MoM_v9).
// Staging personale: si annota il punto durante la riunione e lo
// si assegna poi alla MoM di destinazione (campo Azione).
// Immissione allineata all'inbox «Fissa attività» della check list.
// ─────────────────────────────────────────────────────────────

function momDisplayName(item: MoMListItem): string {
  return item.tipo === "COMMESSA" && item.projectCode
    ? `${item.projectCode} — ${item.title}`
    : item.title
}

function sortMoms(moms: MoMListItem[]) {
  return [...moms].sort((a, b) =>
    momDisplayName(a).localeCompare(momDisplayName(b), "it", {
      numeric: true,
      sensitivity: "base",
    })
  )
}

export function MoMNotePage() {
  const queryClient = useQueryClient()

  const [notes, setNotes] = React.useState<MoMNote[]>([])
  const [loading, setLoading] = React.useState(true)
  const [loadError, setLoadError] = React.useState<string | null>(null)
  const [dialogOpen, setDialogOpen] = React.useState(false)

  const momsQuery = useQuery({
    queryKey: ["mom-list"],
    queryFn: () => fetchMoMList(),
  })
  const moms = React.useMemo(
    () => sortMoms(momsQuery.data ?? []),
    [momsQuery.data]
  )

  const reloadNotes = React.useCallback(async () => {
    const data = await fetchMoMNotes()
    setNotes(data)
  }, [])

  React.useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const data = await fetchMoMNotes()
        if (cancelled) return
        setNotes(data)
      } catch (err) {
        if (!cancelled) setLoadError((err as Error).message)
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  useMoMHub(
    true,
    React.useCallback(() => {
      void queryClient.invalidateQueries({ queryKey: ["mom-list"] })
    }, [queryClient])
  )

  const visibleNotes = React.useMemo(
    () => notes.filter((note) => note.note.trim()),
    [notes]
  )

  const onAssigned = React.useCallback(async () => {
    await reloadNotes()
    void queryClient.invalidateQueries({ queryKey: ["mom-list"] })
  }, [reloadNotes, queryClient])

  if (loading) {
    return <p className="text-sm text-muted-foreground">Caricamento…</p>
  }
  if (loadError) {
    return <PageErrorAlert message={loadError} />
  }

  return (
    <div className="flex flex-col gap-4">
      <Card className="py-0">
        <CardHeader className="flex-row items-center justify-between border-b py-4 [.border-b]:pb-4">
          <div className="space-y-1">
            <CardTitle>Note MoM — Acquisizione rapida</CardTitle>
            <CardDescription>
              Annota i punti emersi e assegnali alla MoM di destinazione: il testo
              finisce nel campo Azione (prima riga vuota o nuova riga in fondo).
            </CardDescription>
          </div>
          <Button size="sm" onClick={() => setDialogOpen(true)}>
            <Plus />
            Nuovo verbale
          </Button>
        </CardHeader>
        <CardContent className="space-y-4 p-4">
          <div className="rounded-lg border border-primary/30 bg-primary/5 p-3 text-sm text-primary">
            Butta giù i punti qui, poi assegnali al verbale di destinazione.
          </div>

          <div className="overflow-hidden rounded-lg border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-12 text-center">#</TableHead>
                  <TableHead>Attività da fissare</TableHead>
                  <TableHead className="w-72">Assegna a…</TableHead>
                  <TableHead className="w-12" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {visibleNotes.map((note, idx) => (
                  <NoteRow
                    key={note.id}
                    index={idx + 1}
                    note={note}
                    moms={moms}
                    onMutated={() => void reloadNotes()}
                    onAssigned={onAssigned}
                  />
                ))}
                <NoteNewRow onCreated={() => void reloadNotes()} />
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>

      <MoMVerbaleDialog
        open={dialogOpen}
        verbaleId={dialogOpen ? "new" : null}
        onClose={() => setDialogOpen(false)}
        onSaved={async () => {
          setDialogOpen(false)
          await queryClient.invalidateQueries({ queryKey: ["mom-list"] })
        }}
      />
    </div>
  )
}

function NoteNewRow({ onCreated }: { onCreated: () => void }) {
  const [text, setText] = React.useState("")
  const committing = React.useRef(false)
  const inputRef = React.useRef<HTMLInputElement>(null)

  async function commit() {
    const v = text.trim()
    if (!v || committing.current) return
    committing.current = true
    try {
      await addMoMNote({ note: v, targetMomId: null })
      setText("")
      onCreated()
      requestAnimationFrame(() => inputRef.current?.focus())
    } catch (err) {
      notifyError(err as Error)
    } finally {
      committing.current = false
    }
  }

  return (
    <TableRow className="border-0 bg-muted/40 hover:bg-muted/50">
      <TableCell />
      <TableCell colSpan={3} className="py-2">
        <div className="flex items-center gap-2">
          <Plus className="size-4 shrink-0 text-muted-foreground" />
          <input
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
            className={cn(
              "h-9 w-full min-w-0 rounded-full border border-dashed border-input bg-background px-3 text-sm shadow-xs outline-none transition-[color,box-shadow] placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
            )}
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

function NoteRow({
  index,
  note,
  moms,
  onMutated,
  onAssigned,
}: {
  index: number
  note: MoMNote
  moms: MoMListItem[]
  onMutated: () => void
  onAssigned: () => void
}) {
  const confirm = useConfirm()
  const [text, setText] = React.useState(note.note)
  const hasText = text.trim().length > 0
  React.useEffect(() => setText(note.note), [note.note])

  async function commit() {
    if (text === note.note) return
    try {
      await updateMoMNote(note.id, { note: text, targetMomId: note.targetMomId })
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  async function remove() {
    const ok = await confirm({
      title: "Eliminare la nota",
      description: "La nota verrà rimossa dall'acquisizione rapida.",
      confirmLabel: "Elimina",
    })
    if (!ok) return
    try {
      await deleteMoMNote(note.id)
      onMutated()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  async function assign(momId: number) {
    if (!text.trim()) {
      notifyError(new Error("Scrivi prima l'attività, poi assegnala"))
      return
    }
    try {
      await updateMoMNote(note.id, { note: text, targetMomId: momId })
      const assignedMomId = await assignMoMNote(note.id)
      const mom = moms.find((m) => m.id === assignedMomId)
      await onAssigned()
      notifySuccess(
        `Nota assegnata a «${mom ? momDisplayName(mom) : `MoM #${assignedMomId}`}»`
      )
    } catch (err) {
      notifyError(err as Error)
    }
  }

  return (
    <TableRow className={hasText ? "bg-muted/20" : undefined}>
      <TableCell className="text-center font-mono text-xs text-muted-foreground">
        {index}
      </TableCell>
      <TableCell className="min-w-[16rem]">
        <AutoTextarea
          value={text}
          onChange={setText}
          onCommit={() => void commit()}
          placeholder=""
        />
      </TableCell>
      <TableCell>
        <Select value="" onValueChange={(v) => void assign(Number(v))}>
          <SelectTrigger
            className={cn(
              "h-9 w-full",
              hasText && "border-primary/40 bg-primary/5"
            )}
          >
            <SelectValue placeholder="Assegna a…" />
          </SelectTrigger>
          <SelectContent position="popper" align="start">
            {moms.map((mom) => (
              <SelectItem key={mom.id} value={String(mom.id)}>
                {momDisplayName(mom)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </TableCell>
      <TableCell className="w-12 py-1.5 align-middle">
        <div className="flex justify-end">
          <RowActionsMenu
            size="icon-sm"
            triggerClassName="size-8"
            actions={[
              {
                label: "Elimina riga",
                icon: Trash2,
                destructive: true,
                onClick: () => void remove(),
              },
            ]}
          />
        </div>
      </TableCell>
    </TableRow>
  )
}
