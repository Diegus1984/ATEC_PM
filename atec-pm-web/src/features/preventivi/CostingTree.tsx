import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus, Trash2, Wand2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { notifyError } from "@/lib/toast"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Switch } from "@/components/ui/switch"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { fetchDepartments } from "@/lib/api/departments"
import {
  addCostSection,
  addMaterialItem,
  addMaterialSection,
  addResource,
  deleteCostSection,
  deleteMaterialItem,
  deleteMaterialSection,
  deleteResource,
  fetchAvailableTemplates,
  fetchCosting,
  fetchSectionEmployees,
  saveDistributionsBatch,
  setSectionDepartments,
  toggleMaterialItemActive,
  updateMaterialItem,
  updateMaterialSectionField,
  updatePricing,
  updateResource,
} from "@/lib/api/quote-costing"
import type {
  ProjectCostResourceDto,
  ProjectCostSectionDto,
  ProjectMaterialItemDto,
  ProjectMaterialSectionDto,
} from "@/lib/api/types"

function fmt2(value: number): string {
  return value.toLocaleString("it-IT", { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}
function parseDecimal(value: string): number {
  const n = Number((value ?? "").replace(",", "."))
  return Number.isFinite(n) ? n : 0
}

// ── Editor principale ──────────────────────────────────────

export function CostingTree({ quoteId, readOnly }: { quoteId: number; readOnly: boolean }) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const costingQuery = useQuery({
    queryKey: ["quote-costing", quoteId],
    queryFn: () => fetchCosting(quoteId),
    enabled: quoteId > 0,
  })
  const templatesQuery = useQuery({
    queryKey: ["quote-costing-templates", quoteId],
    queryFn: () => fetchAvailableTemplates(quoteId),
    enabled: quoteId > 0,
  })

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ["quote-costing", quoteId] })
  }

  const [addSectionOpen, setAddSectionOpen] = React.useState(false)
  const [addMaterialOpen, setAddMaterialOpen] = React.useState(false)

  if (costingQuery.isLoading) {
    return <p className="text-sm text-muted-foreground">Caricamento costing…</p>
  }
  if (costingQuery.isError) {
    return (
      <PageErrorAlert message={(costingQuery.error as Error).message} />
    )
  }
  const data = costingQuery.data
  if (!data) return null

  const enabledCost = data.costSections.filter((s) => s.isEnabled)
  const resourceSale = enabledCost.reduce((a, s) => a + s.totalSale, 0)
  const travelSale = enabledCost.reduce((a, s) => a + s.totalTravel, 0)
  const materialSale = data.materialSections.filter((s) => s.isEnabled).reduce((a, s) => a + s.totalSale, 0)
  const net = resourceSale + materialSale + travelSale
  const matItems = data.materialSections.flatMap((s) => s.items ?? [])
  const contPool = net * data.pricing.contingencyPct
  const margPool = (net + contPool) * data.pricing.negotiationMarginPct

  // Raggruppa le sezioni costo per gruppo (header colorato).
  const groups = new Map<string, { color: string; sections: ProjectCostSectionDto[] }>()
  for (const s of [...data.costSections].sort((a, b) => a.sortOrder - b.sortOrder)) {
    const entry = groups.get(s.groupName) ?? { color: s.groupColor, sections: [] }
    entry.sections.push(s)
    groups.set(s.groupName, entry)
  }

  return (
    <div className="space-y-4">
      {/* SEZIONI COSTO */}
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <h3 className="font-bold">Costi persone</h3>
          {!readOnly ? (
            <Button size="sm" variant="outline" onClick={() => setAddSectionOpen(true)}>
              <Plus className="size-4" />
              Aggiungi sezione
            </Button>
          ) : null}
        </div>
        {data.costSections.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nessuna sezione di costo.</p>
        ) : (
          [...groups.entries()].map(([groupName, g]) => (
            <div key={groupName} className="space-y-2">
              <div className="rounded px-2 py-1 text-xs font-bold text-white" style={{ backgroundColor: g.color }}>
                {groupName}
              </div>
              {g.sections.map((section) => (
                <CostSectionCard
                  key={section.id}
                  quoteId={quoteId}
                  section={section}
                  readOnly={readOnly}
                  onChanged={invalidate}
                  confirmDelete={async () => {
                    const ok = await confirm({
                      title: "Elimina sezione",
                      description: `Eliminare la sezione "${section.name}"?`,
                      confirmLabel: "Elimina",
                      destructive: true,
                    })
                    if (ok) {
                      await deleteCostSection(quoteId, section.id)
                      invalidate()
                    }
                  }}
                />
              ))}
            </div>
          ))
        )}
      </div>

      {/* SEZIONI MATERIALI */}
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <h3 className="font-bold">Materiali</h3>
          {!readOnly ? (
            <Button size="sm" variant="outline" onClick={() => setAddMaterialOpen(true)}>
              <Plus className="size-4" />
              Aggiungi sezione materiali
            </Button>
          ) : null}
        </div>
        {data.materialSections.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nessuna sezione materiali.</p>
        ) : (
          [...data.materialSections]
            .sort((a, b) => a.sortOrder - b.sortOrder)
            .map((section) => (
              <MaterialSectionCard
                key={section.id}
                quoteId={quoteId}
                section={section}
                readOnly={readOnly}
                onChanged={invalidate}
                confirmDelete={async () => {
                  const ok = await confirm({
                    title: "Elimina sezione materiali",
                    description: `Eliminare la sezione "${section.name}"?`,
                    confirmLabel: "Elimina",
                    destructive: true,
                  })
                  if (ok) {
                    await deleteMaterialSection(quoteId, section.id)
                    invalidate()
                  }
                }}
              />
            ))
        )}
      </div>

      {/* SCHEDA PREZZI + DISTRIBUZIONE */}
      <PricingPanel
        quoteId={quoteId}
        readOnly={readOnly}
        net={net}
        resourceSale={resourceSale}
        materialSale={materialSale}
        travelSale={travelSale}
        pricing={data.pricing}
        onChanged={invalidate}
      />

      <DistributionPanel
        quoteId={quoteId}
        readOnly={readOnly}
        sections={enabledCost}
        materialItems={matItems}
        contPool={contPool}
        margPool={margPool}
      />

      {/* Dialoghi */}
      <AddCostSectionDialog
        open={addSectionOpen}
        quoteId={quoteId}
        templates={templatesQuery.data?.templates ?? []}
        onClose={() => setAddSectionOpen(false)}
        onAdded={() => {
          setAddSectionOpen(false)
          invalidate()
        }}
      />
      <AddMaterialSectionDialog
        open={addMaterialOpen}
        quoteId={quoteId}
        onClose={() => setAddMaterialOpen(false)}
        onAdded={() => {
          setAddMaterialOpen(false)
          invalidate()
        }}
      />
    </div>
  )
}

