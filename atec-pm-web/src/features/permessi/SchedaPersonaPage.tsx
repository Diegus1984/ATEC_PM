import * as React from "react"
import { useNavigate, useParams } from "react-router-dom"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowLeft, ChevronDown, Copy, RotateCcw, TriangleAlert, Wand2 } from "lucide-react"

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
import { Collapsible } from "@/components/ui/collapsible"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
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
import type {
  AreaPermessoDto,
  EsitoApplicaClasseDto,
  FunzionePermessoDto,
  StatoCombo,
} from "@/lib/api/types"

const SCHEDA_KEY = (id: number) => ["permessi", "scheda", id] as const
const ELENCO_KEY = ["permessi", "elenco"] as const

/** Etichette delle categorie del catalogo, per raggruppare le funzioni avanzate. */
const CATEGORIE: Record<string, string> = {
  navigation: "Pagine e voci di menu",
  project: "Sezioni di commessa",
  action: "Operazioni",
  data: "Dati riservati",
}

function etichettaStato(stato: StatoCombo, area?: AreaPermessoDto): string {
  if (stato === "NO") return "non abilitato"
  if (area?.dueStati) return area.etichettaAcceso
  return stato === "READ" ? "sola lettura" : "lettura e scrittura"
}

/**
 * Scheda permessi di una persona — PIANO-PERMESSI.md §5, Fase B.
 *
 * È l'unica schermata da cui i permessi si scrivono davvero. Fino alla Fase A la pagina
 * «Permessi» modificava la matrice funzioni × ruoli, che il motore nuovo non legge più: da lì in
 * poi, e fino a questa pagina, l'unico modo di cambiare i permessi di qualcuno era il database.
 *
 * Tre cose non sono decorazione e non vanno tolte:
 * 1. **«diverso dalla classe»** in testa, col filtro: è la differenza fra una persona
 *    *configurata* e una *andata alla deriva*, e senza quel numero non si scopre mai.
 * 2. **«Applica classe» passa dall'anteprima**: si conferma l'elenco dei cambi, non il pulsante.
 * 3. **Lo storico in fondo**: dopo il primo incidente la domanda è «chi ha tolto cosa a chi», e
 *    senza registro non ha risposta.
 */
