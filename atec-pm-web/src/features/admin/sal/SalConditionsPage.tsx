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
  fetchSalConditions,
  createSalCondition,
  updateSalCondition,
  toggleActiveSalCondition,
  deleteSalCondition,
  reorderSalConditions,
  resetSalConditions,
} from "@/lib/api/sal"
import type { SalCondition } from "@/lib/api/types"
import { cn } from "@/lib/utils"

export function SalConditionsPage() {
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

  const conditionsQuery = useQuery({
    queryKey: ["sal-conditions"],
    queryFn: () => fetchSalConditions(false),
  })
  const rows = conditionsQuery.data ?? []

  const invalidate = () =>
    void queryClient.invalidateQueries({ queryKey: ["sal-conditions"] })

  const addMutation = useMutation({
    mutationFn: (label: string) => createSalCondition({ label }),
    onSuccess: async () => {
      setError(null)
      await invalidate()
    },
    onError: (err: Error) => setError(err.message),
  })

  const updateMutation = useMutation({
    mutationFn: ({ id, label }: { id: number; label: string }) =>
      updateSalCondition(id, { label }),
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
  })

  const toggleMutation = useMutation({
    mutationFn: ({ id, active }: { id: number; active: boolean }) =>
      toggleActiveSalCondition(id, active),
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
  })

  const deleteMutation = useMutation({
    mutationFn: deleteSalCondition,
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
  })

  const reorderMutation = useMutation({
    mutationFn: reorderSalConditions,
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
  })

  const resetMutation = useMutation({
    mutationFn: resetSalConditions,
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
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
      // Errore gestito da addMutation
    }
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Condizioni di pagamento SAL</CardTitle>
              <CardDescription>
                Gestione globale delle condizioni di pagamento utilizzabili negli step SAL delle commesse
              </CardDescription>
            </div>
            <Button
              variant="outline"
              size="sm"
              disabled={resetMutation.isPending}
              onClick={() => {
                void confirm({
                  title: "Ripristina condizioni standard",
                  description:
                    "Le condizioni personalizzate verranno sostituite con l'elenco standard (A Vista, 30 gg. dffm., ecc.). Le commesse già create non verranno modificate.",
                  confirmLabel: "Ripristina",
                }).then((ok) => {
                  if (ok) resetMutation.mutate()
                })
              }}
            >
              <RotateCcw className="size-4 mr-1.5" />
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
                  <TableHead>Condizione di pagamento</TableHead>
                  <TableHead className="w-28">Stato</TableHead>
                  <TableHead className="w-14 text-right print:hidden">Azioni</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row, index) => (
                  <ConditionRow
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
                    onSave={(label) => updateMutation.mutate({ id: row.id, label })}
                    onToggle={(active) => toggleMutation.mutate({ id: row.id, active })}
                    onDelete={() => {
                      void confirm({
                        title: "Elimina condizione",
                        description: `Eliminare la condizione "${row.label}"?`,
                        confirmLabel: "Elimina",
                      }).then((ok) => {
                        if (ok) deleteMutation.mutate(row.id)
                      })
                    }}
                  />
                ))}
                <NewConditionRow onAddRow={handleAddRow} />
                {rows.length === 0 && !conditionsQuery.isLoading ? (
                  <TableRow>
                    <TableCell
                      colSpan={5}
                      className="text-center text-sm text-muted-foreground py-6"
                    >
                      Nessuna condizione presente. Aggiungine una dal campo qui sotto.
                    </TableCell>
                  </TableRow>
                ) : null}
                {conditionsQuery.isLoading ? (
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

function ConditionRow({
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
  onToggle,
  onDelete,
}: {
  row: SalCondition
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
  onSave: (label: string) => void
  onToggle: (active: boolean) => void
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
    onSave(trimmed)
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
            onCheckedChange={onToggle}
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

function NewConditionRow({
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
            placeholder="Nuova condizione di pagamento (es. 45 gg. dffm.)..."
            className="h-8 border-dashed shadow-none"
          />
        </div>
      </TableCell>
    </TableRow>
  )
}
