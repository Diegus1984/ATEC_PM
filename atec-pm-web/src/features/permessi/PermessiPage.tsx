import * as React from "react"
import { Link, useNavigate } from "react-router-dom"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { BookOpen, RefreshCw, Search, Wand2 } from "lucide-react"

import { GridScroller } from "@/components/shared/grid-scroller"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
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
import { notifyError, notifySuccess } from "@/lib/toast"
import { applicaClasse, fetchElencoPermessi } from "@/lib/api/permessi"
import type { EsitoApplicaClasseDto } from "@/lib/api/types"

const TUTTE = "__tutte__"

/**
 * Pagina «Permessi» — l'elenco delle persone (PIANO-PERMESSI.md §5).
 *
 * Una riga = una persona. Da qui si entra nella sua scheda, che è l'unico posto da cui i
 * permessi si scrivono davvero.
 *
 * I due filtri sono **classe** e **reparto**, e sono due cose diverse: il reparto dice dove uno
 * lavora (anagrafica) e non concede niente, la classe dice che autorità ha nel gestionale.
 * Vinardi è *Responsabile* in *Acquisti*, non «un Acquisti»: tenerli separati è ciò che evita di
 * inventare una classe per ogni ufficio.
 *
 * La colonna «diverso dalla classe» è la più importante dell'elenco: dice a colpo d'occhio chi è
 * stato configurato apposta e chi è andato alla deriva.
 */
