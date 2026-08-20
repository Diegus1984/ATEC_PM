import * as React from "react"
import { useMutation } from "@tanstack/react-query"

import { MoneyInput } from "@/components/shared/money-input"
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import { patchCostSectionGroupField, patchCostSectionTemplateField } from "@/lib/api/cost-sections"
import { createDepartment, updateDepartment } from "@/lib/api/departments"
import { patchPhaseTemplateField } from "@/lib/api/phases"
import type {
  CostSectionGroupDto,
  CostSectionTemplateDto,
  DepartmentDto,
  PhaseTemplateDto,
} from "@/lib/api/types"
import { cn } from "@/lib/utils"

const GROUP_COLOR_PALETTE = [
  "#2563EB", "#3B82F6", "#1D4ED8",
  "#059669", "#10B981", "#047857",
  "#D97706", "#F59E0B", "#B45309",
  "#DC2626", "#EF4444", "#B91C1C",
  "#7C3AED", "#8B5CF6", "#6D28D9",
  "#0891B2", "#06B6D4", "#0E7490",
  "#BE185D", "#EC4899", "#9D174D",
  "#374151", "#6B7280", "#1F2937",
]

export function PromptDialog({
  open,
  title,
  label,
  value: controlledValue,
  onValueChange,
  extra,
  onClose,
  onConfirm,
  confirmLabel = "Salva",
  description,
}: {
  open: boolean
  title: string
  label: string
  value?: string
  onValueChange?: (value: string) => void
  extra?: React.ReactNode
  onClose: () => void
  onConfirm: (value: string) => Promise<void>
  confirmLabel?: string
  description?: string
}) {
  const [internalValue, setInternalValue] = React.useState("")
  const value = controlledValue ?? internalValue
  const setValue = onValueChange ?? setInternalValue
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (open) {
      setInternalValue("")
      setError(null)
    }
  }, [open])

  const saveMutation = useMutation({
    mutationFn: () => onConfirm(value.trim()),
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          {description ? (
            <p className="text-sm text-muted-foreground">{description}</p>
          ) : null}
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-2">
            <Label>{label}</Label>
            <Input
              value={value}
              autoFocus
              onChange={(event) => setValue(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter" && value.trim()) {
                  event.preventDefault()
                  saveMutation.mutate()
                }
              }}
            />
          </div>
          {extra}
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!value.trim() || saveMutation.isPending}
          >
            {confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function EditGroupDialog({
  open,
  group,
  onClose,
  onSaved,
}: {
  open: boolean
  group: CostSectionGroupDto | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [name, setName] = React.useState("")
  const [sortOrder, setSortOrder] = React.useState("0")
  const [bgColor, setBgColor] = React.useState("#3B82F6")

  React.useEffect(() => {
    if (!open || !group) return
    setName(group.name)
    setSortOrder(String(group.sortOrder))
    setBgColor(group.bgColor || "#3B82F6")
  }, [open, group])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!group) return
      if (name.trim() !== group.name) {
        await patchCostSectionGroupField(group.id, { field: "name", value: name.trim() })
      }
      if (bgColor !== group.bgColor) {
        await patchCostSectionGroupField(group.id, { field: "bg_color", value: bgColor })
      }
      if (Number(sortOrder) !== group.sortOrder) {
        await patchCostSectionGroupField(group.id, {
          field: "sort_order",
          value: sortOrder,
        })
      }
    },
    onSuccess: onSaved,
  })

  if (!group) return null

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Modifica gruppo</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-2">
            <Label>Nome</Label>
            <Input value={name} onChange={(event) => setName(event.target.value)} />
          </div>
          <div className="space-y-2">
            <Label>Ordine</Label>
            <Input
              type="number"
              value={sortOrder}
              onChange={(event) => setSortOrder(event.target.value)}
            />
          </div>
          <div className="space-y-2">
            <Label>Colore</Label>
            <div className="flex flex-wrap gap-1.5">
              {GROUP_COLOR_PALETTE.map((hex) => (
                <button
                  key={hex}
                  type="button"
                  className={cn(
                    "size-7 rounded border-2",
                    bgColor === hex ? "border-foreground" : "border-transparent"
                  )}
                  style={{ backgroundColor: hex }}
                  onClick={() => setBgColor(hex)}
                />
              ))}
            </div>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!name.trim() || saveMutation.isPending}
          >
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function EditSectionDialog({
  open,
  section,
  onClose,
  onSaved,
}: {
  open: boolean
  section: CostSectionTemplateDto | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [name, setName] = React.useState("")
  const [sectionType, setSectionType] = React.useState("IN_SEDE")
  const [sortOrder, setSortOrder] = React.useState("0")
  const [isDefault, setIsDefault] = React.useState(false)
  const [isDefaultQuote, setIsDefaultQuote] = React.useState(false)
  const [isActive, setIsActive] = React.useState(true)

  React.useEffect(() => {
    if (!open || !section) return
    setName(section.name)
    setSectionType(section.sectionType || "IN_SEDE")
    setSortOrder(String(section.sortOrder))
    setIsDefault(section.isDefault)
    setIsDefaultQuote(section.isDefaultQuote)
    setIsActive(section.isActive)
  }, [open, section])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!section) return
      if (name.trim() !== section.name) {
        await patchCostSectionTemplateField(section.id, {
          field: "name",
          value: name.trim(),
        })
      }
      if (sectionType !== section.sectionType) {
        await patchCostSectionTemplateField(section.id, {
          field: "section_type",
          value: sectionType,
        })
      }
      if (isDefault !== section.isDefault) {
        await patchCostSectionTemplateField(section.id, {
          field: "is_default_project",
          value: isDefault ? "1" : "0",
        })
      }
      if (isDefaultQuote !== section.isDefaultQuote) {
        await patchCostSectionTemplateField(section.id, {
          field: "is_default_quote",
          value: isDefaultQuote ? "1" : "0",
        })
      }
      if (isActive !== section.isActive) {
        await patchCostSectionTemplateField(section.id, {
          field: "is_active",
          value: isActive ? "1" : "0",
        })
      }
      if (Number(sortOrder) !== section.sortOrder) {
        await patchCostSectionTemplateField(section.id, {
          field: "sort_order",
          value: sortOrder,
        })
      }
    },
    onSuccess: onSaved,
  })

  if (!section) return null

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Modifica sezione</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-2">
            <Label>Nome</Label>
            <Input value={name} onChange={(event) => setName(event.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>Tipo</Label>
              <Select value={sectionType} onValueChange={setSectionType}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="IN_SEDE">In sede</SelectItem>
                  <SelectItem value="DA_CLIENTE">Da cliente</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label>Ordine</Label>
              <Input
                type="number"
                value={sortOrder}
                onChange={(event) => setSortOrder(event.target.value)}
              />
            </div>
          </div>
          <label className="flex items-center gap-2 text-sm">
            <Switch
              size="sm"
              checked={isDefault}
              onCheckedChange={setIsDefault}
            />
            Default commessa
          </label>
          <label className="flex items-center gap-2 text-sm">
            <Switch
              size="sm"
              checked={isDefaultQuote}
              onCheckedChange={setIsDefaultQuote}
            />
            Default preventivo
          </label>
          <label className="flex items-center gap-2 text-sm">
            <Switch
              size="sm"
              checked={isActive}
              onCheckedChange={setIsActive}
            />
            Attiva (se spenta non viene proposta su nuovi preventivi/commesse)
          </label>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!name.trim() || saveMutation.isPending}
          >
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function EditPhaseDialog({
  open,
  phase,
  onClose,
  onSaved,
}: {
  open: boolean
  phase: PhaseTemplateDto | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [name, setName] = React.useState("")

  React.useEffect(() => {
    if (!open || !phase) return
    setName(phase.name)
  }, [open, phase])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!phase) return
      if (name.trim() !== phase.name) {
        await patchPhaseTemplateField(phase.id, { field: "name", value: name.trim() })
      }
    },
    onSuccess: onSaved,
  })

  if (!phase) return null

  // Il nome è tutto ciò che appartiene alla FASE. Sezione, ordine e «nasce da sola» sono del
  // legame con la singola sezione — si toccano dalla riga della fase dentro la sezione, perché
  // la stessa fase può nascere da sola in Program Manager e non in Progettazione.
  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Rinomina fase dettaglio commessa</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-2">
            <Label>Nome fase</Label>
            <Input value={name} onChange={(event) => setName(event.target.value)} />
          </div>
          {phase.sections.length > 0 ? (
            <div className="space-y-1 rounded-md border bg-muted/40 p-2">
              <p className="text-xs font-medium">
                In {phase.sections.length} sezion
                {phase.sections.length === 1 ? "e" : "i"}
              </p>
              <ul className="space-y-0.5 text-xs text-muted-foreground">
                {phase.sections.map((link) => (
                  <li key={link.sectionId}>
                    {link.groupName ? `${link.groupName} · ` : ""}
                    {link.sectionName}
                    {link.isDefault ? " — nasce da sola" : ""}
                  </li>
                ))}
              </ul>
              <p className="text-[11px] text-muted-foreground">
                Rinominarla la rinomina ovunque: è la stessa fase.
              </p>
            </div>
          ) : (
            <p className="text-xs text-amber-700 dark:text-amber-400">
              Questa fase non è in nessuna sezione: non entrerà in nessuna commessa.
              Trascinala su una sezione dal dock.
            </p>
          )}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!name.trim() || saveMutation.isPending}
          >
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

