import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { KeyRound, PlugZap } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  fetchHrEcosSettings,
  saveHrEcosSettings,
  testHrEcosSettings,
} from "@/lib/api/hr"
import { notifyError, notifySuccess } from "@/lib/toast"

/**
 * Credenziali di accesso a EcosAgile: utente, password e Client ID.
 *
 * <p>È il dialogo «Configurazione Credenziali» del programma «Timbrature», portato qui:
 * prima queste tre cose si potevano scrivere solo a mano nell'`appsettings.json` del
 * server, cioè entrando sulla macchina. Ora si mettono da qui, la password viene cifrata
 * a riposo e non torna più indietro — si può solo sostituire.</p>
 *
 * <p>Quello che resta nell'appsettings continua a valere come ripiego: chi le ha già messe
 * là non deve rifare niente, e la riga «da dove arrivano» lo dice sempre.</p>
 */
export function CredenzialiEcosDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const queryClient = useQueryClient()

  const impostazioniQuery = useQuery({
    queryKey: ["hr-ecos-settings"],
    queryFn: fetchHrEcosSettings,
    enabled: open,
  })

  const [baseUrl, setBaseUrl] = React.useState("")
  const [userId, setUserId] = React.useState("")
  const [clientId, setClientId] = React.useState("")
  const [password, setPassword] = React.useState("")
  const [esitoProva, setEsitoProva] = React.useState<{ ok: boolean; message: string } | null>(null)

  // I campi si riempiono quando il dialogo si apre: se li si ricaricasse a ogni risposta
  // del server, una modifica a metà sparirebbe sotto le dita.
  React.useEffect(() => {
    if (!open || !impostazioniQuery.data) return
    setBaseUrl(impostazioniQuery.data.baseUrl)
    setUserId(impostazioniQuery.data.userId)
    setClientId(impostazioniQuery.data.clientId)
    setPassword("")
    setEsitoProva(null)
  }, [open, impostazioniQuery.data])

  const salva = useMutation({
    mutationFn: () =>
      saveHrEcosSettings({
        baseUrl,
        userId,
        clientId,
        // Vuota = non la si sta cambiando: il server tiene quella che ha.
        password: password.length > 0 ? password : null,
      }),
    onSuccess: () => {
      setPassword("")
      void queryClient.invalidateQueries({ queryKey: ["hr-ecos-settings"] })
      void queryClient.invalidateQueries({ queryKey: ["hr-status"] })
      notifySuccess("Credenziali Ecos salvate.")
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Salvataggio non riuscito."),
  })

  const prova = useMutation({
    mutationFn: testHrEcosSettings,
    onSuccess: (esito) => setEsitoProva(esito),
    onError: (e) =>
      setEsitoProva({
        ok: false,
        message: e instanceof Error ? e.message : "Prova non riuscita.",
      }),
  })

  const impostazioni = impostazioniQuery.data
  const daFile = impostazioni?.source === "APPSETTINGS"

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <KeyRound className="size-4" />
            Credenziali Ecos
          </DialogTitle>
          <DialogDescription>
            Utente, password e Client ID con cui ATEC PM entra in EcosAgile. La password
            viene cifrata sul server e non è più leggibile: si può solo sostituire.
          </DialogDescription>
        </DialogHeader>

        {impostazioniQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">Caricamento…</p>
        ) : (
          <div className="space-y-3">
            <div className="space-y-1.5">
              <Label htmlFor="ecos-user">Utente</Label>
              <Input
                id="ecos-user"
                value={userId}
                onChange={(e) => setUserId(e.target.value)}
                autoComplete="off"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="ecos-password">Password</Label>
              <Input
                id="ecos-password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder={
                  impostazioni?.hasPassword
                    ? "••••••••  (lasciare vuoto per non cambiarla)"
                    : "nessuna password impostata"
                }
                autoComplete="new-password"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="ecos-client">Client ID</Label>
              <Input
                id="ecos-client"
                value={clientId}
                onChange={(e) => setClientId(e.target.value)}
                autoComplete="off"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="ecos-baseurl">Indirizzo API</Label>
              <Input
                id="ecos-baseurl"
                value={baseUrl}
                onChange={(e) => setBaseUrl(e.target.value)}
                className="font-mono text-xs"
                autoComplete="off"
              />
              <p className="text-[11px] text-muted-foreground">
                Un solo indirizzo per tutte le operazioni: l&apos;API la sceglie il
                parametro <code>ApiName</code>. Si cambia solo se lo dice SoftAgile.
              </p>
            </div>

            {impostazioni && (
              <p className="text-xs text-muted-foreground">
                {daFile
                  ? "In uso: quelle scritte nell'appsettings del server. Salvando qui, da qui in poi valgono queste."
                  : "In uso: quelle salvate da questa pagina."}
                {!impostazioni.configured &&
                  " ⚠ Mancano utente, password o Client ID: l'import resta fermo."}
              </p>
            )}

            {esitoProva && (
              <p
                className={
                  esitoProva.ok
                    ? "text-xs text-emerald-700 dark:text-emerald-400"
                    : "text-xs text-destructive"
                }
              >
                {esitoProva.ok ? "✅ " : "⚠ "}
                {esitoProva.message}
              </p>
            )}

            <div className="flex justify-end gap-2 pt-1">
              <Button
                variant="outline"
                size="sm"
                onClick={() => prova.mutate()}
                disabled={prova.isPending || salva.isPending}
                title="Chiede un token a Ecos: non legge e non scrive nessun dato"
              >
                <PlugZap className="mr-1 size-3.5" />
                {prova.isPending ? "Provo…" : "Prova collegamento"}
              </Button>
              <Button
                size="sm"
                onClick={() => salva.mutate()}
                disabled={salva.isPending || !userId.trim() || !clientId.trim()}
              >
                {salva.isPending ? "Salvo…" : "Salva"}
              </Button>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
