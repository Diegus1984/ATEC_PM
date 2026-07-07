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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  deleteBackup,
  downloadBackup,
  fetchBackupList,
  restoreBackup,
  runBackupNow,
} from "@/lib/api/backup"
import { getSession } from "@/lib/auth/session"

export function BackupPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const session = getSession()
  const isAdmin = session?.user.userRole === "ADMIN"

  const query = useQuery({
    queryKey: ["backup-list"],
    queryFn: fetchBackupList,
    enabled: isAdmin,
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

  if (!isAdmin) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Backup database</CardTitle>
          <CardDescription>
            Solo gli utenti ADMIN possono gestire i backup.
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
            <Table>
              <TableHeader>
                <TableRow>
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
                      colSpan={4}
                      className="h-24 text-center text-muted-foreground"
                    >
                      Nessun backup disponibile.
                    </TableCell>
                  </TableRow>
                ) : (
                  query.data.map((row) => (
                    <TableRow key={row.fileName}>
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
          ) : null}
        </CardContent>
      </Card>
    </div>
  )
}
