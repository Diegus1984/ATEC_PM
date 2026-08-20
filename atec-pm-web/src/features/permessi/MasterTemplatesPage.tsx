import * as React from "react"
import { useNavigate } from "react-router-dom"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ArrowLeft, TriangleAlert, Users, Wand2 } from "lucide-react"

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
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { notifyError, notifySuccess } from "@/lib/toast"
import {
  applicaClasse,
  fetchClassi,
  fetchElencoPermessi,
  fetchPacchetto,
  impostaPacchetto,
} from "@/lib/api/permessi"
import type {
  EsitoApplicaClasseDto,
  FunzionePermessoDto,
  StatoCombo,
} from "@/lib/api/types"
import { MatrioskaEditor } from "./MatrioskaPermessi"
import { etichettaStato } from "./stato-permesso"

const CLASSI_KEY = ["permessi", "classi"] as const
const PACCHETTO_KEY = (classe: string) => ["permessi", "pacchetto", classe] as const

/**
 * Master / Template (PIANO-PERMESSI-REBUILD.md §3.5 e §5.4, passo 6): una scheda matrioska
 * per ogni profilo — TECH, RESP_REPARTO, PM, ADMIN — IDENTICA come UX alla scheda persona,
 * senza la colonna «cosa vedrebbe a video» di una persona reale.
 *
 * Il patto che regge la pagina: **salvare il master aggiorna SOLO il template** — nessun
 * utente cambia finché non si preme «Applica a…», che passa dall'anteprima e rispetta le
 * eccezioni a mano. Prima di questa pagina un ritocco al pacchetto era una MIGRAZIONE
 * (la #77 è diventata la M089, codice deployato).
 *
 * «Spenta» nel master = la voce ESCE dal pacchetto (§3.7): il diniego `NO` è delle persone,
 * non dei template.
 */