export function PermessiPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [cerca, setCerca] = React.useState("")
  const [classe, setClasse] = React.useState(TUTTE)
  const [reparto, setReparto] = React.useState(TUTTE)
  const [selezione, setSelezione] = React.useState<Set<number>>(new Set())
  const [anteprima, setAnteprima] = React.useState<EsitoApplicaClasseDto | null>(null)

  const elencoQuery = useQuery({
    queryKey: ["permessi", "elenco"],
    queryFn: fetchElencoPermessi,
  })

  const applicaMutation = useMutation({
    mutationFn: applicaClasse,
    onError: (e) => notifyError(e, "Classe non applicata"),
  })

  // `?? []` creerebbe un array nuovo a ogni render, e i due useMemo qui sotto si
  // ricalcolerebbero sempre: la lista vuota va tenuta stabile.
  const righe = React.useMemo(() => elencoQuery.data ?? [], [elencoQuery.data])

  const classi = React.useMemo(
    () =>
      Array.from(
        new Map(righe.map((r) => [r.classe, r.classeDisplay || r.classe])).entries()
      ).sort((a, b) => a[1].localeCompare(b[1])),
    [righe]
  )

  const reparti = React.useMemo(
    () => Array.from(new Set(righe.flatMap((r) => r.reparti))).sort(),
    [righe]
  )

  const filtrate = righe.filter((r) => {
    const testo = cerca.trim().toLowerCase()
    if (testo && !`${r.nome} ${r.username}`.toLowerCase().includes(testo)) return false
    if (classe !== TUTTE && r.classe !== classe) return false
    if (reparto !== TUTTE && !r.reparti.includes(reparto)) return false
    return true
  })

  const tutteSelezionate = filtrate.length > 0 && filtrate.every((r) => selezione.has(r.employeeId))

  function commuta(employeeId: number) {
    setSelezione((prec) => {
      const succ = new Set(prec)
      if (succ.has(employeeId)) succ.delete(employeeId)
      else succ.add(employeeId)
      return succ
    })
  }

  /**
   * ⚠️ **Non applica: chiede l'anteprima.** È la regola §4.4 del piano — un timbro di massa può
   * cancellare in silenzio le eccezioni messe apposta sulle singole persone, quindi quello che
   * si conferma è l'elenco dei cambi, non il pulsante.
   */
  async function chiediAnteprima() {
    try {
      setAnteprima(
        await applicaMutation.mutateAsync({
          employeeIds: Array.from(selezione),
          anteprima: true,
        })
      )
    } catch {
      /* già segnalato da onError */
    }
  }

  async function conferma() {
    try {
      const esito = await applicaMutation.mutateAsync({
        employeeIds: Array.from(selezione),
        anteprima: false,
      })
      setAnteprima(null)
      setSelezione(new Set())
      void queryClient.invalidateQueries({ queryKey: ["permessi"] })
      notifySuccess(
        `${esito.combo} ${esito.combo === 1 ? "combo aggiornata" : "combo aggiornate"} su ${esito.persone} ${esito.persone === 1 ? "persona" : "persone"}`
      )
    } catch {
      /* già segnalato */
    }
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Permessi</CardTitle>
              <CardDescription>
                Chi vede cosa, persona per persona ({filtrate.length} di {righe.length})
              </CardDescription>
            </div>
            <div className="flex gap-2">
              {selezione.size > 0 ? (
                <Button size="sm" onClick={chiediAnteprima}>
                  <Wand2 />
                  Applica classe ai selezionati ({selezione.size})
                </Button>
              ) : null}
              <Button
                variant="outline"
                size="sm"
                onClick={() => void elencoQuery.refetch()}
              >
                <RefreshCw />
                Aggiorna
              </Button>
              <Button variant="outline" size="sm" asChild>
                <Link to="/permessi/master">
                  <Wand2 />
                  Master / Template
                </Link>
              </Button>
              <Button variant="outline" size="sm" asChild>
                <Link to="/permessi/catalogo">
                  <BookOpen />
                  Catalogo funzioni
                </Link>
              </Button>
            </div>
          </div>
        </CardHeader>

        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative min-w-[220px] flex-1">
              <Search className="absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                className="pl-8"
                placeholder="Cerca per nome o utenza…"
                value={cerca}
                onChange={(e) => setCerca(e.target.value)}
              />
            </div>
            <Select value={classe} onValueChange={setClasse}>
              <SelectTrigger className="w-[200px]">
                <SelectValue placeholder="Classe" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={TUTTE}>Tutte le classi</SelectItem>
                {classi.map(([valore, etichetta]) => (
                  <SelectItem key={valore} value={valore}>
                    {etichetta}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Select value={reparto} onValueChange={setReparto}>
              <SelectTrigger className="w-[200px]">
                <SelectValue placeholder="Reparto" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={TUTTE}>Tutti i reparti</SelectItem>
                {reparti.map((r) => (
                  <SelectItem key={r} value={r}>
                    {r}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <GridScroller>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[40px]">
                    <Checkbox
                      checked={tutteSelezionate}
                      onCheckedChange={(v) =>
                        setSelezione(
                          v ? new Set(filtrate.map((r) => r.employeeId)) : new Set()
                        )
                      }
                      aria-label="Seleziona tutti"
                    />
                  </TableHead>
                  <TableHead>Persona</TableHead>
                  <TableHead className="w-[180px]">Classe</TableHead>
                  <TableHead className="w-[220px]">Reparti</TableHead>
                  <TableHead className="w-[110px]">Funzioni</TableHead>
                  <TableHead className="w-[190px]">Eccezioni a mano</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {elencoQuery.isLoading ? (
                  <TableRow>
                    <TableCell colSpan={6} className="text-sm text-muted-foreground">
                      Caricamento…
                    </TableCell>
                  </TableRow>
                ) : null}
                {!elencoQuery.isLoading && filtrate.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={6} className="text-sm text-muted-foreground">
                      Nessuna persona con questi filtri.
                    </TableCell>
                  </TableRow>
                ) : null}
                {filtrate.map((r) => (
                  <TableRow
                    key={r.employeeId}
                    className="cursor-pointer"
                    onClick={() => navigate(`/permessi/persona/${r.employeeId}`)}
                  >
                    <TableCell onClick={(e) => e.stopPropagation()}>
                      <Checkbox
                        checked={selezione.has(r.employeeId)}
                        onCheckedChange={() => commuta(r.employeeId)}
                        aria-label={`Seleziona ${r.nome}`}
                      />
                    </TableCell>
                    <TableCell>
                      <div className="font-medium">{r.nome}</div>
                      <div className="text-xs text-muted-foreground">{r.username}</div>
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap items-center gap-1">
                        <Badge variant="outline">{r.classeDisplay || r.classe}</Badge>
                        {r.jolly ? <Badge>vede tutto</Badge> : null}
                      </div>
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-wrap gap-1">
                        {r.reparti.map((d) => (
                          <Badge key={d} variant="secondary">
                            {d}
                          </Badge>
                        ))}
                      </div>
                    </TableCell>
                    <TableCell className="text-sm">{r.funzioni}</TableCell>
                    <TableCell>
                      {r.aMano === 0 ? (
                        <span className="text-sm text-muted-foreground">—</span>
                      ) : (
                        <Badge variant="secondary" title="Righe decise a mano: «Applica template» le rispetta.">
                          {r.aMano} a mano
                        </Badge>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </GridScroller>
        </CardContent>
      </Card>

      {/* L'anteprima obbligatoria (§4.4): si conferma questo elenco, non il pulsante. Senza,
          un'applicazione di massa cancellerebbe in silenzio le eccezioni per persona — il
          Timesheet spento agli Acquisti, le commesse chiuse alla Contabilità. */}
      <Dialog open={anteprima != null} onOpenChange={(o) => !o && setAnteprima(null)}>
        <DialogContent className="max-w-3xl">
          <DialogHeader>
            <DialogTitle>Applica la classe ai selezionati</DialogTitle>
          </DialogHeader>
          {anteprima ? (
            <div className="space-y-3">
              <p className="text-sm">
                <strong>{anteprima.persone}</strong>{" "}
                {anteprima.persone === 1 ? "persona" : "persone"}, <strong>{anteprima.combo}</strong>{" "}
                {anteprima.combo === 1 ? "combo cambiata" : "combo cambiate"}.
                {anteprima.rispettateAMano > 0 ? (
                  <span className="text-muted-foreground">
                    {" "}
                    {anteprima.rispettateAMano} decise a mano restano dove sono.
                  </span>
                ) : null}
              </p>
              {anteprima.cambi.length > 0 ? (
                <GridScroller>
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead className="w-[200px]">Persona</TableHead>
                        <TableHead>Funzione</TableHead>
                        <TableHead className="w-[150px]">Da → a</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {anteprima.cambi.map((cambio) => (
                        <TableRow key={`${cambio.employeeId}-${cambio.featureKey}`}>
                          <TableCell className="text-sm">{cambio.nome}</TableCell>
                          <TableCell className="text-sm">{cambio.displayName}</TableCell>
                          <TableCell className="text-sm">
                            {cambio.da} → {cambio.a}
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
              onClick={conferma}
              disabled={applicaMutation.isPending || anteprima?.combo === 0}
            >
              Applica queste modifiche
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
