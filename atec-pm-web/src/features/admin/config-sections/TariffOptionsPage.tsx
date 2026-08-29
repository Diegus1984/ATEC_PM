import * as React from "react"
import { useQueryClient } from "@tanstack/react-query"
import { RefreshCw } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { TariffOptionsPanel } from "./TariffOptionsPanel"

export function TariffOptionsPage() {
  const queryClient = useQueryClient()

  const refreshAll = React.useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: ["tariff-options"] })
  }, [queryClient])

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Anagrafica tariffe</CardTitle>
              <CardDescription>
                I valori proposti dai calcoli: tariffe orarie delle Officine interne, rimborso
                km, vitto, alloggio e indennità di trasferta
              </CardDescription>
            </div>
            <Button variant="outline" size="sm" onClick={() => void refreshAll()}>
              <RefreshCw />
              Aggiorna
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <TariffOptionsPanel />
        </CardContent>
      </Card>
    </div>
  )
}