// ── Card sezione costo ─────────────────────────────────────

function CostSectionCard({
  quoteId,
  section,
  readOnly,
  onChanged,
  confirmDelete,
}: {
  quoteId: number
  section: ProjectCostSectionDto
  readOnly: boolean
  onChanged: () => void
  confirmDelete: () => void
}) {
  const [addResourceOpen, setAddResourceOpen] = React.useState(false)
  const departmentsQuery = useQuery({ queryKey: ["departments"], queryFn: fetchDepartments })
  const isDaCliente = section.sectionType === "DA_CLIENTE"

  function toggleDept(deptId: number, checked: boolean) {
    const next = checked
      ? [...section.departmentIds, deptId]
      : section.departmentIds.filter((d) => d !== deptId)
    void setSectionDepartments(quoteId, section.id, next).then(onChanged)
  }

  return (
    <div className="rounded-md border">
      <div className="flex flex-wrap items-center gap-2 border-b bg-muted/40 px-3 py-2">
        <span className="font-semibold">{section.name}</span>
        <span
          className="rounded px-1.5 py-0.5 text-[10px] font-bold"
          style={{ backgroundColor: isDaCliente ? "#FEF3C7" : "#DBEAFE", color: isDaCliente ? "#92400E" : "#1D4ED8" }}
        >
          {isDaCliente ? "CLIENTE" : "SEDE"}
        </span>
        {!readOnly ? (
          <Popover>
            <PopoverTrigger asChild>
              <Button variant="outline" size="sm" className="h-7">
                Reparti ({section.departmentIds.length})
              </Button>
            </PopoverTrigger>
            <PopoverContent className="w-56">
              <div className="max-h-64 space-y-1 overflow-y-auto">
                {(departmentsQuery.data ?? []).map((d) => (
                  <label key={d.id} className="flex items-center gap-2 text-sm">
                    <Checkbox
                      checked={section.departmentIds.includes(d.id)}
                      onCheckedChange={(v) => toggleDept(d.id, v === true)}
                    />
                    {d.code} — {d.name}
                  </label>
                ))}
              </div>
            </PopoverContent>
          </Popover>
        ) : null}
        <span className="ml-auto font-bold tabular-nums">{fmt2(section.totalSale)} €</span>
        {!readOnly ? (
          <>
            <Button size="sm" variant="outline" className="h-7" onClick={() => setAddResourceOpen(true)}>
              <Plus className="size-3.5" /> Risorsa
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              className="text-destructive hover:bg-destructive/10"
              title="Elimina sezione"
              onClick={confirmDelete}
            >
              <Trash2 className="size-3.5" />
            </Button>
          </>
        ) : null}
      </div>

      <div>
        <div
          className="grid gap-1 border-b bg-muted/20 px-3 py-1 text-[10px] font-bold text-muted-foreground"
          style={{ gridTemplateColumns: isDaCliente ? "1fr 60px 60px 80px 60px 1fr 90px 36px" : "1fr 60px 60px 80px 60px 90px 36px" }}
        >
          <span>RISORSA</span>
          <span className="text-right">GG</span>
          <span className="text-right">ORE/G</span>
          <span className="text-right">€/H</span>
          <span className="text-center">K</span>
          {isDaCliente ? <span className="text-right">TRASFERTA</span> : null}
          <span className="text-right">VENDITA</span>
          <span />
        </div>
        {section.resources.length === 0 ? (
          <p className="px-3 py-2 text-xs text-muted-foreground">Nessuna risorsa.</p>
        ) : (
          [...section.resources]
            .sort((a, b) => a.sortOrder - b.sortOrder)
            .map((res) => (
              <ResourceRow
                key={res.id}
                quoteId={quoteId}
                resource={res}
                isDaCliente={isDaCliente}
                readOnly={readOnly}
                onChanged={onChanged}
              />
            ))
        )}
      </div>

      <AddResourceDialog
        open={addResourceOpen}
        quoteId={quoteId}
        section={section}
        onClose={() => setAddResourceOpen(false)}
        onAdded={() => {
          setAddResourceOpen(false)
          onChanged()
        }}
      />
    </div>
  )
}

