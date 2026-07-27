// ── Card ed editor di riga dell'albero costing (sezioni costo e materiali) ──

import * as React from "react"
import { useQuery } from "@tanstack/react-query"
import { Plus, Trash2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Switch } from "@/components/ui/switch"
import { fetchDepartments } from "@/lib/api/departments"
import {
  addMaterialItem,
  deleteMaterialItem,
  deleteResource,
  setSectionDepartments,
  toggleMaterialItemActive,
  updateMaterialItem,
  updateMaterialSectionField,
  updateResource,
} from "@/lib/api/quote-costing"
import type {
  ProjectCostResourceDto,
  ProjectCostSectionDto,
  ProjectMaterialItemDto,
  ProjectMaterialSectionDto,
} from "@/lib/api/types"
import { notifyError } from "@/lib/toast"

import { AddResourceDialog } from "./costing-dialogs"
import { fmt2, parseDecimal } from "@/lib/format"

// ── Card sezione costo ─────────────────────────────────────

export function CostSectionCard({
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

export function MaterialSectionCard({
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
