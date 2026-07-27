import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import {
  addProjectCostSection,
  addProjectMaterialItem,
  addProjectResource,
  fetchProjectAvailableTemplates,
  fetchProjectSectionEmployees,
  updateProjectMaterialItem,
  updateProjectResource,
} from "@/lib/api/project-costing"
import type {
  AvailableCostTemplatesDto,
  EmployeeCostLookup,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { notifyError } from "@/lib/toast"

export function hours(value: number): string {
  return `${value.toFixed(1)} h`
}

/** Parsing numerico all'italiana (virgola o punto). NaN → 0. */
export function num(value: string): number {
  const n = parseFloat(value.replace(",", "."))
  return Number.isFinite(n) ? n : 0
}

export interface ResourceDialogSectionInfo {
  sectionId: number
  sectionName: string
  sectionType: string
  resourcesCount: number
}

export interface ResourceDialogItem {
  id?: number
  employeeId?: number | null
  resourceName?: string
  workDays?: number
  hoursPerDay?: number
  hourlyCost?: number
  markupValue?: number
  numTrips?: number
  kmPerTrip?: number
  costPerKm?: number
  dailyFood?: number
  dailyHotel?: number
  allowanceDays?: number
  dailyAllowance?: number
  sortOrder?: number
}

export function ResourceDialog({
  projectId,
  section,
  resource,
  onClose,
  onSaved,
}: {
  projectId: number
  section: ResourceDialogSectionInfo
  resource: ResourceDialogItem | "new" | null
  onClose: () => void
  onSaved: () => void
}) {
  const open = resource !== null
  const editing = resource !== null && resource !== "new" ? resource : null
  const isClient = section.sectionType === "DA_CLIENTE"

  const employeesQuery = useQuery({
    queryKey: ["project-costing", "section-employees", projectId, section.sectionId],
    queryFn: () => fetchProjectSectionEmployees(projectId, section.sectionId),
    enabled: open,
  })
  const employees: EmployeeCostLookup[] = employeesQuery.data ?? []

  const [name, setName] = React.useState("")
  const [employeeId, setEmployeeId] = React.useState<number | null>(null)
  const [workDays, setWorkDays] = React.useState("0")
  const [hoursPerDay, setHoursPerDay] = React.useState("8")
  const [hourlyCost, setHourlyCost] = React.useState("0")
  const [markup, setMarkup] = React.useState("1.450")
  // Trasferta
  const [numTrips, setNumTrips] = React.useState("0")
  const [kmPerTrip, setKmPerTrip] = React.useState("0")
  const [costPerKm, setCostPerKm] = React.useState("0.90")
  const [dailyFood, setDailyFood] = React.useState("0")
  const [dailyHotel, setDailyHotel] = React.useState("0")
  const [allowanceDays, setAllowanceDays] = React.useState("0")
  const [dailyAllowance, setDailyAllowance] = React.useState("0")

  React.useEffect(() => {
    if (!open) return
    const r = editing
    setName(r?.resourceName ?? "")
    setEmployeeId(r?.employeeId ?? null)
    setWorkDays(String(r?.workDays ?? 0))
    setHoursPerDay(String(r?.hoursPerDay ?? 8))
    setHourlyCost(String(r?.hourlyCost ?? 0))
    setMarkup(String(r?.markupValue ?? 1.45))
    setNumTrips(String(r?.numTrips ?? 0))
    setKmPerTrip(String(r?.kmPerTrip ?? 0))
    setCostPerKm(String(r?.costPerKm ?? 0.9))
    setDailyFood(String(r?.dailyFood ?? 0))
    setDailyHotel(String(r?.dailyHotel ?? 0))
    setAllowanceDays(String(r?.allowanceDays ?? 0))
    setDailyAllowance(String(r?.dailyAllowance ?? 0))
  }, [open, editing?.id])

  function onPickEmployee(value: string) {
    if (value === "__manual__") {
      setEmployeeId(null)
      return
    }
    const emp = employees.find((e) => String(e.id) === value)
    if (!emp) return
    setEmployeeId(emp.id)
    setName(emp.fullName)
    setHourlyCost(String(emp.hourlyCost))
    setMarkup(String(emp.defaultMarkup))
  }

  const saveMutation = useMutation({
    mutationFn: async () => {
      const payload = {
        id: editing?.id ?? 0,
        sectionId: section.sectionId,
        employeeId,
        resourceName: name.trim(),
        workDays: num(workDays),
        hoursPerDay: num(hoursPerDay),
        hourlyCost: num(hourlyCost),
        markupValue: num(markup),
        numTrips: isClient ? Math.round(num(numTrips)) : 0,
        kmPerTrip: isClient ? num(kmPerTrip) : 0,
        costPerKm: isClient ? num(costPerKm) : 0,
        dailyFood: isClient ? num(dailyFood) : 0,
        dailyHotel: isClient ? num(dailyHotel) : 0,
        allowanceDays: isClient ? num(allowanceDays) : 0,
        dailyAllowance: isClient ? num(dailyAllowance) : 0,
        sortOrder: editing?.sortOrder ?? section.resourcesCount + 1,
      }
      if (editing && editing.id) await updateProjectResource(projectId, editing.id, payload)
      else await addProjectResource(projectId, payload)
    },
    onSuccess: onSaved,
    onError: (err: Error) => notifyError(err),
  })

  const totHours = num(workDays) * num(hoursPerDay)
  const totCost = totHours * num(hourlyCost)

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{editing ? "Modifica risorsa" : "Nuova risorsa"}</DialogTitle>
          <DialogDescription>
            Sezione «{section.sectionName}» — {hours(totHours)} · costo {euro(totCost)}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label>Dipendente (opzionale, precompila €/h e K)</Label>
            <Select
              value={employeeId != null ? String(employeeId) : "__manual__"}
              onValueChange={onPickEmployee}
            >
              <SelectTrigger className="h-9">
                <SelectValue placeholder="Manuale…" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__manual__">Manuale (nome libero)</SelectItem>
                {employees.map((e) => (
                  <SelectItem key={e.id} value={String(e.id)}>
                    {e.fullName} — {e.departmentCode} · {euro(e.hourlyCost)}/h
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label>Nome risorsa</Label>
            <Input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Es. Progettista meccanico"
            />
          </div>

          <div className="grid grid-cols-4 gap-2">
            <NumField label="Giorni" value={workDays} onChange={setWorkDays} />
            <NumField label="Ore/g" value={hoursPerDay} onChange={setHoursPerDay} />
            <NumField label="€/h" value={hourlyCost} onChange={setHourlyCost} />
            <NumField label="K (markup)" value={markup} onChange={setMarkup} />
          </div>

          {isClient ? (
            <div className="rounded-md border bg-amber-50/40 p-2.5">
              <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-amber-700">
                Trasferta (sezione da cliente)
              </p>
              <div className="grid grid-cols-3 gap-2">
                <NumField label="N° viaggi" value={numTrips} onChange={setNumTrips} />
                <NumField label="Km/viaggio" value={kmPerTrip} onChange={setKmPerTrip} />
                <NumField label="€/km" value={costPerKm} onChange={setCostPerKm} />
                <NumField label="Vitto/g" value={dailyFood} onChange={setDailyFood} />
                <NumField label="Hotel/g" value={dailyHotel} onChange={setDailyHotel} />
                <NumField label="GG indennità" value={allowanceDays} onChange={setAllowanceDays} />
                <NumField
                  label="Indennità/g"
                  value={dailyAllowance}
                  onChange={setDailyAllowance}
                />
              </div>
            </div>
          ) : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={saveMutation.isPending || name.trim() === ""}
          >
            {editing ? "Salva" : "Aggiungi"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function NumField({
  label,
  value,
  onChange,
}: {
  label: string
  value: string
  onChange: (v: string) => void
}) {
  return (
    <div className="space-y-1">
      <Label className="text-[11px] text-muted-foreground">{label}</Label>
      <Input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        inputMode="decimal"
        className="h-9 text-right font-mono tabular-nums"
      />
    </div>
  )
}

export function AddSectionDialog({
  projectId,
  open,
  existingTemplateIds,
  nextSortOrder,
  onClose,
  onAdded,
}: {
  projectId: number
  open: boolean
  existingTemplateIds: Set<number>
  nextSortOrder: number
  onClose: () => void
  onAdded: () => void
}) {
  const templatesQuery = useQuery({
    queryKey: ["project-costing", "available-templates", projectId],
    queryFn: () => fetchProjectAvailableTemplates(projectId),
    enabled: open,
  })
  const available: AvailableCostTemplatesDto | undefined = templatesQuery.data

  const [choice, setChoice] = React.useState<string>("")

  React.useEffect(() => {
    if (open) setChoice("")
  }, [open])

  const templates = React.useMemo(
    () => (available?.templates ?? []).filter((t) => !existingTemplateIds.has(t.id)),
    [available, existingTemplateIds]
  )

  const addMutation = useMutation({
    mutationFn: () => {
      const tpl = templates.find((t) => String(t.id) === choice)
      if (!tpl) throw new Error("Seleziona una sezione dal modello")
      return addProjectCostSection(projectId, {
        templateId: tpl.id,
        name: tpl.name,
        sectionType: tpl.sectionType,
        groupName: tpl.groupName,
        sortOrder: nextSortOrder,
        isEnabled: true,
      })
    },
    onSuccess: onAdded,
    onError: (err: Error) => notifyError(err),
  })

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Aggiungi sezione di costo</DialogTitle>
          <DialogDescription>
            Scegli una sezione dai modelli disponibili (le già presenti sono escluse).
          </DialogDescription>
        </DialogHeader>

        {templatesQuery.isLoading ? (
          <Skeleton className="h-9 w-full" />
        ) : templates.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            Tutte le sezioni disponibili sono già presenti nel preventivo.
          </p>
        ) : (
          <Select value={choice} onValueChange={setChoice}>
            <SelectTrigger>
              <SelectValue placeholder="Seleziona sezione…" />
            </SelectTrigger>
            <SelectContent>
              {templates.map((t) => (
                <SelectItem key={t.id} value={String(t.id)}>
                  {t.groupName} · {t.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => addMutation.mutate()}
            disabled={addMutation.isPending || choice === ""}
          >
            Aggiungi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export interface MaterialItemDialogSectionInfo {
  sectionId: number
  sectionName: string
  markupValue: number
  itemsCount: number
}

export interface MaterialItemDialogItem {
  id?: number
  description?: string
  quantity?: number
  unitCost?: number
  markupValue?: number
  parentItemId?: number | null
  productId?: number | null
  variantId?: number | null
  code?: string
  descriptionRtf?: string | null
  itemType?: string
  sortOrder?: number
  isActive?: boolean
}

export function MaterialItemDialog({
  projectId,
  section,
  item,
  onClose,
  onSaved,
}: {
  projectId: number
  section: MaterialItemDialogSectionInfo
  item: MaterialItemDialogItem | "new" | null
  onClose: () => void
  onSaved: () => void
}) {
  const open = item !== null
  const editing = item !== null && item !== "new" ? item : null

  const [description, setDescription] = React.useState("")
  const [quantity, setQuantity] = React.useState("1")
  const [unitCost, setUnitCost] = React.useState("0")
  const [markup, setMarkup] = React.useState("1.300")

  React.useEffect(() => {
    if (!open) return
    setDescription(editing?.description ?? "")
    setQuantity(String(editing?.quantity ?? 1))
    setUnitCost(String(editing?.unitCost ?? 0))
    setMarkup(String(editing?.markupValue ?? section.markupValue ?? 1.3))
  }, [open, editing?.id])

  const saveMutation = useMutation({
    mutationFn: async () => {
      const payload = {
        id: editing?.id ?? 0,
        sectionId: section.sectionId,
        parentItemId: editing?.parentItemId ?? null,
        productId: editing?.productId ?? null,
        variantId: editing?.variantId ?? null,
        code: editing?.code ?? "",
        description: description.trim(),
        descriptionRtf: editing?.descriptionRtf ?? null,
        quantity: num(quantity),
        unitCost: num(unitCost),
        markupValue: num(markup),
        itemType: editing?.itemType ?? "MATERIAL",
        sortOrder: editing?.sortOrder ?? section.itemsCount + 1,
        isActive: editing?.isActive ?? true,
      }
      if (editing && editing.id) await updateProjectMaterialItem(projectId, editing.id, payload)
      else await addProjectMaterialItem(projectId, payload)
    },
    onSuccess: onSaved,
    onError: (err: Error) => notifyError(err),
  })

  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>{editing ? "Modifica materiale" : "Nuovo materiale"}</DialogTitle>
          <DialogDescription>Sezione «{section.sectionName || "Materiali"}»</DialogDescription>
        </DialogHeader>

        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label>Descrizione</Label>
            <Input
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Es. Guida lineare THK HSR25"
            />
          </div>
          <div className="grid grid-cols-3 gap-2">
            <NumField label="Q.tà" value={quantity} onChange={setQuantity} />
            <NumField label="€ unit." value={unitCost} onChange={setUnitCost} />
            <NumField label="K (markup)" value={markup} onChange={setMarkup} />
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={saveMutation.isPending || description.trim() === ""}
          >
            {editing ? "Salva" : "Aggiungi"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
