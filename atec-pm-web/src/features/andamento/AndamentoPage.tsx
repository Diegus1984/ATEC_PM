import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Info, RefreshCw } from "lucide-react"

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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { canAccessFeature } from "@/lib/auth/permissions"
import { fetchAndamentoOfferte } from "@/lib/api/andamento"
import { fetchSalesOfferSellers } from "@/lib/api/sales-offers"
import type { AndamentoGruppo } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { dash, euro } from "@/lib/format"
import { useSalesOffersHub } from "@/lib/signalr/use-sales-offers-hub"
import { cn } from "@/lib/utils"

import {
  GraficoChance,
  GraficoMensile,
  GraficoMultiAnno,
  GraficoPortafoglio,
} from "./andamento-charts"

const CATEGORIE = ["Impianto", "Intervento", "Ricambio", "Altro"]

function anniDisponibili(): number[] {
  const corrente = new Date().getFullYear()
  const anni: number[] = []
  for (let a = corrente; a >= 2022; a--) anni.push(a)
  return anni
}

function pct(v: number | null): string {
  return v === null || v === undefined ? "—" : `${v.toLocaleString("it-IT")} %`
}

/**
 * **Andamento** — il cruscotto con cui il venditore riferisce alla proprietà.
 *
 * <p>Questa metà è quella delle <b>offerte</b>. Le commesse (ordine, costi, redditività,
 * fatturato, incassato) arrivano dal Bilancio e dal SAL e sono la fase successiva.</p>
 */
