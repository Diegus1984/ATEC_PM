import * as React from "react"
import {
  Bar,
  BarChart,
  CartesianGrid,
  Line,
  LineChart,
  XAxis,
  YAxis,
} from "recharts"

import {
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
} from "@/components/ui/chart"
import type {
  AndamentoChance,
  AndamentoMese,
  AndamentoPortafoglio,
  AndamentoSerieAnno,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { CONFIG_SINGOLA, CONFIG_STATI, assiEuro, coloreAnno } from "./andamento-palette"

/**
 * Quali anni sono accesi nel confronto pluriennale. Chiave **versionata**, come per il menu
 * «Colonne» delle griglie: se un giorno cambiasse il senso dei valori salvati si alza il
 * numero, invece di leggere male i salvataggi vecchi.
 */
const ANNI_STORAGE_KEY = "atec_pm_andamento_anni_v1"

/**
 * La legenda, disegnata a mano sopra il grafico.
 *
 * <p>🪤 <b>Quella di recharts non si può ordinare.</b> La v3 costruisce il proprio
 * <c>payload</c> da un registro interno e lo riordina alfabeticamente: «Aperte, Emesse,
 * Prese» mentre le barre stanno nell'ordine in cui sono dichiarate. Chi legge accoppia la
 * prima pastiglia alla prima barra e sbaglia due colori su tre. Passare <c>payload</c> a
 * <c>Legend</c> o al suo <c>content</c> non serve a niente — provato tutti e due.</p>
 *
 * <p>Sta <b>sopra</b> il grafico e non sotto: è la chiave di lettura, e si legge prima di
 * guardare le barre, non dopo.</p>
 */
interface VoceLegenda {
  etichetta: string
  colore: string
}

/** Legenda di sola lettura: dice cosa sono i colori e basta. */
function Legenda({ voci }: { voci: VoceLegenda[] }) {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
      {voci.map((v) => (
        <span key={v.etichetta} className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <span className="size-2 shrink-0 rounded-[2px]" style={{ backgroundColor: v.colore }} />
          {v.etichetta}
        </span>
      ))}
    </div>
  )
}

/**
 * Legenda a interruttori: ogni voce accende e spegne la propria serie.
 *
 * <p>Con cinque linee sovrapposte il confronto che interessa e quasi sempre fra due o tre
 * anni: spegnere gli altri e il gesto che rende leggibile il grafico, e la scelta si
 * ricorda — come per il menu «Colonne» delle griglie.</p>
 *
 * <p>🪤 <b>Il colore non si ricalcola mai.</b> Resta agganciato all'anno, non alla sua
 * posizione fra quelli accesi: spegnere il 2023 non deve ridipingere il 2024, o il lettore
 * ricomincia da capo ogni volta che tocca un interruttore.</p>
 */
function LegendaInterruttori({
  voci,
  acceso,
  onCambia,
}: {
  voci: VoceLegenda[]
  acceso: (etichetta: string) => boolean
  onCambia: (etichetta: string, valore: boolean) => void
}) {
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {voci.map((v) => {
        const on = acceso(v.etichetta)
        return (
          <button
            key={v.etichetta}
            type="button"
            aria-pressed={on}
            onClick={() => onCambia(v.etichetta, !on)}
            title={on ? `Nascondi ${v.etichetta}` : `Mostra ${v.etichetta}`}
            className={cn(
              "flex items-center gap-1.5 rounded-md border px-2 py-1 text-xs transition-colors",
              on
                ? "bg-muted/50 text-foreground"
                : "text-muted-foreground opacity-60 hover:opacity-100"
            )}
          >
            <span
              className="size-2 shrink-0 rounded-[2px] border"
              style={{
                backgroundColor: on ? v.colore : "transparent",
                borderColor: v.colore,
              }}
            />
            {v.etichetta}
          </button>
        )
      })}
    </div>
  )
}

/** L'ordine con cui si leggono i tre stati: emessa → presa → ancora aperta. */
const LEGENDA_STATI = (["emesse", "prese", "aperte"] as const).map((k) => ({
  etichetta: CONFIG_STATI[k].label,
  colore: CONFIG_STATI[k].color,
}))

