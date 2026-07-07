import type { PieLabelRenderProps } from "recharts"

const RADIAN = Math.PI / 180

/** Raggio esterno di riferimento (torta sintesi ~280px). */
const REFERENCE_OUTER_RADIUS = 126

/** Soglia minima (% fetta) per mostrare il codice stato dentro il grafico. */
const MIN_SLICE_PERCENT = 0.035

interface SlicePayload {
  key?: string
  fg?: string
}

/** Etichetta codice stato (es. ANN, DO) centrata nello spicchio, scalata sul raggio. */
export function DdpPieSliceLabel(props: PieLabelRenderProps) {
  const { cx, cy, midAngle, innerRadius, outerRadius, percent, payload } = props
  const slice = payload as SlicePayload | undefined

  if (cx == null || cy == null || outerRadius == null || !slice?.key) {
    return null
  }
  if ((percent ?? 0) < MIN_SLICE_PERCENT) {
    return null
  }

  const inner = Number(innerRadius ?? 0)
  const outer = Number(outerRadius)
  const scale = outer / REFERENCE_OUTER_RADIUS
  const angle = Number(midAngle ?? 0)
  const radius = inner + (outer - inner) * 0.55
  const x = Number(cx) + radius * Math.cos(-angle * RADIAN)
  const y = Number(cy) + radius * Math.sin(-angle * RADIAN)
  const p = percent ?? 0
  const baseFont = p < 0.07 ? 9 : p < 0.12 ? 10 : 11
  const fontSize = Math.max(5, Math.round(baseFont * scale))
  const strokeWidth = Math.max(0.6, 2 * scale)

  return (
    <text
      x={x}
      y={y}
      fill={slice.fg ?? "#ffffff"}
      textAnchor="middle"
      dominantBaseline="central"
      fontSize={fontSize}
      fontWeight={700}
      style={{
        pointerEvents: "none",
        paintOrder: "stroke fill",
        stroke: "rgba(0,0,0,0.25)",
        strokeWidth,
        strokeLinejoin: "round",
      }}
    >
      {slice.key}
    </text>
  )
}