// ── Riga risorsa editabile ─────────────────────────────────

function buildResourceSave(res: ProjectCostResourceDto, patch: Partial<ProjectCostResourceDto>) {
  const m = { ...res, ...patch }
  return {
    id: m.id,
    sectionId: m.sectionId,
    employeeId: m.employeeId,
    resourceName: m.resourceName,
    workDays: m.workDays,
    hoursPerDay: m.hoursPerDay,
    hourlyCost: m.hourlyCost,
    markupValue: m.markupValue,
    numTrips: m.numTrips,
    kmPerTrip: m.kmPerTrip,
    costPerKm: m.costPerKm,
    dailyFood: m.dailyFood,
    dailyHotel: m.dailyHotel,
    allowanceDays: m.allowanceDays,
    dailyAllowance: m.dailyAllowance,
    sortOrder: m.sortOrder,
  }
}

function ResourceRow({
  quoteId,
  resource,
  isDaCliente,
  readOnly,
  onChanged,
}: {
  quoteId: number
  resource: ProjectCostResourceDto
  isDaCliente: boolean
  readOnly: boolean
  onChanged: () => void
}) {
  const [days, setDays] = React.useState(String(resource.workDays))
  const [hpd, setHpd] = React.useState(String(resource.hoursPerDay))
  const [cost, setCost] = React.useState(resource.hourlyCost.toFixed(2))
  const [markup, setMarkup] = React.useState(resource.markupValue.toFixed(3))
  const [trips, setTrips] = React.useState(String(resource.numTrips))
  const [km, setKm] = React.useState(String(resource.kmPerTrip))

  function save(patch: Partial<ProjectCostResourceDto>) {
    if (readOnly) return
    void updateResource(quoteId, resource.id, buildResourceSave(resource, patch))
      .then(onChanged)
      .catch((err: Error) => notifyError(err))
  }

  const cols = isDaCliente ? "1fr 60px 60px 80px 60px 1fr 90px 36px" : "1fr 60px 60px 80px 60px 90px 36px"

  return (
    <div className="grid items-center gap-1 border-b px-3 py-1.5 text-xs last:border-b-0" style={{ gridTemplateColumns: cols }}>
      <span className="truncate font-medium">{resource.resourceName}</span>
      <Input className="h-7 text-right text-xs" value={days} readOnly={readOnly} onChange={(e) => setDays(e.target.value)} onBlur={() => save({ workDays: parseDecimal(days) })} />
      <Input className="h-7 text-right text-xs" value={hpd} readOnly={readOnly} onChange={(e) => setHpd(e.target.value)} onBlur={() => save({ hoursPerDay: parseDecimal(hpd) })} />
      <Input className="h-7 text-right text-xs" value={cost} readOnly={readOnly} onChange={(e) => setCost(e.target.value)} onBlur={() => save({ hourlyCost: parseDecimal(cost) })} />
      <Input className="h-7 text-center text-xs" value={markup} readOnly={readOnly} onChange={(e) => setMarkup(e.target.value)} onBlur={() => save({ markupValue: parseDecimal(markup) })} />
      {isDaCliente ? (
        <div className="flex items-center gap-1">
          <Input className="h-7 w-12 text-right text-xs" value={trips} readOnly={readOnly} title="N. viaggi" onChange={(e) => setTrips(e.target.value)} onBlur={() => save({ numTrips: Math.round(parseDecimal(trips)) })} />
          <span className="text-[10px] text-muted-foreground">×</span>
          <Input className="h-7 w-14 text-right text-xs" value={km} readOnly={readOnly} title="Km/viaggio" onChange={(e) => setKm(e.target.value)} onBlur={() => save({ kmPerTrip: parseDecimal(km) })} />
          <span className="ml-auto text-[10px] tabular-nums text-muted-foreground">{fmt2(resource.travelTotal + resource.accommodationTotal + resource.allowanceTotal)}€</span>
        </div>
      ) : null}
      <span className="text-right font-semibold tabular-nums text-[#059669]">{fmt2(resource.totalSale)}€</span>
      {!readOnly ? (
        <Button
          variant="ghost"
          size="icon-sm"
          className="text-destructive hover:bg-destructive/10"
          title="Elimina risorsa"
          onClick={() => void deleteResource(quoteId, resource.id).then(onChanged)}
        >
          <Trash2 className="size-3.5" />
        </Button>
      ) : (
        <span />
      )}
    </div>
  )
}

// ── Card sezione materiali ─────────────────────────────────

