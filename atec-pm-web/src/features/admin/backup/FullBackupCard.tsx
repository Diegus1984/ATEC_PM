import { useEffect, useRef, useState } from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  Archive,
  Database,
  Download,
  FolderTree,
  RotateCcw,
  Trash2,
  Upload,
} from "lucide-react"

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
import { GridScroller } from "@/components/shared/grid-scroller"
import { Checkbox } from "@/components/ui/checkbox"
import {
  deleteFullBackup,
  deleteFullBackupBatch,
  downloadFullBackup,
  fetchFullBackupCurrentJob,
  fetchFullBackupEstimate,
  fetchFullBackupJob,
  fetchFullBackupList,
  restoreFullBackup,
  startFullBackup,
  uploadFullBackup,
} from "@/lib/api/backup"
import { getSession } from "@/lib/auth/session"
import { notifyError, notifyInfo } from "@/lib/toast"
import type { FullBackupPackage } from "@/lib/api/types"

/**
 * Backup completo: database + cartelle su disco (documenti di commessa, foto, video,
 * allegati) in un unico .zip. È quello che serve per rimettere in piedi il gestionale
 * su un'altra macchina — il backup .sql da solo salva i dati ma non i file.
 */
export function FullBackupCard() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const session = getSession()
  const inputFile = useRef<HTMLInputElement>(null)

  const stima = useQuery({
    queryKey: ["full-backup-estimate"],
    queryFn: fetchFullBackupEstimate,
  })

  const pacchetti = useQuery({
    queryKey: ["full-backup-list"],
    queryFn: fetchFullBackupList,
  })

  // Backup e ripristino girano sul server: si segue l'avanzamento interrogandolo.
  // Si tiene l'identificativo dell'operazione avviata da qui, altrimenti appena finisce
  // il server risponde "nessuna operazione in corso" e la barra sparirebbe all'ultima
  // percentuale vista, senza mai dire che è andata a buon fine.
  const [jobId, setJobId] = useState<string | null>(null)

  const job = useQuery({
    queryKey: ["full-backup-job", jobId],
    // Senza identificativo (pagina appena aperta) si chiede se c'è qualcosa in corso,
    // così ci si aggancia anche a un'operazione lanciata da un altro computer.
    queryFn: () =>
      jobId ? fetchFullBackupJob(jobId) : fetchFullBackupCurrentJob(),
    refetchInterval: (query) =>
      query.state.data?.stato === "in_corso" ? 1500 : false,
  })

  const inCorso = job.data?.stato === "in_corso"
  const statoJob = job.data?.stato

  // La console segue l'ultima riga, come i log di installazione.
  const consoleRef = useRef<HTMLDivElement>(null)
  const numeroRighe = job.data?.righe?.length ?? 0
  useEffect(() => {
    const box = consoleRef.current
    if (box) box.scrollTop = box.scrollHeight
  }, [numeroRighe])

  // A operazione finita l'elenco dei pacchetti è cambiato: va riletto.
  useEffect(() => {
    if (statoJob === "completato") {
      void queryClient.invalidateQueries({ queryKey: ["full-backup-list"] })
      void queryClient.invalidateQueries({ queryKey: ["full-backup-estimate"] })
    }
  }, [statoJob, queryClient])

  async function aggiornaTutto() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["full-backup-list"] }),
      queryClient.invalidateQueries({ queryKey: ["full-backup-estimate"] }),
      queryClient.invalidateQueries({ queryKey: ["full-backup-job"] }),
    ])
  }

  const avvia = useMutation({
    mutationFn: startFullBackup,
    onSuccess: (nuovo) => {
      setJobId(nuovo.id)
    },
  })

  const ripristina = useMutation({
    mutationFn: restoreFullBackup,
    onSuccess: (nuovo) => {
      setJobId(nuovo.id)
    },
  })

  const elimina = useMutation({
    mutationFn: deleteFullBackup,
    onSuccess: async () => {
      await aggiornaTutto()
    },
  })

  const carica = useMutation({
    mutationFn: (file: File) => uploadFullBackup(file, session?.token ?? null),
    onSuccess: async () => {
      await aggiornaTutto()
    },
  })

  async function chiediRipristino(
    row: FullBackupPackage,
    database: boolean,
    file: boolean
  ) {
    const cosa = database && file ? "database e cartelle" : database ? "solo il database" : "solo le cartelle"
    const ok = await confirm({
      title: "Ripristina dal pacchetto",
      description:
        `Verranno ripristinati ${cosa} dal pacchetto "${row.fileName}" (del ${row.date}). ` +
        (database
          ? "I dati attuali del database vengono cancellati e sostituiti (ne viene fatta prima una copia di sicurezza). "
          : "") +
        (file
          ? "Le cartelle attuali non vengono cancellate: restano accanto con il suffisso «.prima-ripristino». "
          : "") +
        "Meglio che nessuno stia lavorando sul gestionale in questo momento. Continuare?",
      confirmLabel: "Ripristina",
    })
    if (!ok) return
    ripristina.mutate({ fileName: row.fileName, database, file })
  }

  // Selezione multipla per la cancellazione in blocco dei pacchetti.
  const [selezionati, setSelezionati] = useState<Set<string>>(new Set())
  const righe = pacchetti.data ?? []
  const tuttiSelezionati =
    righe.length > 0 && righe.every((r) => selezionati.has(r.fileName))

  const toggleUno = (fileName: string, on: boolean) =>
    setSelezionati((prev) => {
      const next = new Set(prev)
      if (on) next.add(fileName)
      else next.delete(fileName)
      return next
    })

  const eliminaBatch = useMutation({
    mutationFn: deleteFullBackupBatch,
    onSuccess: async (messaggio) => {
      notifyInfo(messaggio || "Pacchetti eliminati")
      setSelezionati(new Set())
      await queryClient.invalidateQueries({ queryKey: ["full-backup-list"] })
    },
    onError: (err: Error) => notifyError(err.message),
  })

  const handleEliminaSelezionati = async () => {
    const nomi = righe.filter((r) => selezionati.has(r.fileName)).map((r) => r.fileName)
    if (nomi.length === 0) return
    const ok = await confirm({
      title: `Elimina ${nomi.length} pacchetti`,
      description:
        `Verranno eliminati definitivamente ${nomi.length} pacchetti completi. ` +
        "Un pacchetto eliminato non si può più ripristinare: controlla di averne " +
        "una copia da un'altra parte se ti serve.",
      confirmLabel: "Elimina selezionati",
    })
    if (ok) eliminaBatch.mutate(nomi)
  }

  const errore =
    avvia.error ||
    ripristina.error ||
    elimina.error ||
    carica.error ||
    pacchetti.error ||
    stima.error

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <div className="flex items-center gap-2">
              <CardTitle>Backup completo (database + file)</CardTitle>
              <Badge variant="secondary">ADMIN</Badge>
            </div>
            <CardDescription>
              Un unico pacchetto .zip con il database e le cartelle: documenti di
              commessa, foto, video e allegati. È la copia da portare fuori dal
              server.
            </CardDescription>
          </div>
          <div className="flex gap-2">
            {selezionati.size > 0 ? (
              <Button
                variant="destructive"
                onClick={() => void handleEliminaSelezionati()}
                disabled={eliminaBatch.isPending || inCorso}
              >
                <Trash2 />
                {eliminaBatch.isPending
                  ? "Elimino…"
                  : `Elimina selezionati (${righe.filter((r) => selezionati.has(r.fileName)).length})`}
              </Button>
            ) : null}
            {/* Pacchetto creato su un'altra macchina (es. il PC di sviluppo): si carica
                qui e poi si ripristina come gli altri. */}
            <input
              ref={inputFile}
              type="file"
              accept=".zip"
              className="hidden"
              onChange={(event) => {
                const file = event.target.files?.[0]
                if (file) carica.mutate(file)
                event.target.value = ""
              }}
            />
            <Button
              variant="outline"
              onClick={() => inputFile.current?.click()}
              disabled={carica.isPending || inCorso}
            >
              <Upload />
              {carica.isPending ? "Caricamento…" : "Carica pacchetto"}
            </Button>
            <Button onClick={() => avvia.mutate()} disabled={inCorso || avvia.isPending}>
              <Archive />
              {inCorso ? "Operazione in corso…" : "Crea pacchetto completo"}
            </Button>
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        {stima.data ? (
          <div className="grid gap-2 rounded-md border p-3 text-sm sm:grid-cols-2">
            {stima.data.cartelle.map((c) => (
              <div key={c.nome} className="flex items-start gap-2">
                <FolderTree className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                <div>
                  <div className="font-medium capitalize">{c.nome}</div>
                  <div className="text-muted-foreground">
                    <span className="font-mono text-xs break-all">{c.percorso}</span>
                    <br />
                    {c.esiste
                      ? `${c.file} file · ${c.dimensioneMB} MB`
                      : "cartella non trovata su questa macchina"}
                  </div>
                </div>
              </div>
            ))}
            <div className="flex items-start gap-2">
              <Database className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
              <div>
                <div className="font-medium">Destinazione dei pacchetti</div>
                <div className="text-muted-foreground">
                  <span className="font-mono text-xs break-all">
                    {stima.data.destinazione}
                  </span>
                  <br />
                  {stima.data.spazioLiberoDestinazioneGB > 0
                    ? `${stima.data.spazioLiberoDestinazioneGB} GB liberi`
                    : "spazio libero non rilevabile (percorso di rete)"}
                </div>
              </div>
            </div>
          </div>
        ) : null}

        {job.data ? (
          <div className="rounded-md border p-3 text-sm">
            <div className="flex items-center justify-between gap-2">
              <span className="font-medium">
                {job.data.tipo === "backup" ? "Creazione pacchetto" : "Ripristino"}
                {inCorso
                  ? ` — ${job.data.passo}`
                  : job.data.stato === "completato"
                    ? " — completato"
                    : " — non riuscito"}
              </span>
              <span className="text-muted-foreground">{job.data.percentuale}%</span>
            </div>
            <div className="mt-2 h-2 w-full overflow-hidden rounded-full bg-muted">
              <div
                className={
                  job.data.stato === "errore"
                    ? "h-full bg-destructive transition-all"
                    : "h-full bg-primary transition-all"
                }
                style={{ width: `${job.data.percentuale}%` }}
              />
            </div>
            {job.data.messaggio ? (
              <p
                className={
                  job.data.stato === "errore"
                    ? "mt-2 text-destructive"
                    : "mt-2 text-muted-foreground"
                }
              >
                {job.data.messaggio}
              </p>
            ) : null}

            {job.data.righe?.length ? (
              <div
                ref={consoleRef}
                className="mt-3 max-h-60 overflow-auto rounded-md border bg-muted/40 p-2 font-mono text-xs leading-relaxed"
              >
                {job.data.righe.map((riga, i) => (
                  <div
                    key={`${i}-${riga}`}
                    className={
                      riga.includes("ERRORE") || riga.includes("SCARTATA") || riga.includes("SALTATO")
                        ? "text-destructive"
                        : riga.includes("FATTO")
                          ? "font-medium"
                          : undefined
                    }
                  >
                    {riga}
                  </div>
                ))}
              </div>
            ) : null}
          </div>
        ) : null}

        {errore ? (
          <p className="text-sm text-destructive">{(errore as Error).message}</p>
        ) : null}

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
                      on ? new Set(righe.map((r) => r.fileName)) : new Set()
                    )
                  }
                  aria-label="Seleziona tutti i pacchetti"
                />
              </TableHead>
              <TableHead>Pacchetto</TableHead>
              <TableHead>Data</TableHead>
              <TableHead>Dimensione</TableHead>
              <TableHead>Contenuto</TableHead>
              <TableHead className="w-40" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {!pacchetti.data || pacchetti.data.length === 0 ? (
              <TableRow>
                <TableCell
                  colSpan={6}
                  className="h-24 text-center text-muted-foreground"
                >
                  {pacchetti.isLoading
                    ? "Caricamento…"
                    : "Nessun pacchetto completo. Creane uno e portalo fuori dal server."}
                </TableCell>
              </TableRow>
            ) : (
              pacchetti.data.map((row) => (
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
                  <TableCell className="text-muted-foreground">
                    {row.contenuto?.database
                      ? `${row.contenuto.database.righe} righe`
                      : "—"}
                    {row.contenuto?.file
                      ? ` · ${row.contenuto.file.totali} file`
                      : ""}
                    {row.contenuto?.file?.saltati
                      ? ` (${row.contenuto.file.saltati} saltati)`
                      : ""}
                  </TableCell>
                  <TableCell>
                    <div className="flex justify-end">
                      <RowActionsMenu
                        actions={[
                          {
                            label: "Scarica",
                            icon: Download,
                            onClick: () =>
                              downloadFullBackup(
                                row.fileName,
                                session?.token ?? null
                              ),
                          },
                          {
                            label: "Ripristina tutto",
                            icon: RotateCcw,
                            disabled: inCorso,
                            onClick: () => void chiediRipristino(row, true, true),
                          },
                          {
                            label: "Ripristina solo database",
                            icon: Database,
                            disabled: inCorso,
                            onClick: () => void chiediRipristino(row, true, false),
                          },
                          {
                            label: "Ripristina solo cartelle",
                            icon: FolderTree,
                            disabled: inCorso,
                            onClick: () => void chiediRipristino(row, false, true),
                          },
                          {
                            label: "Elimina",
                            icon: Trash2,
                            destructive: true,
                            separatorBefore: true,
                            disabled: elimina.isPending,
                            onClick: () => {
                              void confirm({
                                title: "Elimina pacchetto",
                                description: `Eliminare il pacchetto "${row.fileName}"?`,
                                confirmLabel: "Elimina",
                              }).then((ok) => {
                                if (ok) elimina.mutate(row.fileName)
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
      </CardContent>
    </Card>
  )
}