export function SchedaPersonaPage() {
  const { employeeId } = useParams()
  const id = Number(employeeId)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const [soloDiverse, setSoloDiverse] = React.useState(false)
  const [avanzateAperte, setAvanzateAperte] = React.useState(false)
  const [anteprima, setAnteprima] = React.useState<EsitoApplicaClasseDto | null>(null)
  const [copiaAperta, setCopiaAperta] = React.useState(false)
  const [copiaDa, setCopiaDa] = React.useState<string>("")

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

  const applicaMutation = useMutation({
    mutationFn: applicaClasse,
    onError: (e) => notifyError(e, "Classe non applicata"),
  })

  const riallineaMutation = useMutation({
    mutationFn: riallineaAllaClasse,
    onSuccess: (esito) => {
      ricarica()
      notifySuccess(
        esito.combo === 0
          ? "Era già allineata alla classe"
          : `${esito.combo} ${esito.combo === 1 ? "funzione riportata" : "funzioni riportate"} al valore della classe`
      )
    },
    onError: (e) => notifyError(e, "Riallineamento non riuscito"),
  })

  const copiaMutation = useMutation({
    mutationFn: copiaPermessi,
    onSuccess: () => {
      ricarica()
      setCopiaAperta(false)
      setCopiaDa("")
      notifySuccess("Permessi copiati dal collega")
    },
    onError: (e) => notifyError(e, "Copia non riuscita"),
  })

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
          ? "Nessuna modifica: era già come la sua classe"
          : `${esito.combo} ${esito.combo === 1 ? "combo aggiornata" : "combo aggiornate"}`
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

  async function riallineaTutto() {
    const ok = await confirm({
      title: "Riportare tutto al valore della classe?",
      description:
        "Le combo decise a mano su questa persona tornano a quelle della sua classe. Le eccezioni messe apposta vanno perse.",
      confirmLabel: "Riallinea",
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

  const areeDaMostrare = soloDiverse
    ? scheda.aree.filter((a) => a.stato !== a.statoClasse)
    : scheda.aree

  // Le funzioni già governate da una delle 9 aree non si ripetono fra le avanzate: sarebbero
  // due comandi per la stessa cosa, e il secondo che si tocca smentisce il primo.
  const avanzate = scheda.funzioni.filter(
    (f) => !f.areaId && (!soloDiverse || f.stato !== f.statoClasse)
  )
  const perCategoria = avanzate.reduce<Record<string, FunzionePermessoDto[]>>(
    (acc, f) => {
      ;(acc[f.categoria] ??= []).push(f)
      return acc
    },
    {}
  )

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => navigate("/permessi")}
                >
                  <ArrowLeft />
                  Permessi
                </Button>
              </div>
              <CardTitle>{scheda.nome}</CardTitle>
              <CardDescription className="flex flex-wrap items-center gap-2">
                <Badge variant="outline">{scheda.classeDisplay || "senza classe"}</Badge>
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
                Applica classe
              </Button>
              <Button variant="outline" size="sm" onClick={() => setCopiaAperta(true)}>
                <Copy />
                Copia da…
              </Button>
              <Button variant="outline" size="sm" onClick={riallineaTutto}>
                <RotateCcw />
                Riallinea alla classe
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
                  Le combo qui sotto restano usabili: mettendone una su «non abilitato» si scrive
                  un <strong>diniego</strong> su quella funzione, e la decisione sulla singola
                  voce vince sul jolly. Per toglierle tutto in un colpo, invece, si toglie il
                  jolly.
                </p>
                <Button variant="outline" size="sm" onClick={togliJolly}>
                  Togli il jolly
                </Button>
              </AlertDescription>
            </Alert>
          ) : null}

          {/* «diverso dalla classe»: la differenza fra configurato e andato alla deriva. */}
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border px-4 py-3">
            <div className="text-sm">
              {scheda.diverseDallaClasse === 0 ? (
                <span className="text-muted-foreground">
                  Tutto come la classe «{scheda.classeDisplay}».
                </span>
              ) : (
                <span>
                  <strong>{scheda.diverseDallaClasse}</strong>{" "}
                  {scheda.diverseDallaClasse === 1 ? "funzione diversa" : "funzioni diverse"} da
                  quello che direbbe la classe «{scheda.classeDisplay}».
                </span>
              )}
            </div>
            <div className="flex items-center gap-2">
              <Switch
                id="solo-diverse"
                checked={soloDiverse}
                onCheckedChange={setSoloDiverse}
              />
              <Label htmlFor="solo-diverse" className="text-sm font-normal">
                Mostra solo quelle diverse
              </Label>
            </div>
          </div>

          {/* Le 9 aree della maschera. */}
          <GridScroller>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[240px]">Area</TableHead>
                  <TableHead className="w-[220px]">Permesso</TableHead>
                  <TableHead>Classe</TableHead>
                  <TableHead className="w-[120px]" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {areeDaMostrare.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={4} className="text-sm text-muted-foreground">
                      Nessuna area diversa dalla classe.
                    </TableCell>
                  </TableRow>
                ) : null}
                {areeDaMostrare.map((area) => (
                  <TableRow key={area.id}>
                    <TableCell>
                      <div className="font-medium">{area.titolo}</div>
                      {area.incoerente ? (
                        <div className="text-xs text-destructive">
                          Le chiavi di quest'area non sono allo stesso livello: qui si vede il
                          più basso.
                        </div>
                      ) : null}
                    </TableCell>
                    <TableCell>
                      <ComboStato
                        area={area}
                        valore={area.stato}
                        onChange={(stato) =>
                          impostaMutation.mutate({
                            employeeId: id,
                            areaId: area.id,
                            stato,
                          })
                        }
                      />
                    </TableCell>
                    <TableCell className="text-sm">
                      {area.stato === area.statoClasse ? (
                        <span className="text-muted-foreground">come la classe</span>
                      ) : (
                        <span className="flex flex-wrap items-center gap-2">
                          <Badge variant="outline">
                            classe: {etichettaStato(area.statoClasse, area)}
                          </Badge>
                          {area.aMano ? <Badge variant="secondary">a mano</Badge> : null}
                        </span>
                      )}
                    </TableCell>
                    <TableCell>
                      {area.stato !== area.statoClasse ? (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() =>
                            riallineaMutation.mutate({
                              employeeId: id,
                              featureKey: area.chiavi[0],
                            })
                          }
                        >
                          Riallinea
                        </Button>
                      ) : null}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </GridScroller>
        </CardContent>
      </Card>

      {/* Funzioni avanzate: chiuse di default. Il catalogo è molto più largo delle 9 aree, ma
          deve esserci tutto — «non vedo la pagina X» si risponde da qui. */}
      <Card>
        <CardHeader>
          <button
            type="button"
            className="flex w-full items-center justify-between text-left"
            onClick={() => setAvanzateAperte((v) => !v)}
          >
            <div>
              <CardTitle>Funzioni avanzate</CardTitle>
              <CardDescription>
                Tutto il resto del catalogo ({avanzate.length}) — backup, utenti, Codex, Danea,
                operazioni riservate.
              </CardDescription>
            </div>
            <ChevronDown
              className={`size-4 shrink-0 transition-transform duration-(--accordion-duration) ease-(--accordion-ease) ${
                avanzateAperte ? "rotate-180" : ""
              }`}
            />
          </button>
        </CardHeader>
        <Collapsible open={avanzateAperte}>
          <CardContent className="space-y-6">
              {Object.entries(perCategoria).map(([categoria, funzioni]) => (
                <div key={categoria} className="space-y-2">
                  <div className="text-sm font-medium text-muted-foreground">
                    {CATEGORIE[categoria] ?? categoria}
                  </div>
                  <GridScroller>
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead className="w-[280px]">Funzione</TableHead>
                          <TableHead className="w-[200px]">Permesso</TableHead>
                          <TableHead>Classe</TableHead>
                          <TableHead className="w-[120px]" />
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {funzioni.map((f) => (
                          <TableRow key={f.featureKey}>
                            <TableCell>
                              <div className="font-medium">{f.displayName}</div>
                              <div className="font-mono text-xs text-muted-foreground">
                                {f.featureKey}
                              </div>
                            </TableCell>
                            <TableCell>
                              <ComboStato
                                valore={f.stato}
                                        onChange={(stato) =>
                                  impostaMutation.mutate({
                                    employeeId: id,
                                    featureKey: f.featureKey,
                                    stato,
                                  })
                                }
                              />
                            </TableCell>
                            <TableCell className="text-sm">
                              {f.stato === f.statoClasse ? (
                                <span className="text-muted-foreground">come la classe</span>
                              ) : (
                                <span className="flex flex-wrap items-center gap-2">
                                  <Badge variant="outline">
                                    classe: {etichettaStato(f.statoClasse)}
                                  </Badge>
                                  {f.origin === "MANO" ? (
                                    <Badge variant="secondary">a mano</Badge>
                                  ) : null}
                                </span>
                              )}
                            </TableCell>
                            <TableCell>
                              {f.stato !== f.statoClasse ? (
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  onClick={() =>
                                    riallineaMutation.mutate({
                                      employeeId: id,
                                      featureKey: f.featureKey,
                                    })
                                  }
                                >
                                  Riallinea
                                </Button>
                              ) : null}
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </GridScroller>
                </div>
              ))}
          </CardContent>
        </Collapsible>
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
            <div className="text-sm text-muted-foreground">
              Nessuna modifica registrata.
            </div>
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
                      <TableCell className="text-sm">
                        {formatDateShort(r.changedAt)}
                      </TableCell>
                      <TableCell className="text-sm">{r.displayName}</TableCell>
                      <TableCell className="text-sm">
                        {r.accessBefore ?? "non abilitato"} → {r.accessAfter ?? "non abilitato"}
                      </TableCell>
                      <TableCell>
                        <Badge variant={r.origin === "MANO" ? "secondary" : "outline"}>
                          {r.origin === "MANO" ? "a mano" : "classe"}
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

      {/* Anteprima di «Applica classe»: si conferma questo elenco, non il pulsante (§4.4). */}
      <Dialog open={anteprima != null} onOpenChange={(o) => !o && setAnteprima(null)}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle>Applica la classe «{scheda.classeDisplay}»</DialogTitle>
          </DialogHeader>
          {anteprima ? (
            <div className="space-y-3">
              <div className="text-sm">
                {anteprima.combo === 0 ? (
                  <span>
                    Non cambierebbe niente: questa persona è già come la sua classe.
                  </span>
                ) : (
                  <span>
                    <strong>{anteprima.combo}</strong>{" "}
                    {anteprima.combo === 1 ? "funzione cambierebbe" : "funzioni cambierebbero"}.
                  </span>
                )}
                {anteprima.rispettateAMano > 0 ? (
                  <span className="text-muted-foreground">
                    {" "}
                    {anteprima.rispettateAMano} decise a mano restano dove sono.
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

      {/* «Copia da»: copia tutto e marca tutto MANO — «voglio esattamente le sue», non «la sua classe». */}
      <Dialog open={copiaAperta} onOpenChange={setCopiaAperta}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Copia i permessi di un collega</DialogTitle>
          </DialogHeader>
          <div className="space-y-3">
            <p className="text-sm text-muted-foreground">
              Arriva esattamente quello che ha lui, e resta marcato «a mano»: applicare una
              classe in futuro non lo sovrascriverà.
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
          <DialogFooter>
            <Button variant="outline" onClick={() => setCopiaAperta(false)}>
              Annulla
            </Button>
            <Button
              disabled={!copiaDa || copiaMutation.isPending}
              onClick={() =>
                copiaMutation.mutate({
                  daEmployeeId: Number(copiaDa),
                  aEmployeeId: id,
                })
              }
            >
              Copia
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}

/** La combo a due o tre stati. Un'area con `dueStati` non ha la via di mezzo. */
function ComboStato({
  valore,
  area,
  onChange,
}: {
  valore: StatoCombo
  area?: AreaPermessoDto
  onChange: (stato: StatoCombo) => void
}) {
  const stati: StatoCombo[] = area?.dueStati ? ["NO", "FULL"] : ["NO", "READ", "FULL"]
  return (
    <Select value={valore} onValueChange={(v) => onChange(v as StatoCombo)}>
      <SelectTrigger className="w-full">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {stati.map((s) => (
          <SelectItem key={s} value={s}>
            {etichettaStato(s, area)}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