function MaterialSectionCard({
  quoteId,
  section,
  readOnly,
  onChanged,
  confirmDelete,
}: {
  quoteId: number
  section: ProjectMaterialSectionDto
  readOnly: boolean
  onChanged: () => void
  confirmDelete: () => void
}) {
  const [markup, setMarkup] = React.useState(section.markupValue.toFixed(3))

  const parents = section.items.filter((i) => i.parentItemId == null)

  function addLocalItem() {
    void addMaterialItem(quoteId, {
      id: 0,
      sectionId: section.id,
      parentItemId: null,
      productId: null,
      variantId: null,
      code: "",
      description: "Nuovo materiale",
      descriptionRtf: null,
      quantity: 1,
      unitCost: 0,
      markupValue: section.markupValue,
      itemType: "MATERIAL",
      sortOrder: 0,
      isActive: true,
    }).then(onChanged)
  }

  return (
    <div className="rounded-md border">
      <div className="flex flex-wrap items-center gap-2 border-b bg-muted/40 px-3 py-2">
        <span className="font-semibold">{section.name}</span>
        <span className="flex items-center gap-1 text-xs text-muted-foreground">
          K:
          <Input
            className="h-7 w-16 text-center text-xs"
            value={markup}
            readOnly={readOnly}
            onChange={(e) => setMarkup(e.target.value)}
            onBlur={() => void updateMaterialSectionField(quoteId, section.id, "markup_value", String(parseDecimal(markup))).then(onChanged)}
          />
        </span>
        <span className="ml-auto font-bold tabular-nums">{fmt2(section.totalSale)} €</span>
        {!readOnly ? (
          <>
            <Button size="sm" variant="outline" className="h-7" onClick={addLocalItem}>
              <Plus className="size-3.5" /> Materiale
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              className="text-destructive hover:bg-destructive/10"
              title="Elimina sezione"
              onClick={confirmDelete}
            >
              <Trash2 className="size-3.5" />
            </Button>
          </>
        ) : null}
      </div>

      <div>
        <div className="grid grid-cols-[1fr_70px_90px_60px_90px_70px_36px] gap-1 border-b bg-muted/20 px-3 py-1 text-[10px] font-bold text-muted-foreground">
          <span>DESCRIZIONE</span>
          <span className="text-right">QTÀ</span>
          <span className="text-right">COSTO UN.</span>
          <span className="text-center">K</span>
          <span className="text-right">VENDITA</span>
          <span className="text-center">ATT.</span>
          <span />
        </div>
        {parents.length === 0 ? (
          <p className="px-3 py-2 text-xs text-muted-foreground">Nessun materiale.</p>
        ) : (
          parents.map((item) => (
            <MaterialItemRow key={item.id} quoteId={quoteId} item={item} readOnly={readOnly} onChanged={onChanged} />
          ))
        )}
      </div>
    </div>
  )
}

function buildMaterialSave(item: ProjectMaterialItemDto, patch: Partial<ProjectMaterialItemDto>) {
  const m = { ...item, ...patch }
  return {
    id: m.id,
    sectionId: m.sectionId,
    parentItemId: m.parentItemId,
    productId: m.productId,
    variantId: m.variantId,
    code: m.code,
    description: m.description,
    descriptionRtf: m.descriptionRtf,
    quantity: m.quantity,
    unitCost: m.unitCost,
    markupValue: m.markupValue,
    itemType: m.itemType,
    sortOrder: m.sortOrder,
    isActive: m.isActive,
  }
}

function MaterialItemRow({
  quoteId,
  item,
  readOnly,
  onChanged,
}: {
  quoteId: number
  item: ProjectMaterialItemDto
  readOnly: boolean
  onChanged: () => void
}) {
  const [desc, setDesc] = React.useState(item.description)
  const [qty, setQty] = React.useState(String(item.quantity))
  const [cost, setCost] = React.useState(item.unitCost.toFixed(2))
  const [markup, setMarkup] = React.useState(item.markupValue.toFixed(3))

  function save(patch: Partial<ProjectMaterialItemDto>) {
    if (readOnly) return
    void updateMaterialItem(quoteId, item.id, buildMaterialSave(item, patch)).then(onChanged).catch((err: Error) => notifyError(err))
  }

  return (
    <div className="grid grid-cols-[1fr_70px_90px_60px_90px_70px_36px] items-center gap-1 border-b px-3 py-1.5 text-xs last:border-b-0" style={item.isActive ? undefined : { opacity: 0.5 }}>
      <Input className="h-7 border-transparent bg-transparent text-xs focus-visible:border-input" value={desc} readOnly={readOnly} onChange={(e) => setDesc(e.target.value)} onBlur={() => save({ description: desc })} />
      <Input className="h-7 text-right text-xs" value={qty} readOnly={readOnly} onChange={(e) => setQty(e.target.value)} onBlur={() => save({ quantity: parseDecimal(qty) })} />
      <Input className="h-7 text-right text-xs" value={cost} readOnly={readOnly} onChange={(e) => setCost(e.target.value)} onBlur={() => save({ unitCost: parseDecimal(cost) })} />
      <Input className="h-7 text-center text-xs" value={markup} readOnly={readOnly} onChange={(e) => setMarkup(e.target.value)} onBlur={() => save({ markupValue: parseDecimal(markup) })} />
      <span className="text-right font-semibold tabular-nums text-[#059669]">{fmt2(item.totalSale)}€</span>
      <div className="flex justify-center">
        <Switch
          checked={item.isActive}
          disabled={readOnly}
          aria-label="Materiale attivo"
          onCheckedChange={(v) =>
            void toggleMaterialItemActive(quoteId, item.id, v).then(onChanged)
          }
        />
      </div>
      {!readOnly ? (
        <Button variant="ghost" size="icon-sm" className="text-destructive hover:bg-destructive/10" title="Elimina" onClick={() => void deleteMaterialItem(quoteId, item.id).then(onChanged)}>
          <Trash2 className="size-3.5" />
        </Button>
      ) : (
        <span />
      )}
    </div>
  )
}

// ── Scheda prezzi + distribuzione ──────────────────────────

