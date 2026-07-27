// ── Blocchi sezione del Preventivo vs Consuntivo ───────────────────────────
// Gruppo colorato → sezione costo (risorse pianificate + fasi assegnate) e
// sezione materiali.

import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { ChevronRight, Pencil, Plus, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Collapsible } from "@/components/ui/collapsible"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { SectionPhases } from "@/features/commesse/SectionPhases"
import {
  MaterialItemDialog,
  ResourceDialog,
  hours,
  type ResourceDialogItem,
} from "@/features/commesse/preventivo-dialogs"
import {
  deleteProjectCostSection,
  deleteProjectMaterialItem,
  deleteProjectResource,
} from "@/lib/api/project-costing"
import type {
  BvaBudgetResourceDto,
  BvaGroupDto,
  BvaMaterialItemDto,
  BvaMaterialSectionDto,
  BvaSectionDto,
  PhaseListItem,
} from "@/lib/api/types"
import { euro } from "@/lib/format"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { ThreeColumn } from "./bva-shared"

function SectionBlock({
  section,
  projectId,
  phasesByTemplate,
  existingTemplateIds,
  canEditBudget,
  onPhasesChanged,
}: {
  section: BvaSectionDto
  projectId: number
  phasesByTemplate: Map<number, PhaseListItem[]>
  existingTemplateIds: Set<number>
  canEditBudget: boolean
  onPhasesChanged: () => void
}) {
  const confirm = useConfirm()
  const isClient = section.sectionType === "DA_CLIENTE"
  const hasTravel = section.budgetTotalTravelCost > 0
  const sectionPhases =
    section.templateId != null
      ? phasesByTemplate.get(section.templateId) ?? []
      : []

  const [resourceDialog, setResourceDialog] = React.useState<
    ResourceDialogItem | "new" | null
  >(null)

  const deleteSectionMutation = useMutation({
    mutationFn: () => deleteProjectCostSection(projectId, section.sectionId),
    onSuccess: onPhasesChanged,
    onError: (err: Error) => notifyError(err),
  })

  const deleteResourceMutation = useMutation({
    mutationFn: (id: number) => deleteProjectResource(projectId, id),
    onSuccess: onPhasesChanged,
    onError: (err: Error) => notifyError(err),
  })

  async function handleDeleteSection() {
    const ok = await confirm({
      title: "Elimina sezione",
      description: `Eliminare la sezione "${section.sectionName}" e tutte le sue righe risorsa?`,
      confirmLabel: "Elimina",
    })
    if (ok) deleteSectionMutation.mutate()
  }

  async function handleDeleteResource(r: BvaBudgetResourceDto) {
    const ok = await confirm({
      title: "Elimina risorsa",
      description: `Rimuovere "${r.resourceName || "(senza nome)"}" dal preventivo?`,
      confirmLabel: "Elimina",
    })
    if (ok) deleteResourceMutation.mutate(r.resourceId)
  }

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
          {canEditBudget && (
            <RowActionsMenu
              size="icon-sm"
              actions={[
                {
                  label: "Elimina sezione",
                  icon: Trash2,
                  destructive: true,
                  onClick: () => void handleDeleteSection(),
                },
              ]}
            />
          )}
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
          <div className="flex items-center justify-between gap-2">
            <p className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
              Risorse pianificate
            </p>
            {canEditBudget && (
              <Button
                variant="outline"
                size="sm"
                className="h-6 text-[10px] px-2"
                onClick={() => setResourceDialog("new")}
              >
                <Plus className="size-3 mr-1" />
                Risorsa
              </Button>
            )}
          </div>
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
                    {canEditBudget && <TableHead className="w-10" />}
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
                      {canEditBudget && (
                        <TableCell className="w-10">
                          <RowActionsMenu
                            size="icon-sm"
                            actions={[
                              {
                                label: "Modifica",
                                icon: Pencil,
                                onClick: () =>
                                  setResourceDialog({
                                    id: r.resourceId,
                                    employeeId: r.employeeId,
                                    resourceName: r.resourceName,
                                    workDays: r.workDays,
                                    hoursPerDay: r.hoursPerDay,
                                    hourlyCost: r.hourlyCost,
                                    markupValue: r.markupValue,
                                    numTrips: r.numTrips,
                                    kmPerTrip: r.kmPerTrip,
                                    costPerKm: r.costPerKm,
                                    dailyFood: r.dailyFood,
                                    dailyHotel: r.dailyHotel,
                                    allowanceDays: r.allowanceDays,
                                    dailyAllowance: r.dailyAllowance,
                                    sortOrder: index + 1,
                                  }),
                              },
                              {
                                label: "Elimina",
                                icon: Trash2,
                                destructive: true,
                                separatorBefore: true,
                                onClick: () => void handleDeleteResource(r),
                              },
                            ]}
                          />
                        </TableCell>
                      )}
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

      {canEditBudget && (
        <ResourceDialog
          projectId={projectId}
          section={{
            sectionId: section.sectionId,
            sectionName: section.sectionName,
            sectionType: section.sectionType,
            resourcesCount: section.budgetResources.length,
          }}
          resource={resourceDialog}
          onClose={() => setResourceDialog(null)}
          onSaved={() => {
            setResourceDialog(null)
            onPhasesChanged()
          }}
        />
      )}
    </div>
  )
}

