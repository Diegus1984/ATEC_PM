// ── Blocchi sezione del Preventivo vs Consuntivo ───────────────────────────
// Gruppo colorato → sezione costo (risorse pianificate + fasi assegnate) e
// sezione materiali.

import * as React from "react"
import { useMutation } from "@tanstack/react-query"
import { ChevronRight, Pencil, Plane, Plus, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Collapsible } from "@/components/ui/collapsible"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { GridScroller } from "@/components/shared/grid-scroller"
import { SectionPhases } from "@/features/commesse/SectionPhases"
import {
  MaterialItemDialog,
  ResourceDialog,
  hours,
  type ResourceDialogItem,
} from "@/features/commesse/preventivo/preventivo-dialogs"
import {
  deleteProjectCostSection,
  deleteProjectMaterialItem,
  deleteProjectResource,
} from "@/lib/api/project-costing"
import type {
  BvaActualEmployeeDto,
  BvaBudgetResourceDto,
  BvaGroupDto,
  BvaMaterialItemDto,
  BvaMaterialSectionDto,
  BvaSectionDto,
  PhaseListItem,
} from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"

import {
  ThreeColumn,
  bvaActualTitleClass,
  bvaHoursClass,
  bvaMoneyClass,
} from "./bva-shared"
import { PreventivoTravelTable } from "../preventivo/preventivo-travel-table"
import { useCostTravelRows } from "../preventivo/preventivo-travel-shared"

/**
 * Risorse a CONSUNTIVO della sezione, a due livelli: una riga per dipendente che si apre
 * sulle sue ore versate (data · fase · causale · ore · €/h · costo).
 *
 * `actualEmployees` arrivava nel DTO da sempre e non veniva reso da nessuna parte: il
 * consuntivo si leggeva solo come totale in testata. È la «riga con N linee-risorsa» del
 * punto 5 del blocco 5 — qui però il dato non è digitato, viene dal timesheet reale.
 */
