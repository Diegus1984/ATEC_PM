import * as React from "react"
import { useNavigate, useParams } from "react-router-dom"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowLeft, Copy, RotateCcw, TriangleAlert, Wand2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
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
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { formatDateShort } from "@/lib/date-iso"
import { notifyError, notifySuccess } from "@/lib/toast"
import {
  applicaClasse,
  copiaPermessi,
  fetchElencoPermessi,
  fetchSchedaPermessi,
  impostaPermesso,
  riallineaAllaClasse,
} from "@/lib/api/permessi"
import type { EsitoApplicaClasseDto, StatoCombo } from "@/lib/api/types"
import { AnteprimaVideo, MatrioskaEditor } from "./MatrioskaPermessi"
import { etichettaStato } from "./stato-permesso"

const SCHEDA_KEY = (id: number) => ["permessi", "scheda", id] as const
const ELENCO_KEY = ["permessi", "elenco"] as const

/**
 * Scheda permessi di una persona — la MATRIOSKA (PIANO-PERMESSI-REBUILD.md §5, passo 5).
 *
 * A sinistra «cosa vedrebbe a video» (menu + albero commessa, ricalcolato a ogni modifica);
 * a destra l'editor, che RENDE l'albero del catalogo unico: sezioni con toggle padre e pill
 * spenta/parziale/tutta, voci con micro «sola lettura» e «vede prezzi», azioni annidate sotto
 * la voce che le ospita. Ogni gesto scrive una riga sulla persona (anche il diniego `NO`,
 * §3.7) marcata «a mano»: «Applica template» la rispetta.
 *
 * Due cose restano dalla pagina di Fase B, perché non erano decorazione:
 * 1. **«Applica template» passa dall'anteprima**: si conferma l'elenco dei cambi, non il bottone.
 * 2. **Lo storico in fondo**: dopo il primo incidente la domanda è «chi ha tolto cosa a chi».
 */