function PricingPanel({
  quoteId,
  readOnly,
  net,
  resourceSale,
  materialSale,
  travelSale,
  pricing,
  onChanged,
}: {
  quoteId: number
  readOnly: boolean
  net: number
  resourceSale: number
  materialSale: number
  travelSale: number
  pricing: import("@/lib/api/types").ProjectPricingDto
  onChanged: () => void
}) {
  const [cont, setCont] = React.useState((pricing.contingencyPct * 100).toFixed(1))
  const [margin, setMargin] = React.useState((pricing.negotiationMarginPct * 100).toFixed(1))

  const contFrac = parseDecimal(cont) / 100
  const marginFrac = parseDecimal(margin) / 100
  const contingencyAmount = net * contFrac
  const offer = net + contingencyAmount
  const marginAmount = offer * marginFrac
  const final = offer + marginAmount

  function savePricing() {
    if (readOnly) return
    void updatePricing(quoteId, {
      ...pricing,
      contingencyPct: contFrac,
      negotiationMarginPct: marginFrac,
    }).then(onChanged)
  }

  const Row = ({ label, value, strong, color }: { label: string; value: string; strong?: boolean; color?: string }) => (
    <div className="flex items-center justify-between gap-6">
      <span className={strong ? "font-bold" : "text-muted-foreground"} style={color ? { color } : undefined}>{label}</span>
      <span className={strong ? "font-bold tabular-nums" : "tabular-nums"} style={color ? { color } : undefined}>{value}</span>
    </div>
  )

  return (
    <div className="rounded-md border p-4">
      <div className="flex flex-wrap items-start justify-between gap-6">
        <div className="space-y-3">
          <h3 className="font-bold">Scheda prezzi</h3>
          <div className="flex items-center gap-2 text-sm">
            <Label className="w-40">Contingency %</Label>
            <Input className="h-8 w-24 text-right" value={cont} readOnly={readOnly} onChange={(e) => setCont(e.target.value)} onBlur={savePricing} />
          </div>
          <div className="flex items-center gap-2 text-sm">
            <Label className="w-40">Margine trattativa %</Label>
            <Input className="h-8 w-24 text-right" value={margin} readOnly={readOnly} onChange={(e) => setMargin(e.target.value)} onBlur={savePricing} />
          </div>
        </div>

        <div className="w-80 space-y-1 text-sm">
          <Row label="Vendita risorse" value={`${fmt2(resourceSale)} €`} />
          <Row label="Vendita materiali" value={`${fmt2(materialSale)} €`} />
          <Row label="Trasferte" value={`${fmt2(travelSale)} €`} />
          <div className="border-t pt-1">
            <Row label="PREZZO NETTO" value={`${fmt2(net)} €`} strong />
          </div>
          <Row label={`Contingency (${cont}%)`} value={`${fmt2(contingencyAmount)} €`} />
          <Row label="PREZZO OFFERTA" value={`${fmt2(offer)} €`} strong color="#2563EB" />
          <Row label={`Margine (${margin}%)`} value={`${fmt2(marginAmount)} €`} />
          <div className="border-t pt-1">
            <Row label="OFFERTA FINALE" value={`${fmt2(final)} €`} strong color="#059669" />
          </div>
        </div>
      </div>
    </div>
  )
}

// ── Distribuzione prezzo ───────────────────────────────────
// Fedele a CostingTreeControl: righe = sezioni costo (R) + righe materiali (M)
// con vendita>0. Pesi contingency/margin proporzionali alla vendita tra le righe
// visibili non-pinnate; pinnate fisse; shadowed a 0 col costo spalmato sulle visibili.

interface DistRow {
  rowType: "R" | "M"
  sectionId: number
  itemId: number
  name: string
  sale: number
  contingencyPct: number
  marginPct: number
  contingencyPinned: boolean
  marginPinned: boolean
  isShadowed: boolean
}

interface DistComputed extends DistRow {
  contingencyAmount: number
  marginAmount: number
  shadowedAmount: number
  sectionTotal: number
}

const distKey = (r: { rowType: string; sectionId: number; itemId: number }) =>
  `${r.rowType}_${r.sectionId}_${r.itemId}`
const round4 = (n: number) => Math.round(n * 10000) / 10000
const round2 = (n: number) => Math.round(n * 100) / 100

/** Ridistribuisce le % (contingency o margin) tra le righe visibili non-pinnate, proporzionali alla vendita. Muta `rows`. */
function rebalanceField(rows: DistRow[], isCont: boolean): void {
  let pinnedSum = 0
  const unpinned: DistRow[] = []
  for (const r of rows) {
    if (r.isShadowed) {
      if (isCont) r.contingencyPct = 0
      else r.marginPct = 0
      continue
    }
    if (r.sale === 0) continue
    const pinned = isCont ? r.contingencyPinned : r.marginPinned
    if (pinned) pinnedSum += isCont ? r.contingencyPct : r.marginPct
    else unpinned.push(r)
  }
  const remaining = Math.max(0, 1 - pinnedSum)
  const totalSale = unpinned.reduce((a, r) => a + r.sale, 0)
  for (const r of unpinned) {
    const v =
      totalSale > 0
        ? round4((r.sale / totalSale) * remaining)
        : round4(remaining / Math.max(1, unpinned.length))
    if (isCont) r.contingencyPct = v
    else r.marginPct = v
  }
}