export function DepartmentDialog({
  open,
  department,
  onClose,
  onSaved,
}: {
  open: boolean
  department: DepartmentDto | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [code, setCode] = React.useState("")
  const [name, setName] = React.useState("")
  const [hourlyCost, setHourlyCost] = React.useState("0")
  const [defaultMarkup, setDefaultMarkup] = React.useState("1.450")
  const [sortOrder, setSortOrder] = React.useState("0")
  const [error, setError] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!open) return
    setCode(department?.code ?? "")
    setName(department?.name ?? "")
    setHourlyCost(department ? String(department.hourlyCost) : "0")
    setDefaultMarkup(department ? String(department.defaultMarkup) : "1.450")
    setSortOrder(department ? String(department.sortOrder) : "0")
    setError(null)
  }, [open, department])

  const saveMutation = useMutation({
    mutationFn: async () => {
      const payload = {
        id: department?.id ?? 0,
        code: code.trim().toUpperCase(),
        name: name.trim(),
        hourlyCost: Number(hourlyCost),
        defaultMarkup: Number(defaultMarkup),
        sortOrder: Number(sortOrder),
        isActive: true,
      }
      if (department) {
        await updateDepartment(department.id, payload)
        return
      }
      await createDepartment(payload)
    },
    onSuccess: onSaved,
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {department ? `Modifica reparto — ${department.code}` : "Nuovo reparto"}
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>Codice</Label>
              <Input value={code} onChange={(event) => setCode(event.target.value)} />
            </div>
            <div className="space-y-2">
              <Label>Ordine</Label>
              <Input
                type="number"
                value={sortOrder}
                onChange={(event) => setSortOrder(event.target.value)}
              />
            </div>
          </div>
          <div className="space-y-2">
            <Label>Nome</Label>
            <Input value={name} onChange={(event) => setName(event.target.value)} />
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>Costo orario (€)</Label>
              <MoneyInput value={hourlyCost} onChange={setHourlyCost} />
            </div>
            <div className="space-y-2">
              <Label>K ricarico</Label>
              <Input
                value={defaultMarkup}
                onChange={(event) => setDefaultMarkup(event.target.value)}
              />
            </div>
          </div>
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!code.trim() || !name.trim() || saveMutation.isPending}
          >
            Salva
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