export function AndamentoPage() {
  const [anno, setAnno] = React.useState(new Date().getFullYear())
  const [venditore, setVenditore] = React.useState("")
  const [categoria, setCategoria] = React.useState("")

  const vedeImporti = canAccessFeature("data.revenue")

  const query = useQuery({
    queryKey: ["andamento-offerte", anno, venditore, categoria],
    queryFn: () =>
      fetchAndamentoOfferte({
        year: anno,
        sellerCode: venditore || undefined,
        category: categoria || undefined,
      }),
  })

  const sellersQuery = useQuery({
    queryKey: ["sales-offer-sellers-tutti"],
    // Anche i cessati: FF e GS da soli sono il 79% dello storico, e un filtro che non li
    // contiene non permette di leggere gli anni passati.
    queryFn: () => fetchSalesOfferSellers(true),
  })

  useSalesOffersHub(true, () => void query.refetch())

  const d = query.data
  const k = d?.kpi
  /** Un anno già concluso non dovrebbe avere offerte ancora aperte: se ne ha, lo storico non è chiuso. */
  const annoChiuso = anno < new Date().getFullYear()

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Andamento</CardTitle>
              <CardDescription>
                Come vanno le offerte: emesse, prese, portafoglio e mercato
              </CardDescription>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Select value={String(anno)} onValueChange={(v) => setAnno(Number(v))}>
                <SelectTrigger size="sm" className="w-[110px]">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {anniDisponibili().map((a) => (
                    <SelectItem key={a} value={String(a)}>
                      {a}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <Select
                value={venditore || "all"}
                onValueChange={(v) => setVenditore(v === "all" ? "" : v)}
              >
                <SelectTrigger size="sm" className="w-[180px]">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Tutti i venditori</SelectItem>
                  {(sellersQuery.data ?? []).map((s) => (
                    <SelectItem key={s.code} value={s.code}>
                      {s.name ? `${s.code} — ${s.name}` : s.code}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <Select
                value={categoria || "all"}
                onValueChange={(v) => setCategoria(v === "all" ? "" : v)}
              >
                <SelectTrigger size="sm" className="w-[170px]">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Tutte le categorie</SelectItem>
                  {CATEGORIE.map((c) => (
                    <SelectItem key={c} value={c}>
                      {c}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>

              <Button
                variant="outline"
                size="sm"
                onClick={() => query.refetch()}
                disabled={query.isFetching}
              >
                <RefreshCw className={query.isFetching ? "animate-spin" : undefined} />
                Aggiorna
              </Button>
            </div>
          </div>
        </CardHeader>

        <CardContent className="space-y-4">
          {query.isError ? (
            <p className="text-sm text-destructive">
              {(query.error as Error).message || "Errore nel caricamento dell'andamento."}
            </p>
          ) : null}

          {query.isLoading || !k ? (
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              {Array.from({ length: 8 }).map((_, i) => (
                <Skeleton key={i} className="h-24 w-full" />
              ))}
            </div>
          ) : (
            <>
              {/* ── I numeri dell'anno ─────────────────────────────────── */}
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <Riquadro
                  titolo="Offerte emesse"
                  numero={k.emesse}
                  sotto={vedeImporti ? euro(k.valoreEmesse) : undefined}
                  nota={
                    k.haForbice && vedeImporti
                      ? `fino a ${euro(k.valoreEmesseMax)}`
                      : undefined
                  }
                />
                <Riquadro
                  titolo="Prese"
                  numero={k.prese}
                  sotto={vedeImporti ? euro(k.valorePrese) : undefined}
                  nota={`${k.preseACorpo} a corpo · ${k.preseAConsuntivo} a consuntivo`}
                />
                <Riquadro
                  titolo="Aperte"
                  numero={k.aperte}
                  sotto={vedeImporti ? euro(k.valoreAperte) : undefined}
                  nota={`${k.perse} perse · ${k.sospese} sospese`}
                />
                <Riquadro
                  titolo="Conversione"
                  numero={pct(k.conversionePct)}
                  nota="prese su prese + perse"
                />
                <Riquadro
                  titolo="Portafoglio ponderato"
                  numero={vedeImporti ? euro(k.portafoglioPonderato) : "—"}
                  nota={`stimato su ${k.apertConChance} aperte di ${k.aperte}`}
                />
                <Riquadro titolo="Numeri riservati" numero={k.bozze} nota="bozze senza dati" />
                <Riquadro
                  titolo="Senza importo"
                  numero={k.senzaImporto}
                  nota="fuori dai totali"
                />
                <Riquadro
                  titolo="Prese incomplete"
                  numero={k.preseIncomplete}
                  nota="senza ordine né «a consuntivo»"
                  attenzione={k.preseIncomplete > 0}
                />
              </div>

              {/*
                🪤 L'avviso che rende onesta questa pagina. Nel vecchio Excel le offerte perse
                non venivano quasi mai chiuse: sui dati veri il 2025 ha 155 prese e 7 perse, e
                la conversione esce **95,7%**. Un numero così, messo davanti alla proprietà
                senza una riga di contesto, è peggio di nessun numero.
              */}
              {/*
                Due modi diversi di dire la stessa cosa, perché il sintomo cambia con l'anno.
                Su un anno CHIUSO restano offerte aperte che nessuno ha chiuso; sull'anno IN
                CORSO le perse sono zero e ogni conversione esce 100%. In entrambi i casi la
                causa è una: nel vecchio Excel le offerte perse non si registravano.
              */}
              {k.perse === 0 && k.prese > 0 ? (
                <Avviso>
                  Nel <b>{anno}</b> non c'è <b>nessuna offerta segnata come persa</b>: per
                  questo ogni conversione di questa pagina dice <b>100%</b>. Non è un risultato,
                  è un dato che manca — le offerte perse vanno chiuse dal Registro, una per una
                  o con «Chiudi anni passati».
                </Avviso>
              ) : annoChiuso && k.aperte > 0 ? (
                <Avviso>
                  Il <b>{anno}</b> è un anno chiuso, ma risultano ancora{" "}
                  <b>{k.aperte} offerte aperte</b>: nel vecchio Excel le offerte perse non
                  venivano quasi mai chiuse
                  {k.conversionePct !== null ? (
                    <>
                      , quindi la conversione di <b>{pct(k.conversionePct)}</b> è calcolata su{" "}
                      {k.prese + k.perse} chiuse di cui solo {k.perse} perse
                    </>
                  ) : null}
                  . Conversione e portafoglio di quest'anno sono ottimisti finché non si passa
                  la chiusura in blocco dal Registro Offerte.
                </Avviso>
              ) : null}

              {k.apertConChance < k.aperte ? (
                <Avviso>
                  Il portafoglio ponderato pesa ogni offerta per la sua chance, e{" "}
                  <b>{k.aperte - k.apertConChance} aperte su {k.aperte}</b> non ne hanno una:
                  quelle restano fuori dalla stima, non valgono zero.
                </Avviso>
              ) : null}

              {/* ── Andamento nel tempo ─────────────────────────────────── */}
              <div className="grid gap-4 xl:grid-cols-2">
                <Riquadrone
                  titolo="Cinque anni a confronto"
                  descrizione="Valore emesso mese per mese"
                >
                  <GraficoMultiAnno serie={d!.mensileMultiAnno} annoCorrente={anno} />
                </Riquadrone>

                <Riquadrone
                  titolo={`Mese per mese — ${anno}`}
                  descrizione="Emesse, prese e ancora aperte"
                >
                  <GraficoMensile mesi={d!.mensile} />
                </Riquadrone>
              </div>

              {/* ── Chance e portafoglio ────────────────────────────────── */}
              <div className="grid gap-4 xl:grid-cols-2">
                <Riquadrone
                  titolo="Aperte per livello di chance"
                  descrizione="Quanto vale il portafoglio, per quanto ci crediamo"
                >
                  {d!.perChance.length > 0 ? (
                    <>
                      <GraficoChance livelli={d!.perChance} />
                      <TabellaChance dati={d!.perChance} vedeImporti={vedeImporti} />
                    </>
                  ) : (
                    <Vuoto>Nessuna offerta aperta in questo filtro.</Vuoto>
                  )}
                </Riquadrone>

                <Riquadrone
                  titolo="Portafoglio negli ultimi 12 mesi"
                  descrizione="Ricostruito dal registro modifiche"
                >
                  {d!.portafoglio.length > 0 ? (
                    <GraficoPortafoglio mesi={d!.portafoglio} />
                  ) : (
                    <Vuoto>{d!.portafoglioNota}</Vuoto>
                  )}
                </Riquadrone>
              </div>

              {/* ── Mercato, venditori, tipi ────────────────────────────── */}
              <div className="grid gap-4 xl:grid-cols-2">
                <Riquadrone
                  titolo="Analisi di mercato"
                  descrizione="Impianti, interventi e ricambi"
                >
                  <TabellaGruppi dati={d!.perCategoria} vedeImporti={vedeImporti} intestazione="Categoria" />
                </Riquadrone>

                <Riquadrone titolo="Per venditore" descrizione="Emesso, preso e conversione">
                  <TabellaGruppi dati={d!.perVenditore} vedeImporti={vedeImporti} intestazione="Venditore" />
                </Riquadrone>

                <Riquadrone titolo="Per tipo impianto" descrizione="Dove si concentra il lavoro">
                  <TabellaGruppi dati={d!.perTipo} vedeImporti={vedeImporti} intestazione="Tipo" />
                </Riquadrone>

                <Riquadrone titolo="Primi dieci clienti" descrizione="Per valore preso">
                  <TabellaGruppi dati={d!.topClienti} vedeImporti={vedeImporti} intestazione="Cliente" />
                </Riquadrone>
              </div>

              {/* ── Le aperte più grandi ────────────────────────────────── */}
              <Riquadrone
                titolo="Le quindici aperte più grandi"
                descrizione="La lista che si guarda per prima"
              >
                {d!.topAperte.length > 0 ? (
                  <GridScroller className="rounded-lg border" scrollerClassName="max-h-[420px]">
                    <Table>
                      <TableHeader className="bg-muted/50">
                        <TableRow className="hover:bg-transparent">
                          <TableHead>Numero</TableHead>
                          <TableHead>Data</TableHead>
                          <TableHead>Cliente</TableHead>
                          <TableHead>Descrizione</TableHead>
                          <TableHead className="text-right">Importo</TableHead>
                          <TableHead className="text-right">Chance</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {d!.topAperte.map((o) => (
                          <TableRow key={o.id}>
                            <TableCell className="font-medium tabular-nums">{o.numero}</TableCell>
                            <TableCell className="tabular-nums">{formatDateShort(o.data)}</TableCell>
                            <TableCell className="max-w-[240px] truncate" title={o.cliente}>
                              {dash(o.cliente)}
                            </TableCell>
                            <TableCell className="max-w-[320px] truncate" title={o.descrizione}>
                              {dash(o.descrizione)}
                            </TableCell>
                            <TableCell className="text-right tabular-nums">
                              {vedeImporti ? euro(o.importo) : "—"}
                            </TableCell>
                            <TableCell className="text-right tabular-nums text-muted-foreground">
                              {o.chance == null ? "—" : `${o.chance}%`}
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </GridScroller>
                ) : (
                  <Vuoto>Nessuna offerta aperta in questo filtro.</Vuoto>
                )}
              </Riquadrone>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

// ══════════════════════════════════════════════════════════════
// PEZZI
// ══════════════════════════════════════════════════════════════

function Riquadro({
  titolo,
  numero,
  sotto,
  nota,
  attenzione,
}: {
  titolo: string
  numero: React.ReactNode
  sotto?: string
  nota?: string
  attenzione?: boolean
}) {
  return (
    <div
      className={cn(
        "rounded-lg border p-3",
        attenzione && "border-amber-500/50 bg-amber-500/5"
      )}
    >
      <p className="text-xs text-muted-foreground">{titolo}</p>
      <p className="text-2xl font-semibold tabular-nums">{numero}</p>
      {sotto ? <p className="text-sm tabular-nums">{sotto}</p> : null}
      {nota ? <p className="mt-0.5 text-xs text-muted-foreground">{nota}</p> : null}
    </div>
  )
}

function Riquadrone({
  titolo,
  descrizione,
  children,
}: {
  titolo: string
  descrizione?: string
  children: React.ReactNode
}) {
  return (
    <div className="space-y-3 rounded-lg border p-4">
      <div>
        <h3 className="text-sm font-medium">{titolo}</h3>
        {descrizione ? (
          <p className="text-xs text-muted-foreground">{descrizione}</p>
        ) : null}
      </div>
      {children}
    </div>
  )
}

function Avviso({ children }: { children: React.ReactNode }) {
  return (
    <p className="flex items-start gap-2 rounded-lg border bg-muted/40 px-3 py-2 text-sm text-muted-foreground">
      <Info className="mt-0.5 size-4 shrink-0" />
      <span>{children}</span>
    </p>
  )
}

function Vuoto({ children }: { children: React.ReactNode }) {
  return (
    <p className="rounded-lg border border-dashed px-3 py-6 text-center text-sm text-muted-foreground">
      {children}
    </p>
  )
}

/**
 * La tabella che accompagna ogni raggruppamento. **Non è un di più**: è la via di lettura
 * alternativa al colore, quella che serve a chi stampa, a chi non distingue le tinte e a chi
 * vuole il numero esatto invece dell'altezza di una barra.
 */
function TabellaGruppi({
  dati,
  vedeImporti,
  intestazione,
}: {
  dati: AndamentoGruppo[]
  vedeImporti: boolean
  intestazione: string
}) {
  if (dati.length === 0) return <Vuoto>Niente in questo filtro.</Vuoto>
  return (
    <GridScroller className="rounded-lg border" scrollerClassName="max-h-[300px]">
      <Table>
        <TableHeader className="bg-muted/50">
          <TableRow className="hover:bg-transparent">
            <TableHead>{intestazione}</TableHead>
            <TableHead className="text-right">Emesse</TableHead>
            {vedeImporti ? <TableHead className="text-right">Valore</TableHead> : null}
            <TableHead className="text-right">Prese</TableHead>
            {vedeImporti ? <TableHead className="text-right">Valore preso</TableHead> : null}
            <TableHead className="text-right">Conv.</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {dati.map((g) => (
            <TableRow key={g.chiave}>
              <TableCell className="max-w-[220px] truncate" title={g.etichetta}>
                {g.etichetta}
              </TableCell>
              <TableCell className="text-right tabular-nums">{g.emesse}</TableCell>
              {vedeImporti ? (
                <TableCell className="text-right tabular-nums">{euro(g.valoreEmesse)}</TableCell>
              ) : null}
              <TableCell className="text-right tabular-nums">{g.prese}</TableCell>
              {vedeImporti ? (
                <TableCell className="text-right tabular-nums">{euro(g.valorePrese)}</TableCell>
              ) : null}
              <TableCell className="text-right tabular-nums">{pct(g.conversionePct)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </GridScroller>
  )
}

function TabellaChance({
  dati,
  vedeImporti,
}: {
  dati: { livello: number; etichetta: string; offerte: number; valore: number; valorePonderato: number }[]
  vedeImporti: boolean
}) {
  return (
    <GridScroller className="rounded-lg border" scrollerClassName="max-h-[240px]">
      <Table>
        <TableHeader className="bg-muted/50">
          <TableRow className="hover:bg-transparent">
            <TableHead>Chance</TableHead>
            <TableHead className="text-right">Offerte</TableHead>
            {vedeImporti ? <TableHead className="text-right">Valore</TableHead> : null}
            {vedeImporti ? <TableHead className="text-right">Ponderato</TableHead> : null}
          </TableRow>
        </TableHeader>
        <TableBody>
          {dati.map((c) => (
            <TableRow key={c.livello}>
              <TableCell>
                {c.livello < 0 ? (
                  <Badge variant="outline">{c.etichetta}</Badge>
                ) : (
                  <span className="tabular-nums">{c.etichetta}</span>
                )}
              </TableCell>
              <TableCell className="text-right tabular-nums">{c.offerte}</TableCell>
              {vedeImporti ? (
                <TableCell className="text-right tabular-nums">{euro(c.valore)}</TableCell>
              ) : null}
              {vedeImporti ? (
                <TableCell className="text-right tabular-nums text-muted-foreground">
                  {c.livello < 0 ? "—" : euro(c.valorePonderato)}
                </TableCell>
              ) : null}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </GridScroller>
  )
}
