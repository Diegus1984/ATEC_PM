import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { GripVertical, Plus, RotateCcw, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { ActiveStatus } from "@/components/shared/status-dot"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  createActivityCatalog,
  deleteActivityCatalog,
  fetchActivityCatalog,
  reorderActivityCatalog,
  resetActivityCatalog,
  updateActivityCatalog,
} from "@/lib/api/activity-catalog"
import type { ActivityCatalogItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

// Anagrafica attività: catalogo globale delle voci-attività standard, precaricate alla creazione
// di una commessa. Fedele al modale "Anagrafica attività" del prototipo: aggiungi, rinomina inline,
// riordina con drag-and-drop, disattiva/elimina, «Ripristina standard». Le voci hanno id stabile.
export function ActivityCatalogPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [error, setError] = React.useState<string | null>(null)

  // Stati per il Drag and Drop
  const dragIdRef = React.useRef<number | null>(null)
  const [grabId, setGrabId] = React.useState<number | null>(null)
  const [draggingId, setDraggingId] = React.useState<number | null>(null)
  const [dropHint, setDropHint] = React.useState<{
    id: number
    after: boolean
    valid: boolean
  } | null>(null)

  const catalogQuery = useQuery({
    queryKey: ["activity-catalog"],
    queryFn: fetchActivityCatalog,
  })
  const rows = catalogQuery.data ?? []

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["activity-catalog"] })

  const addMutation = useMutation({
    mutationFn: (label: string) =>
      createActivityCatalog({ id: 0, label, sortOrder: 0, isActive: true }),
    onSuccess: async () => {
      setError(null)
      await invalidate()
    },
    onError: (err: Error) => setError(err.message),
  })

  const updateMutation = useMutation({
    mutationFn: (item: ActivityCatalogItem) =>
      updateActivityCatalog(item.id, item),
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteActivityCatalog,
    onSuccess: invalidate,
  })

  const reorderMutation = useMutation({
    mutationFn: reorderActivityCatalog,
    onSuccess: invalidate,
  })

  const resetMutation = useMutation({
    mutationFn: resetActivityCatalog,
    onSuccess: invalidate,
  })

  const clearDrag = () => {
    dragIdRef.current = null
    setGrabId(null)
    setDraggingId(null)
    setDropHint(null)
  }

  const handleReorder = (dragId: number, targetId: number, after: boolean) => {
    const dragIndex = rows.findIndex((r) => r.id === dragId)
    if (dragIndex === -1) return
    const next = [...rows]
    const [moved] = next.splice(dragIndex, 1)

    const targetIndex = next.findIndex((r) => r.id === targetId)
    if (targetIndex === -1) return

    const insertAt = after ? targetIndex + 1 : targetIndex
    next.splice(insertAt, 0, moved)

    reorderMutation.mutate(next.map((r) => r.id))
  }

  const handleAddRow = async (label: string) => {
    try {
      await addMutation.mutateAsync(label)
    } catch {
      // Errore già gestito da addMutation
    }
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Anagrafica attività</CardTitle>
              <CardDescription>
                Catalogo delle voci-attività standard precaricate alla creazione
                di una commessa
              </CardDescription>
            </div>
            <Button
              variant="outline"
              size="sm"
              disabled={resetMutation.isPending}
              onClick={() => {
                void confirm({
                  title: "Ripristina elenco standard",
                  description:
                    "Le voci personalizzate verranno sostituite con l'elenco di partenza. Le commesse già create non vengono modificate.",
                  confirmLabel: "Ripristina",
                }).then((ok) => {
                  if (ok) resetMutation.mutate()
                })
              }}
            >
              <RotateCcw />
              Ripristina standard
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {error ? <p className="text-sm text-destructive">{error}</p> : null}

          <div className="overflow-hidden rounded-lg border bg-card">
            <Table>
              <TableHeader className="bg-muted/40">
                <TableRow>
                  <TableHead className="w-7 print:hidden" />
                  <TableHead className="w-10 text-center">#</TableHead>
                  <TableHead>Voce attività</TableHead>
                  <TableHead className="w-28">Stato</TableHead>
                  <TableHead className="w-14 text-right print:hidden">Azioni</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row, index) => (
                  <CatalogRow
                    key={row.id}
                    row={row}
                    index={index}
                    grabId={grabId}
                    setGrabId={setGrabId}
                    draggingId={draggingId}
                    dropHint={dropHint}
                    onDragStart={(event) => {
                      dragIdRef.current = row.id
                      setDraggingId(row.id)
                      event.dataTransfer.effectAllowed = "move"
                      try {
                        event.dataTransfer.setData("text/plain", String(row.id))
                      } catch {
                        // ignore
                      }
                    }}
                    onDragEnd={clearDrag}
                    onDragOver={(event) => {
                      const dragId = dragIdRef.current
                      if (dragId == null || dragId === row.id) return
                      event.preventDefault()
                      event.dataTransfer.dropEffect = "move"
                      const rect = event.currentTarget.getBoundingClientRect()
                      const after = event.clientY - rect.top > rect.height / 2
                      setDropHint({ id: row.id, after, valid: true })
                    }}
                    onDragLeave={() =>
                      setDropHint((prev) => (prev?.id === row.id ? null : prev))
                    }
                    onDrop={(event) => {
                      event.preventDefault()
                      const dragId = dragIdRef.current
                      clearDrag()
                      if (dragId == null || dragId === row.id) return
                      const rect = event.currentTarget.getBoundingClientRect()
                      const after = event.clientY - rect.top > rect.height / 2
                      handleReorder(dragId, row.id, after)
                    }}
                    onSave={(item) => updateMutation.mutate(item)}
                    onDelete={() => {
                      void confirm({
                        title: "Elimina voce",
                        description: `Eliminare la voce "${row.label}" dall'anagrafica attività?`,
                        confirmLabel: "Elimina",
                      }).then((ok) => {
                        if (ok) deleteMutation.mutate(row.id)
                      })
                    }}
                  />
                ))}
                <SheetNewRow onAddRow={handleAddRow} />
                {rows.length === 0 && !catalogQuery.isLoading ? (
                  <TableRow>
                    <TableCell
                      colSpan={5}
                      className="text-center text-sm text-muted-foreground py-6"
                    >
                      Nessuna voce in anagrafica. Aggiungine una dal campo qui sotto.
                    </TableCell>
                  </TableRow>
                ) : null}
                {catalogQuery.isLoading ? (
                  <TableRow>
                    <TableCell
                      colSpan={5}
                      className="text-center text-sm text-muted-foreground py-6"
                    >
                      Caricamento…
                    </TableCell>
                  </TableRow>
                ) : null}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

