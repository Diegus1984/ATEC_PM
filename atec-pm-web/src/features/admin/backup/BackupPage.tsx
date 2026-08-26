import { useState } from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Download, HardDriveDownload, RefreshCw, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  deleteBackup,
  deleteBackupBatch,
  downloadBackup,
  fetchBackupList,
  restoreBackup,
  runBackupNow,
} from "@/lib/api/backup"
import { notifyError, notifyInfo } from "@/lib/toast"
import { canAccessFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"

import { FullBackupCard } from "./FullBackupCard"
import { PackageDestinationCard } from "./PackageDestinationCard"

export function BackupPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const session = getSession()
  // Permesso della pagina, non livello del ruolo: la funzione si concede alla persona.
  // Governa anche l'`enabled` della query, così senza permesso non si chiede nemmeno
  // l'elenco dei backup (l'API risponderebbe 403 e la pagina mostrerebbe un errore rosso).
  const canManageBackup = canAccessFeature("nav.backup")

  const query = useQuery({
    queryKey: ["backup-list"],
    queryFn: fetchBackupList,
    enabled: canManageBackup,
  })

  const backupMutation = useMutation({
    mutationFn: runBackupNow,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["backup-list"] })
    },
  })

  const restoreMutation = useMutation({
    mutationFn: restoreBackup,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["backup-list"] })
    },
  })

  const deleteMutation = useMutation({
    mutationFn: deleteBackup,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["backup-list"] })
    },
  })

  // Selezione multipla per la cancellazione in blocco (i backup si accumulano ogni
  // notte: toglierli uno alla volta dal menu era una punizione).
  const [selezionati, setSelezionati] = useState<Set<string>>(new Set())
  const files = query.data ?? []
  const tuttiSelezionati = files.length > 0 && files.every((f) => selezionati.has(f.fileName))

  const toggleUno = (fileName: string, on: boolean) =>
    setSelezionati((prev) => {
      const next = new Set(prev)
      if (on) next.add(fileName)
      else next.delete(fileName)
      return next
    })

  const deleteBatchMutation = useMutation({
    mutationFn: deleteBackupBatch,
    onSuccess: async (messaggio) => {
      notifyInfo(messaggio || "Backup eliminati")
      setSelezionati(new Set())
      await queryClient.invalidateQueries({ queryKey: ["backup-list"] })
    },
    onError: (err: Error) => notifyError(err.message),
  })

  const handleDeleteSelected = async () => {
    // Solo i nomi ancora in elenco: una selezione può contenere file già spariti
    // (cancellati da un collega, lista ricaricata) e i loro errori confonderebbero.
    const nomi = files.filter((f) => selezionati.has(f.fileName)).map((f) => f.fileName)
    if (nomi.length === 0) return
    const ok = await confirm({
      title: `Elimina ${nomi.length} backup`,
      description:
        `Verranno eliminati definitivamente ${nomi.length} file di backup dal server. ` +
        "L'operazione non è reversibile.",
      confirmLabel: "Elimina selezionati",
    })
    if (ok) deleteBatchMutation.mutate(nomi)
  }

  if (!canManageBackup) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Backup database</CardTitle>
          <CardDescription>
            Non hai il permesso per gestire i backup. Chiedi a un amministratore di
            abilitarti la funzione «Backup DB».
          </CardDescription>
        </CardHeader>
      </Card>
    )
  }

  async function handleRestore(fileName: string) {
    const ok = await confirm({
      title: "Ripristina backup",
      description:
        `ATTENZIONE: il ripristino da "${fileName}" CANCELLERÀ tutti i dati attuali ` +
        `e li sostituirà con quelli del backup. L'operazione non è reversibile. Continuare?`,
      confirmLabel: "Ripristina e sovrascrivi",
    })
    if (!ok) return
    restoreMutation.mutate(fileName)
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <div className="flex items-center gap-2">
                <CardTitle>Backup database</CardTitle>
                <Badge variant="secondary">ADMIN</Badge>
              </div>
              <CardDescription>
                Backup SQL completi del database ATEC PM
              </CardDescription>
            </div>
            <div className="flex gap-2">
              {selezionati.size > 0 ? (
                <Button
                  variant="destructive"
                  onClick={() => void handleDeleteSelected()}
                  disabled={deleteBatchMutation.isPending}
                >
                  <Trash2 />
                  {deleteBatchMutation.isPending
                    ? "Elimino…"
                    : `Elimina selezionati (${files.filter((f) => selezionati.has(f.fileName)).length})`}
                </Button>
              ) : null}
              <Button
                variant="outline"
                onClick={() => query.refetch()}
                disabled={query.isFetching}
              >
                <RefreshCw className={query.isFetching ? "animate-spin" : ""} />
                Aggiorna
              </Button>
              <Button
                onClick={() => backupMutation.mutate()}
                disabled={backupMutation.isPending}
              >
                <HardDriveDownload />
                {backupMutation.isPending ? "Backup…" : "Backup ora"}
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {backupMutation.isSuccess ? (
            <p className="text-sm text-muted-foreground">
              Ultimo backup completato.
            </p>
          ) : null}
          {restoreMutation.isSuccess ? (
            <div className="rounded-md border border-amber-300 bg-amber-50 p-3 text-sm dark:border-amber-900 dark:bg-amber-950/30">
              <p className="font-medium">Ripristino completato.</p>
              {restoreMutation.data ? (
                <p className="mt-1 text-muted-foreground">
                  Backup di sicurezza salvato in:{" "}
                  <span className="font-mono text-xs break-all">
                    {restoreMutation.data}
                  </span>
                </p>
              ) : null}
              <p className="mt-1 text-muted-foreground">
                Ricarica l&apos;applicazione se necessario.
              </p>
            </div>
          ) : null}
          {(backupMutation.error ||
            restoreMutation.error ||
            deleteMutation.error ||
            query.error) && (
            <p className="text-sm text-destructive">
              {(
                (backupMutation.error ||
                  restoreMutation.error ||
                  deleteMutation.error ||
                  query.error) as Error
              ).message}
            </p>
          )}

          {query.isLoading ? (
            <p className="text-sm text-muted-foreground">Caricamento…</p>
          ) : null}

          {query.data ? (
            <GridScroller className="rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-10">
                    <Checkbox
                      checked={
                        tuttiSelezionati
                          ? true
                          : selezionati.size > 0
                            ? "indeterminate"
                            : false
                      }
                      onCheckedChange={(on) =>
                        setSelezionati(
                          on ? new Set(files.map((f) => f.fileName)) : new Set()
                        )
                      }
                      aria-label="Seleziona tutti i backup"
                    />
                  </TableHead>
                  <TableHead>File</TableHead>
                  <TableHead>Data</TableHead>
                  <TableHead>Dimensione</TableHead>
                  <TableHead className="w-40" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {query.data.length === 0 ? (
                  <TableRow>
                    <TableCell
                      colSpan={5}
                      className="h-24 text-center text-muted-foreground"
                    >
                      Nessun backup disponibile.
                    </TableCell>
                  </TableRow>
                ) : (
                  query.data.map((row) => (
                    <TableRow
                      key={row.fileName}
                      data-state={selezionati.has(row.fileName) ? "selected" : undefined}
                    >
                      <TableCell>
                        <Checkbox
                          checked={selezionati.has(row.fileName)}
                          onCheckedChange={(on) => toggleUno(row.fileName, on === true)}
                          aria-label={`Seleziona ${row.fileName}`}
                        />
                      </TableCell>
                      <TableCell className="font-medium">{row.fileName}</TableCell>
                      <TableCell>{row.date}</TableCell>
                      <TableCell>{row.sizeMB} MB</TableCell>
                      <TableCell>
                        <div className="flex justify-end">
                          <RowActionsMenu
                            actions={[
                              {
                                label: "Scarica",
                                icon: Download,
                                onClick: () =>
                                  downloadBackup(
                                    row.fileName,
                                    session?.token ?? null
                                  ),
                              },
                              {
                                label: "Ripristina",
                                icon: HardDriveDownload,
                                disabled: restoreMutation.isPending,
                                onClick: () => handleRestore(row.fileName),
                              },
                              {
                                label: "Elimina",
                                icon: Trash2,
                                destructive: true,
                                separatorBefore: true,
                                disabled: deleteMutation.isPending,
                                onClick: () => {
                                  void confirm({
                                    title: "Elimina backup",
                                    description: `Eliminare il backup "${row.fileName}"?`,
                                    confirmLabel: "Elimina",
                                  }).then((ok) => {
                                    if (ok) {
                                      deleteMutation.mutate(row.fileName)
                                    }
                                  })
                                },
                              },
                            ]}
                          />
                        </div>
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
            </GridScroller>
          ) : null}
        </CardContent>
      </Card>

      <FullBackupCard />

      <PackageDestinationCard />
    </div>
  )
}
