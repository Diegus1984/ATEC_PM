import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Download, Pencil, Plus, RefreshCw, Trash2 } from "lucide-react"

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
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs"
import { Checkbox } from "@/components/ui/checkbox"
import {
  createDdpStatus,
  deleteDdpDestination,
  deleteDdpTreatment,
  fetchDdpDestinations,
  fetchDdpStatuses,
  fetchDdpStatusTransitions,
  fetchDdpTreatments,
  saveDdpStatusTransitions,
  updateDdpStatus,
} from "@/lib/api/ddp-config"
import type { DdpDestinationItem, DdpStatusItem, DdpTreatmentItem } from "@/lib/api/types"
import { DdpDestinationFormDialog } from "@/features/commesse/DdpDestinationFormDialog"
import { DdpTreatmentFormDialog } from "@/features/commesse/DdpTreatmentFormDialog"

/** Export CSV stati DDP (separatore `;`, BOM UTF-8 — come Prospetto SAL / Ferie). */
function downloadDdpStatusesCsv(rows: DdpStatusItem[]): void {
  const esc = (value: string | number | boolean): string => {
    const text = String(value)
    return /[;"\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
  }
  const sorted = [...rows].sort(
    (a, b) =>
      a.sortOrder - b.sortOrder ||
      a.statusKey.localeCompare(b.statusKey, "it")
  )
  const header = [
    "Chiave",
    "Etichetta",
    "ColoreSfondo",
    "ColoreTesto",
    "Ordine",
    "Attivo",
  ]
  const lines = sorted.map((row) =>
    [
      esc(row.statusKey),
      esc(row.label),
      esc(row.colorBg),
      esc(row.colorFg),
      esc(row.sortOrder),
      esc(row.isActive ? "Sì" : "No"),
    ].join(";")
  )
  const csv = "\uFEFF" + [header.join(";"), ...lines].join("\r\n")
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" })
  const url = URL.createObjectURL(blob)
  const a = document.createElement("a")
  a.href = url
  a.download = "ddp-stati.csv"
  a.click()
  URL.revokeObjectURL(url)
}

export function DdpConfigPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [destDialog, setDestDialog] = React.useState<DdpDestinationItem | "new" | null>(
    null
  )
  const [statusDialog, setStatusDialog] = React.useState<DdpStatusItem | "new" | null>(
    null
  )
  const [treatDialog, setTreatDialog] = React.useState<DdpTreatmentItem | "new" | null>(
    null
  )

  const destinationsQuery = useQuery({
    queryKey: ["ddp-destinations"],
    queryFn: fetchDdpDestinations,
  })

  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })

  const treatmentsQuery = useQuery({
    queryKey: ["ddp-treatments"],
    queryFn: fetchDdpTreatments,
  })

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ["ddp-destinations"] })
    await queryClient.invalidateQueries({ queryKey: ["ddp-statuses"] })
    await queryClient.invalidateQueries({ queryKey: ["ddp-treatments"] })
  }

  const deleteDestMutation = useMutation({
    mutationFn: deleteDdpDestination,
    onSuccess: invalidate,
  })

  const deleteTreatMutation = useMutation({
    mutationFn: deleteDdpTreatment,
    onSuccess: invalidate,
  })

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Configurazione DDP</CardTitle>
              <CardDescription>
                Destinazioni distinta e stati/causali delle righe DDP
              </CardDescription>
            </div>
            <Button
              variant="outline"
              size="sm"
              onClick={() => {
                void destinationsQuery.refetch()
                void statusesQuery.refetch()
                void treatmentsQuery.refetch()
              }}
            >
              <RefreshCw />
              Aggiorna
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <Tabs defaultValue="destinations">
            <TabsList>
              <TabsTrigger value="destinations">
                Destinazioni ({destinationsQuery.data?.length ?? 0})
              </TabsTrigger>
              <TabsTrigger value="statuses">
                Stati ({statusesQuery.data?.length ?? 0})
              </TabsTrigger>
              <TabsTrigger value="treatments">
                Trattamenti ({treatmentsQuery.data?.length ?? 0})
              </TabsTrigger>
              <TabsTrigger value="transitions">Matrice</TabsTrigger>
            </TabsList>

            <TabsContent value="destinations" className="space-y-4">
              <div className="flex justify-end">
                <Button onClick={() => setDestDialog("new")}>
                  <Plus />
                  Nuova destinazione
                </Button>
              </div>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Nome</TableHead>
                    <TableHead>Stato</TableHead>
                    <TableHead className="w-24" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(destinationsQuery.data ?? []).map((row) => (
                    <TableRow
                      key={row.id}
                      className="cursor-pointer"
                      onDoubleClick={() => setDestDialog(row)}
                    >
                      <TableCell className="font-medium">{row.name}</TableCell>
                      <TableCell>
                        <ActiveStatus active={row.isActive} />
                      </TableCell>
                      <TableCell>
                        <div className="flex justify-end">
                          <RowActionsMenu
                            actions={[
                              {
                                label: "Modifica",
                                icon: Pencil,
                                onClick: () => setDestDialog(row),
                              },
                              {
                                label: "Elimina",
                                icon: Trash2,
                                destructive: true,
                                separatorBefore: true,
                                onClick: () => {
                                  void confirm({
                                    title: "Elimina destinazione",
                                    description: `Eliminare "${row.name}"?`,
                                    confirmLabel: "Elimina",
                                  }).then((ok) => {
                                    if (ok) {
                                      deleteDestMutation.mutate(row.id)
                                    }
                                  })
                                },
                              },
                            ]}
                          />
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TabsContent>

            <TabsContent value="statuses" className="space-y-4">
              <div className="flex justify-end gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={(statusesQuery.data?.length ?? 0) === 0}
                  onClick={() =>
                    downloadDdpStatusesCsv(statusesQuery.data ?? [])
                  }
                >
                  <Download className="size-3.5" />
                  Esporta CSV
                </Button>
                <Button onClick={() => setStatusDialog("new")}>
                  <Plus />
                  Nuovo stato
                </Button>
              </div>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Chiave</TableHead>
                    <TableHead>Etichetta</TableHead>
                    <TableHead>Colori</TableHead>
                    <TableHead>Ordine</TableHead>
                    <TableHead>Stato</TableHead>
                    <TableHead className="w-16" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(statusesQuery.data ?? []).map((row) => (
                    <TableRow
                      key={row.id}
                      className="cursor-pointer"
                      onDoubleClick={() => setStatusDialog(row)}
                    >
                      <TableCell className="font-mono text-xs">
                        {row.statusKey}
                      </TableCell>
                      <TableCell>{row.label}</TableCell>
                      <TableCell>
                        <span
                          className="inline-flex rounded px-2 py-0.5 text-xs"
                          style={{
                            backgroundColor: row.colorBg,
                            color: row.colorFg,
                          }}
                        >
                          Anteprima
                        </span>
                      </TableCell>
                      <TableCell>{row.sortOrder}</TableCell>
                      <TableCell>
                        <ActiveStatus active={row.isActive} />
                      </TableCell>
                      <TableCell>
                        <div className="flex justify-end">
                          <RowActionsMenu
                            actions={[
                              {
                                label: "Modifica",
                                icon: Pencil,
                                onClick: () => setStatusDialog(row),
                              },
                            ]}
                          />
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TabsContent>

            <TabsContent value="treatments" className="space-y-4">
              <div className="flex justify-end">
                <Button onClick={() => setTreatDialog("new")}>
                  <Plus />
                  Nuovo trattamento
                </Button>
              </div>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Nome</TableHead>
                    <TableHead>Stato</TableHead>
                    <TableHead className="w-24" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(treatmentsQuery.data ?? []).map((row) => (
                    <TableRow
                      key={row.id}
                      className="cursor-pointer"
                      onDoubleClick={() => setTreatDialog(row)}
                    >
                      <TableCell className="font-medium">{row.name}</TableCell>
                      <TableCell>
                        <ActiveStatus active={row.isActive} />
                      </TableCell>
                      <TableCell>
                        <div className="flex justify-end">
                          <RowActionsMenu
                            actions={[
                              {
                                label: "Modifica",
                                icon: Pencil,
                                onClick: () => setTreatDialog(row),
                              },
                              {
                                label: "Elimina",
                                icon: Trash2,
                                destructive: true,
                                separatorBefore: true,
                                onClick: () => {
                                  void confirm({
                                    title: "Elimina trattamento",
                                    description: `Eliminare "${row.name}"?`,
                                    confirmLabel: "Elimina",
                                  }).then((ok) => {
                                    if (ok) {
                                      deleteTreatMutation.mutate(row.id)
                                    }
                                  })
                                },
                              },
                            ]}
                          />
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TabsContent>
            <TabsContent value="transitions">
              <TransitionsMatrixTab statuses={statusesQuery.data ?? []} />
            </TabsContent>
          </Tabs>
        </CardContent>
      </Card>

      <DdpDestinationFormDialog
        open={destDialog !== null}
        item={destDialog === "new" ? null : destDialog}
        existingNames={(destinationsQuery.data ?? []).map((d) => d.name)}
        onClose={() => setDestDialog(null)}
        onSaved={async () => {
          setDestDialog(null)
          await invalidate()
        }}
      />

      <DdpTreatmentFormDialog
        open={treatDialog !== null}
        item={treatDialog === "new" ? null : treatDialog}
        existingNames={(treatmentsQuery.data ?? []).map((t) => t.name)}
        onClose={() => setTreatDialog(null)}
        onSaved={async () => {
          setTreatDialog(null)
          await invalidate()
        }}
      />

      <StatusDialog
        open={statusDialog !== null}
        item={statusDialog === "new" ? null : statusDialog}
        onClose={() => setStatusDialog(null)}
        onSaved={async () => {
          setStatusDialog(null)
          await invalidate()
        }}
      />
    </div>
  )
}

const MATRIX_TYPES = [
  { key: "COMMERCIAL", label: "Commerciale" },
  { key: "OFFICINA", label: "Officina" },
] as const

type MatrixType = (typeof MATRIX_TYPES)[number]["key"]
type MatrixState = Record<MatrixType, Record<string, Set<string>>>

/** Chiave della finestra di partenza (riga DDP senza stato) nella matrice. */
const MATRIX_START_KEY = "INIZIO"

/**
 * Editor della matrice degli avanzamenti di stato (v7), una per tipo di distinta:
 * la Commerciale non contempla DC (il materiale commerciale si acquista), l'Officina sì.
 * Riga = stato corrente (più la riga INIZIO = finestra di partenza delle righe senza
 * stato), colonna = stato selezionabile nella finestra opzioni. Una coppia (tipo, stato)
 * non ancora governata appare con tutte le spunte ("tutto ammesso"); al salvataggio
 * diventa esplicita. Il salvataggio scrive SEMPRE entrambe le matrici.
 */
function TransitionsMatrixTab({ statuses }: { statuses: DdpStatusItem[] }) {
  const queryClient = useQueryClient()
  const [matrixType, setMatrixType] = React.useState<MatrixType>("COMMERCIAL")

  const transitionsQuery = useQuery({
    queryKey: ["ddp-status-transitions"],
    queryFn: fetchDdpStatusTransitions,
  })

  const active = React.useMemo(
    () =>
      [...statuses]
        .filter((s) => s.isActive)
        .sort(
          (a, b) =>
            a.sortOrder - b.sortOrder ||
            a.statusKey.localeCompare(b.statusKey, "it")
        ),
    [statuses]
  )

  const [matrix, setMatrix] = React.useState<MatrixState | null>(null)
  const [dirty, setDirty] = React.useState(false)
  const [error, setError] = React.useState<string | null>(null)

  // Stato locale dall'ultima risposta server; i refetch (focus/realtime) non
  // sovrascrivono le modifiche in corso (dirty).
  React.useEffect(() => {
    if (dirty || !transitionsQuery.data) return
    const allKeys = active.map((s) => s.statusKey.toUpperCase())
    const next = {} as MatrixState
    for (const { key: type } of MATRIX_TYPES) {
      const governed = new Map(
        transitionsQuery.data
          .filter((row) => row.ddpType.toUpperCase() === type)
          .map((row) => [
            row.fromKey.toUpperCase(),
            new Set(row.toKeys.map((k) => k.toUpperCase())),
          ])
      )
      const rows: Record<string, Set<string>> = {}
      for (const fromKey of [MATRIX_START_KEY, ...allKeys]) {
        const fromServer = governed.get(fromKey)
        rows[fromKey] = fromServer
          ? new Set(fromServer)
          : new Set(allKeys.filter((k) => k !== fromKey))
      }
      next[type] = rows
    }
    setMatrix(next)
  }, [transitionsQuery.data, active, dirty])

  const toggle = (fromKey: string, toKey: string) => {
    setMatrix((prev) => {
      if (!prev) return prev
      const rows = { ...prev[matrixType], [fromKey]: new Set(prev[matrixType][fromKey]) }
      if (rows[fromKey].has(toKey)) rows[fromKey].delete(toKey)
      else rows[fromKey].add(toKey)
      return { ...prev, [matrixType]: rows }
    })
    setDirty(true)
    setError(null)
  }

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!matrix) return false
      const allKeys = active.map((s) => s.statusKey.toUpperCase())
      return saveDdpStatusTransitions(
        MATRIX_TYPES.flatMap(({ key: type }) =>
          [MATRIX_START_KEY, ...allKeys].map((fromKey) => ({
            ddpType: type,
            fromKey,
            toKeys: [...(matrix[type][fromKey] ?? new Set<string>())],
          }))
        )
      )
    },
    onSuccess: async () => {
      setDirty(false)
      await queryClient.invalidateQueries({
        queryKey: ["ddp-status-transitions"],
      })
    },
    onError: (err: Error) => setError(err.message),
  })

  if (transitionsQuery.isLoading || !matrix) {
    return (
      <p className="py-8 text-center text-sm text-muted-foreground">
        Caricamento matrice…
      </p>
    )
  }

  const rows = matrix[matrixType]
  const columns = active
  const rowDefs: { fromKey: string; code: string; label: string }[] = [
    {
      fromKey: MATRIX_START_KEY,
      code: MATRIX_START_KEY,
      label: "DDP di partenza (riga senza stato)",
    },
    ...active.map((s) => ({
      fromKey: s.statusKey.toUpperCase(),
      code: s.statusKey,
      label: s.label,
    })),
  ]

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Tabs
          value={matrixType}
          onValueChange={(value) => setMatrixType(value as MatrixType)}
        >
          <TabsList>
            {MATRIX_TYPES.map(({ key, label }) => (
              <TabsTrigger key={key} value={key}>
                {label}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            disabled={!dirty || saveMutation.isPending}
            onClick={() => {
              setDirty(false)
              setError(null)
              void transitionsQuery.refetch()
            }}
          >
            Ripristina
          </Button>
          <Button
            size="sm"
            disabled={!dirty || saveMutation.isPending}
            onClick={() => saveMutation.mutate()}
          >
            Salva matrici
          </Button>
        </div>
      </div>
      <p className="text-xs text-muted-foreground">
        Riga = stato corrente · colonna = stato selezionabile al passaggio
        successivo. La riga INIZIO è la finestra proposta alle righe senza stato;
        uno stato senza alcuna spunta è terminale (nessun avanzamento). Il
        salvataggio scrive entrambe le matrici (Commerciale e Officina).
      </p>
      {error ? <p className="text-sm text-destructive">{error}</p> : null}
      <div className="overflow-x-auto rounded-md border">
        <table className="w-max border-collapse text-xs">
          <thead>
            <tr>
              <th className="sticky left-0 z-10 border-b border-r bg-muted px-2 py-1.5 text-left font-medium">
                Stato corrente ↓ / Opzione →
              </th>
              {columns.map((col) => (
                <th
                  key={col.statusKey}
                  title={col.label}
                  className="border-b px-1.5 py-1.5 text-center font-mono font-medium"
                >
                  <span
                    className="inline-flex rounded px-1.5 py-0.5"
                    style={{ backgroundColor: col.colorBg, color: col.colorFg }}
                  >
                    {col.statusKey}
                  </span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rowDefs.map((row) => (
              <tr key={row.fromKey} className="odd:bg-muted/30">
                <th
                  title={row.label}
                  className="sticky left-0 z-10 max-w-[22rem] truncate border-r bg-muted px-2 py-1 text-left font-normal"
                >
                  <span className="font-mono font-medium">{row.code}</span>
                  <span className="ml-2 text-muted-foreground">{row.label}</span>
                </th>
                {columns.map((col) => {
                  const toKey = col.statusKey.toUpperCase()
                  const isDiagonal = row.fromKey === toKey
                  return (
                    <td
                      key={col.statusKey}
                      className="px-1.5 py-1 text-center align-middle"
                    >
                      {isDiagonal ? (
                        <span className="text-muted-foreground/40">—</span>
                      ) : (
                        <Checkbox
                          checked={rows[row.fromKey]?.has(toKey) ?? false}
                          disabled={saveMutation.isPending}
                          aria-label={`${row.code} → ${col.statusKey}`}
                          onCheckedChange={() => toggle(row.fromKey, toKey)}
                        />
                      )}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

const STATUS_BG_PRESETS = [
  "#FF0000", "#FFC000", "#FFFF00", "#00B050", "#006400", "#00B0F0", "#2563EB",
  "#7030A0", "#8B008B", "#FFB6C1", "#B4B4B4", "#ADD8E6", "#000000", "#FFFFFF",
]

function StatusDialog({
  open,
  item,
  onClose,
  onSaved,
}: {
  open: boolean
  item: DdpStatusItem | null
  onClose: () => void
  onSaved: () => void
}) {
  const [label, setLabel] = React.useState("")
  const [colorBg, setColorBg] = React.useState("#CCCCCC")
  const [colorFg, setColorFg] = React.useState("#000000")
  const [sortOrder, setSortOrder] = React.useState("0")
  const [isActive, setIsActive] = React.useState(true)
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open) return
    setLabel(item?.label ?? "")
    setColorBg(item?.colorBg ?? "#CCCCCC")
    setColorFg(item?.colorFg ?? "#000000")
    setSortOrder(String(item?.sortOrder ?? 0))
    setIsActive(item?.isActive ?? true)
    setError(null)
  }, [open, item])

  const saveMutation = useMutation({
    mutationFn: async () => {
      const payload = {
        id: item?.id ?? 0,
        label: label.trim(),
        colorBg,
        colorFg,
        sortOrder: Number(sortOrder),
        isActive,
      }
      if (item) {
        return updateDdpStatus(item.id, payload)
      }
      return createDdpStatus(payload)
    },
    onSuccess: onSaved,
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{item ? "Modifica stato" : "Nuovo stato"}</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          {item ? (
            <p className="text-xs text-muted-foreground">
              Chiave: <span className="font-mono">{item.statusKey}</span> (non
              modificabile)
            </p>
          ) : null}
          <div className="space-y-2">
            <Label htmlFor="status-label">Etichetta</Label>
            <Input
              id="status-label"
              value={label}
              onChange={(event) => setLabel(event.target.value)}
            />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label htmlFor="status-bg">Sfondo</Label>
              <Input
                id="status-bg"
                value={colorBg}
                onChange={(event) => setColorBg(event.target.value)}
              />
              <div className="flex flex-wrap gap-1">
                {STATUS_BG_PRESETS.map((hex) => (
                  <button
                    key={hex}
                    type="button"
                    className="size-5 rounded border"
                    style={{ backgroundColor: hex }}
                    onClick={() => setColorBg(hex)}
                  />
                ))}
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="status-fg">Testo</Label>
              <Input
                id="status-fg"
                value={colorFg}
                onChange={(event) => setColorFg(event.target.value)}
              />
              <div className="flex gap-2">
                <Button type="button" variant="outline" size="sm" onClick={() => setColorFg("#FFFFFF")}>
                  Bianco
                </Button>
                <Button type="button" variant="outline" size="sm" onClick={() => setColorFg("#000000")}>
                  Nero
                </Button>
              </div>
            </div>
          </div>
          <div
            className="inline-flex rounded px-3 py-1 text-sm font-medium"
            style={{ backgroundColor: colorBg, color: colorFg }}
          >
            {label.trim() || "(etichetta)"}
          </div>
          <div className="space-y-2">
            <Label htmlFor="status-order">Ordine</Label>
            <Input
              id="status-order"
              type="number"
              value={sortOrder}
              onChange={(event) => setSortOrder(event.target.value)}
            />
          </div>
          <div className="flex items-center gap-2">
            <Switch
              id="status-active"
              checked={isActive}
              onCheckedChange={setIsActive}
            />
            <Label htmlFor="status-active">Attivo</Label>
          </div>
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!label.trim() || saveMutation.isPending}
          >
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
