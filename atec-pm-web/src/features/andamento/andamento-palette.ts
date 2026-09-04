import type { ChartConfig } from "@/components/ui/chart"

/**
 * La palette delle serie dell'Andamento.
 *
 * <p><b>Validata, non scelta a occhio</b> (`scripts/validate_palette.js` della skill dataviz,
 * 04/09/2026): banda di luminosità, croma minimo, separazione per daltonismo sulle coppie
 * adiacenti, soglia a vista normale e contrasto sul fondo — **tutti PASS sia in chiaro sia in
 * scuro**. I primi due colori sono gli stessi già in uso in Analisi Consegne, così l'app resta
 * un sistema solo.</p>
 *
 * <p>🪤 <b>L'ordine è fisso e non si cicla.</b> Il colore segue l'entità (l'anno, lo stato),
 * mai la sua posizione in classifica: un filtro che cambia quante serie ci sono non deve
 * ridipingere quelle rimaste.</p>
 */
export const SERIE = [
  "#4A86C6", // blu
  "#C2701C", // arancio
  "#1F9077", // verde
  "#8A63D2", // viola
  "#BC4462", // rosso
] as const

/** Un colore per anno, ancorato alla distanza dall'anno corrente: il 2026 è sempre blu. */
export function coloreAnno(anno: number, annoCorrente: number): string {
  const distanza = Math.max(0, Math.min(SERIE.length - 1, annoCorrente - anno))
  return SERIE[distanza]
}

/**
 * Emesse / prese / aperte. Tre serie, tre colori fissi: «prese» resta verde in ogni
 * grafico della pagina, o il lettore deve riconsultare la legenda ogni volta.
 */
export const CONFIG_STATI = {
  emesse: { label: "Emesse", color: SERIE[0] },
  prese: { label: "Prese", color: SERIE[2] },
  aperte: { label: "Aperte", color: SERIE[1] },
} satisfies ChartConfig

/** Serie singola (chance, portafoglio, classifiche): una sola tinta, nessuna legenda. */
export const CONFIG_SINGOLA = {
  valore: { label: "Valore", color: SERIE[0] },
} satisfies ChartConfig

/**
 * Etichetta compatta per gli assi: gli importi veri stanno nel suggerimento e nelle tabelle,
 * sull'asse serve solo l'ordine di grandezza.
 */
export function assiEuro(valore: number): string {
  if (Math.abs(valore) >= 1_000_000) return `${(valore / 1_000_000).toFixed(1)} M€`
  if (Math.abs(valore) >= 1000) return `${Math.round(valore / 1000)} k€`
  return `${Math.round(valore)} €`
}
