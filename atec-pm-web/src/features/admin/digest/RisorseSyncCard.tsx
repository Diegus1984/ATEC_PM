import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plug, RefreshCw, Save } from "lucide-react"

import { GridScroller } from "@/components/shared/grid-scroller"
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
  fetchSyncSettings,
  fetchSyncStatus,
  runSyncNow,
  saveSyncSettings,
  testSync,
} from "@/lib/api/risorse-sync"
import type { RisorseSyncSettingsDto, RisorseSyncStatusDto } from "@/lib/api/types"
import { formatDateTimeOrDash } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"

/** I soli campi che il form governa: `lastRun`/`lastEsito`/`lastError` restano fuori,
 *  così un giro automatico in sottofondo non fa «cambiare» le impostazioni. */
type SyncSettingsEditabili = Pick<
  RisorseSyncSettingsDto,
  "enabled" | "baseUrl" | "username" | "hasPassword"
>

type SyncForm = SyncSettingsEditabili & { password: string }

const EMPTY_FORM: SyncForm = {
  enabled: false,
  baseUrl: "",
  username: "",
  password: "",
  hasPassword: false,
}

/** `select` della query impostazioni: tiene solo i campi editabili (vedi `SyncSettingsEditabili`). */
function soloCampiEditabili(d: RisorseSyncSettingsDto): SyncSettingsEditabili {
  return {
    enabled: d.enabled,
    baseUrl: d.baseUrl,
    username: d.username,
    hasPassword: d.hasPassword,
  }
}

/** Ogni quanto si rilegge lo stato mentre la pagina resta aperta. */
const STATUS_REFRESH_MS = 30_000

/**
 * Difesa UTC: il server manda date-ora UTC, ma se la stringa ISO arriva senza "Z"
 * né offset il browser la leggerebbe come ora locale. Si aggiunge la "Z" mancante.
 */