export function MasterTemplatesPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const classiQuery = useQuery({ queryKey: CLASSI_KEY, queryFn: fetchClassi })
  const [classe, setClasse] = React.useState<string>("")

  // Il primo profilo configurabile (senza jolly) come default, appena l'elenco arriva.
  React.useEffect(() => {
    if (!classe && classiQuery.data?.length) {
      setClasse((classiQuery.data.find((c) => !c.jolly) ?? classiQuery.data[0]).classe)
    }
  }, [classe, classiQuery.data])

  const profilo = classiQuery.data?.find((c) => c.classe === classe)

  const pacchettoQuery = useQuery({
    queryKey: PACCHETTO_KEY(classe),
    queryFn: () => fetchPacchetto(classe),
    enabled: classe !== "" && profilo != null && !profilo.jolly,
  })

  // La matrioska parla FunzionePermessoDto: il pacchetto si traveste — origin vuoto, quindi
  // niente badge «a mano» né «torna al template», che su un template non significano nulla.
  const funzioni: FunzionePermessoDto[] = React.useMemo(
    () =>
      (pacchettoQuery.data ?? []).map((r) => ({
        featureKey: r.featureKey,
        displayName: r.featureKey,
        categoria: "",
        stato: r.access,
        statoClasse: r.access,
        origin: "" as const,
        areaId: null,
      })),
    [pacchettoQuery.data]
  )

  function ricaricaPacchetto() {
    void queryClient.invalidateQueries({ queryKey: PACCHETTO_KEY(classe) })
    void queryClient.invalidateQueries({ queryKey: CLASSI_KEY })
  }

  const impostaMutation = useMutation({
    mutationFn: (req: { featureKey: string; stato: StatoCombo }) =>
      impostaPacchetto({ classe, ...req }),
    onSuccess: () => {
      ricaricaPacchetto()
      notifySuccess("Template aggiornato: nessun utente cambia finché non lo applichi")
    },
    onError: (e) => notifyError(e, "Template non aggiornato"),
  })

  const impostaTanteMutation = useMutation({
    mutationFn: async ({ featureKeys, stato }: { featureKeys: string[]; stato: StatoCombo }) => {
      for (const featureKey of featureKeys) {
        await impostaPacchetto({ classe, featureKey, stato })
      }
      return featureKeys.length
    },
    onSuccess: (quante) => {
      ricaricaPacchetto()
      notifySuccess(`${quante} ${quante === 1 ? "voce aggiornata" : "voci aggiornate"} nel template`)
    },
    onError: (e) => {
      ricaricaPacchetto()
      notifyError(e, "Template non aggiornato del tutto")
    },
  })

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
              <CardTitle>Master / Template</CardTitle>
              <CardDescription>
                Il profilo di partenza di ogni gerarchia. Salvare il template{" "}
                <strong>non cambia nessuno</strong>: i permessi si muovono solo con
                «Applica a…», che passa dall'anteprima e rispetta le eccezioni a mano.
              </CardDescription>
            </div>
            {profilo && !profilo.jolly ? <ApplicaA classe={profilo.classe} display={profilo.display} /> : null}
          </div>
        </CardHeader>

        <CardContent className="space-y-4">
          <Tabs value={classe} onValueChange={setClasse}>
            <TabsList>
              {(classiQuery.data ?? []).map((c) => (
                <TabsTrigger key={c.classe} value={c.classe}>
                  {c.display}
                  <Badge variant="outline" className="ml-1">
                    {c.jolly ? "jolly" : c.voci}
                  </Badge>
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>

          {profilo?.jolly ? (
            <Alert>
              <TriangleAlert />
              <AlertTitle>Questo template è il jolly</AlertTitle>
              <AlertDescription>
                Il pacchetto «{profilo.display}» è la riga <code>*</code>: chi lo riceve vede
                tutto, comprese le funzioni che verranno aggiunte in futuro. Non si configura
                voce per voce — è ciò che tiene amministrabile il gestionale dopo ogni deploy.
              </AlertDescription>
            </Alert>
          ) : pacchettoQuery.isLoading ? (
            <div className="text-sm text-muted-foreground">Caricamento…</div>
          ) : profilo ? (
            <MatrioskaEditor
              funzioni={funzioni}
              pending={impostaMutation.isPending || impostaTanteMutation.isPending}
              onImposta={(featureKey, stato) => impostaMutation.mutate({ featureKey, stato })}
              onImpostaTante={(featureKeys, stato) =>
                impostaTanteMutation.mutate({ featureKeys, stato })
              }
              onRiallinea={() => {}}
            />
          ) : null}
        </CardContent>
      </Card>
    </div>
  )
}

/**
 * «Applica a…» (§5.4): selezione persone → anteprima → conferma. Il template scelto qui è
 * ESPLICITO: si può dare il template PM a un tecnico — l'anteprima dice esattamente cosa
 * gli cambierebbe, e le sue eccezioni a mano restano dove sono.
 */
function ApplicaA({ classe, display }: { classe: string; display: string }) {
  const [aperto, setAperto] = React.useState(false)
  const [scelti, setScelti] = React.useState<Set<number>>(new Set())
  const [anteprima, setAnteprima] = React.useState<EsitoApplicaClasseDto | null>(null)

  const elencoQuery = useQuery({
    queryKey: ["permessi", "elenco"],
    queryFn: fetchElencoPermessi,
    enabled: aperto,
  })

  const applicaMutation = useMutation({
    mutationFn: applicaClasse,
    onError: (e) => notifyError(e, "Template non applicato"),
  })

  function chiudi() {
    setAperto(false)
    setScelti(new Set())
    setAnteprima(null)
  }

  async function chiediAnteprima() {
    try {
      const esito = await applicaMutation.mutateAsync({
        employeeIds: [...scelti],
        anteprima: true,
        classe,
      })
      setAnteprima(esito)
    } catch {
      /* già segnalato */
    }
  }

  async function conferma() {
    try {
      const esito = await applicaMutation.mutateAsync({
        employeeIds: [...scelti],
        anteprima: false,
        classe,
      })
      notifySuccess(
        esito.combo === 0
          ? "Nessuna modifica: erano già come il template"
          : `${esito.combo} ${esito.combo === 1 ? "voce aggiornata" : "voci aggiornate"} su ${esito.persone} ${esito.persone === 1 ? "persona" : "persone"}`
      )
      chiudi()
    } catch {
      /* già segnalato */
    }
  }

  return (
    <>
      <Button variant="outline" size="sm" onClick={() => setAperto(true)}>
        <Users />
        Applica a…
      </Button>

      <Dialog open={aperto} onOpenChange={(o) => !o && chiudi()}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle>Applica il template «{display}»</DialogTitle>
          </DialogHeader>

          {anteprima == null ? (
            <div className="space-y-3">
              <p className="text-sm text-muted-foreground">
                Scegli a chi applicarlo: l'anteprima mostrerà esattamente cosa cambierebbe.
                Le eccezioni a mano restano dove sono.
              </p>
              <GridScroller>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="w-[40px]" />
                      <TableHead>Persona</TableHead>
                      <TableHead className="w-[180px]">Gerarchia</TableHead>
                      <TableHead className="w-[140px]">Eccezioni a mano</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {(elencoQuery.data ?? [])
                      .filter((r) => !r.segnaposto)
                      .map((r) => (
                        <TableRow key={r.employeeId}>
                          <TableCell>
                            <Checkbox
                              checked={scelti.has(r.employeeId)}
                              onCheckedChange={(v) =>
                                setScelti((prima) => {
                                  const dopo = new Set(prima)
                                  if (v) dopo.add(r.employeeId)
                                  else dopo.delete(r.employeeId)
                                  return dopo
                                })
                              }
                            />
                          </TableCell>
                          <TableCell className="text-sm">{r.nome}</TableCell>
                          <TableCell className="text-sm">{r.classeDisplay}</TableCell>
                          <TableCell className="text-sm">
                            {r.aMano > 0 ? `${r.aMano} a mano` : "—"}
                          </TableCell>
                        </TableRow>
                      ))}
                  </TableBody>
                </Table>
              </GridScroller>
            </div>
          ) : (
            <div className="space-y-3">
              <div className="text-sm">
                {anteprima.combo === 0 ? (
                  <span>Non cambierebbe niente: sono già come il template.</span>
                ) : (
                  <span>
                    <strong>{anteprima.combo}</strong>{" "}
                    {anteprima.combo === 1 ? "voce cambierebbe" : "voci cambierebbero"} su{" "}
                    {anteprima.persone} {anteprima.persone === 1 ? "persona" : "persone"}.
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
                        <TableHead className="w-[200px]">Persona</TableHead>
                        <TableHead>Funzione</TableHead>
                        <TableHead className="w-[200px]">Da → a</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {anteprima.cambi.map((cambio) => (
                        <TableRow key={`${cambio.employeeId}-${cambio.featureKey}`}>
                          <TableCell className="text-sm">{cambio.nome}</TableCell>
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
            <Button variant="outline" onClick={chiudi}>
              Annulla
            </Button>
            {anteprima == null ? (
              <Button
                disabled={scelti.size === 0 || applicaMutation.isPending}
                onClick={chiediAnteprima}
              >
                <Wand2 />
                Vedi l'anteprima
              </Button>
            ) : (
              <Button
                disabled={applicaMutation.isPending || anteprima.combo === 0}
                onClick={conferma}
              >
                Applica queste modifiche
              </Button>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}
