// ── Dialoghi aggiunta (sezione costo, sezione materiali, risorsa) ──────────

import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import { LookupCombobox } from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  addCostSection,
  addMaterialSection,
  addResource,
  fetchSectionEmployees,
} from "@/lib/api/quote-costing"
import type { CostSectionTemplateDto, ProjectCostSectionDto } from "@/lib/api/types"
import { notifyError } from "@/lib/toast"
import { parseDecimal } from "@/lib/format"


export function AddCostSectionDialog({
  open,
  quoteId,
  templates,
  onClose,
  onAdded,
}: {
  open: boolean
  quoteId: number
  templates: CostSectionTemplateDto[]
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
      if (!tpl) throw new Error("Seleziona una sezione di costo.")
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
          <Label>Sezione di costo</Label>
          <LookupCombobox
            options={templates.map((t) => ({
              id: String(t.id),
              name: `${t.groupName} — ${t.name}`,
            }))}
            value={templateId || null}
            onValueChange={(id) => setTemplateId(id ?? "")}
            placeholder="Seleziona una sezione di costo"
            searchPlaceholder="Cerca sezione…"
            emptyText="Nessuna sezione di costo trovata"
          />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Annulla</Button>
          <Button disabled={!templateId || addMutation.isPending} onClick={() => addMutation.mutate()}>Aggiungi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function AddMaterialSectionDialog({
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

export function AddResourceDialog({
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
              <LookupCombobox
                options={employees.map((e) => ({
                  id: String(e.id),
                  name: `${e.fullName} (${e.departmentCode})`,
                }))}
                value={employeeId || null}
                onValueChange={(id) => setEmployeeId(id ?? "")}
                placeholder={
                  employees.length
                    ? "Seleziona"
                    : "Assegna prima i reparti alla sezione"
                }
                searchPlaceholder="Cerca dipendente…"
                emptyText="Nessun dipendente trovato"
              />
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