export function GroupBlock({
  group,
  open,
  onToggle,
  projectId,
  phasesByTemplate,
  existingTemplateIds,
  canEditBudget,
  onPhasesChanged,
}: {
  group: BvaGroupDto
  open: boolean
  onToggle: () => void
  projectId: number
  phasesByTemplate: Map<number, PhaseListItem[]>
  existingTemplateIds: Set<number>
  canEditBudget: boolean
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
              canEditBudget={canEditBudget}
              onPhasesChanged={onPhasesChanged}
            />
          ))}
        </div>
      </Collapsible>
    </div>
  )
}

export function MaterialSectionBlock({
  projectId,
  section,
  canEditBudget,
  onChanged,
}: {
  projectId: number
  section: BvaMaterialSectionDto
  canEditBudget: boolean
  onChanged: () => void
}) {
  const confirm = useConfirm()
  const [itemDialog, setItemDialog] = React.useState<BvaMaterialItemDto | "new" | null>(null)

  const deleteItemMutation = useMutation({
    mutationFn: (id: number) => deleteProjectMaterialItem(projectId, id),
    onSuccess: onChanged,
    onError: (err: Error) => notifyError(err),
  })

  async function handleDeleteItem(item: BvaMaterialItemDto) {
    const ok = await confirm({
      title: "Elimina materiale",
      description: `Rimuovere "${item.description || "(senza descrizione)"}"?`,
      confirmLabel: "Elimina",
    })
    if (ok) deleteItemMutation.mutate(item.id)
  }

  return (
    <div className="overflow-x-auto rounded-lg border">
      {/* table-fixed + larghezze fisse: le colonne numeriche restano allineate
          tra tutte le sezioni materiali (la 1ª colonna nome prende il resto). */}
      <Table className="table-fixed">
        <TableHeader className="bg-muted/40">
          <TableRow className="hover:bg-transparent">
            <TableHead className="text-xs">
              <div className="flex items-center gap-2">
                <span className="truncate">{section.sectionName || "Materiali"}</span>
                {canEditBudget && (
                  <Button
                    variant="outline"
                    size="sm"
                    className="h-6 shrink-0 text-[10px] px-2 py-0"
                    onClick={() => setItemDialog("new")}
                  >
                    <Plus className="size-3 mr-1" />
                    Materiale
                  </Button>
                )}
              </div>
            </TableHead>
            <TableHead className="w-20 text-right text-xs">Q.tà</TableHead>
            <TableHead className="w-24 text-right text-xs">€ unit.</TableHead>
            <TableHead className="w-16 text-right text-xs">K</TableHead>
            <TableHead className="w-28 text-right text-xs">Netto</TableHead>
            <TableHead className="w-28 text-right text-xs">Vendita</TableHead>
            {canEditBudget && <TableHead className="w-10" />}
          </TableRow>
        </TableHeader>
        <TableBody>
          {section.items.map((item) => (
            <TableRow key={item.id}>
              <TableCell className="truncate" title={item.description}>
                {item.description || "—"}
              </TableCell>
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
              {canEditBudget && (
                <TableCell className="w-10">
                  <RowActionsMenu
                    size="icon-sm"
                    actions={[
                      {
                        label: "Modifica",
                        icon: Pencil,
                        onClick: () => setItemDialog(item),
                      },
                      {
                        label: "Elimina",
                        icon: Trash2,
                        destructive: true,
                        separatorBefore: true,
                        onClick: () => void handleDeleteItem(item),
                      },
                    ]}
                  />
                </TableCell>
              )}
            </TableRow>
          ))}
          <TableRow className="bg-muted/30 hover:bg-muted/30">
            <TableCell className="font-semibold" colSpan={4}>
              Totale
            </TableCell>
            <TableCell className="text-right font-semibold tabular-nums">
              {euro(section.totalNetCost)}
            </TableCell>
            <TableCell className="text-right font-semibold tabular-nums">
              {euro(section.totalSaleCost)}
            </TableCell>
            {canEditBudget && <TableCell />}
          </TableRow>
        </TableBody>
      </Table>

      {canEditBudget && (
        <MaterialItemDialog
          projectId={projectId}
          section={{
            sectionId: section.sectionId,
            sectionName: section.sectionName,
            markupValue: section.markupValue,
            itemsCount: section.items.length,
          }}
          item={itemDialog}
          onClose={() => setItemDialog(null)}
          onSaved={() => {
            setItemDialog(null)
            onChanged()
          }}
        />
      )}
    </div>
  )
}
