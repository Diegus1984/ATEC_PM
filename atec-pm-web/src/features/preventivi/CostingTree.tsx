import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { Button } from "@/components/ui/button"
import {
  deleteCostSection,
  deleteMaterialSection,
  fetchAvailableTemplates,
  fetchCosting,
} from "@/lib/api/quote-costing"
import type { ProjectCostSectionDto } from "@/lib/api/types"

import { AddCostSectionDialog, AddMaterialSectionDialog } from "./costing-dialogs"
import { DistributionPanel, PricingPanel } from "./costing-panels"
import { CostSectionCard, MaterialSectionCard } from "./costing-rows"

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
