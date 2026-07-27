import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { PRIORITY_META } from "@/features/checklist/checklist-utils"
import { cn } from "@/lib/utils"

// Select inline della griglia Lavorazioni, allineate al pattern PM (Check list):
// trigger compatto h-8 senza ombra, voci con pallino colorato + etichetta.

const TRIGGER_CLASS =
  "h-8 gap-1.5 border-input bg-white dark:bg-zinc-950 px-2 font-normal shadow-none"

function DotItemLabel({ dot, label, code }: { dot: string; label: string; code?: string }) {
  return (
    <span className="flex items-center gap-2">
      <span className={cn("size-2 shrink-0 rounded-full", dot)} />
      {code ? <span className="font-mono text-xs font-semibold">{code}</span> : null}
      <span className="text-muted-foreground">{label}</span>
    </span>
  )
}

/** Tipo lavorazione: Interna (officina ATEC) o Esterna (fornitore). */
export const WR_TYPE_META = [
  { value: "Internal", label: "Interna", dot: "bg-blue-500" },
  { value: "External", label: "Esterna", dot: "bg-purple-500" },
] as const

const TYPE_NONE = "__none__"

export function WrTypeSelect({
  value,
  onChange,
}: {
  value: string
  onChange: (type: string) => void
}) {
  return (
    <Select
      value={value || TYPE_NONE}
      onValueChange={(v) => onChange(v === TYPE_NONE ? "" : v)}
    >
      <SelectTrigger size="sm" aria-label="Tipo" className={cn(TRIGGER_CLASS, "min-w-[7rem]")}>
        <SelectValue placeholder="Seleziona…" />
      </SelectTrigger>
      <SelectContent align="start">
        <SelectItem value={TYPE_NONE} textValue="Seleziona…">
          <span className="text-muted-foreground">Seleziona…</span>
        </SelectItem>
        {WR_TYPE_META.map((t) => (
          <SelectItem key={t.value} value={t.value} textValue={t.label}>
            <DotItemLabel dot={t.dot} label={t.label} />
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

const PRIORITY_NONE = "__none__"

// Le lavorazioni usano i livelli P0/P1/P2 (stessa semantica e colori della Check list)
const WR_PRIORITY_META = PRIORITY_META.filter((p) => p.value <= 2)

/** Priorità 0/1/2 oppure nessuna. */
export function WrPrioritySelect({
  value,
  onChange,
}: {
  value: number | null
  onChange: (priority: number | null) => void
}) {
  return (
    <Select
      value={value === null ? PRIORITY_NONE : String(value)}
      onValueChange={(v) => onChange(v === PRIORITY_NONE ? null : Number(v))}
    >
      <SelectTrigger
        size="sm"
        aria-label="Priorità"
        className={cn(TRIGGER_CLASS, "min-w-[8.5rem]")}
      >
        <SelectValue placeholder="—" />
      </SelectTrigger>
      <SelectContent align="start">
        <SelectItem value={PRIORITY_NONE} textValue="Nessuna">
          <span className="text-muted-foreground">— Nessuna</span>
        </SelectItem>
        {WR_PRIORITY_META.map((p) => (
          <SelectItem key={p.value} value={String(p.value)} textValue={`${p.code} · ${p.name}`}>
            <DotItemLabel dot={p.dot} code={p.code} label={p.name} />
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/** Stato consegna: In corso / Consegnato. */
const DELIVERED_META = [
  { value: "false", label: "In corso", dot: "bg-blue-500" },
  { value: "true", label: "Consegnato", dot: "bg-emerald-500" },
] as const

export function WrDeliveredSelect({
  value,
  onChange,
}: {
  value: boolean
  onChange: (delivered: boolean) => void
}) {
  return (
    <Select value={String(value)} onValueChange={(v) => onChange(v === "true")}>
      <SelectTrigger size="sm" aria-label="Stato" className={cn(TRIGGER_CLASS, "min-w-[8rem]")}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent align="start">
        {DELIVERED_META.map((s) => (
          <SelectItem key={s.value} value={s.value} textValue={s.label}>
            <DotItemLabel dot={s.dot} label={s.label} />
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

/** Richiesta trattamenti termici/superficiali: Sì / No. */
const TREATMENT_META = [
  { value: "false", label: "No", dot: "bg-muted-foreground/40" },
  { value: "true", label: "Sì", dot: "bg-emerald-500" },
] as const

export function WrTreatmentSelect({
  value,
  onChange,
}: {
  value: boolean
  onChange: (hasTreatment: boolean) => void
}) {
  return (
    <Select value={String(value)} onValueChange={(v) => onChange(v === "true")}>
      <SelectTrigger size="sm" aria-label="Trattamenti" className={cn(TRIGGER_CLASS, "min-w-[5.5rem]")}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent align="start">
        {TREATMENT_META.map((t) => (
          <SelectItem key={t.value} value={t.value} textValue={t.label}>
            <DotItemLabel dot={t.dot} label={t.label} />
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