function utc(value: string | null | undefined): string | null | undefined {
  if (!value) return value
  return /(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : `${value}Z`
}

/** Host raggiungibili in chiaro: localhost e le reti private (127.x, 10.x, 192.168.x, 172.16-31.x). */
function isRetePrivata(host: string): boolean {
  const h = host.toLowerCase()
  if (h === "localhost" || h === "[::1]" || h === "::1") return true
  const m = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(h)
  if (!m) return false
  const a = Number(m[1])
  const b = Number(m[2])
  return (
    a === 127 || a === 10 || (a === 192 && b === 168) || (a === 172 && b >= 16 && b <= 31)
  )
}

/**
 * Validazione dell'indirizzo, speculare a quella del server: URL assoluto, http ammesso
 * solo verso la rete locale, verso Internet serve https. Restituisce il messaggio da
 * mostrare, o `null` se l'indirizzo passa.
 */
function validaIndirizzo(raw: string): string | null {
  const testo = raw.trim()
  if (!testo) return null // vuoto ammesso a sincronizzazione spenta: l'obbligo lo decide il server
  let url: URL
  try {
    url = new URL(testo)
  } catch {
    return "Indirizzo non valido: serve un URL assoluto (https://…)"
  }
  if (url.protocol === "https:") return null
  if (url.protocol !== "http:") return "Indirizzo non valido: serve http:// o https://"
  return isRetePrivata(url.hostname) ? null : "Verso Internet serve https"
}

/** Durata di un giro: millisecondi sotto il secondo, altrimenti secondi con un decimale. */
function formatDurata(ms: number): string {
  if (ms < 1000) return `${ms} ms`
  return `${(ms / 1000).toLocaleString("it-IT", { maximumFractionDigits: 1 })} s`
}

function descriviStato(status: RisorseSyncStatusDto): string {
  if (!status.configured) return "non configurata"
  return status.enabled ? "attiva" : "spenta"
}

/**
 * Scheda di configurazione della sincronizzazione con ATEC Risorse sul VPS:
 * credenziali, prova di collegamento, giro manuale e stato del servizio con
 * gli ultimi giri. Vive nella pagina «Digest email» sotto le altre schede.
 */
export function RisorseSyncCard() {
  const queryClient = useQueryClient()

  const settingsQuery = useQuery({
    queryKey: ["risorse-sync-settings"],
    queryFn: fetchSyncSettings,
    select: soloCampiEditabili,
  })
  // Lo stato si rilegge da solo ogni 30 s: il servizio gira sul server e
  // può cambiare (hub che cade, giro automatico) senza che l'utente tocchi nulla.
  // Se la lettura fallisce il giro si ferma (niente martellate su un server giù)
  // e riparte con «Aggiorna»; in una scheda in sottofondo non si rilegge.
  const statusQuery = useQuery({
    queryKey: ["risorse-sync-status"],
    queryFn: fetchSyncStatus,
    refetchInterval: (q) => (q.state.error ? false : STATUS_REFRESH_MS),
    refetchIntervalInBackground: false,
    retry: false,
  })

  const [form, setForm] = React.useState<SyncForm>(EMPTY_FORM)
  // `dirty` = l'utente ha toccato un campo e non ha ancora salvato: finché è vero
  // i dati che arrivano dal server non sovrascrivono quello che sta scrivendo.
  const [dirty, setDirty] = React.useState(false)

  React.useEffect(() => {
    // La password non arriva mai dal server: il campo parte vuoto e resta vuoto
    // finché l'utente non ne scrive una nuova (vuoto = non cambiarla).
    if (!settingsQuery.data || dirty) return
    setForm({ ...settingsQuery.data, password: "" })
  }, [settingsQuery.data, dirty])

  const invalidaTutto = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["risorse-sync-settings"] }),
      queryClient.invalidateQueries({ queryKey: ["risorse-sync-status"] }),
    ])
  }

  const testMutation = useMutation({
    mutationFn: testSync,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["risorse-sync-status"] })
    },
    onError: (error) => notifyError(error, "Il server ATEC Risorse non risponde"),
  })

  const saveMutation = useMutation({
    mutationFn: () =>
      saveSyncSettings({
        enabled: form.enabled,
        baseUrl: form.baseUrl.trim(),
        username: form.username.trim(),
        password: form.password ? form.password : null,
        hasPassword: form.hasPassword,
      }),
    onSuccess: async () => {
      // Il campo password si svuota; se ne era stata scritta una, ora c'è una password salvata.
      setForm((p) => ({
        ...p,
        password: "",
        hasPassword: p.hasPassword || Boolean(p.password),
      }))
      // L'esito di una prova fatta con le impostazioni vecchie non vale più.
      testMutation.reset()
      notifySuccess("Impostazioni di sincronizzazione salvate.")
      // Prima i dati freschi, poi il form torna «pulito»: con dirty ancora alzato l'effetto
      // ignora la cache vecchia, e al passaggio a false applica quelli appena riletti.
      await invalidaTutto()
      setDirty(false)
    },
    onError: (error) => notifyError(error, "Salvataggio delle impostazioni non riuscito"),
  })

  const runNowMutation = useMutation({
    mutationFn: runSyncNow,
    onSuccess: async () => {
      await invalidaTutto()
    },
    onError: (error) => notifyError(error, "Sincronizzazione non riuscita"),
  })

  /** Modifica di un campo del form: segna `dirty` e, sui campi del collegamento,
   *  azzera l'esito della prova (che valeva per le impostazioni salvate prima). */
  const aggiornaForm = (patch: Partial<SyncForm>) => {
    setForm((p) => ({ ...p, ...patch }))
    setDirty(true)
    if ("baseUrl" in patch || "username" in patch || "password" in patch) {
      testMutation.reset()
    }
  }

  const status = statusQuery.data
  const inCorso = status?.inCorso ?? false
  const occupato =
    saveMutation.isPending || testMutation.isPending || runNowMutation.isPending
  // Finché le impostazioni non sono arrivate il form non si tocca e non si salva.
  const settingsPronte = Boolean(settingsQuery.data)
  const erroreIndirizzo = validaIndirizzo(form.baseUrl)

  const salva = () => {
    // Controllo speculare a quello del server, senza chiamarlo.
    if (erroreIndirizzo) {
      notifyError(erroreIndirizzo)
      return
    }
    saveMutation.mutate()
  }

  const provaCollegamento = () => {
    if (erroreIndirizzo) {
      notifyError(erroreIndirizzo)
      return
    }
    testMutation.mutate()
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <div className="flex items-center gap-2">
              <CardTitle>Sincronizzazione ATEC Risorse (VPS)</CardTitle>
              <Badge variant="secondary">ADMIN</Badge>
            </div>
            <CardDescription>
              Tiene allineato il planner Risorse con il programma ATEC Risorse sul VPS, nei
              due versi.
            </CardDescription>
          </div>
          <Button
            variant="outline"
            onClick={() => statusQuery.refetch()}
            disabled={statusQuery.isFetching}
          >
            <RefreshCw className={statusQuery.isFetching ? "animate-spin" : ""} />
            Aggiorna
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {settingsQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">Caricamento…</p>
        ) : null}
        {settingsQuery.error ? (
          <p className="text-sm text-destructive">{settingsQuery.error.message}</p>
        ) : null}

        <label className="flex items-center gap-2 text-sm">
          <Switch
            checked={form.enabled}
            disabled={!settingsPronte}
            onCheckedChange={(checked) => aggiornaForm({ enabled: checked })}
          />
          Attiva
        </label>

        <div className="grid gap-1.5">
          <Label>Indirizzo del server</Label>
          <Input
            value={form.baseUrl}
            disabled={!settingsPronte}
            onChange={(e) => aggiornaForm({ baseUrl: e.target.value })}
            placeholder="https://nome-del-server"
          />
          {settingsPronte && erroreIndirizzo ? (
            <p className="text-xs text-destructive">{erroreIndirizzo}</p>
          ) : null}
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div className="grid gap-1.5">
            <Label>Utente</Label>
            <Input
              value={form.username}
              disabled={!settingsPronte}
              onChange={(e) => aggiornaForm({ username: e.target.value })}
              placeholder="utente di servizio"
            />
          </div>
          <div className="grid gap-1.5">
            <Label>
              Password{" "}
              {form.hasPassword ? (
                <span className="text-xs text-muted-foreground">(già salvata)</span>
              ) : null}
            </Label>
            <Input
              type="password"
              autoComplete="new-password"
              value={form.password}
              disabled={!settingsPronte}
              onChange={(e) => aggiornaForm({ password: e.target.value })}
              placeholder="lascia vuoto per non cambiarla"
            />
            <p className="text-xs text-muted-foreground">
              Lascia vuoto per non cambiarla.
            </p>
          </div>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-4">
          <div className="flex flex-wrap items-center gap-2">
            {/* La prova usa le impostazioni SALVATE sul server: con modifiche in sospeso non ha senso. */}
            <Button
              variant="outline"
              disabled={occupato || !settingsPronte || dirty || Boolean(erroreIndirizzo)}
              title={dirty ? "Salva prima le impostazioni" : undefined}
              onClick={provaCollegamento}
            >
              <Plug />{" "}
              {testMutation.isPending ? "Prova in corso…" : "Prova collegamento"}
            </Button>
            <Button
              variant="outline"
              disabled={occupato || inCorso}
              onClick={() => runNowMutation.mutate()}
            >
              <RefreshCw className={runNowMutation.isPending ? "animate-spin" : ""} />
              {runNowMutation.isPending || inCorso
                ? "Sincronizzazione…"
                : "Sincronizza adesso"}
            </Button>
            {dirty ? (
              <span className="text-xs text-muted-foreground">
                Salva prima le impostazioni per provare il collegamento.
              </span>
            ) : null}
          </div>
          <Button
            disabled={occupato || !settingsPronte || Boolean(erroreIndirizzo)}
            onClick={salva}
          >
            <Save /> {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </div>

        {/* Esito della prova di collegamento: versione, ora e conteggi letti dal VPS. */}
        {testMutation.data ? (
          <div className="rounded-md border bg-muted/40 p-3 text-sm">
            <div className="mb-2 font-medium">Collegamento riuscito</div>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
              <div>
                <div className="text-muted-foreground">Versione</div>
                <div className="font-medium">{testMutation.data.version || "—"}</div>
              </div>
              <div>
                <div className="text-muted-foreground">Ora del server</div>
                <div className="font-medium">
                  {formatDateTimeOrDash(utc(testMutation.data.serverUtc))}
                </div>
              </div>
              <div>
                <div className="text-muted-foreground">Dipendenti</div>
                <div className="font-medium tabular-nums">
                  {testMutation.data.employees}
                </div>
              </div>
              <div>
                <div className="text-muted-foreground">Commesse</div>
                <div className="font-medium tabular-nums">
                  {testMutation.data.projects}
                </div>
              </div>
              <div>
                <div className="text-muted-foreground">Reparti</div>
                <div className="font-medium tabular-nums">
                  {testMutation.data.departments}
                </div>
              </div>
              <div>
                <div className="text-muted-foreground">Allocazioni</div>
                <div className="font-medium tabular-nums">
                  {testMutation.data.assignments}
                </div>
              </div>
            </div>
          </div>
        ) : null}

        {/* Esito del giro lanciato a mano. */}
        {runNowMutation.data ? (
          <p className="rounded-md border bg-muted/40 p-3 text-sm">
            <span className="font-medium">
              Giro del {formatDateTimeOrDash(utc(runNowMutation.data.runUtc))}
            </span>
            {" · "}
            {runNowMutation.data.esito}
            {" · "}
            {formatDurata(runNowMutation.data.durataMs)}
            {runNowMutation.data.dettaglio ? (
              <>
                <br />
                <span className="text-muted-foreground">
                  {runNowMutation.data.dettaglio}
                </span>
              </>
            ) : null}
          </p>
        ) : null}

        {statusQuery.error ? (
          <p className="text-sm text-destructive">{statusQuery.error.message}</p>
        ) : null}

        {status ? (
          <div className="grid grid-cols-2 gap-3 border-t pt-4 text-sm sm:grid-cols-3">
            <div>
              <div className="text-muted-foreground">Stato</div>
              <div className="font-medium">{descriviStato(status)}</div>
            </div>
            <div>
              <div className="text-muted-foreground">Hub</div>
              <div className="font-medium">
                {status.hubConnected ? "collegato" : "scollegato"}
              </div>
            </div>
            <div>
              <div className="text-muted-foreground">In corso</div>
              <div className="font-medium">{status.inCorso ? "sì" : "no"}</div>
            </div>
            <div>
              <div className="text-muted-foreground">Ultimo giro</div>
              <div className="font-medium">
                {formatDateTimeOrDash(utc(status.lastRun))}
              </div>
            </div>
            <div>
              <div className="text-muted-foreground">Esito</div>
              <div className="font-medium">{status.lastEsito || "—"}</div>
            </div>
            <div>
              <div className="text-muted-foreground">Errore</div>
              <div
                className={
                  status.lastError ? "font-medium text-destructive" : "font-medium"
                }
              >
                {status.lastError || "—"}
              </div>
            </div>
          </div>
        ) : null}

        {status ? (
          <div className="border-t pt-4">
            <div className="mb-2 text-sm font-medium">Ultimi giri</div>
            <GridScroller className="rounded-lg border" scrollerClassName="max-h-60">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Quando</TableHead>
                    <TableHead>Innesco</TableHead>
                    <TableHead>Esito</TableHead>
                    <TableHead className="text-right">Durata</TableHead>
                    <TableHead>Dettaglio</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {status.ultimiGiri.length === 0 ? (
                    <TableRow>
                      <TableCell
                        colSpan={5}
                        className="h-24 text-center text-muted-foreground"
                      >
                        Nessun giro eseguito finora.
                      </TableCell>
                    </TableRow>
                  ) : (
                    status.ultimiGiri.map((giro, i) => (
                      <TableRow key={`${giro.runUtc}-${i}`}>
                        <TableCell className="whitespace-nowrap">
                          {formatDateTimeOrDash(utc(giro.runUtc))}
                        </TableCell>
                        <TableCell>{giro.innesco}</TableCell>
                        <TableCell>{giro.esito}</TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatDurata(giro.durataMs)}
                        </TableCell>
                        <TableCell className="max-w-md whitespace-normal text-muted-foreground">
                          {giro.dettaglio || "—"}
                        </TableCell>
                      </TableRow>
                    ))
                  )}
                </TableBody>
              </Table>
            </GridScroller>
          </div>
        ) : null}
      </CardContent>
    </Card>
  )
}
