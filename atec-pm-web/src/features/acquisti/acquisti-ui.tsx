// ── Pezzi di UI dell'Inbox Acquisti: badge ordine, KPI, filtro stato ──────

import * as React from "react"
import { ChevronDown, Search } from "lucide-react"
import type { LucideIcon } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import type { AcquistiInboxItem, DdpStatusItem } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { buildStatusCounts } from "./acquisti-shared"

/** Badge «Ordine Danea»: link che apre il documento se c'è l'IDDoc; senza IDDoc
 *  ma con un Rif. Danea scritto a mano apre la ricerca per numero (che in
 *  migrazione pesca anche dal VECCHIO archivio); altrimenti testo. */
export function DaneaOrderBadge({
  label,
  idDoc,
  daneaRef = null,
  icon: Icon,
  className,
  iconClassName = "size-4",
  onOpen,
  onOpenByRef,
}: {
  label: string
  idDoc: number | null
  daneaRef?: string | null
  icon: LucideIcon
  className?: string
  iconClassName?: string
  onOpen: (idDoc: number) => void
  onOpenByRef?: (rif: string) => void
}) {
  const inner = (
    <>
      <Icon className={iconClassName} />
      {label}
    </>
  )
  if (idDoc != null) {
    return (
      <button
        type="button"
        className={className}
        title="Apri ordine Danea"
        onClick={() => onOpen(idDoc)}
      >
        {inner}
      </button>
    )
  }
  if (daneaRef?.trim() && onOpenByRef) {
    const rif = daneaRef.trim()
    return (
      <button
        type="button"
        className={className}
        title={`Cerca l'ordine n. ${rif} in Danea (anche nel vecchio archivio)`}
        onClick={() => onOpenByRef(rif)}
      >
        {inner}
      </button>
    )
  }
  return <span className={className}>{inner}</span>
}

/** Card di riepilogo in testa alla pagina. Le classi colore arrivano complete
 *  (mai interpolate) altrimenti Tailwind non le include nel bundle.
 *  Con `onClick` la card diventa un filtro: click = applica, ri-click = toglie
 *  (`active` accende l'anello e lo dice anche nel title). */
export function KpiCard({
  label,
  value,
  unit,
  icon: Icon,
  borderClassName,
  iconClassName,
  onClick,
  active = false,
  children,
}: {
  label: string
  value: React.ReactNode
  unit?: React.ReactNode
  icon: LucideIcon
  borderClassName: string
  iconClassName: string
  onClick?: () => void
  active?: boolean
  children: React.ReactNode
}) {
  return (
    <Card
      className={cn(
        "border-l-4 shadow-sm",
        borderClassName,
        onClick && "cursor-pointer transition-shadow hover:shadow-md",
        active && "ring-2 ring-primary/60"
      )}
      role={onClick ? "button" : undefined}
      aria-pressed={onClick ? active : undefined}
      tabIndex={onClick ? 0 : undefined}
      title={
        onClick
          ? active
            ? "Filtro attivo: clicca di nuovo per mostrare tutto"
            : "Clicca per vedere solo queste righe nelle griglie"
          : undefined
      }
      onClick={onClick}
      onKeyDown={
        onClick
          ? (e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault()
                onClick()
              }
            }
          : undefined
      }
    >
      <CardHeader className="p-4 pb-2">
        <CardDescription className="text-xs font-semibold uppercase">{label}</CardDescription>
        <CardTitle className="text-2xl font-bold flex items-center justify-between">
          <span>
            {value}
            {unit ? (
              <span className="text-xs font-normal text-muted-foreground"> {unit}</span>
            ) : null}
          </span>
          <Icon className={cn("h-5 w-5", iconClassName)} />
        </CardTitle>
      </CardHeader>
      <CardContent className="p-4 pt-0">
        <div className="text-xs text-muted-foreground">{children}</div>
      </CardContent>
    </Card>
  )
}

/** Combo con ricerca integrata per il filtro di colonna Stato (solo stati presenti nella grid). */
export function StatusFilterCombobox({
  value,
  onChange,
  gridItems,
  statusMap,
}: {
  value: string
  onChange: (val: string | undefined) => void
  gridItems: AcquistiInboxItem[]
  statusMap: Map<string, DdpStatusItem>
}) {
  const [open, setOpen] = React.useState(false)
  const [search, setSearch] = React.useState("")

  // Calcola SOLO gli stati attualmente presenti nelle righe di questa grid
  const presentStatuses = React.useMemo(
    () => buildStatusCounts(gridItems, statusMap),
    [gridItems, statusMap]
  )

  const filteredStatuses = React.useMemo(() => {
    if (!search.trim()) return presentStatuses
    const q = search.toLowerCase()
    return presentStatuses.filter(
      (s) =>
        s.key.toLowerCase().includes(q) || (s.label && s.label.toLowerCase().includes(q))
    )
  }, [presentStatuses, search])

  const selectedLabel = React.useMemo(() => {
    if (!value) return "Tutti gli stati"
    const matched = presentStatuses.find((s) => s.label === value || s.key === value)
    return matched ? matched.label : value
  }, [value, presentStatuses])

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          role="combobox"
          aria-expanded={open}
          className="h-8 w-full justify-between px-2 text-xs font-normal bg-background border-input"
        >
          <span className="truncate">{selectedLabel}</span>
          <ChevronDown className="ml-1 h-3.5 w-3.5 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-56 p-2" align="start">
        <div className="space-y-2">
          <div className="relative">
            <Search className="absolute left-2 top-2 h-3.5 w-3.5 text-muted-foreground" />
            <Input
              placeholder="Cerca stato..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="h-7 pl-7 text-xs"
            />
          </div>
          <div className="max-h-48 overflow-y-auto space-y-0.5 text-xs">
            <button
              type="button"
              className={cn(
                "w-full text-left px-2 py-1.5 rounded flex items-center justify-between hover:bg-muted/70 transition-colors",
                !value && "bg-muted font-bold"
              )}
              onClick={() => {
                onChange(undefined)
                setOpen(false)
              }}
            >
              <span>Tutti gli stati</span>
              <span className="font-mono text-[10px] text-muted-foreground">
                ({gridItems.length})
              </span>
            </button>
            {filteredStatuses.map((st) => {
              const isSelected = value === st.label || value === st.key
              return (
                <button
                  key={st.key}
                  type="button"
                  className={cn(
                    "w-full text-left px-2 py-1.5 rounded flex items-center justify-between hover:bg-muted/70 transition-colors",
                    isSelected && "bg-muted font-bold"
                  )}
                  onClick={() => {
                    onChange(st.label || st.key)
                    setOpen(false)
                  }}
                >
                  <div className="flex items-center gap-1.5 truncate pr-2">
                    {st.colorBg ? (
                      <span
                        className="h-2.5 w-2.5 rounded-full shrink-0 border"
                        style={{ backgroundColor: st.colorBg }}
                      />
                    ) : null}
                    <span className="truncate">{st.label}</span>
                  </div>
                  <span className="font-mono text-[10px] text-muted-foreground">
                    ({st.count})
                  </span>
                </button>
              )
            })}
          </div>
        </div>
      </PopoverContent>
    </Popover>
  )
}
