import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Pencil, Plus, RefreshCw, Trash2 } from "lucide-react"

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
import {
  createDdpStatus,
  deleteDdpDestination,
  fetchDdpDestinations,
  fetchDdpStatuses,
  updateDdpStatus,
} from "@/lib/api/ddp-config"
import type { DdpDestinationItem, DdpStatusItem } from "@/lib/api/types"
import { DdpDestinationFormDialog } from "@/features/commesse/DdpDestinationFormDialog"

export function DdpConfigPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [destDialog, setDestDialog] = React.useState<DdpDestinationItem | "new" | null>(
    null
  )
  const [statusDialog, setStatusDialog] = React.useState<DdpStatusItem | "new" | null>(
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

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ["ddp-destinations"] })
    await queryClient.invalidateQueries({ queryKey: ["ddp-statuses"] })
  }

  const deleteDestMutation = useMutation({
    mutationFn: deleteDdpDestination,
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
              <div className="flex justify-end">
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