function ActualEmployees({ employees }: { employees: BvaActualEmployeeDto[] }) {
  const [open, setOpen] = React.useState<Set<string>>(new Set())

  function toggle(name: string) {
    setOpen((prev) => {
      const next = new Set(prev)
      if (next.has(name)) next.delete(name)
      else next.add(name)
      return next
    })
  }

  return (
    <div className="space-y-3">
      <p className={bvaActualTitleClass}>Risorse a consuntivo</p>
      <div className="divide-y rounded-md border">
        {employees.map((employee) => {
          const isOpen = open.has(employee.employeeName)
          return (
            <div key={employee.employeeName}>
              <button
                type="button"
                className="flex w-full items-center gap-2 px-2 py-1.5 text-left text-xs hover:bg-muted/40"
                onClick={() => toggle(employee.employeeName)}
              >
                <ChevronRight
                  className={cn(
                    "size-3.5 shrink-0 transition-transform duration-[var(--accordion-duration)] ease-[var(--accordion-ease)]",
                    isOpen && "rotate-90"
                  )}
                />
                <span className="flex-1 truncate font-medium">
                  {employee.employeeName}
                </span>
                <span className={bvaHoursClass}>{hours(employee.totalHours)}</span>
                <span className={bvaMoneyClass}>{euro(employee.totalCost)}</span>
              </button>
              <Collapsible open={isOpen}>
                <GridScroller>
                  <Table>
                    <TableHeader className="bg-muted/20">
                      <TableRow className="hover:bg-transparent">
                        <TableHead className="text-[10px]">Data</TableHead>
                        <TableHead className="text-[10px]">Fase dettaglio commessa</TableHead>
                        <TableHead className="text-right text-[10px]">Ore</TableHead>
                        <TableHead className="text-right text-[10px]">€/h</TableHead>
                        <TableHead className="text-right text-[10px]">Costo</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {employee.details.map((detail, index) => (
                        <TableRow key={`${detail.workDate}-${detail.phaseName}-${index}`}>
                          <TableCell className="text-xs tabular-nums">
                            {formatDateShort(detail.workDate)}
                          </TableCell>
                          <TableCell className="text-xs" title={detail.phaseName}>
                            {detail.phaseName || "—"}
                            {detail.entryType && detail.entryType !== "REGULAR" ? (
                              <span className="ml-1 text-[10px] text-muted-foreground">
                                ({detail.entryType})
                              </span>
                            ) : null}
                          </TableCell>
                          <TableCell
                            className={cn("text-right text-xs", bvaHoursClass)}
                          >
                            {detail.hours.toFixed(1)}
                          </TableCell>
                          <TableCell
                            className={cn("text-right text-xs", bvaMoneyClass)}
                          >
                            {euro(detail.hourlyCost)}
                          </TableCell>
                          <TableCell
                            className={cn("text-right text-xs", bvaMoneyClass)}
                          >
                            {euro(detail.totalCost)}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </GridScroller>
              </Collapsible>
            </div>
          )
        })}
      </div>
    </div>
  )
}

function SectionBlock({
  section,
  projectId,
  phasesByTemplate,
  existingPhaseKeys,
  canEditBudget,
  onPhasesChanged,
}: {
  section: BvaSectionDto
  projectId: number
  phasesByTemplate: Map<number, PhaseListItem[]>
  existingPhaseKeys: Set<string>
  canEditBudget: boolean
  onPhasesChanged: () => void
}) {
  const confirm = useConfirm()
  const isClient = section.sectionType === "DA_CLIENTE"

  // Righe trasferta della sezione (segnalazione #33). Si chiedono solo sulle sezioni
  // con Tag Cliente: sulle altre la trasferta non esiste proprio.
  const travelRowsQuery = useCostTravelRows(projectId, section.sectionId, isClient)
  const travelRows = React.useMemo(
    () => travelRowsQuery.data ?? [],
    [travelRowsQuery.data]
  )

  // #99: la tabella «Trasferta (sezione da cliente)» e la riga gialla del riepilogo non
  // stanno più distese nel Bilancio — Zanoni le leggeva come un doppione della Gestione
  // Trasferta. La tabella però è l'UNICO punto dove il PREVENTIVO trasferta si compila
  // (alimenta la voce «Spese Trasferta» del Riepilogo Costi e i previsti della card #96/#98),
  // quindi non sparisce: vive in un dialogo, dietro il pulsante qui sotto.
  const [travelOpen, setTravelOpen] = React.useState(false)

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
      title: "Elimina sezione di costo",
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
                  label: "Elimina sezione di costo",
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
            <div className="flex items-center gap-1.5">
              {/* #99: la trasferta a preventivo si apre da qui. Il pulsante compare anche in
                  sola lettura se ci sono righe da vedere, mai su una sezione «in Atec». */}
              {isClient && (canEditBudget || travelRows.length > 0) ? (
                <Button
                  variant="outline"
                  size="sm"
                  className="h-6 text-[10px] px-2"
                  onClick={() => setTravelOpen(true)}
                >
                  <Plane className="size-3 mr-1" />
                  Trasferta preventivo
                </Button>
              ) : null}
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
          </div>
          {/* Preventivo: risorse pianificate */}
          {section.budgetResources.length > 0 ? (
            <GridScroller className="rounded-md border">
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
                      <TableCell className={cn("text-right", bvaHoursClass)}>
                        {r.totalHours.toFixed(1)}
                      </TableCell>
                      <TableCell className={cn("text-right", bvaMoneyClass)}>
                        {euro(r.hourlyCost)}
                      </TableCell>
                      <TableCell className={cn("text-right", bvaMoneyClass)}>
                        {euro(r.totalCost)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {r.markupValue.toFixed(3)}
                      </TableCell>
                      <TableCell className={cn("text-right", bvaMoneyClass)}>
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
            </GridScroller>
          ) : (
            <p className="text-xs text-muted-foreground">
              Nessuna risorsa preventivata in questa sezione.
            </p>
          )}

          {section.actualEmployees.length > 0 ? (
            <ActualEmployees employees={section.actualEmployees} />
          ) : null}
        </div>

        {/* DESTRA: fasi assegnate DENTRO la sezione (come il WPF) */}
        <SectionPhases
          projectId={projectId}
          sectionTemplateId={section.templateId}
          phases={sectionPhases}
          existingPhaseKeys={existingPhaseKeys}
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

      {/* #99: la tabella trasferta del preventivo, uscita dal corpo della sezione. */}
      {isClient ? (
        <Dialog open={travelOpen} onOpenChange={(o) => !o && setTravelOpen(false)}>
          {/* sm:max-w-5xl, non max-w-5xl: il default shadcn è `sm:max-w-lg` e senza lo
              stesso breakpoint non viene scavalcato. Il min-w-0 sul wrapper è per la
              griglia del DialogContent: senza, l'item cresce a min-content e la tabella
              esce dal bordo del dialogo invece di scorrere. */}
          <DialogContent className="sm:max-w-5xl">
            <DialogHeader>
              <DialogTitle>Trasferta preventivo — {section.sectionName}</DialogTitle>
              <DialogDescription>
                Alimenta la voce «Spese Trasferta» del Riepilogo Costi e i previsti della
                card Trasferta. Il consuntivo resta nella Gestione Trasferta.
              </DialogDescription>
            </DialogHeader>
            <div className="min-w-0">
              <PreventivoTravelTable
                projectId={projectId}
                sectionId={section.sectionId}
                rows={travelRows}
                resources={section.budgetResources}
                canEdit={canEditBudget}
                onChanged={onPhasesChanged}
              />
            </div>
          </DialogContent>
        </Dialog>
      ) : null}
    </div>
  )
}

export function GroupBlock({
  group,
  open,
  onToggle,
  projectId,
  phasesByTemplate,
  existingPhaseKeys,
  canEditBudget,
  onPhasesChanged,
}: {
  group: BvaGroupDto
  /** Aperto/chiuso: lo tiene il padre, perché chiudendo «Impegno Risorse» questo blocco
   *  viene smontato e uno stato locale si perderebbe. */
  open: boolean
  onToggle: () => void
  projectId: number
  phasesByTemplate: Map<number, PhaseListItem[]>
  existingPhaseKeys: Set<string>
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
        {/* Chip chiaro: stesso blocco delle sezioni (ore + € + delta), etichette
            nere sul fondo colorato del gruppo (#66). Prima c'erano solo le ore. */}
        <div className="hidden rounded-md bg-white/95 px-2.5 py-1 shadow-sm sm:block">
          <ThreeColumn
            budgetHours={group.budgetHours}
            budgetCost={group.budgetCost}
            assignedHours={group.assignedHours}
            assignedCost={group.assignedCost}
            actualHours={group.actualHours}
            actualCost={group.actualCost}
            deltaHours={group.actualHours - group.budgetHours}
          />
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
              existingPhaseKeys={existingPhaseKeys}
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
    <GridScroller className="rounded-lg border">
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
    </GridScroller>
  )
}
