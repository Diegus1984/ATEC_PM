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

import {
  CostSectionsTreePanel,
  DepartmentDragPanel,
} from "./CostSectionsTreePanel"
import { DepartmentDialog } from "./config-sections-dialogs"

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

  async function refreshAll() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["cost-section-groups"] }),
      queryClient.invalidateQueries({ queryKey: ["cost-section-templates"] }),
      queryClient.invalidateQueries({ queryKey: ["departments"] }),
      queryClient.invalidateQueries({ queryKey: ["phase-templates"] }),
    ])
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Configurazione sezioni</CardTitle>
              <CardDescription>
                Albero gruppi → sezioni → reparti e fasi template (come WPF)
              </CardDescription>
            </div>
            <Button variant="outline" size="sm" onClick={() => refreshAll()}>
              <RefreshCw />
              Aggiorna
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <p className="text-sm text-muted-foreground">Caricamento…</p>
          ) : (
            <div className="grid gap-4 lg:grid-cols-[240px_1fr]">
              <div className="rounded-lg border p-3">
                <DepartmentDragPanel
                  departments={departmentsQuery.data ?? []}
                  onEditDepartment={(dept) => setDeptDialog(dept)}
                  onAddDepartment={() => setDeptDialog("new")}
                />
              </div>
              <CostSectionsTreePanel
                groups={groupsQuery.data ?? []}
                templates={templatesQuery.data ?? []}
                departments={departmentsQuery.data ?? []}
                phases={phasesQuery.data ?? []}
                onRefresh={refreshAll}
              />
            </div>
          )}
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