/** Clona, ribilancia contingency+margin, calcola importi (peso×pool) e spalma le shadowed. */
function computeDist(rows: DistRow[], contPool: number, margPool: number): DistComputed[] {
  const work = rows.map((r) => ({ ...r }))
  rebalanceField(work, true)
  rebalanceField(work, false)
  const out: DistComputed[] = work.map((r) => ({
    ...r,
    contingencyAmount: 0,
    marginAmount: 0,
    shadowedAmount: 0,
    sectionTotal: 0,
  }))
  for (const r of out) {
    if (r.isShadowed) {
      r.contingencyPct = 0
      r.marginPct = 0
    } else {
      r.contingencyAmount = r.contingencyPct * contPool
      r.marginAmount = r.marginPct * margPool
      r.sectionTotal = r.sale + r.contingencyAmount + r.marginAmount
    }
  }
  const shadowedSale = out.filter((r) => r.isShadowed).reduce((a, r) => a + r.sale, 0)
  const visibleSale = out.filter((r) => !r.isShadowed).reduce((a, r) => a + r.sale, 0)
  if (shadowedSale > 0 && visibleSale > 0) {
    for (const r of out) {
      if (r.isShadowed) continue
      const quota = r.sale / visibleSale
      r.shadowedAmount = round2(shadowedSale * quota)
      r.sectionTotal += r.shadowedAmount
    }
  }
  return out
}

