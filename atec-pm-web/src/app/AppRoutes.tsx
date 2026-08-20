import type { ReactNode } from "react"
import { Navigate, Route } from "react-router-dom"

import { FeatureGuard } from "@/app/FeatureGuard"
import { ModulePlaceholder } from "@/components/shared/ModulePlaceholder"
import { ALL_NAV_ITEMS, firstAccessibleNavPath } from "@/config/navigation"
import { canAccessFeature } from "@/lib/auth/permissions"
import { ROUTE_FEATURE_KEYS } from "@/lib/auth/route-features"
import { CatalogoPage } from "@/features/catalogo/CatalogoPage"
import { CatalogoPreventiviPage } from "@/features/catalogo-preventivi/CatalogoPreventiviPage"
import { ClientiPage } from "@/features/clienti/ClientiPage"
import { CodexPage } from "@/features/codex/CodexPage"
import { CodexRecodePage } from "@/features/codex/CodexRecodePage"
import { CodexCompositionPage } from "@/features/codex-composizione/CodexCompositionPage"
import { BugReportsPage } from "@/features/bug-reports/BugReportsPage"
import { ChatPage } from "@/features/chat/ChatPage"
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
import { CatalogoFunzioniPage } from "@/features/permessi/CatalogoFunzioniPage"
import { MasterTemplatesPage } from "@/features/permessi/MasterTemplatesPage"
import { PermessiPage } from "@/features/permessi/PermessiPage"
import { SchedaPersonaPage } from "@/features/permessi/SchedaPersonaPage"
import { QuoteDetailPage } from "@/features/preventivi/QuoteDetailPage"
import { QuotesHomePage } from "@/features/preventivi/QuotesHomePage"
import { FeriePage } from "@/features/risorse/FeriePage"
import { ResourcePlannerPage } from "@/features/risorse/ResourcePlannerPage"
import { BackupPage } from "@/features/admin/backup/BackupPage"
import { ActivityCatalogPage } from "@/features/admin/activity-catalog/ActivityCatalogPage"
import { SalConditionsPage } from "@/features/admin/sal/SalConditionsPage"
import { SalPage } from "@/features/sal/SalPage"
import { OreCommessaPage } from "@/features/ore-commessa/OreCommessaPage"
import { TrasfertaPage } from "@/features/trasferta/TrasfertaPage"
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
import { WorkRequestsPage } from "@/features/work-requests/WorkRequestsPage"
import { BilancioPage } from "@/features/bilancio/BilancioPage"

