import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { RefreshCw, Send } from "lucide-react"

import { ApiError } from "@/lib/api/client"
import {
  fetchDigestSettings,
  fetchDigestStatus,
  runDigestNow,
  saveDigestSettings,
} from "@/lib/api/digest"
import { fetchEmailSettings, saveEmailSettings, sendTestEmail } from "@/lib/api/settings"
import type { EmailSettingsDto, PlanDigestSettingsDto } from "@/lib/api/types"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Switch } from "@/components/ui/switch"
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { getSession } from "@/lib/auth/session"

const EMPTY_EMAIL: EmailSettingsDto = {
  enabled: false,
  smtpHost: "",
  smtpPort: 465,
  security: "auto",
  from: "",
  fromName: "ATEC PM",
  username: "",
  password: null,
  hasPassword: false,
  webUrl: "",
}

export function DigestEmailPage() {
  const isAdmin = getSession()?.user.userRole === "ADMIN"
  const queryClient = useQueryClient()

  const emailQuery = useQuery({
    queryKey: ["digest-email-settings"],
    queryFn: fetchEmailSettings,
    enabled: isAdmin,
  })
  const digestSettingsQuery = useQuery({
    queryKey: ["digest-settings"],
    queryFn: fetchDigestSettings,
    enabled: isAdmin,
  })
  const statusQuery = useQuery({
    queryKey: ["digest-status"],
    queryFn: fetchDigestStatus,
    enabled: isAdmin,
  })

  const [emailForm, setEmailForm] = React.useState<EmailSettingsDto>(EMPTY_EMAIL)
  const [testTo, setTestTo] = React.useState("")
  const [digestForm, setDigestForm] = React.useState<PlanDigestSettingsDto>({
    digestEnabled: false,
    digestTime: "07:00",
    digestWeekends: true,
    digestLastRun: "",
  })
  // Finché l'utente non tocca il campo, mostriamo pallini "pieni" al posto del valore reale
  // (mai restituito dal server) così si vede a colpo d'occhio se una password è già salvata.
  const [passwordTouched, setPasswordTouched] = React.useState(false)
  const PASSWORD_PLACEHOLDER_DOTS = "••••••••••"

  React.useEffect(() => {
    if (emailQuery.data) {
      setEmailForm({ ...emailQuery.data, password: null })
      setPasswordTouched(false)
    }
  }, [emailQuery.data])
  React.useEffect(() => {
    if (digestSettingsQuery.data) setDigestForm(digestSettingsQuery.data)
  }, [digestSettingsQuery.data])

  const saveEmailMutation = useMutation({
    mutationFn: () => saveEmailSettings(emailForm),
    onSuccess: async () => {
      setEmailForm((prev) => ({ ...prev, password: null }))
      await queryClient.invalidateQueries({ queryKey: ["digest-email-settings"] })
      await queryClient.invalidateQueries({ queryKey: ["digest-status"] })
    },
  })

  const testEmailMutation = useMutation({
    mutationFn: () => sendTestEmail({ toEmail: testTo }),
  })

  const saveDigestMutation = useMutation({
    mutationFn: () => saveDigestSettings(digestForm),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["digest-settings"] })
      await queryClient.invalidateQueries({ queryKey: ["digest-status"] })
    },
  })

  const runNowMutation = useMutation({
    mutationFn: runDigestNow,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["digest-status"] })
    },
  })

  if (!isAdmin) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>Digest email</CardTitle>
          <CardDescription>Solo gli utenti ADMIN possono configurare questa sezione.</CardDescription>
        </CardHeader>
      </Card>
    )
  }

  const status = statusQuery.data

  return (
    <div className="space-y-4">
      {/* Configurazione SMTP */}
      <Card>
        <CardHeader>
          <div className="flex items-center gap-2">
            <CardTitle>Configurazione email</CardTitle>
            <Badge variant="secondary">ADMIN</Badge>
          </div>
          <CardDescription>
            Server SMTP usato per il digest del piano risorse (riepilogo modifiche a dipendenti,
            responsabili di reparto e PM).
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <label className="flex items-center gap-2 text-sm">
            <Switch
              checked={emailForm.enabled}
              onCheckedChange={(checked) =>
                setEmailForm((p) => ({ ...p, enabled: checked }))
              }
            />
            Invio email attivo
          </label>

          <div className="grid grid-cols-2 gap-3">
            <div className="grid gap-1.5">
              <Label>Server SMTP</Label>
              <Input
                value={emailForm.smtpHost}
                onChange={(e) => setEmailForm((p) => ({ ...p, smtpHost: e.target.value }))}
                placeholder="smtps.aruba.it"
              />
            </div>
            <div className="grid gap-1.5">
              <Label>Porta</Label>
              <Input
                type="number"
                value={emailForm.smtpPort}
                onChange={(e) =>
                  setEmailForm((p) => ({ ...p, smtpPort: Number(e.target.value) || 465 }))
                }
              />
            </div>
          </div>

          <div className="grid gap-1.5">
            <Label>Sicurezza</Label>
            <Select
              value={emailForm.security}
              onValueChange={(v) => setEmailForm((p) => ({ ...p, security: v }))}
            >
              <SelectTrigger className="w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="auto">Automatica</SelectItem>
                <SelectItem value="ssl">SSL</SelectItem>
                <SelectItem value="starttls">STARTTLS</SelectItem>
                <SelectItem value="none">Nessuna</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="grid gap-1.5">
              <Label>Indirizzo mittente</Label>
              <Input
                value={emailForm.from}
                onChange={(e) => setEmailForm((p) => ({ ...p, from: e.target.value }))}
                placeholder="risorse@atec.srl"
              />
            </div>
            <div className="grid gap-1.5">
              <Label>Nome mittente</Label>
              <Input
                value={emailForm.fromName}
                onChange={(e) => setEmailForm((p) => ({ ...p, fromName: e.target.value }))}
              />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="grid gap-1.5">
              <Label>Username</Label>
              <Input
                value={emailForm.username}
                onChange={(e) => setEmailForm((p) => ({ ...p, username: e.target.value }))}
              />
            </div>
            <div className="grid gap-1.5">
              <Label>
                Password{" "}
                {emailForm.hasPassword && (
                  <span className="text-xs text-muted-foreground">(già salvata)</span>
                )}
              </Label>
              <Input
                type="password"
                value={
                  !passwordTouched && emailForm.hasPassword
                    ? PASSWORD_PLACEHOLDER_DOTS
                    : (emailForm.password ?? "")
                }
                onFocus={() => {
                  if (!passwordTouched) {
                    setPasswordTouched(true)
                    setEmailForm((p) => ({ ...p, password: "" }))
                  }
                }}
                onChange={(e) => {
                  setPasswordTouched(true)
                  setEmailForm((p) => ({ ...p, password: e.target.value }))
                }}
                placeholder="nuova password"
              />
            </div>
          </div>

          <div className="grid gap-1.5">
            <Label>URL sito (link nelle email)</Label>
            <Input
              value={emailForm.webUrl}
              onChange={(e) => setEmailForm((p) => ({ ...p, webUrl: e.target.value }))}
              placeholder="https://pm.atec.srl"
            />
          </div>

          {saveEmailMutation.error && (
            <p className="text-sm text-destructive">
              {(saveEmailMutation.error as ApiError).message}
            </p>
          )}
          {saveEmailMutation.isSuccess && (
            <p className="text-sm text-muted-foreground">Configurazione salvata.</p>
          )}

          <div className="flex items-center justify-between gap-2 border-t pt-4">
            <div className="flex items-center gap-2">
              <Input
                className="w-64"
                placeholder="indirizzo per la prova"
                value={testTo}
                onChange={(e) => setTestTo(e.target.value)}
              />
              <Button
                variant="outline"
                disabled={testEmailMutation.isPending || !testTo}
                onClick={() => testEmailMutation.mutate()}
              >
                <Send /> Invia prova
              </Button>
            </div>
            <Button
              disabled={saveEmailMutation.isPending}
              onClick={() => saveEmailMutation.mutate()}
            >
              {saveEmailMutation.isPending ? "Salvataggio…" : "Salva configurazione"}
            </Button>
          </div>
          {testEmailMutation.data && (
            <p className="text-sm text-muted-foreground">{testEmailMutation.data}</p>
          )}
          {testEmailMutation.error && (
            <p className="text-sm text-destructive">
              {(testEmailMutation.error as ApiError).message}
            </p>
          )}
        </CardContent>
      </Card>

      {/* Digest automatico */}
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <CardTitle>Digest automatico</CardTitle>
            <div className="flex gap-2">
              <Button
                variant="outline"
                onClick={() => statusQuery.refetch()}
                disabled={statusQuery.isFetching}
              >
                <RefreshCw className={statusQuery.isFetching ? "animate-spin" : ""} />
                Aggiorna
              </Button>
              <Button
                onClick={() => runNowMutation.mutate()}
                disabled={runNowMutation.isPending}
              >
                <Send /> {runNowMutation.isPending ? "Invio…" : "Esegui ora (prova)"}
              </Button>
            </div>
          </div>
          <CardDescription>
            Riepilogo giornaliero automatico delle modifiche al piano risorse (dipendenti,
            responsabili di reparto, PM).
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <label className="flex items-center gap-2 text-sm">
            <Switch
              checked={digestForm.digestEnabled}
              onCheckedChange={(checked) =>
                setDigestForm((p) => ({
                  ...p,
                  digestEnabled: checked,
                }))
              }
            />
            Abilita digest giornaliero automatico
          </label>

          <div className="grid grid-cols-2 gap-3">
            <div className="grid gap-1.5">
              <Label>Ora di invio</Label>
              <Input
                type="time"
                value={digestForm.digestTime}
                onChange={(e) =>
                  setDigestForm((p) => ({ ...p, digestTime: e.target.value }))
                }
              />
            </div>
            <div className="flex items-end pb-2">
              <label className="flex items-center gap-2 text-sm">
                <Switch
                  checked={digestForm.digestWeekends}
                  onCheckedChange={(checked) =>
                    setDigestForm((p) => ({
                      ...p,
                      digestWeekends: checked,
                    }))
                  }
                />
                Invia anche sabato e domenica
              </label>
            </div>
          </div>

          <p className="text-sm text-muted-foreground">
            Ultima esecuzione automatica: {digestForm.digestLastRun || "mai"}
          </p>

          {saveDigestMutation.error && (
            <p className="text-sm text-destructive">
              {(saveDigestMutation.error as ApiError).message}
            </p>
          )}

          <div className="flex justify-end">
            <Button
              disabled={saveDigestMutation.isPending}
              onClick={() => saveDigestMutation.mutate()}
            >
              {saveDigestMutation.isPending ? "Salvataggio…" : "Salva impostazioni"}
            </Button>
          </div>

          {runNowMutation.data && (
            <p className="rounded-md border bg-muted/40 p-3 text-sm">
              {runNowMutation.data.baselineCreated
                ? "Prima istantanea del piano creata: dal prossimo giro verranno rilevate le variazioni."
                : runNowMutation.data.message}
            </p>
          )}
          {runNowMutation.error && (
            <p className="text-sm text-destructive">
              {(runNowMutation.error as ApiError).message}
            </p>
          )}

          {status && (
            <div className="grid grid-cols-2 gap-3 border-t pt-4 text-sm sm:grid-cols-3">
              <div>
                <div className="text-muted-foreground">Ora server (Italia)</div>
                <div className="font-medium">
                  {new Date(status.serverTimeLocal).toLocaleString("it-IT")}
                </div>
              </div>
              <div>
                <div className="text-muted-foreground">Email configurata</div>
                <div className="font-medium">
                  {status.emailConfigurata ? "Sì" : "No — configurala sopra"}
                </div>
              </div>
              <div>
                <div className="text-muted-foreground">Attività nel piano</div>
                <div className="font-medium">{status.attivitaNelPiano}</div>
              </div>
              <div>
                <div className="text-muted-foreground">Dipendenti con email</div>
                <div className="font-medium">{status.dipendentiConEmail}</div>
              </div>
              <div>
                <div className="text-muted-foreground">Dipendenti senza email</div>
                <div className="font-medium">{status.dipendentiSenzaEmail}</div>
              </div>
            </div>
          )}

          {status && status.ultimeEsecuzioni.length > 0 && (
            <div className="border-t pt-4">
              <div className="mb-2 text-sm font-medium">Ultime esecuzioni</div>
              <div className="max-h-60 overflow-y-auto rounded-md border">
                {status.ultimeEsecuzioni.map((log, i) => (
                  <div
                    key={i}
                    className="flex items-center justify-between border-b px-3 py-1.5 text-xs last:border-b-0"
                  >
                    <span>
                      {new Date(log.runUtc).toLocaleString("it-IT")} · {log.trigger}
                    </span>
                    <span className="text-muted-foreground">{log.esito}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
