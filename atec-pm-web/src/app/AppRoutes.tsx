import type { ReactNode } from "react"
import { Route } from "react-router-dom"

import { ModulePlaceholder } from "@/components/shared/ModulePlaceholder"
import { ALL_NAV_ITEMS } from "@/config/navigation"
import { CatalogoPage } from "@/features/catalogo/CatalogoPage"
import { CatalogoPreventiviPage } from "@/features/catalogo-preventivi/CatalogoPreventiviPage"
import { ClientiPage } from "@/features/clienti/ClientiPage"
import { CodexPage } from "@/features/codex/CodexPage"
import { CodexCompositionPage } from "@/features/codex-composizione/CodexCompositionPage"
import { ChecklistPage } from "@/features/checklist/ChecklistPage"
import { CommessePage } from "@/features/commesse/CommessePage"
import { DashboardPage } from "@/features/dashboard/DashboardPage"
import { MilestonesPage } from "@/features/milestones/MilestonesPage"
import { FornitoriPage } from "@/features/fornitori/FornitoriPage"
import { DdpFeedbackAcquistiPage } from "@/features/gestore-ddp/DdpFeedbackAcquistiPage"
import { DdpFeedbackMagazzinoPage } from "@/features/gestore-ddp/DdpFeedbackMagazzinoPage"
import { DdpSintesiPage } from "@/features/gestore-ddp/DdpSintesiPage"
import { GestoreDdpPage } from "@/features/gestore-ddp/GestoreDdpPage"
import { MoMDetailPage } from "@/features/mom/MoMDetailPage"
import { MoMNotePage } from "@/features/mom/MoMNotePage"
import { MoMPage } from "@/features/mom/MoMPage"
import { PermessiPage } from "@/features/permessi/PermessiPage"
import { QuoteDetailPage } from "@/features/preventivi/QuoteDetailPage"
import { QuotesHomePage } from "@/features/preventivi/QuotesHomePage"
import { FeriePage } from "@/features/risorse/FeriePage"
import { ResourcePlannerPage } from "@/features/risorse/ResourcePlannerPage"
import { BackupPage } from "@/features/admin/backup/BackupPage"
import { ActivityCatalogPage } from "@/features/admin/activity-catalog/ActivityCatalogPage"
import { SalConditionsPage } from "@/features/admin/sal/SalConditionsPage"
import { SalPage } from "@/features/sal/SalPage"
import { ConfigSectionsPage } from "@/features/admin/config-sections/ConfigSectionsPage"
import { DdpAggregationsPage } from "@/features/admin/ddp/DdpAggregationsPage"
import { DdpConfigPage } from "@/features/admin/ddp/DdpConfigPage"
import { ProjectTemplatesPage } from "@/features/admin/templates/ProjectTemplatesPage"
import { DigestEmailPage } from "@/features/admin/digest/DigestEmailPage"
import { TimesheetPage } from "@/features/timesheet/TimesheetPage"
import { UtentiPage } from "@/features/utenti/UtentiPage"

const LIVE_ROUTES: Record<string, ReactNode> = {
  dashboard: <DashboardPage />,
  commesse: <CommessePage />,
  timesheet: <TimesheetPage />,
  risorse: <ResourcePlannerPage />,
  mom: <MoMPage />,
  "mom-note": <MoMNotePage />,
  checklist: <ChecklistPage />,
  "milestones-summary": <MilestonesPage />,
  sal: <SalPage />,
  "config-sezioni": <ConfigSectionsPage />,
  "anagrafica-attivita": <ActivityCatalogPage />,
  "sal-conditions": <SalConditionsPage />,
  "ddp-destinazioni": <DdpConfigPage />,
  "ddp-aggregazioni": <DdpAggregationsPage />,
  backup: <BackupPage />,
  "template-commesse": <ProjectTemplatesPage />,
  "digest-email": <DigestEmailPage />,
  clienti: <ClientiPage />,
  fornitori: <FornitoriPage />,
  catalogo: <CatalogoPage />,
  "catalogo-preventivi": <CatalogoPreventiviPage />,
  codex: <CodexPage />,
  "codex-composizione": <CodexCompositionPage />,
  "gestore-ddp": <GestoreDdpPage />,
  preventivi: <QuotesHomePage />,
  utenti: <UtentiPage />,
  permessi: <PermessiPage />,
}

export function AppRoutes() {
  return (
    <>
      <Route path="risorse/ferie" element={<FeriePage />} />
      <Route path="commesse/:projectId" element={<CommessePage />} />
      <Route path="commesse/:projectId/:section" element={<CommessePage />} />
      <Route path="mom/:id" element={<MoMDetailPage />} />
      <Route path="gestore-ddp/feedback/acquisti" element={<DdpFeedbackAcquistiPage />} />
      <Route path="gestore-ddp/feedback/magazzino" element={<DdpFeedbackMagazzinoPage />} />
      <Route path="gestore-ddp/:projectId" element={<DdpSintesiPage />} />
      <Route path="preventivi/:id" element={<QuoteDetailPage />} />
      {ALL_NAV_ITEMS.map((item) => {
        const element =
          LIVE_ROUTES[item.id] ?? (
            <ModulePlaceholder
              title={item.label}
              description={item.description}
              status={item.status}
            />
          )

        if (item.path === "/") {
          return <Route key={item.id} index element={element} />
        }

        return (
          <Route
            key={item.id}
            path={item.path.replace(/^\//, "")}
            element={element}
          />
        )
      })}
    </>
  )
}
