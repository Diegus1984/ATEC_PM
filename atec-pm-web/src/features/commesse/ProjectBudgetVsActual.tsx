import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { ChevronRight, Pencil, RefreshCw } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import {
  Card,
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
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { SectionPhases } from "@/features/commesse/SectionPhases"
import { ApiError } from "@/lib/api/client"
import {
  fetchBudgetVsActual,
  updateActualTravelCost,
  updateProjectRevenue,
} from "@/lib/api/project-bva"
import { fetchProjectPhases } from "@/lib/api/phases"
import type {
  BvaGroupDto,
  BvaSectionDto,
  BvaEconomicSummary,
  PhaseListItem,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"

function hours(value: number): string {
  return `${value.toFixed(1)} h`
}

function pct(value: number): string {
  return `${value.toFixed(1)}%`
}

/** Rosso se il delta sfora (consuntivo > preventivo), verde se sotto. */
function deltaClass(delta: number): string {
  if (delta > 0.05) return "text-destructive"
  if (delta < -0.05) return "text-emerald-600"
  return "text-muted-foreground"
}

function deltaText(delta: number): string {
  const sign = delta > 0 ? "+" : ""
  return `Δ ${sign}${delta.toFixed(1)} h`
}

function Kpi({
  label,
  value,
  hint,
  accent,
  onEdit,
}: {
  label: string
  value: string
  hint?: string
  accent?: string
  onEdit?: () => void
}) {
  return (
    <Card size="sm">
      <CardHeader>
        <div className="flex items-center justify-between gap-2">
          <CardDescription>{label}</CardDescription>
          {onEdit ? (
            <Button
              variant="ghost"
              size="icon-sm"
              onClick={onEdit}
              aria-label={`Modifica ${label}`}
            >
              <Pencil />
            </Button>
          ) : null}
        </div>
        <CardTitle className={cn("text-xl tabular-nums", accent)}>
          {value}
        </CardTitle>
        {hint ? (
          <CardDescription className="text-xs">{hint}</CardDescription>
        ) : null}
      </CardHeader>
    </Card>
  )
}

/** Dialog di modifica di un singolo valore economico (order price / trasferta). */
function EconomicEditDialog({
  open,
  title,
  label,
  initialValue,
  onClose,
  onSave,
}: {
  open: boolean
  title: string
  label: string
  initialValue: number
  onClose: () => void
  onSave: (value: number) => Promise<void>
}) {
  const [text, setText] = React.useState(String(initialValue))
  const [error, setError] = React.useState<string | null>(null)
  const [saving, setSaving] = React.useState(false)
  const wasOpen = React.useRef(false)

  React.useEffect(() => {
    // Reinizializza SOLO sul fronte di salita di `open`: un refetch in background
    // (initialValue cambia mentre il dialog è aperto) non deve cancellare ciò che
    // l'utente sta digitando.
    if (open && !wasOpen.current) {
      setText(String(initialValue))
      setError(null)
      setSaving(false)
    }
    wasOpen.current = open
  }, [open, initialValue])

  async function handleSave() {
    if (saving) return
    const parsed = Number(text.replace(",", "."))
    if (!Number.isFinite(parsed) || parsed < 0) {
      setError("Inserisci un importo valido (≥ 0).")
      return
    }
    setSaving(true)
    try {
      await onSave(parsed)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>
        <div className="grid gap-2">
          <Label>{label}</Label>
          <Input
            inputMode="decimal"
            value={text}
            autoFocus
            disabled={saving}
            onChange={(event) => setText(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !saving) void handleSave()
            }}
          />
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button onClick={() => void handleSave()} disabled={saving}>
            {saving ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** Blocco Prev / Assegn / Cons (ore + €) con delta colorato. */
function ThreeColumn({
  budgetHours,
  budgetCost,
  assignedHours,
  assignedCost,
  actualHours,
  actualCost,
  deltaHours,
  travel,
}: {
  budgetHours: number
  budgetCost: number
  assignedHours: number
  assignedCost: number
  actualHours: number
  actualCost: number
  deltaHours: number
  travel?: number
}) {
  return (
    <div className="flex flex-wrap items-center gap-x-5 gap-y-1 text-xs tabular-nums">
      <span className="text-muted-foreground">
        Prev:{" "}
        <span className="font-medium text-foreground">{hours(budgetHours)}</span>{" "}
        · {euro(budgetCost)}
      </span>
      {travel != null && travel > 0 ? (
        <span className="text-muted-foreground">+ Trasferta {euro(travel)}</span>
      ) : null}
      <span className="text-muted-foreground">
        Assegn:{" "}
        <span className="font-medium text-foreground">
          {hours(assignedHours)}
        </span>{" "}
        · {euro(assignedCost)}
      </span>
      <span className="text-muted-foreground">
        Cons:{" "}
        <span className="font-medium text-foreground">{hours(actualHours)}</span>{" "}
        · {euro(actualCost)}
      </span>
      {Math.abs(deltaHours) > 0.05 ? (
        <span className={cn("font-medium", deltaClass(deltaHours))}>
          {deltaText(deltaHours)}
        </span>
      ) : null}
    </div>
  )
}

function SectionBlock({
  section,
  projectId,
  phasesByTemplate,
  existingTemplateIds,
  onPhasesChanged,
}: {
  section: BvaSectionDto
  projectId: number
  phasesByTemplate: Map<number, PhaseListItem[]>
  existingTemplateIds: Set<number>
  onPhasesChanged: () => void
}) {
  const isClient = section.sectionType === "DA_CLIENTE"
  const hasTravel = section.budgetTotalTravelCost > 0
  const sectionPhases =
    section.templateId != null
      ? phasesByTemplate.get(section.templateId) ?? []
      : []
  return (
    <div className="rounded-lg border">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-muted/40 px-3 py-2">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium">{section.sectionName}</span>
          <Badge
            variant="outline"
            className={
              isClient
                ? "border-amber-500/40 text-amber-600"
                : "border-emerald-500/40 text-emerald-600"
            }
          >
            {isClient ? "CLIENTE" : "SEDE"}
          </Badge>
        </div>
        <ThreeColumn
          budgetHours={section.budgetHours}
          budgetCost={section.budgetCost}
          assignedHours={section.assignedHours}
          assignedCost={section.assignedCost}
          actualHours={section.actualHours}
          actualCost={section.actualCost}
          deltaHours={section.deltaHours}
          travel={section.budgetTotalTravelCost}
        />
      </div>

      <div className="grid gap-4 p-3 lg:grid-cols-2">
        <div className="space-y-3">
          <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
            Risorse pianificate
          </p>
          {/* Preventivo: risorse pianificate */}
        {section.budgetResources.length > 0 ? (
          <div className="overflow-x-auto rounded-md border">
            <Table>
              <TableHeader className="bg-muted/30">
                <TableRow className="hover:bg-transparent">
                  <TableHead className="text-xs">Risorsa (preventivo)</TableHead>
                  <TableHead className="text-right text-xs">GG</TableHead>
                  <TableHead className="text-right text-xs">Ore/g</TableHead>
                  <TableHead className="text-right text-xs">Ore</TableHead>
                  <TableHead className="text-right text-xs">€/h</TableHead>
                  <TableHead className="text-right text-xs">Costo</TableHead>
                  <TableHead className="text-right text-xs">K</TableHead>
                  <TableHead className="text-right text-xs">Vendita</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {section.budgetResources.map((r, index) => (
                  <TableRow key={`${r.resourceName}-${index}`}>
                    <TableCell className="font-medium">{r.resourceName}</TableCell>
                    <TableCell className="text-right tabular-nums">
                      {r.workDays}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {r.hoursPerDay}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {r.totalHours.toFixed(1)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {euro(r.hourlyCost)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {euro(r.totalCost)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {r.markupValue.toFixed(3)}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {euro(r.totalSale)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        ) : (
          <p className="text-xs text-muted-foreground">
            Nessuna risorsa preventivata in questa sezione.
          </p>
        )}

        {/* Trasferta (solo DA_CLIENTE) */}
        {isClient && hasTravel ? (
          <div className="rounded-md border bg-amber-50/40 px-3 py-2 text-xs tabular-nums">
            <span className="font-medium">Trasferta preventivo:</span>{" "}
            viaggi {euro(section.budgetTravelCost)} · vitto/hotel{" "}
            {euro(section.budgetAccommodationCost)} · indennità{" "}
            {euro(section.budgetAllowanceCost)} ={" "}
            <span className="font-medium">
              {euro(section.budgetTotalTravelCost)}
            </span>
          </div>
        ) : null}

        </div>

        {/* DESTRA: fasi assegnate DENTRO la sezione (come il WPF) */}
        <SectionPhases
          projectId={projectId}
          sectionTemplateId={section.templateId}
          phases={sectionPhases}
          existingTemplateIds={existingTemplateIds}
          onChanged={onPhasesChanged}
        />
      </div>
    </div>
  )
}

function GroupBlock({
  group,
  open,
  onToggle,
  projectId,
  phasesByTemplate,
  existingTemplateIds,
  onPhasesChanged,
}: {
  group: BvaGroupDto
  open: boolean
  onToggle: () => void
  projectId: number
  phasesByTemplate: Map<number, PhaseListItem[]>
  existingTemplateIds: Set<number>
  onPhasesChanged: () => void
}) {
  return (
    <div className="rounded-lg border">
      <div
        className="flex items-center gap-2 px-3 py-2"
        style={{ backgroundColor: group.color, color: "#FFFFFF" }}
      >
        <button
          type="button"
          className="flex flex-1 items-center gap-2 text-left"
          onClick={onToggle}
        >
          <ChevronRight
            className={cn(
              "size-4 shrink-0 transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
              open && "rotate-90"
            )}
          />
          <span className="text-sm font-semibold">{group.groupName}</span>
          <span className="text-xs opacity-80">
            ({group.sections.length} sezioni)
          </span>
        </button>
        <div className="hidden text-xs tabular-nums opacity-90 sm:block">
          Prev {hours(group.budgetHours)} · Assegn {hours(group.assignedHours)} ·
          Cons {hours(group.actualHours)}
        </div>
      </div>

      <Collapsible open={open}>
        <div className="space-y-2 p-2">
          {group.sections.map((section, index) => (
            <SectionBlock
              key={`${section.sectionName}-${index}`}
              section={section}
              projectId={projectId}
              phasesByTemplate={phasesByTemplate}
              existingTemplateIds={existingTemplateIds}
              onPhasesChanged={onPhasesChanged}
            />
          ))}
        </div>
      </Collapsible>
    </div>
  )
}

function EconomicSummary({
  economic,
  projectId,
  onSaved,
}: {
  economic: BvaEconomicSummary
  projectId: number
  onSaved: () => void
}) {
  const [editing, setEditing] = React.useState<"order" | "travel" | null>(null)
  const profitAccent =
    economic.profitabilityPct < 0 ? "text-destructive" : "text-emerald-600"
  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Kpi
          label="Prezzo offerta finale"
          value={euro(economic.finalOfferPrice)}
        />
        <Kpi
          label="Order price"
          value={euro(economic.orderPrice)}
          onEdit={() => setEditing("order")}
        />
        <Kpi
          label="Budget costi"
          value={euro(economic.budgetCost)}
          hint={`Risorse ${euro(economic.budgetResourceCost)} · Mat. ${euro(
            economic.budgetMaterialCost
          )} · Trasf. ${euro(economic.budgetTravelCost)}`}
        />
        <Kpi
          label="Consuntivo costi"
          value={euro(economic.actualTotalCost)}
          hint={`Risorse ${euro(economic.actualResourceCost)} · Mat. ${euro(
            economic.actualMaterialCost
          )} · Trasf. ${euro(economic.actualTravelCost)}`}
          onEdit={() => setEditing("travel")}
        />
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Kpi
          label="Redditività"
          value={pct(economic.profitabilityPct)}
          accent={profitAccent}
        />
        <Kpi label="Avanzamento" value={`${economic.progressPct}%`} />
        <Kpi label="Tecnici attivi" value={String(economic.activeTechnicians)} />
        <Kpi
          label="Fasi completate"
          value={`${economic.completedPhases}/${economic.totalPhases}`}
        />
      </div>

      <EconomicEditDialog
        open={editing === "order"}
        title="Order price"
        label="Prezzo d'ordine (€)"
        initialValue={economic.orderPrice}
        onClose={() => setEditing(null)}
        onSave={async (value) => {
          await updateProjectRevenue(projectId, value)
          setEditing(null)
          onSaved()
        }}
      />
      <EconomicEditDialog
        open={editing === "travel"}
        title="Trasferta a consuntivo"
        label="Costo trasferta consuntivo (€)"
        initialValue={economic.actualTravelCost}
        onClose={() => setEditing(null)}
        onSave={async (value) => {
          await updateActualTravelCost(projectId, value)
          setEditing(null)
          onSaved()
        }}
      />
    </div>
  )
}

/** Macro-sezione collassabile con header blu #2563EB (come il WPF BudgetVsActualControl). */
function BlueSection({
  title,
  defaultOpen = true,
  children,
}: {
  title: string
  defaultOpen?: boolean
  children: React.ReactNode
}) {
  const [open, setOpen] = React.useState(defaultOpen)
  return (
    <div className="overflow-hidden rounded-lg border">
      <button
        type="button"
        className="flex w-full items-center gap-2 px-4 py-2.5 text-left text-white"
        style={{ backgroundColor: "#2563EB" }}
        onClick={() => setOpen((v) => !v)}
      >
        <ChevronRight
          className={cn(
            "size-4 shrink-0 transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
            open && "rotate-90"
          )}
        />
        <span className="text-sm font-semibold uppercase tracking-wide">
          {title}
        </span>
      </button>
      <Collapsible open={open}>
        <div className="space-y-3 p-3">{children}</div>
      </Collapsible>
    </div>
  )
}

export function ProjectBudgetVsActual({ projectId }: { projectId: number }) {
  const queryClient = useQueryClient()
  const query = useQuery({
    queryKey: ["project-bva", projectId],
    queryFn: () => fetchBudgetVsActual(projectId),
    enabled: projectId > 0,
  })

  const phasesQuery = useQuery({
    queryKey: ["project-phases", projectId],
    queryFn: () => fetchProjectPhases(projectId),
    enabled: projectId > 0,
  })

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["project-bva", projectId] })
    void queryClient.invalidateQueries({ queryKey: ["project-phases", projectId] })
  }, [queryClient, projectId])

  const phases = React.useMemo(
    () => phasesQuery.data ?? [],
    [phasesQuery.data]
  )
  // Fasi raggruppate per sezione di costo: vanno mostrate DENTRO la sezione.
  // Come il WPF (BvaSectionVM.FromDto): solo le fasi con cost_section_template_id
  // valido (> 0) compaiono; le fasi senza sezione NON si mostrano affatto.
  const phasesByTemplate = React.useMemo(() => {
    const map = new Map<number, PhaseListItem[]>()
    for (const phase of phases) {
      if (phase.costSectionTemplateId == null || phase.costSectionTemplateId <= 0)
        continue
      const list = map.get(phase.costSectionTemplateId) ?? []
      list.push(phase)
      map.set(phase.costSectionTemplateId, list)
    }
    for (const list of map.values()) {
      list.sort((a, b) => a.sortOrder - b.sortOrder)
    }
    return map
  }, [phases])
  const existingTemplateIds = React.useMemo(
    () =>
      new Set(phases.filter((p) => !p.isLocal).map((p) => p.phaseTemplateId)),
    [phases]
  )

  // Stato apertura gruppi interni (chiave = groupName), così non migra sul
  // gruppo sbagliato quando un refetch ricarica i dati.
  const [collapsedGroups, setCollapsedGroups] = React.useState<Set<string>>(
    new Set()
  )
  const toggleGroup = React.useCallback((name: string) => {
    setCollapsedGroups((prev) => {
      const next = new Set(prev)
      if (next.has(name)) next.delete(name)
      else next.add(name)
      return next
    })
  }, [])

  const data = query.data

  if (query.isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    )
  }
  if (query.isError) {
    return (
      <p className="text-sm text-destructive">
        {query.error instanceof ApiError && query.error.status === 403
          ? "Dati economici riservati ai ruoli PM/ADMIN."
          : (query.error as Error).message ||
            "Errore nel caricamento del confronto."}
      </p>
    )
  }
  if (!data) return null

  return (
    <div className="space-y-3">
      <div className="flex justify-end">
        <Button
          variant="outline"
          size="sm"
          onClick={() => query.refetch()}
          disabled={query.isFetching}
        >
          <RefreshCw className={query.isFetching ? "animate-spin" : ""} />
          Aggiorna
        </Button>
      </div>

      {/* 1. IMPEGNO RISORSE */}
      <BlueSection title="Impegno Risorse">
        <div className="rounded-lg border bg-muted/30 px-4 py-3">
          <ThreeColumn
            budgetHours={data.totalBudgetHours}
            budgetCost={data.totalBudgetCost}
            assignedHours={data.totalAssignedHours}
            assignedCost={data.totalAssignedCost}
            actualHours={data.totalActualHours}
            actualCost={data.totalActualCost}
            deltaHours={data.totalActualHours - data.totalBudgetHours}
          />
        </div>

        {data.groups.length > 0 ? (
          data.groups.map((group, index) => (
            <GroupBlock
              key={`${group.groupName}-${index}`}
              group={group}
              open={!collapsedGroups.has(group.groupName)}
              onToggle={() => toggleGroup(group.groupName)}
              projectId={projectId}
              phasesByTemplate={phasesByTemplate}
              existingTemplateIds={existingTemplateIds}
              onPhasesChanged={invalidate}
            />
          ))
        ) : (
          <Empty className="p-6">
            <EmptyHeader>
              <EmptyTitle>Nessuna sezione di costo</EmptyTitle>
              <EmptyDescription>
                La commessa non deriva da un preventivo con costing.
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        )}

      </BlueSection>

      {/* 2. MATERIALI */}
      {data.materialSections.length > 0 ? (
        <BlueSection title="Materiali">
          {data.materialSections.map((ms, msIndex) => (
            <div
              key={`${ms.sectionName}-${msIndex}`}
              className="overflow-x-auto rounded-lg border"
            >
              <Table>
                <TableHeader className="bg-muted/40">
                  <TableRow className="hover:bg-transparent">
                    <TableHead className="text-xs">
                      {ms.sectionName || "Materiali"}
                    </TableHead>
                    <TableHead className="text-right text-xs">Q.tà</TableHead>
                    <TableHead className="text-right text-xs">€ unit.</TableHead>
                    <TableHead className="text-right text-xs">K</TableHead>
                    <TableHead className="text-right text-xs">Netto</TableHead>
                    <TableHead className="text-right text-xs">Vendita</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {ms.items.map((item) => (
                    <TableRow key={item.id}>
                      <TableCell>{item.description || "—"}</TableCell>
                      <TableCell className="text-right tabular-nums">
                        {item.quantity}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {euro(item.unitCost)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {item.markupValue.toFixed(3)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {euro(item.netCost)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {euro(item.saleCost)}
                      </TableCell>
                    </TableRow>
                  ))}
                  <TableRow className="bg-muted/30 hover:bg-muted/30">
                    <TableCell className="font-semibold" colSpan={4}>
                      Totale
                    </TableCell>
                    <TableCell className="text-right font-semibold tabular-nums">
                      {euro(ms.totalNetCost)}
                    </TableCell>
                    <TableCell className="text-right font-semibold tabular-nums">
                      {euro(ms.totalSaleCost)}
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>
          ))}
        </BlueSection>
      ) : null}

      {/* 3. CONTO ECONOMICO */}
      {data.economic ? (
        <BlueSection title="Conto Economico">
          <EconomicSummary
            economic={data.economic}
            projectId={projectId}
            onSaved={invalidate}
          />
        </BlueSection>
      ) : null}

      {/* 4. SCHEDA PREZZI */}
      {data.pricing ? (
        <BlueSection title="Scheda Prezzi">
          <div className="text-sm tabular-nums">
            <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-muted-foreground">
              <span>Costo netto {euro(data.pricing.netCost)}</span>
              <span>→ + Contingency {euro(data.pricing.contingencyAmount)}</span>
              <span className="text-foreground">
                = Offerta {euro(data.pricing.offerPrice)}
              </span>
              <span>→ + Margine {euro(data.pricing.negotiationAmount)}</span>
              <span className="font-semibold text-foreground">
                = Finale {euro(data.pricing.finalPrice)}
              </span>
            </div>
          </div>
        </BlueSection>
      ) : null}
    </div>
  )
}