/** Formattatore comune del suggerimento: nome della serie a sinistra, importo a destra. */
function riga(nome: React.ReactNode, valore: number) {
  return (
    <span className="flex w-full items-center justify-between gap-3">
      <span>{nome}</span>
      <span className="font-mono font-medium tabular-nums">{euro(valore)}</span>
    </span>
  )
}

/**
 * Cinque anni a confronto, mese per mese. Una linea per anno: è la domanda «stiamo
 * andando meglio o peggio dell'anno scorso?», che è di andamento, quindi linee.
 */
export function GraficoMultiAnno({
  serie,
  annoCorrente,
}: {
  serie: AndamentoSerieAnno[]
  annoCorrente: number
}) {
  const [anniAccesi, setAnniAccesi] = usePersistedColumnVisibility(ANNI_STORAGE_KEY)

  /**
   * 🪤 Solo un `false` esplicito spegne. La scelta si legge da localStorage al primo
   * montaggio, quando le serie non sono ancora arrivate: se un anno assente valesse
   * «spento», al primo caricamento il grafico sarebbe vuoto. Un anno nuovo (il 2027, a
   * gennaio) nasce così acceso, che è quello che ci si aspetta.
   */
  const acceso = React.useCallback(
    (etichetta: string) => anniAccesi[etichetta] !== false,
    [anniAccesi]
  )

  const visibili = serie.filter((s) => acceso(String(s.anno)))

  // Il config tiene TUTTI gli anni, anche gli spenti: il colore è agganciato all'anno e non
  // deve cambiare quando si accende o si spegne qualcosa.
  const config: ChartConfig = Object.fromEntries(
    serie.map((s) => [
      `a${s.anno}`,
      { label: String(s.anno), color: coloreAnno(s.anno, annoCorrente) },
    ])
  )

  const dati = React.useMemo(() => {
    const mesi = ["Gen", "Feb", "Mar", "Apr", "Mag", "Giu", "Lug", "Ago", "Set", "Ott", "Nov", "Dic"]
    return mesi.map((etichetta, i) => {
      const punto: Record<string, string | number> = { etichetta }
      for (const s of serie) punto[`a${s.anno}`] = s.mesi[i] ?? 0
      return punto
    })
  }, [serie])

  return (
    <div className="space-y-2">
      <LegendaInterruttori
        voci={serie.map((s) => ({
          etichetta: String(s.anno),
          colore: coloreAnno(s.anno, annoCorrente),
        }))}
        acceso={acceso}
        onCambia={(etichetta, valore) =>
          setAnniAccesi((prev) => ({ ...prev, [etichetta]: valore }))
        }
      />
      {visibili.length === 0 ? (
        <p className="rounded-lg border border-dashed px-3 py-10 text-center text-sm text-muted-foreground">
          Tutti gli anni sono spenti: riaccendine uno qui sopra.
        </p>
      ) : (
      <ChartContainer config={config} className="h-[280px] w-full">
      <LineChart data={dati} margin={{ top: 8, right: 12, bottom: 4, left: 8 }}>
        <CartesianGrid vertical={false} strokeOpacity={0.35} />
        <XAxis dataKey="etichetta" tickLine={false} axisLine={false} />
        <YAxis tickLine={false} axisLine={false} width={70} tickFormatter={assiEuro} />
        <ChartTooltip
          content={
            <ChartTooltipContent
              formatter={(valore, nome) =>
                riga(config[nome as string]?.label ?? nome, Number(valore))
              }
            />
          }
        />
        {visibili.map((s) => (
          <Line
            key={s.anno}
            dataKey={`a${s.anno}`}
            type="monotone"
            stroke={`var(--color-a${s.anno})`}
            strokeWidth={s.anno === annoCorrente ? 2.5 : 2}
            dot={false}
            activeDot={{ r: 4 }}
          />
        ))}
      </LineChart>
      </ChartContainer>
      )}
    </div>
  )
}

