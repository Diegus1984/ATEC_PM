import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { RefreshCw } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  fetchCostSectionGroups,
  fetchCostSectionTemplates,
} from "@/lib/api/cost-sections"
import { fetchDepartments } from "@/lib/api/departments"
import { fetchPhaseTemplates } from "@/lib/api/phases"
import type { DepartmentDto } from "@/lib/api/types"
import { useCostSectionsHub } from "@/lib/signalr/use-cost-sections-hub"

import {
  CostSectionsTreePanel,
  DepartmentDragPanel,
} from "./CostSectionsTreePanel"
import { DepartmentDialog } from "./config-sections-dialogs"
import { TariffOptionsPanel } from "./TariffOptionsPanel"

export function ConfigSectionsPage() {
  const queryClient = useQueryClient()
  const [deptDialog, setDeptDialog] = React.useState<
    DepartmentDto | "new" | null
  >(null)

  const groupsQuery = useQuery({
    queryKey: ["cost-section-groups"],
    queryFn: fetchCostSectionGroups,
  })

  const templatesQuery = useQuery({
    queryKey: ["cost-section-templates"],
    queryFn: fetchCostSectionTemplates,
  })

  const departmentsQuery = useQuery({
    queryKey: ["departments"],
    queryFn: fetchDepartments,
  })

  const phasesQuery = useQuery({
    queryKey: ["phase-templates"],
    queryFn: fetchPhaseTemplates,
  })

  const isLoading =
    groupsQuery.isLoading ||
    templatesQuery.isLoading ||
    departmentsQuery.isLoading ||
    phasesQuery.isLoading

  const refreshAll = React.useCallback(async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["cost-section-groups"] }),
      queryClient.invalidateQueries({ queryKey: ["cost-section-templates"] }),
      queryClient.invalidateQueries({ queryKey: ["departments"] }),
      queryClient.invalidateQueries({ queryKey: ["phase-templates"] }),
      queryClient.invalidateQueries({ queryKey: ["tariff-options"] }),
    ])
  }, [queryClient])

  // Ambiente condiviso: qui si lavora in due — uno assegna le fasi, l'altro sistema reparti e
  // tariffe — e ogni modifica cambia l'albero sotto gli occhi dell'altro. Senza questo, il
  // secondo trascina fasi su una sezione che nel frattempo è stata rinominata, spenta o
  // cancellata, e se ne accorge solo ricaricando la pagina.
  useCostSectionsHub(true, () => {
    void refreshAll()
  })

  return (
    <div className="space-y-4">
      {/*
        Card alta quanto lo schermo, come la pagina Officina: **i due dock laterali non si
        muovono mai**, a scorrere è solo la colonna centrale dentro di sé. Il tentativo con
        `position: sticky` non reggeva — la Card di shadcn nasce `overflow-hidden` e un
        antenato che ritaglia lo rende inerte — e comunque i dock restavano fermi solo DOPO
        aver scrollato oltre l'intestazione.
      */}
      <Card className="flex h-[calc(100vh-7rem)] flex-col">
        <CardHeader className="shrink-0">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Configurazione sezioni di costo</CardTitle>
              <CardDescription>
                Sinistra: fasi (d), sempre in elenco — trascinane una su ogni sezione in cui
                serve: è la <em>stessa</em> fase, si aggiunge e non si sposta. Centro: gruppi
                (a) → sezioni (b). Destra: reparti (c).
              </CardDescription>
            </div>
            <Button variant="outline" size="sm" onClick={() => refreshAll()}>
              <RefreshCw />
              Aggiorna
            </Button>
          </div>
        </CardHeader>
        {/* `min-h-0`: senza, il contenuto flex non si lascia comprimere e la colonna
            centrale allunga la pagina invece di scorrere dentro di sé. */}
        <CardContent className="min-h-0 flex-1">
          {isLoading ? (
            <p className="text-sm text-muted-foreground">Caricamento…</p>
          ) : (
            <div className="grid h-full min-h-0 gap-4 lg:grid-cols-[1fr_240px]">
              <CostSectionsTreePanel
                groups={groupsQuery.data ?? []}
                templates={templatesQuery.data ?? []}
                departments={departmentsQuery.data ?? []}
                phases={phasesQuery.data ?? []}
                onRefresh={refreshAll}
              />
              {/* Dock destro fermo: se i reparti superano l'altezza, scorre dentro di sé. */}
              <div className="flex min-h-0 flex-col overflow-y-auto rounded-lg border p-3">
                <DepartmentDragPanel
                  departments={departmentsQuery.data ?? []}
                  onEditDepartment={(dept) => setDeptDialog(dept)}
                  onAddDepartment={() => setDeptDialog("new")}
                />
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Anagrafica tariffe</CardTitle>
          <CardDescription>
            I valori proposti dai calcoli: tariffe orarie delle Officine interne, rimborso
            km, vitto, alloggio e indennità di trasferta
          </CardDescription>
        </CardHeader>
        <CardContent>
          <TariffOptionsPanel />
        </CardContent>
      </Card>

      <DepartmentDialog
        open={deptDialog !== null}
        department={deptDialog === "new" ? null : deptDialog}
        onClose={() => setDeptDialog(null)}
        onSaved={async () => {
          setDeptDialog(null)
          await refreshAll()
        }}
      />
    </div>
  )
}
