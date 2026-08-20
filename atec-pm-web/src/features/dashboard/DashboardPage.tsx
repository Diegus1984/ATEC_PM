import * as React from "react"

import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { DashboardCommesseTables } from "@/features/dashboard/components/DashboardCommesseTables"
import { DashboardControlliSection } from "@/features/dashboard/components/DashboardControlliSection"
import { DashboardFolders } from "@/features/dashboard/components/DashboardFolders"

/**
 * Pagina d'ingresso. Dalla #88 si apre sulle due tabelle **Commesse / Altre Attività**
 * (Codice · Titolo · Cliente · Stato cliccabile, senza colonna Ore): la vecchia
 * «Panoramica» — 4 card KPI + grafico ore — è stata eliminata su richiesta esplicita
 * della segnalazione. Le **Cartelle** (blocco 7) restano nella seconda scheda: non le ha
 * chieste via nessuno, e la scelta resta ricordata per chi ci lavora.
 *
 * Sotto le tabelle commesse, la #113 introduce la sezione «Gestione Controlli» con 4 card
 * di avviso/controllo per il PM (DDP aggiornate, SAL fatturazione, Trasferte, Ore Commessa).
 */
const VIEW_STORAGE_KEY = "dashboard:view:v2"

type DashboardView = "commesse" | "cartelle"

function loadView(): DashboardView {
  try {
    return localStorage.getItem(VIEW_STORAGE_KEY) === "cartelle"
      ? "cartelle"
      : "commesse"
  } catch {
    return "commesse"
  }
}

export function DashboardPage() {
  const [view, setView] = React.useState<DashboardView>(loadView)

  function changeView(value: string) {
    const next = value as DashboardView
    setView(next)
    try {
      localStorage.setItem(VIEW_STORAGE_KEY, next)
    } catch {
      // storage non disponibile: nessuna persistenza
    }
  }

  return (
    <div className="flex flex-col gap-4 md:gap-6">
      <Tabs value={view} onValueChange={changeView}>
        <TabsList>
          <TabsTrigger value="commesse">Commesse</TabsTrigger>
          <TabsTrigger value="cartelle">Cartelle</TabsTrigger>
        </TabsList>
      </Tabs>

      {view === "commesse" ? (
        <div className="flex flex-col gap-6">
          <DashboardCommesseTables />
          <DashboardControlliSection />
        </div>
      ) : (
        <DashboardFolders />
      )}
    </div>
  )
}