function CatalogRow({
  row,
  index,
  grabId,
  setGrabId,
  draggingId,
  dropHint,
  onDragStart,
  onDragEnd,
  onDragOver,
  onDragLeave,
  onDrop,
  onSave,
  onDelete,
}: {
  row: ActivityCatalogItem
  index: number
  grabId: number | null
  setGrabId: (id: number | null) => void
  draggingId: number | null
  dropHint: { id: number; after: boolean; valid: boolean } | null
  onDragStart: (event: React.DragEvent) => void
  onDragEnd: () => void
  onDragOver: (event: React.DragEvent) => void
  onDragLeave: () => void
  onDrop: (event: React.DragEvent) => void
  onSave: (item: ActivityCatalogItem) => void
  onDelete: () => void
}) {
  const [label, setLabel] = React.useState(row.label)
  React.useEffect(() => {
    setLabel(row.label)
  }, [row.label])

  const commit = () => {
    const trimmed = label.trim()
    if (!trimmed || trimmed === row.label) {
      setLabel(row.label)
      return
    }
    onSave({ ...row, label: trimmed })
  }

  return (
    <TableRow
      draggable={grabId === row.id}
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      className={cn(
        "border-0 transition-colors",
        !row.isActive && "opacity-60",
        draggingId === row.id && "opacity-50",
        dropHint?.id === row.id &&
          dropHint.valid &&
          !dropHint.after &&
          "[&>td]:shadow-[inset_0_2px_0_0_var(--primary)]",
        dropHint?.id === row.id &&
          dropHint.valid &&
          dropHint.after &&
          "[&>td]:shadow-[inset_0_-2px_0_0_var(--primary)]"
      )}
    >
      <TableCell className="py-1.5 align-middle print:hidden">
        <span
          role="button"
          aria-label="Trascina per riordinare la riga"
          title="Trascina per riordinare la riga"
          className="inline-flex size-7 cursor-grab items-center justify-center rounded text-muted-foreground/60 hover:bg-muted hover:text-foreground active:cursor-grabbing"
          onMouseDown={() => setGrabId(row.id)}
          onMouseUp={() => setGrabId(null)}
        >
          <GripVertical className="size-3.5" />
        </span>
      </TableCell>
      <TableCell className="text-center align-middle font-mono text-xs text-muted-foreground">
        {index + 1}
      </TableCell>
      <TableCell className="py-1.5 align-middle">
        <Input
          value={label}
          onChange={(event) => setLabel(event.target.value)}
          onBlur={commit}
          onKeyDown={(event) => {
            if (event.key === "Enter") event.currentTarget.blur()
          }}
          className="h-8 border-transparent bg-transparent shadow-none hover:border-input focus-visible:border-input focus-visible:bg-background"
        />
      </TableCell>
      <TableCell className="py-1.5 align-middle">
        <div className="flex items-center gap-2">
          <Switch
            checked={row.isActive}
            onCheckedChange={(value) => onSave({ ...row, isActive: value })}
          />
          <ActiveStatus active={row.isActive} />
        </div>
      </TableCell>
      <TableCell className="py-1.5 align-middle print:hidden">
        <div className="flex justify-end">
          <RowActionsMenu
            actions={[
              {
                label: "Elimina",
                icon: Trash2,
                destructive: true,
                onClick: onDelete,
              },
            ]}
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

function SheetNewRow({
  onAddRow,
}: {
  onAddRow: (label: string) => void | Promise<void>
}) {
  const [text, setText] = React.useState("")
  const committing = React.useRef(false)
  const inputRef = React.useRef<HTMLInputElement>(null)

  async function commit() {
    const v = text.trim()
    if (!v || committing.current) return
    committing.current = true
    try {
      await onAddRow(v)
      setText("")
      requestAnimationFrame(() => inputRef.current?.focus())
    } finally {
      committing.current = false
    }
  }

  return (
    <TableRow className="border-0 bg-muted/40 hover:bg-muted/50">
      <TableCell className="print:hidden" />
      <TableCell className="text-center text-muted-foreground font-mono text-xs">
        +
      </TableCell>
      <TableCell colSpan={3} className="py-1.5">
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
            placeholder="Nuova voce attività..."
            className="h-8 border-dashed shadow-none"
          />
        </div>
      </TableCell>
    </TableRow>
  )
}