function DistributionPanel({
  quoteId,
  readOnly,
  sections,
  materialItems,
  contPool,
  margPool,
}: {
  quoteId: number
  readOnly: boolean
  sections: ProjectCostSectionDto[]
  materialItems: ProjectMaterialItemDto[]
  contPool: number
  margPool: number
}) {
  const [rows, setRows] = React.useState<DistRow[]>([])

  // Firma dei dati sorgente: re-init quando cambiano vendite/pin/shadow.
  const sig = React.useMemo(() => {
    const part = (
      p: string,
      id: number,
      sale: number,
      cp: number,
      mp: number,
      cpin: boolean,
      mpin: boolean,
      sh: boolean
    ) => `${p}${id}:${sale}:${cp}:${mp}:${cpin ? 1 : 0}:${mpin ? 1 : 0}:${sh ? 1 : 0}`
    const secs = sections
      .filter((s) => s.isEnabled && s.totalSale > 0)
      .map((s) => part("R", s.id, s.totalSale, s.contingencyPct, s.marginPct, s.contingencyPinned, s.marginPinned, s.isShadowed))
    const mats = materialItems
      .filter((i) => i.totalSale > 0)
      .map((i) => part("M", i.id, i.totalSale, i.contingencyPct, i.marginPct, i.contingencyPinned, i.marginPinned, i.isShadowed))
    return [...secs, ...mats].join("|")
  }, [sections, materialItems])

  React.useEffect(() => {
    const raw: DistRow[] = []
    const seen = new Set<string>()
    for (const s of sections) {
      if (!s.isEnabled || s.totalSale <= 0) continue
      const nameKey = (s.name || "").trim().toLowerCase()
      if (seen.has(nameKey)) continue
      seen.add(nameKey)
      raw.push({
        rowType: "R", sectionId: s.id, itemId: 0, name: s.name, sale: s.totalSale,
        contingencyPct: s.contingencyPct, marginPct: s.marginPct,
        contingencyPinned: s.contingencyPinned, marginPinned: s.marginPinned, isShadowed: s.isShadowed,
      })
    }
    for (const i of materialItems) {
      if (i.totalSale <= 0) continue
      raw.push({
        rowType: "M", sectionId: 0, itemId: i.id, name: i.description, sale: i.totalSale,
        contingencyPct: i.contingencyPct, marginPct: i.marginPct,
        contingencyPinned: i.contingencyPinned, marginPinned: i.marginPinned, isShadowed: i.isShadowed,
      })
    }
    setRows(raw)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sig])

  const computed = React.useMemo(
    () => computeDist(rows, contPool, margPool),
    [rows, contPool, margPool]
  )

  function persist(next: DistRow[]) {
    setRows(next)
    const c = computeDist(next, contPool, margPool)
    const toReq = (r: DistComputed, id: number) => ({
      id,
      contingencyPct: r.contingencyPct,
      marginPct: r.marginPct,
      contingencyPinned: r.contingencyPinned,
      marginPinned: r.marginPinned,
      isShadowed: r.isShadowed,
    })
    void saveDistributionsBatch(quoteId, {
      sections: c.filter((r) => r.rowType === "R").map((r) => toReq(r, r.sectionId)),
      materialItems: c.filter((r) => r.rowType === "M").map((r) => toReq(r, r.itemId)),
    }).catch((err: Error) => notifyError(err))
  }

  function editPct(row: DistComputed, isCont: boolean, percentStr: string) {
    if (readOnly) return
    let val = parseDecimal(percentStr) / 100
    const otherPinned = rows
      .filter((r) => distKey(r) !== distKey(row) && (isCont ? r.contingencyPinned : r.marginPinned))
      .reduce((a, r) => a + (isCont ? r.contingencyPct : r.marginPct), 0)
    val = Math.min(val, Math.max(0, 1 - otherPinned))
    const next = rows.map((r) => {
      if (distKey(r) !== distKey(row)) return r
      return isCont
        ? { ...r, contingencyPct: val, contingencyPinned: true }
        : { ...r, marginPct: val, marginPinned: true }
    })
    persist(next)
  }

  function toggleShadow(row: DistComputed) {
    if (readOnly) return
    persist(rows.map((r) => (distKey(r) === distKey(row) ? { ...r, isShadowed: !r.isShadowed } : r)))
  }

  function redistribute() {
    if (readOnly) return
    persist(rows.map((r) => ({ ...r, contingencyPinned: false, marginPinned: false })))
  }

  if (computed.length === 0) return null

  const totalCont = computed.reduce((a, r) => a + r.contingencyAmount, 0)
  const totalMarg = computed.reduce((a, r) => a + r.marginAmount, 0)
  const totalClient = computed.reduce((a, r) => a + r.sectionTotal, 0)

  const PctCell = ({ row, isCont }: { row: DistComputed; isCont: boolean }) => {
    const pct = isCont ? row.contingencyPct : row.marginPct
    const pinned = isCont ? row.contingencyPinned : row.marginPinned
    if (row.isShadowed) return <span>—</span>
    return (
      <span className="inline-flex items-center justify-end gap-1">
        {pinned ? <span title="Bloccato">🔒</span> : null}
        <input
          key={`${isCont ? "c" : "m"}${distKey(row)}:${pct}`}
          className="h-6 w-14 rounded border bg-background px-1 text-right"
          defaultValue={(pct * 100).toFixed(2)}
          readOnly={readOnly}
          onBlur={(e) => editPct(row, isCont, e.target.value)}
        />
      </span>
    )
  }

  return (
    <div className="rounded-md border">
      <div className="flex items-center justify-between border-b bg-muted/40 px-3 py-2">
        <h3 className="font-bold">Distribuzione prezzo</h3>
        {!readOnly ? (
          <Button size="sm" variant="outline" onClick={redistribute}>
            <Wand2 className="size-4" /> Ridistribuisci
          </Button>
        ) : null}
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead>
            <tr className="border-b bg-muted/20 text-[10px] font-bold text-muted-foreground">
              <th className="px-2 py-1 text-left">SEZIONE</th>
              <th className="px-2 py-1 text-right">VENDITA</th>
              <th className="px-2 py-1 text-right">CONT. %</th>
              <th className="px-2 py-1 text-right">CONT. €</th>
              <th className="px-2 py-1 text-right">MARG. %</th>
              <th className="px-2 py-1 text-right">MARG. €</th>
              <th className="px-2 py-1 text-right">PREZZO CL.</th>
              <th className="w-8" />
            </tr>
          </thead>
          <tbody>
            {computed.map((r) => (
              <tr
                key={distKey(r)}
                className="border-b last:border-b-0"
                style={r.isShadowed ? { backgroundColor: "#FEF2F2", opacity: 0.6 } : undefined}
              >
                <td className="px-2 py-1">
                  <span
                    className="mr-1 rounded px-1 py-0.5 text-[9px] font-bold text-white"
                    style={{ backgroundColor: r.rowType === "M" ? "#7C3AED" : "#2563EB" }}
                  >
                    {r.rowType}
                  </span>
                  {r.name}
                </td>
                <td className="px-2 py-1 text-right tabular-nums">{r.isShadowed ? "—" : `${fmt2(r.sale)}€`}</td>
                <td className="px-2 py-1 text-right"><PctCell row={r} isCont /></td>
                <td className="px-2 py-1 text-right tabular-nums">{r.isShadowed ? "—" : `${fmt2(r.contingencyAmount)}€`}</td>
                <td className="px-2 py-1 text-right"><PctCell row={r} isCont={false} /></td>
                <td className="px-2 py-1 text-right tabular-nums">{r.isShadowed ? "—" : `${fmt2(r.marginAmount)}€`}</td>
                <td className="px-2 py-1 text-right font-semibold tabular-nums">{r.isShadowed ? "—" : `${fmt2(r.sectionTotal)}€`}</td>
                <td className="px-1 py-1 text-center">
                  {!readOnly ? (
                    <button type="button" title={r.isShadowed ? "Includi" : "Escludi"} onClick={() => toggleShadow(r)}>
                      {r.isShadowed ? "🙈" : "👁"}
                    </button>
                  ) : null}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="border-t font-bold">
              <td className="px-2 py-1">TOTALE</td>
              <td />
              <td />
              <td className="px-2 py-1 text-right tabular-nums">{fmt2(totalCont)}€</td>
              <td />
              <td className="px-2 py-1 text-right tabular-nums">{fmt2(totalMarg)}€</td>
              <td className="px-2 py-1 text-right tabular-nums text-[#059669]">{fmt2(totalClient)}€</td>
              <td />
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  )
}

// ── Dialoghi aggiunta ──────────────────────────────────────

function AddCostSectionDialog({
  open,
  quoteId,
  templates,
  onClose,
  onAdded,
}: {
  open: boolean
  quoteId: number
  templates: import("@/lib/api/types").CostSectionTemplateDto[]
  onClose: () => void
  onAdded: () => void
}) {
  const [templateId, setTemplateId] = React.useState("")
  React.useEffect(() => {
    if (open) setTemplateId("")
  }, [open])

  const addMutation = useMutation({
    mutationFn: () => {
      const tpl = templates.find((t) => String(t.id) === templateId)
      if (!tpl) throw new Error("Seleziona un template.")
      return addCostSection(quoteId, {
        templateId: tpl.id,
        name: tpl.name,
        sectionType: tpl.sectionType,
        groupName: tpl.groupName,
        sortOrder: tpl.sortOrder,
        isEnabled: true,
      })
    },
    onSuccess: onAdded,
    onError: (err: Error) => notifyError(err),
  })

  return (
    <Dialog open={open} onOpenChange={(n) => !n && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Aggiungi sezione di costo</DialogTitle>
        </DialogHeader>
        <div className="grid gap-2">
          <Label>Template sezione</Label>
          <Select value={templateId} onValueChange={setTemplateId}>
            <SelectTrigger className="w-full">
              <SelectValue placeholder="Seleziona un template" />
            </SelectTrigger>
            <SelectContent>
              {templates.map((t) => (
                <SelectItem key={t.id} value={String(t.id)}>
                  {t.groupName} — {t.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Annulla</Button>
          <Button disabled={!templateId || addMutation.isPending} onClick={() => addMutation.mutate()}>Aggiungi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function AddMaterialSectionDialog({
  open,
  quoteId,
  onClose,
  onAdded,
}: {
  open: boolean
  quoteId: number
  onClose: () => void
  onAdded: () => void
}) {
  const [name, setName] = React.useState("")
  const [markup, setMarkup] = React.useState("1.300")
  React.useEffect(() => {
    if (open) {
      setName("")
      setMarkup("1.300")
    }
  }, [open])

  const addMutation = useMutation({
    mutationFn: () => {
      if (!name.trim()) throw new Error("Inserisci un nome.")
      return addMaterialSection(quoteId, {
        categoryId: null,
        name: name.trim(),
        markupValue: parseDecimal(markup) || 1.3,
        commissionMarkup: 1.1,
        sortOrder: 0,
        isEnabled: true,
      })
    },
    onSuccess: onAdded,
    onError: (err: Error) => notifyError(err),
  })

  return (
    <Dialog open={open} onOpenChange={(n) => !n && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Aggiungi sezione materiali</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div className="grid gap-2">
            <Label>Nome *</Label>
            <Input value={name} autoFocus onChange={(e) => setName(e.target.value)} />
          </div>
          <div className="grid gap-2">
            <Label>Markup K</Label>
            <Input className="w-28" value={markup} onChange={(e) => setMarkup(e.target.value)} />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Annulla</Button>
          <Button disabled={!name.trim() || addMutation.isPending} onClick={() => addMutation.mutate()}>Aggiungi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function AddResourceDialog({
  open,
  quoteId,
  section,
  onClose,
  onAdded,
}: {
  open: boolean
  quoteId: number
  section: ProjectCostSectionDto
  onClose: () => void
  onAdded: () => void
}) {
  const [mode, setMode] = React.useState<"employee" | "generic">("employee")
  const [employeeId, setEmployeeId] = React.useState("")
  const [name, setName] = React.useState("")
  const [hourlyCost, setHourlyCost] = React.useState("0")
  const [markup, setMarkup] = React.useState("1.450")

  const employeesQuery = useQuery({
    queryKey: ["section-employees", quoteId, section.id],
    queryFn: () => fetchSectionEmployees(quoteId, section.id),
    enabled: open,
  })

  React.useEffect(() => {
    if (open) {
      setMode("employee")
      setEmployeeId("")
      setName("")
      setHourlyCost("0")
      setMarkup("1.450")
    }
  }, [open])

  const employees = employeesQuery.data ?? []

  const addMutation = useMutation({
    mutationFn: () => {
      let resName = name.trim()
      let empId: number | null = null
      let cost = parseDecimal(hourlyCost)
      let mk = parseDecimal(markup)
      if (mode === "employee") {
        const emp = employees.find((e) => String(e.id) === employeeId)
        if (!emp) throw new Error("Seleziona un dipendente.")
        resName = emp.fullName
        empId = emp.id
        cost = emp.hourlyCost
        mk = emp.defaultMarkup
      } else if (!resName) {
        throw new Error("Inserisci un nome risorsa.")
      }
      return addResource(quoteId, {
        id: 0,
        sectionId: section.id,
        employeeId: empId,
        resourceName: resName,
        workDays: 0,
        hoursPerDay: 8,
        hourlyCost: cost,
        markupValue: mk,
        numTrips: 0,
        kmPerTrip: 0,
        costPerKm: 0.9,
        dailyFood: 0,
        dailyHotel: 0,
        allowanceDays: 0,
        dailyAllowance: 0,
        sortOrder: section.resources.length,
      })
    },
    onSuccess: onAdded,
    onError: (err: Error) => notifyError(err),
  })

  return (
    <Dialog open={open} onOpenChange={(n) => !n && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Aggiungi risorsa a «{section.name}»</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div className="flex gap-2">
            <Button variant={mode === "employee" ? "default" : "outline"} size="sm" onClick={() => setMode("employee")}>
              Da dipendente
            </Button>
            <Button variant={mode === "generic" ? "default" : "outline"} size="sm" onClick={() => setMode("generic")}>
              Generica
            </Button>
          </div>
          {mode === "employee" ? (
            <div className="grid gap-2">
              <Label>Dipendente</Label>
              <Select value={employeeId} onValueChange={setEmployeeId}>
                <SelectTrigger className="w-full">
                  <SelectValue placeholder={employees.length ? "Seleziona" : "Assegna prima i reparti alla sezione"} />
                </SelectTrigger>
                <SelectContent>
                  {employees.map((e) => (
                    <SelectItem key={e.id} value={String(e.id)}>
                      {e.fullName} ({e.departmentCode})
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          ) : (
            <div className="space-y-3">
              <div className="grid gap-2">
                <Label>Nome risorsa *</Label>
                <Input value={name} onChange={(e) => setName(e.target.value)} />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div className="grid gap-2">
                  <Label>€/h</Label>
                  <Input className="text-right" value={hourlyCost} onChange={(e) => setHourlyCost(e.target.value)} />
                </div>
                <div className="grid gap-2">
                  <Label>Markup K</Label>
                  <Input className="text-center" value={markup} onChange={(e) => setMarkup(e.target.value)} />
                </div>
              </div>
            </div>
          )}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Annulla</Button>
          <Button disabled={addMutation.isPending} onClick={() => addMutation.mutate()}>Aggiungi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
