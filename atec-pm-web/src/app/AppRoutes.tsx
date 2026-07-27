import type { ReactNode } from "react"
import { Navigate, Route } from "react-router-dom"

import { ModulePlaceholder } from "@/components/shared/ModulePlaceholder"
import { ALL_NAV_ITEMS } from "@/config/navigation"
import { CatalogoPage } from "@/features/catalogo/CatalogoPage"
import { CatalogoPreventiviPage } from "@/features/catalogo-preventivi/CatalogoPreventiviPage"
import { ClientiPage } from "@/features/clienti/ClientiPage"
import { CodexPage } from "@/features/codex/CodexPage"
import { CodexRecodePage } from "@/features/codex/CodexRecodePage"
import { CodexCompositionPage } from "@/features/codex-composizione/CodexCompositionPage"
import { ChecklistPage } from "@/features/checklist/ChecklistPage"
import { CommessePage } from "@/features/commesse/CommessePage"
import { DashboardPage } from "@/features/dashboard/DashboardPage"
import { MilestonesPage } from "@/features/milestones/MilestonesPage"
import { FornitoriPage } from "@/features/fornitori/FornitoriPage"
import { GammaRobotPage } from "@/features/gamma-robot/GammaRobotPage"
import { DdpControlloPage } from "@/features/gestore-ddp/DdpControlloPage"
import { DdpFeedbackMagazzinoPage } from "@/features/gestore-ddp/DdpFeedbackMagazzinoPage"
import { DdpSintesiPage } from "@/features/gestore-ddp/DdpSintesiPage"
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
import { SalProspettoPage } from "@/features/sal/SalProspettoPage"
import { ConfigSectionsPage } from "@/features/admin/config-sections/ConfigSectionsPage"
import { DdpAggregationsPage } from "@/features/admin/ddp/DdpAggregationsPage"
import { DdpConfigPage } from "@/features/admin/ddp/DdpConfigPage"
import { ProjectTemplatesPage } from "@/features/admin/templates/ProjectTemplatesPage"
import { DigestEmailPage } from "@/features/admin/digest/DigestEmailPage"
import { TimesheetPage } from "@/features/timesheet/TimesheetPage"
import { UtentiPage } from "@/features/utenti/UtentiPage"
import ScadenzePage from "@/features/scadenze/ScadenzePage"
import { AcquistiPage } from "@/features/acquisti/AcquistiPage"
import { DaneaMigrationPage } from "@/features/danea-migrazione/DaneaMigrationPage"
import { OfficinaPage } from "@/features/officina/OfficinaPage"
import { WorkRequestsPage } from "@/features/work-requests/WorkRequestsPage"

const LIVE_ROUTES: Record<string, ReactNode> = {
  dashboard: <DashboardPage />,
  commesse: <CommessePage />,
  timesheet: <TimesheetPage />,
  scadenze: <ScadenzePage />,
  "work-requests": <WorkRequestsPage />,
  "officina-inbox": <OfficinaPage />,
  "acquisti-inbox": <AcquistiPage />,
  "danea-migrazione": <DaneaMigrationPage />,
  risorse: <ResourcePlannerPage />,
  mom: <MoMPage />,
  "mom-note": <MoMNotePage />,
  checklist: <ChecklistPage />,
  "milestones-summary": <MilestonesPage />,
  sal: <SalPage />,
  "sal-prospetto": <SalProspettoPage />,
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
  "gamma-robot": <GammaRobotPage />,
  codex: <CodexPage />,
  "codex-composizione": <CodexCompositionPage />,
  "gestore-ddp": <DdpControlloPage defaultView="gestore" />,
  preventivi: <QuotesHomePage />,
  utenti: <UtentiPage />,
  permessi: <PermessiPage />,
}

export function AppRoutes() {
  return (
    <>
      <Route path="risorse/ferie" element={<FeriePage />} />
      <Route path="codex/ricodifica" element={<CodexRecodePage />} />
      {/* La vecchia Inbox Acquisti è stata rimossa (27/07/2026): i preferiti restano validi. */}
      <Route path="acquisti/legacy" element={<Navigate to="/acquisti" replace />} />
      <Route path="commesse/:projectId" element={<CommessePage />} />
      <Route path="commesse/:projectId/:section" element={<CommessePage />} />
      <Route path="mom/:id" element={<MoMDetailPage />} />
      <Route
        path="gestore-ddp/feedback/acquisti"
        element={<Navigate to="/acquisti" replace />}
      />
      <Route path="gestore-ddp/feedback/magazzino" element={<DdpFeedbackMagazzinoPage />} />
      <Route path="gestore-ddp/controllo" element={<DdpControlloPage />} />
      <Route path="gestore-ddp/controllo/:view" element={<DdpControlloPage />} />
      <Route
        path="gestore-ddp/consegne"
        element={<Navigate to="/gestore-ddp/controllo/consegne" replace />}
      />
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
