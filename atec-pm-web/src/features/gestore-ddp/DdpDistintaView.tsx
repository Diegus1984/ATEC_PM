import * as React from "react"

import { DdpRowsTable } from "./DdpRowsTable"
import { DdpSection } from "./DdpSection"
import { DdpStatusBreakdown } from "./DdpStatusBreakdown"
import type { DdpViewProps } from "./ddp-view-props"

/**
 * Scheda «Dati Distinta»: la distinta completa della DDP più i due riepiloghi di feedback.
 * Non fa parte del port del prototipo V41 — è il trasloco di quanto stava nella vecchia
 * pagina unica: si conserva com'era, non si migliora.
 */
export function DdpDistintaView({
  model,
  officina,
  statusDefs,
  statoLabel,
  visibleCols,
  printKey,
  focusSection,
  collapsedParentIds,
  parentIdsWithChildren,
  toggleParentCollapse,
}: DdpViewProps) {
  // Alla prima apertura solo la distinta è aperta: i feedback sono un di più.
  const [openIds, setOpenIds] = React.useState<Set<string>>(
    () => new Set(["distinta"])
  )

  const toggle = React.useCallback((id: string) => {
    setOpenIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }, [])

  // Arrivo da un drill-down (`?tab=distinta&sez=…`): la sezione richiesta si apre e si porta
  // sotto gli occhi, altrimenti il deep-link atterrerebbe su una fisarmonica chiusa.
  React.useEffect(() => {
    if (!focusSection) return
    setOpenIds((prev) => new Set(prev).add(focusSection))
    document
      .getElementById(`ddp-section-${focusSection}`)
      ?.scrollIntoView({ behavior: "smooth", block: "start" })
  }, [focusSection])

  return (
    <div className="flex flex-col gap-3">
      <DdpSection
        id="distinta"
        title="Dati Distinta"
        count={model.distinta.length}
        open={openIds.has("distinta")}
        onToggle={toggle}
        onPrint={() => printKey("distinta")}
      >
        <DdpRowsTable
          rows={model.distinta}
          statusDefs={statusDefs}
          statoLabel={statoLabel}
          officina={officina}
          visibleCols={visibleCols}
          collapsedParentIds={collapsedParentIds}
          parentIdsWithChildren={parentIdsWithChildren}
          toggleParentCollapse={toggleParentCollapse}
        />
      </DdpSection>

      <DdpSection
        id="acq"
        title="Feedback Acquisti"
        open={openIds.has("acq")}
        onToggle={toggle}
        onPrint={() => printKey("acq")}
      >
        <DdpStatusBreakdown
          bars={model.feedbackAcquisti}
          subtitle={model.acqSub}
          sectionLabel="Feedback acquisti"
        />
      </DdpSection>

      <DdpSection
        id="mag"
        title="Feedback Magazzino"
        open={openIds.has("mag")}
        onToggle={toggle}
        onPrint={() => printKey("mag")}
      >
        <DdpStatusBreakdown
          bars={model.feedbackMagazzino}
          subtitle={model.magSub}
          sectionLabel="Feedback magazzino"
        />
      </DdpSection>
    </div>
  )
}
