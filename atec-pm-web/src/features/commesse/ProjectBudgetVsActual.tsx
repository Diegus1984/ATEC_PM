import * as React from "react"
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query"
import { RefreshCw, Plus, Sparkles } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import { Skeleton } from "@/components/ui/skeleton"
import { ApiError } from "@/lib/api/client"
import { fetchBudgetVsActual } from "@/lib/api/project-bva"
import { fetchProjectPhases } from "@/lib/api/phases"
import {
  initProjectCosting,
  fetchProjectCosting,
} from "@/lib/api/project-costing"
import { AddSectionDialog } from "@/features/commesse/preventivo-dialogs"
import type { PhaseListItem } from "@/lib/api/types"
import { useBudgetHub } from "@/lib/signalr/use-budget-hub"
import { notifyError } from "@/lib/toast"

import { EconomicSummary, PricingBlock } from "./bva-economics"
import { CostSummaryBlock, OrderLinesBlock } from "./bva-order"
import { GroupBlock, MaterialSectionBlock } from "./bva-sections"
import { BvaExpandContext, type BvaExpandCommand } from "./bva-expand"
import { BlueSection, ThreeColumn } from "./bva-shared"
import { costTravelRowsKey } from "./preventivo-travel-shared"
import { WorkshopCalcDialog } from "./WorkshopCalcDialog"

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
    void queryClient.invalidateQueries({ queryKey: ["project-costing", projectId] })
    // Le righe trasferta hanno una GET per sezione: la chiave senza sezione le prende
    // tutte, così un evento realtime riallinea anche le tabelle già aperte.
    void queryClient.invalidateQueries({
      queryKey: costTravelRowsKey(projectId),
    })
  }, [queryClient, projectId])

  // Ambiente condiviso: ordine e Totale Vendita li tocca più di una persona.
  useBudgetHub(projectId > 0, invalidate, projectId)

  const data = query.data
  const canEditBudget = data ? data.linkedQuoteId === 0 : false

  // Se editabile, carichiamo le sezioni del preventivo per rilevare tutti i template usati (compresi i disabilitati)
  const costingQuery = useQuery({
    queryKey: ["project-costing", projectId],
    queryFn: () => fetchProjectCosting(projectId),
    enabled: projectId > 0 && canEditBudget,
  })

  const [addSectionOpen, setAddSectionOpen] = React.useState(false)
  const [workshopCalcOpen, setWorkshopCalcOpen] = React.useState(false)

  const initMutation = useMutation({
    mutationFn: () => initProjectCosting(projectId),
    onSuccess: invalidate,
    onError: (err: Error) => notifyError(err),
  })

  const phases = React.useMemo(
    () => phasesQuery.data ?? [],
    [phasesQuery.data]
  )
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
  // Chiavi (fase, sezione) già in commessa: la stessa fase può stare in più sezioni, quindi
  // «già presente» si decide sulla coppia, non sull'id della fase.
  const existingPhaseKeys = React.useMemo(
    () =>
      new Set(
        phases
          .filter((p) => !p.isLocal)
          .map((p) => `${p.phaseTemplateId}:${p.costSectionTemplateId ?? ""}`)
      ),
    [phases]
  )

  const existingTemplateIdsForAdd = React.useMemo(() => {
    const set = new Set<number>()
    if (costingQuery.data) {
      for (const s of costingQuery.data.costSections) {
        if (s.templateId != null) set.add(s.templateId)
      }
    }
    return set
  }, [costingQuery.data])

  // Stato apertura gruppi interni (chiave = groupName). Sta QUI e non dentro GroupBlock
  // perché chiudendo «Impegno Risorse» i gruppi vengono smontati.
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

  // «Espandi tutto» / «Chiudi tutto» (segnalazione #60): valgono su TUTTE le finestre
  // della pagina — le macro-sezioni blu (che obbediscono al comando qui sotto) e i
  // gruppi colorati dentro Impegno Risorse. Ognuna resta poi apribile e richiudibile
  // per conto suo.
  const [expandCommand, setExpandCommand] = React.useState<BvaExpandCommand>({
    open: true,
    nonce: 0,
  })
  const groupNames = React.useMemo(
    () => (data?.groups ?? []).map((g) => g.groupName),
    [data?.groups]
  )
  const expandAll = React.useCallback(() => {
    setExpandCommand((c) => ({ open: true, nonce: c.nonce + 1 }))
    setCollapsedGroups(new Set())
  }, [])
  const collapseAll = React.useCallback(() => {
    setExpandCommand((c) => ({ open: false, nonce: c.nonce + 1 }))
    setCollapsedGroups(new Set(groupNames))
  }, [groupNames])

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
    <BvaExpandContext.Provider value={expandCommand}>
      <div className="space-y-3">
        {!canEditBudget && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 px-4 py-2.5 text-sm text-amber-800 flex items-center gap-2">
            <span>⚠️</span>
            <span>Preventivo da offerta #{data.linkedQuoteId} — sola lettura</span>
          </div>
        )}

        <div className="flex flex-wrap items-center justify-end gap-2">
          <div className="flex items-center gap-1.5 rounded-lg border bg-card p-1">
            <Button
              variant="ghost"
              size="sm"
              className="h-7 text-xs px-2"
              onClick={expandAll}
            >
              Espandi tutto
            </Button>
            <div className="h-3 w-px bg-muted" />
            <Button
              variant="ghost"
              size="sm"
              className="h-7 text-xs px-2"
              onClick={collapseAll}
            >
              Chiudi tutto
            </Button>
          </div>

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
        <BlueSection
          title="Impegno Risorse"
          action={
            canEditBudget && (
              <Button
                size="sm"
                variant="secondary"
                className="h-7 text-xs bg-white text-blue-700 hover:bg-zinc-100"
                onClick={() => setAddSectionOpen(true)}
              >
                <Plus className="size-3.5 mr-1" />
                Sezione di costo
              </Button>
            )
          }
        >
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
                existingPhaseKeys={existingPhaseKeys}
                canEditBudget={canEditBudget}
                onPhasesChanged={invalidate}
              />
            ))
          ) : (
            <Empty className="p-6">
              <EmptyHeader>
                <EmptyTitle>Preventivo non ancora impostato</EmptyTitle>
                <EmptyDescription>
                  Questa commessa non deriva da un'offerta, quindi non ha un preventivo.
                  Inizializzalo dai modelli standard per compilarlo a mano.
                </EmptyDescription>
              </EmptyHeader>
              {canEditBudget && (
                <Button
                  onClick={() => initMutation.mutate()}
                  disabled={initMutation.isPending}
                >
                  <Sparkles className="size-4 mr-1.5" />
                  Inizializza preventivo
                </Button>
              )}
            </Empty>
          )}
        </BlueSection>

        {/* 2. MATERIALI */}
        {data.materialSections.length > 0 ? (
          <BlueSection title="Materiali">
            {data.materialSections.map((ms, msIndex) => (
              <MaterialSectionBlock
                key={`${ms.sectionName}-${msIndex}`}
                projectId={projectId}
                section={ms}
                canEditBudget={canEditBudget}
                onChanged={invalidate}
              />
            ))}
          </BlueSection>
        ) : null}

        {/* 3. ORDINE COMMESSA — l'ordine cliente spezzato in posizioni */}
        <BlueSection title="Ordine Commessa">
          <OrderLinesBlock
            projectId={projectId}
            lines={data.orderLines}
            economic={data.economic}
            onChanged={invalidate}
          />
        </BlueSection>

        {/* 4. CONTO ECONOMICO */}
        {data.economic ? (
          <BlueSection title="Conto Economico">
            <EconomicSummary
              economic={data.economic}
              projectId={projectId}
              onSaved={invalidate}
            />
          </BlueSection>
        ) : null}

        {/* 5. RIEPILOGO COSTI — voce per voce, Preventivati | Consuntivati.
               «Lavorazioni Officine» non è un campo libero: è il pulsante che apre il suo
               calcolo a righe, l'unica delle 4 voci senza una struttura che la alimenti. */}
        {data.economic && data.costLines.length > 0 ? (
          <BlueSection title="Riepilogo Costi">
            <CostSummaryBlock
              costLines={data.costLines}
              economic={data.economic}
              onOpenWorkshopCalc={
                canEditBudget ? () => setWorkshopCalcOpen(true) : undefined
              }
            />
          </BlueSection>
        ) : null}

        {/* 6. SCHEDA PREZZI */}
        {data.pricing ? (
          <BlueSection title="Scheda Prezzi">
            <PricingBlock
              projectId={projectId}
              pricing={data.pricing}
              costingPricing={costingQuery.data?.pricing}
              canEditBudget={canEditBudget}
              onChanged={invalidate}
            />
          </BlueSection>
        ) : null}

        {canEditBudget && (
          <WorkshopCalcDialog
            open={workshopCalcOpen}
            projectId={projectId}
            sheet={data.workshopBudgetSheet}
            onClose={() => setWorkshopCalcOpen(false)}
            onSaved={() => {
              setWorkshopCalcOpen(false)
              invalidate()
            }}
          />
        )}

        {canEditBudget && (
          <AddSectionDialog
            projectId={projectId}
            open={addSectionOpen}
            existingTemplateIds={existingTemplateIdsForAdd}
            nextSortOrder={data.groups.flatMap((g) => g.sections).length + 1}
            onClose={() => setAddSectionOpen(false)}
            onAdded={() => {
              setAddSectionOpen(false)
              invalidate()
            }}
          />
        )}
      </div>
    </BvaExpandContext.Provider>
  )
}