export function SchedaPersonaPage() {
  const { employeeId } = useParams()
  const id = Number(employeeId)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const [anteprima, setAnteprima] = React.useState<EsitoApplicaClasseDto | null>(null)
  const [copiaAperta, setCopiaAperta] = React.useState(false)
  const [copiaDa, setCopiaDa] = React.useState<string>("")
  const [copiaAnteprima, setCopiaAnteprima] = React.useState<EsitoApplicaClasseDto | null>(null)

  const schedaQuery = useQuery({
    queryKey: SCHEDA_KEY(id),
    queryFn: () => fetchSchedaPermessi(id),
    enabled: Number.isFinite(id) && id > 0,
  })

  const elencoQuery = useQuery({
    queryKey: ELENCO_KEY,
    queryFn: fetchElencoPermessi,
    enabled: copiaAperta,
  })

  const scheda = schedaQuery.data

  function ricarica() {
    void queryClient.invalidateQueries({ queryKey: SCHEDA_KEY(id) })
    void queryClient.invalidateQueries({ queryKey: ELENCO_KEY })
  }

  const impostaMutation = useMutation({
    mutationFn: impostaPermesso,
    onSuccess: () => {
      ricarica()
      notifySuccess("Permesso aggiornato")
    },
    onError: (e) => notifyError(e, "Permesso non modificato"),
  })

  /** Toggle di sezione: tante chiavi in un gesto solo, un avviso solo alla fine. */
  const impostaTanteMutation = useMutation({
    mutationFn: async ({ featureKeys, stato }: { featureKeys: string[]; stato: StatoCombo }) => {
      for (const featureKey of featureKeys) {
        await impostaPermesso({ employeeId: id, featureKey, stato })
      }
      return featureKeys.length
    },
    onSuccess: (quante) => {
      ricarica()
      notifySuccess(`${quante} ${quante === 1 ? "voce aggiornata" : "voci aggiornate"}`)
    },
    onError: (e) => {
      ricarica() // a metà strada: la scheda deve mostrare la verità, non l'intenzione
      notifyError(e, "Sezione non aggiornata del tutto")
    },
  })

  const applicaMutation = useMutation({
    mutationFn: applicaClasse,
    onError: (e) => notifyError(e, "Template non applicato"),
  })

  const riallineaMutation = useMutation({
    mutationFn: riallineaAllaClasse,
    onSuccess: (esito) => {
      ricarica()
      notifySuccess(
        esito.combo === 0
          ? "Era già come il template"
          : `${esito.combo} ${esito.combo === 1 ? "funzione riportata" : "funzioni riportate"} al template`
      )
    },
    onError: (e) => notifyError(e, "Ritorno al template non riuscito"),
  })

  const copiaMutation = useMutation({
    mutationFn: copiaPermessi,
    onError: (e) => notifyError(e, "Copia non riuscita"),
  })

  function chiudiCopia() {
    setCopiaAperta(false)
    setCopiaDa("")
    setCopiaAnteprima(null)
  }

  /** L'anteprima è obbligatoria (§3.6): si conferma l'elenco dei cambi, non il pulsante. */
  async function chiediAnteprimaCopia() {
    try {
      const esito = await copiaMutation.mutateAsync({
        daEmployeeId: Number(copiaDa),
        aEmployeeId: id,
        anteprima: true,
      })
      setCopiaAnteprima(esito)
    } catch {
      /* già segnalato */
    }
  }

  async function confermaCopia() {
    try {
      const esito = await copiaMutation.mutateAsync({
        daEmployeeId: Number(copiaDa),
        aEmployeeId: id,
        anteprima: false,
      })
      ricarica()
      chiudiCopia()
      notifySuccess(
        esito.combo === 0
          ? "Era già identica alla scheda del collega"
          : `Scheda copiata: ${esito.combo} ${esito.combo === 1 ? "voce cambiata" : "voci cambiate"}`
      )
    } catch {
      /* già segnalato */
    }
  }

  /** Chiede l'anteprima e la mostra: si conferma quella, non «Applica». */
  async function chiediAnteprima() {
    try {
      const esito = await applicaMutation.mutateAsync({
        employeeIds: [id],
        anteprima: true,
      })
      setAnteprima(esito)
    } catch {
      /* l'errore lo ha già mostrato onError */
    }
  }

  async function confermaApplica() {
    try {
      const esito = await applicaMutation.mutateAsync({
        employeeIds: [id],
        anteprima: false,
      })
      setAnteprima(null)
      ricarica()
      notifySuccess(
        esito.combo === 0
          ? "Nessuna modifica: era già come il template"
          : `${esito.combo} ${esito.combo === 1 ? "voce aggiornata" : "voci aggiornate"}`
      )
    } catch {
      /* già segnalato */
    }
  }

  /**
   * Toglie la riga `*`. Se era l'ultima strada verso l'amministrazione dei permessi il server
   * rifiuta con un 409 leggibile, e la conferma qui sopra non serve a evitarlo: serve a non
   * farlo per sbaglio.
   */
  async function togliJolly() {
    const ok = await confirm({
      title: "Togliere il jolly a questa persona?",
      description:
        "Smetterà di vedere tutto e resteranno solo le funzioni che ha riga per riga. Le funzioni aggiunte da un deploy futuro non le vedrà più in automatico.",
      confirmLabel: "Togli il jolly",
    })
    if (ok) impostaMutation.mutate({ employeeId: id, featureKey: "*", stato: "NO" })
  }

  async function tornaAlTemplate() {
    const ok = await confirm({
      title: "Riportare tutta la scheda al template?",
      description:
        "Le eccezioni decise a mano su questa persona vengono annullate e la scheda torna al template della sua gerarchia. Le eccezioni messe apposta vanno perse.",
      confirmLabel: "Torna al template",
    })
    if (ok) riallineaMutation.mutate({ employeeId: id })
  }

  if (schedaQuery.isLoading) {
    return <div className="p-6 text-sm text-muted-foreground">Caricamento…</div>
  }

  if (schedaQuery.isError || !scheda) {
    return (
      <div className="p-6">
        <Alert variant="destructive">
          <TriangleAlert />
          <AlertTitle>Scheda non disponibile</AlertTitle>
          <AlertDescription>
            Non è stato possibile leggere i permessi di questa persona.
          </AlertDescription>
        </Alert>
      </div>
    )
  }

  const eccezioniAMano = scheda.funzioni.filter((f) => f.origin === "MANO").length
  const pending =
    impostaMutation.isPending || impostaTanteMutation.isPending || riallineaMutation.isPending

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <Button variant="ghost" size="sm" onClick={() => navigate("/permessi")}>
                  <ArrowLeft />
                  Permessi
                </Button>
              </div>
              <CardTitle>{scheda.nome}</CardTitle>
              <CardDescription className="flex flex-wrap items-center gap-2">
                {/* Chi è: gerarchia + reparto. L'etichetta indica il template di partenza,
                    i permessi veri stanno nelle righe qui sotto (§3.4). */}
                <Badge variant="outline">{scheda.classeDisplay || "senza gerarchia"}</Badge>
                {scheda.reparti.map((r) => (
                  <Badge key={r} variant="secondary">
                    {r}
                  </Badge>
                ))}
                {scheda.username ? <span>· {scheda.username}</span> : null}
                {scheda.status !== "ACTIVE" ? (
                  <Badge variant="destructive">{scheda.status}</Badge>
                ) : null}
              </CardDescription>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" size="sm" onClick={chiediAnteprima}>
                <Wand2 />
                Applica template
              </Button>
              <Button variant="outline" size="sm" onClick={() => setCopiaAperta(true)}>
                <Copy />
                Copia scheda da…
              </Button>
              <Button variant="outline" size="sm" onClick={tornaAlTemplate}>
                <RotateCcw />
                Torna al template
              </Button>
            </div>
          </div>
        </CardHeader>

        <CardContent className="space-y-4">
          {scheda.jolly ? (
            <Alert>
              <TriangleAlert />
              <AlertTitle>Vede tutto (riga jolly)</AlertTitle>
              <AlertDescription className="space-y-2">
                <p>
                  Questa persona ha la riga <code>*</code>: le vale qualunque funzione, comprese
                  quelle che verranno aggiunte in futuro. È ciò che tiene amministrabile il
                  gestionale dopo ogni deploy — con il fallback invertito una funzione nuova
                  nasce invisibile a chiunque altro, jolly a parte.
                </p>
                <p>
                  La scheda qui sotto resta usabile: spegnendo una voce si scrive un{" "}
                  <strong>diniego</strong> su quella funzione, e la decisione sulla singola voce
                  vince sul jolly. Per toglierle tutto in un colpo, invece, si toglie il jolly.
                </p>
                <Button variant="outline" size="sm" onClick={togliJolly}>
                  Togli il jolly
                </Button>
              </AlertDescription>
            </Alert>
          ) : null}

          {/* Le eccezioni a mano sono le decisioni prese su QUESTA persona: «Applica template»
              le rispetta, e questo numero dice quante sono (§5.9). */}
          <div className="rounded-lg border px-4 py-3 text-sm">
            {eccezioniAMano === 0 ? (
              <span className="text-muted-foreground">
                Nessuna eccezione a mano: la scheda segue il template «{scheda.classeDisplay}».
              </span>
            ) : (
              <span>
                <strong>{eccezioniAMano}</strong>{" "}
                {eccezioniAMano === 1 ? "eccezione decisa a mano" : "eccezioni decise a mano"}{" "}
                (righe col badge «a mano»): «Applica template» non le tocca.
              </span>
            )}
          </div>

          {/* §5.2: sinistra l'anteprima, destra l'editor matrioska. */}
          <div className="grid gap-4 lg:grid-cols-[300px_1fr]">
            <div className="rounded-lg border bg-muted/30 p-3 lg:sticky lg:top-2 lg:self-start">
              <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                Cosa vedrebbe a video
              </div>
              <AnteprimaVideo funzioni={scheda.funzioni} />
            </div>
            <MatrioskaEditor
              funzioni={scheda.funzioni}
              pending={pending}
              onImposta={(featureKey, stato) =>
                impostaMutation.mutate({ employeeId: id, featureKey, stato })
              }
              onImpostaTante={(featureKeys, stato) =>
                impostaTanteMutation.mutate({ featureKeys, stato })
              }
              onRiallinea={(featureKey) => riallineaMutation.mutate({ employeeId: id, featureKey })}
            />
          </div>
        </CardContent>
      </Card>

      {/* Storico: la risposta alla prima domanda dopo il primo incidente. */}
      <Card>
        <CardHeader>
          <CardTitle>Ultime modifiche</CardTitle>
          <CardDescription>
            Chi ha cambiato cosa sui permessi di questa persona, e quando.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {scheda.storico.length === 0 ? (
            <div className="text-sm text-muted-foreground">Nessuna modifica registrata.</div>
          ) : (
            <GridScroller>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-[120px]">Quando</TableHead>
                    <TableHead>Funzione</TableHead>
                    <TableHead className="w-[180px]">Da → a</TableHead>
                    <TableHead className="w-[100px]">Origine</TableHead>
                    <TableHead className="w-[180px]">Chi</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {scheda.storico.map((r) => (
                    <TableRow key={r.id}>
                      <TableCell className="text-sm">{formatDateShort(r.changedAt)}</TableCell>
                      <TableCell className="text-sm">{r.displayName}</TableCell>
                      <TableCell className="text-sm">
                        {r.accessBefore ?? "non abilitato"} → {r.accessAfter ?? "non abilitato"}
                      </TableCell>
                      <TableCell>
                        <Badge variant={r.origin === "MANO" ? "secondary" : "outline"}>
                          {r.origin === "MANO" ? "a mano" : "template"}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-sm">{r.changedBy}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </GridScroller>
          )}
        </CardContent>
      </Card>

      {/* Anteprima di «Applica template»: si conferma questo elenco, non il pulsante (§3.5). */}
      <Dialog open={anteprima != null} onOpenChange={(o) => !o && setAnteprima(null)}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle>Applica il template «{scheda.classeDisplay}»</DialogTitle>
          </DialogHeader>
          {anteprima ? (
            <div className="space-y-3">
              <div className="text-sm">
                {anteprima.combo === 0 ? (
                  <span>Non cambierebbe niente: questa scheda è già come il template.</span>
                ) : (
                  <span>
                    <strong>{anteprima.combo}</strong>{" "}
                    {anteprima.combo === 1 ? "voce cambierebbe" : "voci cambierebbero"}.
                  </span>
                )}
                {anteprima.rispettateAMano > 0 ? (
                  <span className="text-muted-foreground">
                    {" "}
                    {anteprima.rispettateAMano} eccezioni a mano restano dove sono.
                  </span>
                ) : null}
              </div>
              {anteprima.cambi.length > 0 ? (
                <GridScroller>
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Funzione</TableHead>
                        <TableHead className="w-[200px]">Da → a</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {anteprima.cambi.map((cambio) => (
                        <TableRow key={`${cambio.employeeId}-${cambio.featureKey}`}>
                          <TableCell className="text-sm">{cambio.displayName}</TableCell>
                          <TableCell className="text-sm">
                            {etichettaStato(cambio.da)} → {etichettaStato(cambio.a)}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </GridScroller>
              ) : null}
            </div>
          ) : null}
          <DialogFooter>
            <Button variant="outline" onClick={() => setAnteprima(null)}>
              Annulla
            </Button>
            <Button
              onClick={confermaApplica}
              disabled={applicaMutation.isPending || anteprima?.combo === 0}
            >
              Applica queste modifiche
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* «Copia scheda da»: un CLONE, origin compresi (§3.6) — le righe da template restano
          template, le eccezioni restano eccezioni. Anteprima obbligatoria. */}
      <Dialog open={copiaAperta} onOpenChange={(o) => !o && chiudiCopia()}>
        <DialogContent className={copiaAnteprima ? "max-w-2xl" : undefined}>
          <DialogHeader>
            <DialogTitle>Copia la scheda di un collega</DialogTitle>
          </DialogHeader>
          {copiaAnteprima == null ? (
            <div className="space-y-3">
              <p className="text-sm text-muted-foreground">
                La scheda diventa un clone della sua: stesse voci, stesse eccezioni. Quello che
                viene dal suo template resta «da template», così i futuri «Applica template»
                continuano a funzionare anche sul clone.
              </p>
              <Select value={copiaDa} onValueChange={setCopiaDa}>
                <SelectTrigger>
                  <SelectValue placeholder="Scegli il collega…" />
                </SelectTrigger>
                <SelectContent>
                  {(elencoQuery.data ?? [])
                    // Si copia da un COLLEGA. Fuori: sé stessi, le utenze segnaposto di reparto
                    // (`[ACQ] Generico`… non sono persone) e chi ha il jolly — copiare da lui
                    // vorrebbe dire copiargli il «vede tutto», cioè dare tutto senza accorgersene.
                    .filter((r) => r.employeeId !== id && !r.segnaposto && !r.jolly)
                    .map((r) => (
                      <SelectItem key={r.employeeId} value={String(r.employeeId)}>
                        {r.nome} · {r.classeDisplay}
                      </SelectItem>
                    ))}
                </SelectContent>
              </Select>
            </div>
          ) : (
            <div className="space-y-3">
              <div className="text-sm">
                {copiaAnteprima.combo === 0 ? (
                  <span>Le due schede sono già identiche: non cambierebbe niente.</span>
                ) : (
                  <span>
                    <strong>{copiaAnteprima.combo}</strong>{" "}
                    {copiaAnteprima.combo === 1 ? "voce cambierebbe" : "voci cambierebbero"} su
                    questa scheda.
                  </span>
                )}
              </div>
              {copiaAnteprima.cambi.length > 0 ? (
                <GridScroller>
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Funzione</TableHead>
                        <TableHead className="w-[200px]">Da → a</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {copiaAnteprima.cambi.map((cambio) => (
                        <TableRow key={cambio.featureKey}>
                          <TableCell className="text-sm">{cambio.displayName}</TableCell>
                          <TableCell className="text-sm">
                            {etichettaStato(cambio.da)} → {etichettaStato(cambio.a)}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </GridScroller>
              ) : null}
            </div>
          )}
          <DialogFooter>
            <Button variant="outline" onClick={chiudiCopia}>
              Annulla
            </Button>
            {copiaAnteprima == null ? (
              <Button disabled={!copiaDa || copiaMutation.isPending} onClick={chiediAnteprimaCopia}>
                Vedi l'anteprima
              </Button>
            ) : (
              <Button
                disabled={copiaMutation.isPending || copiaAnteprima.combo === 0}
                onClick={confermaCopia}
              >
                Copia queste modifiche
              </Button>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
