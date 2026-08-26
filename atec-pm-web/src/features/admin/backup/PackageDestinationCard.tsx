// ── Destinazione dei pacchetti di backup: si vede e si cambia da qui ──

import { useEffect, useState } from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { FolderCog, RotateCcw, Save } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  fetchBackupDestination,
  saveBackupDestination,
} from "@/lib/api/backup"
import { notifyError, notifyInfo } from "@/lib/toast"

const ORIGINE_LABEL: Record<string, string> = {
  pagina: "impostata da questa pagina",
  appsettings: "appsettings.json del server",
  predefinita: "predefinita (cartella locale del server)",
}

/**
 * Il PC/NAS di backup può cambiare nel tempo: da qui si vede DOVE nascono i
 * pacchetti completi e con che utente il servizio entra nella share, e si cambia
 * senza toccare i file del server. Il salvataggio passa dalla prova di scrittura
 * lato server: una destinazione che non funziona non viene salvata.
 */
export function PackageDestinationCard() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const query = useQuery({
    queryKey: ["backup-destination"],
    queryFn: fetchBackupDestination,
  })

  const [percorso, setPercorso] = useState("")
  const [shareUser, setShareUser] = useState("")
  const [sharePassword, setSharePassword] = useState("")

  // I campi si riallineano al dato del server quando arriva (e dopo un salvataggio):
  // la password no — non viene mai restituita, il campo resta il posto dove digitarla.
  useEffect(() => {
    if (!query.data) return
    setPercorso(query.data.percorso)
    setShareUser(query.data.origine === "pagina" ? query.data.shareUser : "")
  }, [query.data])

  const salva = useMutation({
    mutationFn: saveBackupDestination,
    onSuccess: async ({ messaggio }) => {
      notifyInfo(messaggio || "Destinazione salvata")
      setSharePassword("")
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["backup-destination"] }),
        queryClient.invalidateQueries({ queryKey: ["full-backup-estimate"] }),
        queryClient.invalidateQueries({ queryKey: ["full-backup-list"] }),
      ])
    },
    onError: (err: Error) => notifyError(err.message),
  })

  const handleRipristina = async () => {
    const ok = await confirm({
      title: "Tornare alla destinazione del server?",
      description:
        "L'impostazione fatta da questa pagina viene rimossa: i pacchetti torneranno " +
        "dove dice la configurazione del server (appsettings.json o cartella locale). " +
        "I pacchetti già creati restano dove sono.",
      confirmLabel: "Torna al server",
    })
    if (ok) salva.mutate({ percorso: "" })
  }

  const dest = query.data

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <div className="flex items-center gap-2">
              <FolderCog className="size-5 text-muted-foreground" />
              <CardTitle>Destinazione dei pacchetti</CardTitle>
              {dest ? (
                <Badge variant={dest.origine === "pagina" ? "default" : "secondary"}>
                  {ORIGINE_LABEL[dest.origine] ?? dest.origine}
                </Badge>
              ) : null}
            </div>
            <CardDescription>
              Dove nascono i pacchetti di backup completo. Un percorso di rete
              (\\server\cartella) mette la copia già fuori da questa macchina.
            </CardDescription>
          </div>
          {dest?.origine === "pagina" ? (
            <Button
              variant="outline"
              size="sm"
              onClick={() => void handleRipristina()}
              disabled={salva.isPending}
            >
              <RotateCcw />
              Torna alla destinazione del server
            </Button>
          ) : null}
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {query.error ? (
          <p className="text-sm text-destructive">{(query.error as Error).message}</p>
        ) : null}

        {dest ? (
          <div className="rounded-md border p-3 text-sm">
            <div className="font-medium">Adesso i pacchetti finiscono in</div>
            <div className="font-mono text-xs break-all text-muted-foreground">
              {dest.percorso}
            </div>
            {dest.inRete ? (
              <div className="mt-1 text-xs text-muted-foreground">
                Il servizio entra nella share come{" "}
                <span className="font-mono">{dest.shareUser || "credenziali del server"}</span>
                {dest.passwordSalvata ? " (password salvata da questa pagina)" : ""}
              </div>
            ) : null}
          </div>
        ) : null}

        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="dest-percorso">Nuovo percorso</Label>
            <Input
              id="dest-percorso"
              value={percorso}
              onChange={(e) => setPercorso(e.target.value)}
              placeholder="\\NAS\cartella\Backup oppure D:\Backups"
              className="font-mono text-xs"
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="dest-utente">Utente della share (solo percorsi di rete)</Label>
            <Input
              id="dest-utente"
              value={shareUser}
              onChange={(e) => setShareUser(e.target.value)}
              placeholder="vuoto = credenziali del server"
              className="font-mono text-xs"
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="dest-password">Password</Label>
            <Input
              id="dest-password"
              type="password"
              value={sharePassword}
              onChange={(e) => setSharePassword(e.target.value)}
              placeholder={
                dest?.passwordSalvata ? "vuota = resta quella salvata" : "vuota = nessuna"
              }
              autoComplete="new-password"
              className="text-xs"
            />
          </div>
        </div>

        <div className="flex items-center justify-between gap-2">
          <p className="text-xs text-muted-foreground">
            Il salvataggio prova DAVVERO la destinazione (accesso e scrittura di un file
            di prova): se non funziona, non viene salvata. La password resta cifrata sul
            server e non si può rileggere da qui.
          </p>
          <Button
            onClick={() => {
              // Il server la rifiuterebbe comunque, ma qui si spiega PRIMA del giro di rete.
              if (!shareUser.trim() && sharePassword) {
                notifyError(
                  "Hai indicato la password ma non l'utente: compila anche l'utente, " +
                    "oppure svuota la password per usare le credenziali del server."
                )
                return
              }
              salva.mutate({
                percorso: percorso.trim(),
                shareUser: shareUser.trim(),
                sharePassword,
              })
            }}
            disabled={salva.isPending || !percorso.trim()}
            className="shrink-0"
          >
            <Save />
            {salva.isPending ? "Provo e salvo…" : "Prova e salva"}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
