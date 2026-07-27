import { useState, useMemo } from "react"
import { useQuery } from "@tanstack/react-query"
import { useNavigate } from "react-router-dom"
import { AlarmClock, RefreshCw, Search, ExternalLink } from "lucide-react"

import { Card, CardHeader, CardTitle, CardDescription, CardContent } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import { Switch } from "@/components/ui/switch"
import { Label } from "@/components/ui/label"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { formatDateWithWeekday } from "@/components/shared/date-field"
import { fetchDeadlines } from "@/lib/api/deadlines"
import { deadlineHref } from "@/features/notifications/notification-navigation"
import type { Deadline } from "@/lib/api/types"
import { cn } from "@/lib/utils"

export default function ScadenzePage() {
  const navigate = useNavigate()
  const [search, setSearch] = useState("")
  const [selectedTypes, setSelectedTypes] = useState<string[]>([
    "SAL",
    "SAL_INCASSO",
    "PROJECT",
    "CHECKLIST",
    "MOM",
    "DDP",
  ])
  const [onlyPending, setOnlyPending] = useState(true)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const {
    data: deadlines = [],
    isLoading,
    error,
    refetch,
    isFetching,
  } = useQuery({
    queryKey: ["deadlines"],
    queryFn: fetchDeadlines,
    refetchInterval: 60000,
    refetchOnWindowFocus: true,
  })

  // Chiave con anche il tipo: SAL e SAL_INCASSO condividono refType/refId
  // (stessa riga sal_rows) e senza `type` le chiavi sarebbero duplicate
  const itemKey = (d: Deadline) => `${d.type}-${d.refType}-${d.refId}`

  const filteredDeadlines = useMemo(() => {
    return deadlines.filter((d) => {
      // Filtro tipo
      const matchesType = selectedTypes.includes(d.type)
      if (!matchesType) return false

      // Filtro "Solo da gestire" (days <= 7)
      if (onlyPending && d.days > 7) return false

      // Filtro ricerca
      if (search.trim()) {
        const q = search.toLowerCase()
        const matchCode = d.code?.toLowerCase().includes(q)
        const matchTitle = d.title?.toLowerCase().includes(q)
        const matchDesc = d.description?.toLowerCase().includes(q)
        if (!matchCode && !matchTitle && !matchDesc) return false
      }

      return true
    })
  }, [deadlines, selectedTypes, onlyPending, search])

  const selectedItem = useMemo(() => {
    if (!selectedId) return null
    return deadlines.find((d) => itemKey(d) === selectedId) || null
  }, [deadlines, selectedId])

  return (
    <div className="flex flex-col h-[calc(100vh-8rem)] lg:flex-row gap-4 overflow-hidden">
      {/* Colonna Sinistra (Lista) */}
      <Card className="flex flex-col flex-1 lg:max-w-[400px] h-full overflow-hidden">
        <CardHeader className="p-4 border-b space-y-4">
          <div className="flex items-center justify-between">
            <div>
              <CardTitle className="text-lg flex items-center gap-2">
                <AlarmClock className="h-5 w-5" />
                Scadenze
              </CardTitle>
              <CardDescription>Gestione scadenze e scostamenti</CardDescription>
            </div>
            <Button
              variant="outline"
              size="icon"
              className="h-8 w-8"
              onClick={() => refetch()}
              disabled={isFetching}
            >
              <RefreshCw className={cn("h-4 w-4", isFetching && "animate-spin")} />
            </Button>
          </div>

          <div className="space-y-2">
            {/* Ricerca */}
            <div className="relative">
              <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
              <Input
                type="search"
                placeholder="Cerca commessa, titolo..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-8 h-9"
              />
            </div>

            {/* Filtri Tipo */}
            <div className="flex flex-wrap gap-1.5 pt-1">
              {(["SAL", "SAL_INCASSO", "PROJECT", "CHECKLIST", "MOM", "DDP"] as const).map((t) => {
                const isSelected = selectedTypes.includes(t)
                return (
                  <Button
                    key={t}
                    variant={isSelected ? "default" : "outline"}
                    size="xs"
                    className="h-7 px-2 text-[11px]"
                    onClick={() => {
                      if (isSelected) {
                        setSelectedTypes(selectedTypes.filter((x) => x !== t))
                      } else {
                        setSelectedTypes([...selectedTypes, t])
                      }
                    }}
                  >
                    {t === "DDP" ? "DDP Articoli" : t === "MOM" ? "MoM Riunioni" : t === "SAL_INCASSO" ? "Incasso SAL" : t}
                  </Button>
                )
              })}
            </div>

            {/* Toggle Solo da Gestire */}
            <div className="flex items-center justify-between pt-1 border-t">
              <Label htmlFor="only-pending" className="text-xs text-muted-foreground cursor-pointer">
                Mostra solo da gestire (≤ 7 giorni)
              </Label>
              <Switch
                id="only-pending"
                checked={onlyPending}
                onCheckedChange={setOnlyPending}
              />
            </div>
          </div>
        </CardHeader>

        <CardContent className="p-0 flex-1 overflow-y-auto">
          {isLoading ? (
            <div className="p-4 space-y-3">
              {[1, 2, 3, 4].map((i) => (
                <div key={i} className="h-20 bg-muted animate-pulse rounded-lg" />
              ))}
            </div>
          ) : error ? (
            <div className="p-4">
              <PageErrorAlert message={error instanceof Error ? error.message : String(error)} />
            </div>
          ) : filteredDeadlines.length === 0 ? (
            <div className="p-8 text-center text-muted-foreground text-sm">
              Nessuna scadenza trovata.
            </div>
          ) : (
            <div className="divide-y">
              {filteredDeadlines.map((d) => {
                const key = itemKey(d)
                const isSelected = selectedId === key

                let badgeVariant: "destructive" | "secondary" | "outline" = "secondary"
                let badgeClass = ""
                let badgeLabel = ""

                if (d.days < 0) {
                  badgeVariant = "destructive"
                  badgeLabel = `Scaduta (${Math.abs(d.days)}g fa)`
                } else if (d.days <= 7) {
                  badgeVariant = "outline"
                  badgeClass = "bg-amber-100 dark:bg-amber-950/40 text-amber-800 dark:text-amber-400 border-amber-200 dark:border-amber-900/30"
                  badgeLabel = d.days === 0 ? "Scade oggi" : `Tra ${d.days} giorni`
                } else {
                  badgeLabel = `Tra ${d.days} giorni`
                }

                return (
                  <div
                    key={key}
                    onClick={() => setSelectedId(key)}
                    className={cn(
                      "p-3 text-left transition-colors cursor-pointer select-none hover:bg-muted/50",
                      isSelected && "bg-muted hover:bg-muted"
                    )}
                  >
                    <div className="flex items-center justify-between gap-2 mb-1">
                      <span className="text-[10px] font-semibold text-muted-foreground tracking-wider uppercase">
                        {d.type === "DDP" ? "DDP Articoli" : d.type === "MOM" ? "MoM" : d.type === "SAL_INCASSO" ? "Incasso SAL" : d.type}
                      </span>
                      <Badge variant={badgeVariant} className={cn("text-[10px] px-1.5 py-0", badgeClass)}>
                        {badgeLabel}
                      </Badge>
                    </div>

                    <div className="font-semibold text-xs text-foreground mb-0.5 truncate">
                      {d.code ? `[${d.code}] ` : ""}{d.title || "Generico"}
                    </div>

                    <div className="text-[11px] text-muted-foreground line-clamp-2 mb-1.5">
                      {d.description}
                    </div>

                    <div className="text-[10px] text-muted-foreground flex items-center justify-between">
                      <span>Scadenza:</span>
                      <span className="font-medium text-foreground">
                        {formatDateWithWeekday(d.dueDate, false)}
                      </span>
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Colonna Destra (Dettaglio) */}
      <Card className="flex-1 h-full flex flex-col overflow-hidden">
        <CardHeader className="border-b bg-muted/20">
          <CardTitle className="text-lg">Dettaglio Scadenza</CardTitle>
          <CardDescription>Informazioni sull'entità e navigazione</CardDescription>
        </CardHeader>
        <CardContent className="flex-1 overflow-y-auto p-6 space-y-6">
          {selectedItem ? (
            <div className="space-y-6">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b pb-4">
                <div>
                  <div className="text-[11px] font-bold text-muted-foreground uppercase tracking-widest mb-1">
                    Tipo Scadenza
                  </div>
                  <div className="text-xl font-semibold text-foreground flex items-center gap-2 flex-wrap">
                    {selectedItem.type === "DDP" ? "DDP Articoli" : selectedItem.type === "MOM" ? "MoM Riunioni" : selectedItem.type === "SAL_INCASSO" ? "Incasso SAL" : selectedItem.type}
                    <Badge
                      variant={selectedItem.days < 0 ? "destructive" : "outline"}
                      className={cn(
                        selectedItem.days >= 0 && selectedItem.days <= 7
                          ? "bg-amber-100 dark:bg-amber-950/40 text-amber-800 dark:text-amber-400 border-amber-200 dark:border-amber-900/30"
                          : selectedItem.days > 7
                          ? "bg-secondary text-secondary-foreground"
                          : ""
                      )}
                    >
                      {selectedItem.days < 0
                        ? `Scaduta da ${Math.abs(selectedItem.days)} giorni`
                        : selectedItem.days === 0
                        ? "Scade oggi"
                        : `Tra ${selectedItem.days} giorni`}
                    </Badge>
                  </div>
                </div>

                {deadlineHref(selectedItem) && (
                  <Button
                    onClick={() => {
                      const href = deadlineHref(selectedItem)
                      if (href) navigate(href)
                    }}
                    className="flex items-center gap-2 self-start sm:self-auto"
                  >
                    <ExternalLink className="h-4 w-4" />
                    Apri Elemento
                  </Button>
                )}
              </div>

              {/* Informazioni Commessa */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div className="bg-muted/30 p-3 rounded-lg border">
                  <div className="text-[10px] font-bold text-muted-foreground uppercase tracking-wider mb-1">
                    Codice Commessa
                  </div>
                  <div className="text-sm font-semibold text-foreground">
                    {selectedItem.code || "—"}
                  </div>
                </div>

                <div className="bg-muted/30 p-3 rounded-lg border">
                  <div className="text-[10px] font-bold text-muted-foreground uppercase tracking-wider mb-1">
                    Titolo Commessa / Contesto
                  </div>
                  <div className="text-sm font-semibold text-foreground truncate">
                    {selectedItem.title || "Generico / Non associata a commessa"}
                  </div>
                </div>
              </div>

              {/* Descrizione ed entità colpevole */}
              <div className="space-y-2">
                <div className="text-[10px] font-bold text-muted-foreground uppercase tracking-wider">
                  Descrizione Elemento / Attività
                </div>
                <div className="text-sm bg-muted/10 p-4 rounded-lg border border-dashed text-foreground whitespace-pre-wrap">
                  {selectedItem.description}
                </div>
              </div>

              {/* Dettagli della Scadenza */}
              <div className="border-t pt-4 space-y-3">
                <div className="flex justify-between text-sm">
                  <span className="text-muted-foreground">Data di Scadenza:</span>
                  <span className="font-semibold text-foreground">
                    {formatDateWithWeekday(selectedItem.dueDate)}
                  </span>
                </div>
                <div className="flex justify-between text-sm">
                  <span className="text-muted-foreground">Stato temporale:</span>
                  <span
                    className={cn(
                      "font-semibold",
                      selectedItem.days < 0
                        ? "text-destructive"
                        : selectedItem.days <= 7
                        ? "text-yellow-600 dark:text-yellow-400"
                        : "text-muted-foreground"
                    )}
                  >
                    {selectedItem.days < 0
                      ? `Ritardo di ${Math.abs(selectedItem.days)} giorni`
                      : selectedItem.days === 0
                      ? "Scade oggi"
                      : `Mancano ${selectedItem.days} giorni`}
                  </span>
                </div>
                <div className="flex justify-between text-sm">
                  <span className="text-muted-foreground">ID Riferimento Interno:</span>
                  <span className="font-mono text-xs text-muted-foreground">
                    {selectedItem.refType} #{selectedItem.refId}
                  </span>
                </div>
              </div>
            </div>
          ) : (
            <div className="h-full flex flex-col items-center justify-center text-muted-foreground py-12">
              <AlarmClock className="h-12 w-12 mb-3 stroke-[1.5] text-muted-foreground/50" />
              <p className="text-sm">Seleziona una scadenza dall'elenco per visualizzare i dettagli.</p>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