/**
 * L'anno scelto, mese per mese: quanto emesso, quanto preso, quanto è ancora aperto.
 * Barre affiancate — sono tre grandezze dello stesso tipo confrontate nello stesso mese.
 */
export function GraficoMensile({ mesi }: { mesi: AndamentoMese[] }) {
  return (
    <div className="space-y-2">
      <Legenda voci={LEGENDA_STATI} />
      <ChartContainer config={CONFIG_STATI} className="h-[280px] w-full">
      <BarChart data={mesi} margin={{ top: 8, right: 12, bottom: 4, left: 8 }} barGap={2}>
        <CartesianGrid vertical={false} strokeOpacity={0.35} />
        <XAxis dataKey="etichetta" tickLine={false} axisLine={false} />
        <YAxis tickLine={false} axisLine={false} width={70} tickFormatter={assiEuro} />
        <ChartTooltip
          content={
            <ChartTooltipContent
              formatter={(valore, nome) =>
                riga(CONFIG_STATI[nome as keyof typeof CONFIG_STATI]?.label ?? nome, Number(valore))
              }
            />
          }
        />
        <Bar dataKey="emesse" fill="var(--color-emesse)" radius={[4, 4, 0, 0]} />
        <Bar dataKey="prese" fill="var(--color-prese)" radius={[4, 4, 0, 0]} />
        <Bar dataKey="aperte" fill="var(--color-aperte)" radius={[4, 4, 0, 0]} />
      </BarChart>
      </ChartContainer>
    </div>
  )
}

/**
 * Le offerte aperte per livello di chance. Una serie sola, quindi una tinta sola e
 * nessuna legenda: il titolo dice già cos'è.
 */
export function GraficoChance({ livelli }: { livelli: AndamentoChance[] }) {
  return (
    <ChartContainer config={CONFIG_SINGOLA} className="h-[260px] w-full">
      <BarChart data={livelli} margin={{ top: 8, right: 12, bottom: 4, left: 8 }}>
        <CartesianGrid vertical={false} strokeOpacity={0.35} />
        <XAxis dataKey="etichetta" tickLine={false} axisLine={false} interval={0} />
        <YAxis tickLine={false} axisLine={false} width={70} tickFormatter={assiEuro} />
        <ChartTooltip
          content={
            <ChartTooltipContent
              formatter={(valore, _nome, item) =>
                riga(
                  `${(item?.payload as AndamentoChance | undefined)?.offerte ?? 0} offerte`,
                  Number(valore)
                )
              }
            />
          }
        />
        <Bar dataKey="valore" fill="var(--color-valore)" radius={[4, 4, 0, 0]} />
      </BarChart>
    </ChartContainer>
  )
}

/**
 * Il portafoglio aperto negli ultimi dodici mesi, ricostruito dal registro modifiche.
 * <p>Si disegna <b>solo se</b> il registro copre il periodo: il chiamante mostra la nota
 * quando la serie è vuota, invece di una linea piatta che sembra un dato.</p>
 */
export function GraficoPortafoglio({ mesi }: { mesi: AndamentoPortafoglio[] }) {
  return (
    <ChartContainer config={CONFIG_SINGOLA} className="h-[260px] w-full">
      <LineChart data={mesi} margin={{ top: 8, right: 12, bottom: 4, left: 8 }}>
        <CartesianGrid vertical={false} strokeOpacity={0.35} />
        <XAxis dataKey="etichetta" tickLine={false} axisLine={false} />
        <YAxis tickLine={false} axisLine={false} width={70} tickFormatter={assiEuro} />
        <ChartTooltip
          content={
            <ChartTooltipContent
              formatter={(valore, _nome, item) =>
                riga(
                  `${(item?.payload as AndamentoPortafoglio | undefined)?.offerte ?? 0} aperte`,
                  Number(valore)
                )
              }
            />
          }
        />
        <Line
          dataKey="valore"
          type="monotone"
          stroke="var(--color-valore)"
          strokeWidth={2}
          dot={{ r: 4 }}
        />
      </LineChart>
    </ChartContainer>
  )
}