const LIVE_ROUTES: Record<string, ReactNode> = {
  dashboard: <DashboardPage />,
  commesse: <CommessePage />,
  timesheet: <TimesheetPage />,
  chat: <ChatPage />,
  scadenze: <ScadenzePage />,
  "work-requests": <WorkRequestsPage />,
  "acquisti-inbox": <AcquistiPage />,
  "danea-migrazione": <DaneaMigrationPage />,
  risorse: <ResourcePlannerPage />,
  mom: <MoMPage />,
  "mom-note": <MoMNotePage />,
  checklist: <ChecklistPage />,
  "bug-reports": <BugReportsPage />,
  "milestones-summary": <MilestonesPage />,
  sal: <SalPage />,
  trasferta: <TrasfertaPage />,
  "ore-commessa": <OreCommessaPage />,
  bilancio: <BilancioPage />,
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

/**
 * Rotta protetta dal permesso della sua pagina: senza il livello minimo si vede
 * «Accesso negato» invece della pagina, anche entrando dall'indirizzo diretto.
 * Le chiavi delle sotto-pagine stanno in `ROUTE_FEATURE_KEYS`.
 */
function guarded(path: string, element: ReactNode): ReactNode {
  return (
    <FeatureGuard featureKey={ROUTE_FEATURE_KEYS[path]}>{element}</FeatureGuard>
  )
}

/**
 * Rotta di ingresso «/»: chi non vede la Dashboard (reparti a lista bianca) atterra sulla
 * sua prima pagina visibile.
 *
 * La scelta va fatta QUI, al render, e non mentre si costruisce l'albero delle rotte:
 * `AppRoutes()` la valuta `App` PRIMA che `AuthBootstrap` abbia caricato i permessi, quando
 * `canAccessFeature` nega ancora tutto — la rotta resterebbe congelata su quella risposta
 * e l'utente sarebbe accolto da «Accesso negato» all'ingresso.
 *
 * Chi non ha proprio niente non passa di qui: `AppShell` sostituisce il contenuto con
 * l'avviso «Nessun permesso assegnato» prima che `HomeRoute` possa rimbalzarlo.
 */
function HomeRoute({ children }: { children: ReactNode }) {
  if (canAccessFeature("nav.dashboard")) {
    return <>{children}</>
  }
  const fallback = firstAccessibleNavPath(canAccessFeature)
  return fallback ? <Navigate to={fallback} replace /> : <>{children}</>
}

export function AppRoutes() {
  return (
    <>
      <Route
        path="risorse/ferie"
        element={guarded("risorse/ferie", <FeriePage />)}
      />
      <Route
        path="codex/ricodifica"
        element={guarded("codex/ricodifica", <CodexRecodePage />)}
      />
      {/* La vecchia Inbox Acquisti è stata rimossa (27/07/2026): i preferiti restano validi. */}
      <Route path="acquisti/legacy" element={<Navigate to="/acquisti" replace />} />
      {/* Il «Prospetto SAL» come voce di menu autonoma è stato rimosso (03/08/2026):
          la stessa vista vive dentro /sal. I preferiti restano validi. */}
      <Route
        path="sal/prospetto"
        element={<Navigate to="/sal?view=prospetto" replace />}
      />
      <Route
        path="commesse/:projectId"
        element={guarded("commesse/:projectId", <CommessePage />)}
      />
      <Route
        path="commesse/:projectId/:section"
        element={guarded("commesse/:projectId/:section", <CommessePage />)}
      />
      <Route path="mom/:id" element={guarded("mom/:id", <MoMDetailPage />)} />
      <Route
        path="gestore-ddp/feedback/acquisti"
        element={<Navigate to="/acquisti" replace />}
      />
      <Route
        path="gestore-ddp/feedback/magazzino"
        element={guarded(
          "gestore-ddp/feedback/magazzino",
          <DdpFeedbackMagazzinoPage />
        )}
      />
      <Route
        path="gestore-ddp/controllo"
        element={guarded("gestore-ddp/controllo", <DdpControlloPage />)}
      />
      <Route
        path="gestore-ddp/controllo/:view"
        element={guarded("gestore-ddp/controllo/:view", <DdpControlloPage />)}
      />
      <Route
        path="gestore-ddp/consegne"
        element={<Navigate to="/gestore-ddp/controllo/consegne" replace />}
      />
      <Route
        path="gestore-ddp/:projectId"
        element={guarded("gestore-ddp/:projectId", <DdpSintesiPage />)}
      />
      <Route
        path="preventivi/:id"
        element={guarded("preventivi/:id", <QuoteDetailPage />)}
      />
      <Route
        path="permessi/persona/:employeeId"
        element={guarded("permessi/persona/:employeeId", <SchedaPersonaPage />)}
      />
      <Route
        path="permessi/master"
        element={guarded("permessi/master", <MasterTemplatesPage />)}
      />
      <Route
        path="permessi/catalogo"
        element={guarded("permessi/catalogo", <CatalogoFunzioniPage />)}
      />
      {ALL_NAV_ITEMS.map((item) => {
        const page =
          LIVE_ROUTES[item.id] ?? (
            <ModulePlaceholder
              title={item.label}
              description={item.description}
              status={item.status}
            />
          )
        const element = (
          <FeatureGuard featureKey={item.featureKey}>{page}</FeatureGuard>
        )

        if (item.path === "/") {
          return (
            <Route key={item.id} index element={<HomeRoute>{element}</HomeRoute>} />
          )
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
