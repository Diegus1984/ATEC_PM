import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { GripVertical, Lock, Palette, Plus, RotateCcw, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { ActiveStatus } from "@/components/shared/status-dot"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
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
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import type { SalCondition } from "@/lib/api/types"
import { cn } from "@/lib/utils"

/**
 * Contratto API del pannello: le tre anagrafiche SAL (condizioni di pagamento,
 * causali Conto SAP, stati pagamento) condividono la stessa shape `SalCondition`
 * e le stesse 7 operazioni — cambiano solo gli endpoint.
 */
export interface SalOptionListApi {
  /** Elenco completo (incluse le voci disattivate). */
  list: () => Promise<SalCondition[]>
  /** Crea una nuova voce; ritorna l'id. */
  create: (label: string) => Promise<number>
  /**
   * Rinomina una voce esistente. Riceve la riga intera (non solo l'id) così le
   * anagrafiche con colori possono ripassarli invariati nel PUT full-replace.
   */
  rename: (row: SalCondition, label: string) => Promise<number>
  /** Attiva/disattiva una voce. */
  toggleActive: (id: number, active: boolean) => Promise<number>
  /** Elimina una voce. */
  remove: (id: number) => Promise<boolean>
  /** Riordina l'elenco (array completo di id nel nuovo ordine). */
  reorder: (ids: number[]) => Promise<boolean>
  /** Ripristina l'elenco standard. */
  reset: () => Promise<boolean>
  /**
   * Salva i colori di una voce (label invariata). Richiesto solo con
   * `withColors` (tab Stati Pagamento); null/null = nessuna tinta.
   */
  saveColors?: (
    row: SalCondition,
    colorBg: string | null,
    colorFg: string | null
  ) => Promise<number>
}

/**
 * Pannello anagrafica riusabile per la pagina «Anagrafiche SAL»: lista con
 * rename inline, toggle attiva, riordino drag&drop, aggiunta in coda e reset
 * con conferma. Estratto dalla vecchia pagina condizioni di pagamento
 * (aspetto invariato), parametrizzato sulle funzioni API.
 *
 * Le voci per cui `isSystemEntry` ritorna true (es. «Pagata» / «Parzialmente
 * Pagata» negli stati pagamento) sono di sistema: rename e delete disabilitati
 * in UI con tooltip «Voce di sistema» (il server le blocca comunque) — i loro
 * COLORI però restano modificabili quando `withColors` è attivo.
 *
 * `withColors` (solo Stati Pagamento): aggiunge la colonna «Colori» con badge
 * anteprima e l'editor colori (replica dello StatusDialog DDP, ma con colori
 * opzionali: «Nessun colore» azzera la tinta).
 */
export function SalOptionListPanel({
  queryKey,
  description,
  columnLabel,
  newItemPlaceholder,
  emptyText,
  resetConfirm,
  deleteConfirm,
  api,
  isSystemEntry,
  withColors,
}: {
  queryKey: string
  description: string
  columnLabel: string
  newItemPlaceholder: string
  emptyText: string
  resetConfirm: { title: string; description: string }
  deleteConfirm: { title: string; description: (label: string) => string }
  api: SalOptionListApi
  isSystemEntry?: (row: SalCondition) => boolean
  withColors?: boolean
}) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [error, setError] = React.useState<string | null>(null)
  // Voce di cui si stanno modificando i colori (dialog aperto se non null)
  const [colorsDialog, setColorsDialog] = React.useState<SalCondition | null>(null)

  // Stati per il Drag and Drop
  const dragIdRef = React.useRef<number | null>(null)
  const [grabId, setGrabId] = React.useState<number | null>(null)
  const [draggingId, setDraggingId] = React.useState<number | null>(null)
  const [dropHint, setDropHint] = React.useState<{
    id: number
    after: boolean
    valid: boolean
  } | null>(null)

  const listQuery = useQuery({
    queryKey: [queryKey],
    queryFn: () => api.list(),
  })
  const rows = listQuery.data ?? []

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: [queryKey] })
    // Invalida anche il prefisso ampio "sal": i Select del foglio SAL leggono
    // chiavi tipo ["sal","sap-causali","active"] con staleTime lungo e la voce
    // appena creata/modificata deve comparire subito al ritorno dal deep-link
    void queryClient.invalidateQueries({ queryKey: ["sal"] })
  }

  const addMutation = useMutation({
    mutationFn: (label: string) => api.create(label),
    onSuccess: async () => {
      setError(null)
      await invalidate()
    },
    onError: (err: Error) => setError(err.message),
  })

  const updateMutation = useMutation({
    mutationFn: ({ row, label }: { row: SalCondition; label: string }) =>
      api.rename(row, label),
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
  })

  const toggleMutation = useMutation({
    mutationFn: ({ id, active }: { id: number; active: boolean }) =>
      api.toggleActive(id, active),
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
  })

  const deleteMutation = useMutation({
    mutationFn: (id: number) => api.remove(id),
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
  })

  const reorderMutation = useMutation({
    mutationFn: (ids: number[]) => api.reorder(ids),
    onSuccess: invalidate,
    onError: (err: Error) => setError(err.message),
  })

  const resetMutation = useMutation({
    mutationFn: () => api.reset(),
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

  // 5 colonne base + l'eventuale colonna «Colori»
  const totalCols = withColors ? 6 : 5

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">{description}</p>
        <Button
          variant="outline"
          size="sm"
          disabled={resetMutation.isPending}
          onClick={() => {
            void confirm({
              title: resetConfirm.title,
              description: resetConfirm.description,
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

      {error ? <p className="text-sm text-destructive">{error}</p> : null}

      <div className="overflow-hidden rounded-lg border bg-card">
        <Table>
          <TableHeader className="bg-muted/40">
            <TableRow>
              <TableHead className="w-7 print:hidden" />
              <TableHead className="w-10 text-center">#</TableHead>
              <TableHead>{columnLabel}</TableHead>
              {withColors ? <TableHead className="w-32">Colori</TableHead> : null}
              <TableHead className="w-28">Stato</TableHead>
              <TableHead className="w-14 text-right print:hidden">Azioni</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((row, index) => (
              <OptionRow
                key={row.id}
                row={row}
                index={index}
                system={isSystemEntry?.(row) ?? false}
                withColors={withColors ?? false}
                onEditColors={() => setColorsDialog(row)}
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
                onSave={(label) => updateMutation.mutate({ row, label })}
                onToggle={(active) => toggleMutation.mutate({ id: row.id, active })}
                onDelete={() => {
                  void confirm({
                    title: deleteConfirm.title,
                    description: deleteConfirm.description(row.label),
                    confirmLabel: "Elimina",
                  }).then((ok) => {
                    if (ok) deleteMutation.mutate(row.id)
                  })
                }}
              />
            ))}
            <NewOptionRow
              placeholder={newItemPlaceholder}
              onAddRow={handleAddRow}
              colSpan={totalCols - 2}
            />
            {rows.length === 0 && !listQuery.isLoading ? (
              <TableRow>
                <TableCell
                  colSpan={totalCols}
                  className="text-center text-sm text-muted-foreground py-6"
                >
                  {emptyText}
                </TableCell>
              </TableRow>
            ) : null}
            {listQuery.isLoading ? (
              <TableRow>
                <TableCell
                  colSpan={totalCols}
                  className="text-center text-sm text-muted-foreground py-6"
                >
                  Caricamento…
                </TableCell>
              </TableRow>
            ) : null}
          </TableBody>
        </Table>
      </div>

      {withColors && api.saveColors ? (
        <OptionColorsDialog
          item={colorsDialog}
          onClose={() => setColorsDialog(null)}
          save={(colorBg, colorFg) => {
            if (!colorsDialog) return Promise.reject(new Error("Nessuna voce selezionata"))
            return api.saveColors!(colorsDialog, colorBg, colorFg)
          }}
          onSaved={async () => {
            setColorsDialog(null)
            await invalidate()
          }}
        />
      ) : null}
    </div>
  )
}

function OptionRow({
  row,
  index,
  system,
  withColors,
  onEditColors,
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
  system: boolean
  withColors: boolean
  onEditColors: () => void
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
        {system ? (
          // Voce di sistema: rename disabilitato (il server lo blocca comunque)
          <Tooltip>
            <TooltipTrigger asChild>
              <div className="flex items-center gap-2">
                <Input
                  value={label}
                  disabled
                  className="h-8 border-transparent bg-transparent shadow-none disabled:opacity-100"
                />
                <Lock className="size-3.5 shrink-0 text-muted-foreground" />
              </div>
            </TooltipTrigger>
            <TooltipContent>Voce di sistema</TooltipContent>
          </Tooltip>
        ) : (
          <Input
            value={label}
            onChange={(event) => setLabel(event.target.value)}
            onBlur={commit}
            onKeyDown={(event) => {
              if (event.key === "Enter") event.currentTarget.blur()
            }}
            className="h-8 border-transparent bg-transparent shadow-none hover:border-input focus-visible:border-input focus-visible:bg-background"
          />
        )}
      </TableCell>
      {withColors ? (
        <TableCell className="py-1.5 align-middle">
          {/* Badge anteprima cliccabile (come la colonna Colori del DDP);
              i colori sono modificabili anche sulle voci di sistema */}
          <button
            type="button"
            title="Modifica i colori"
            onClick={onEditColors}
            className="cursor-pointer rounded focus-visible:outline-2 focus-visible:outline-ring"
          >
            {row.colorBg ? (
              <span
                className="inline-flex rounded px-2 py-0.5 text-xs"
                style={{
                  backgroundColor: row.colorBg,
                  color: row.colorFg ?? undefined,
                }}
              >
                Anteprima
              </span>
            ) : (
              <span className="inline-flex px-1 text-xs text-muted-foreground hover:text-foreground">
                —
              </span>
            )}
          </button>
        </TableCell>
      ) : null}
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
              ...(withColors
                ? [
                    {
                      // Sempre abilitata: sulle voci di sistema è bloccato solo
                      // rename/delete, i colori restano modificabili
                      label: "Colori…",
                      icon: Palette,
                      onClick: onEditColors,
                    },
                  ]
                : []),
              {
                label: system ? "Elimina (voce di sistema)" : "Elimina",
                icon: Trash2,
                destructive: true,
                disabled: system,
                separatorBefore: withColors,
                onClick: onDelete,
              },
            ]}
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

function NewOptionRow({
  placeholder,
  onAddRow,
  colSpan,
}: {
  placeholder: string
  onAddRow: (label: string) => void | Promise<void>
  /** Colonne coperte dal campo di inserimento (varia con la colonna Colori). */
  colSpan: number
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
      <TableCell colSpan={colSpan} className="py-1.5">
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
            placeholder={placeholder}
            className="h-8 border-dashed shadow-none"
          />
        </div>
      </TableCell>
    </TableRow>
  )
}

// Preset sfondo: stessa griglia dell'editor stati DDP (DdpConfigPage)
const STATUS_BG_PRESETS = [
  "#FF0000", "#FFC000", "#FFFF00", "#00B050", "#006400", "#00B0F0", "#2563EB",
  "#7030A0", "#8B008B", "#FFB6C1", "#B4B4B4", "#ADD8E6", "#000000", "#FFFFFF",
]

/** Hex CSS valido per i colori: #RRGGBB o #RRGGBBAA (VARCHAR(9) lato server). */
const HEX_COLOR_RE = /^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$/

/**
 * Editor colori di una voce (replica dello StatusDialog del DDP, adattato):
 * qui i colori sono OPZIONALI — «Nessun colore» svuota i campi e il salvataggio
 * persiste null/null (stato neutro senza tinta). L'etichetta non si tocca:
 * viaggia invariata nel PUT (così funziona anche sulle voci di sistema).
 */
function OptionColorsDialog({
  item,
  onClose,
  save,
  onSaved,
}: {
  item: SalCondition | null
  onClose: () => void
  save: (colorBg: string | null, colorFg: string | null) => Promise<number>
  onSaved: () => void
}) {
  const open = item !== null
  // "" = nessun colore (→ null al salvataggio)
  const [colorBg, setColorBg] = React.useState("")
  const [colorFg, setColorFg] = React.useState("")
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open) return
    setColorBg(item?.colorBg ?? "")
    setColorFg(item?.colorFg ?? "")
    setError(null)
  }, [open, item])

  const bgTrimmed = colorBg.trim()
  const fgTrimmed = colorFg.trim()
  const noColor = bgTrimmed === ""
  const bgValid = noColor || HEX_COLOR_RE.test(bgTrimmed)
  // Senza sfondo il testo è irrilevante (si salva null/null)
  const fgValid = noColor || fgTrimmed === "" || HEX_COLOR_RE.test(fgTrimmed)

  const saveMutation = useMutation({
    mutationFn: () =>
      noColor
        ? save(null, null)
        : save(bgTrimmed, fgTrimmed === "" ? null : fgTrimmed),
    onSuccess: onSaved,
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Colori — {item?.label ?? ""}</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <p className="text-xs text-muted-foreground">
            Tinta della riga nel foglio SAL quando è selezionato questo stato.
            Facoltativa: senza colore la riga resta neutra (o mantiene i colori
            standard di «Pagata» / «Parzialmente Pagata» / fattura emessa).
          </p>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="paystate-bg">Sfondo</Label>
              <Input
                id="paystate-bg"
                value={colorBg}
                onChange={(event) => setColorBg(event.target.value)}
                placeholder="#RRGGBB"
              />
              <div className="flex flex-wrap gap-1">
                {STATUS_BG_PRESETS.map((hex) => (
                  <button
                    key={hex}
                    type="button"
                    title={hex}
                    className="size-5 rounded border"
                    style={{ backgroundColor: hex }}
                    onClick={() => setColorBg(hex)}
                  />
                ))}
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="paystate-fg">Testo</Label>
              <Input
                id="paystate-fg"
                value={colorFg}
                onChange={(event) => setColorFg(event.target.value)}
                placeholder="#RRGGBB"
                disabled={noColor}
              />
              <div className="flex gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={noColor}
                  onClick={() => setColorFg("#FFFFFF")}
                >
                  Bianco
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={noColor}
                  onClick={() => setColorFg("#000000")}
                >
                  Nero
                </Button>
              </div>
            </div>
          </div>
          <div className="flex items-center gap-3">
            {noColor ? (
              <span className="inline-flex rounded border border-dashed px-3 py-1 text-sm text-muted-foreground">
                {item?.label ?? "(etichetta)"}
              </span>
            ) : (
              <span
                className="inline-flex rounded px-3 py-1 text-sm font-medium"
                style={{
                  backgroundColor: bgValid ? bgTrimmed : undefined,
                  color: fgValid && fgTrimmed !== "" ? fgTrimmed : undefined,
                }}
              >
                {item?.label ?? "(etichetta)"}
              </span>
            )}
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={noColor}
              onClick={() => {
                setColorBg("")
                setColorFg("")
              }}
            >
              Nessun colore
            </Button>
          </div>
          {!bgValid || !fgValid ? (
            <p className="text-sm text-destructive">
              Colore non valido: usare il formato esadecimale #RRGGBB (o #RRGGBBAA).
            </p>
          ) : null}
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!bgValid || !fgValid || saveMutation.isPending}
          >
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
