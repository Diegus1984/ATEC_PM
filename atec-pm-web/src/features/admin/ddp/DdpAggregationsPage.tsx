import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { RefreshCw, Save } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  fetchDdpAggregations,
  fetchDdpStatuses,
  updateDdpAggregation,
} from "@/lib/api/ddp-config"
import type { DdpAggregation } from "@/lib/api/types"

export function DdpAggregationsPage() {
  const queryClient = useQueryClient()
  const [aggregations, setAggregations] = React.useState<DdpAggregation[]>([])
  const [matrix, setMatrix] = React.useState<Record<string, Record<number, boolean>>>(
    {}
  )
  const [message, setMessage] = React.useState<string | null>(null)

  const aggsQuery = useQuery({
    queryKey: ["ddp-aggregations"],
    queryFn: fetchDdpAggregations,
  })

  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })

  React.useEffect(() => {
    if (!aggsQuery.data) return
    setAggregations(
      aggsQuery.data.map((agg) => ({
        ...agg,
        name: agg.name,
        description: agg.description,
      }))
    )
    const nextMatrix: Record<string, Record<number, boolean>> = {}
    for (const status of statusesQuery.data ?? []) {
      nextMatrix[status.statusKey] = {}
      for (const agg of aggsQuery.data) {
        nextMatrix[status.statusKey][agg.id] = agg.statusKeys.includes(
          status.statusKey
        )
      }
    }
    setMatrix(nextMatrix)
  }, [aggsQuery.data, statusesQuery.data])

  const saveMutation = useMutation({
    mutationFn: async () => {
      for (const agg of aggregations) {
        const statusKeys = Object.entries(matrix)
          .filter(([, cols]) => cols[agg.id])
          .map(([key]) => key)
        await updateDdpAggregation(agg.id, {
          id: agg.id,
          name: agg.name,
          description: agg.description,
          statusKeys,
        })
      }
    },
    onSuccess: async () => {
      setMessage("Aggregazioni salvate.")
      await queryClient.invalidateQueries({ queryKey: ["ddp-aggregations"] })
    },
    onError: (err: Error) => setMessage(err.message),
  })

  const statuses = (statusesQuery.data ?? []).slice().sort((a, b) => {
    if (a.sortOrder !== b.sortOrder) return a.sortOrder - b.sortOrder
    return a.statusKey.localeCompare(b.statusKey)
  })

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Aggregazioni DDP</CardTitle>
              <CardDescription>
                Matrice stati × aggregazioni (A1–A9). Modifica nome/descrizione e
                appartenenze.
              </CardDescription>
            </div>
            <div className="flex gap-2">
              <Button
                variant="outline"
                onClick={() => {
                  void aggsQuery.refetch()
                  void statusesQuery.refetch()
                }}
              >
                <RefreshCw />
                Aggiorna
              </Button>
              <Button
                onClick={() => saveMutation.mutate()}
                disabled={saveMutation.isPending || aggsQuery.isLoading}
              >
              <Save />
              {saveMutation.isPending ? "Salvataggio…" : "Salva tutto"}
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-6">
          {message ? (
            <p className="text-sm text-muted-foreground">{message}</p>
          ) : null}

          <GridScroller className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Codice</TableHead>
                <TableHead>Nome</TableHead>
                <TableHead>Descrizione</TableHead>
                <TableHead>Tipo</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {aggregations.map((agg, index) => (
                <TableRow key={agg.id}>
                  <TableCell className="font-mono font-medium">{agg.code}</TableCell>
                  <TableCell>
                    <Input
                      value={agg.name}
                      onChange={(event) => {
                        const next = [...aggregations]
                        next[index] = { ...agg, name: event.target.value }
                        setAggregations(next)
                      }}
                    />
                  </TableCell>
                  <TableCell>
                    <Input
                      value={agg.description}
                      onChange={(event) => {
                        const next = [...aggregations]
                        next[index] = { ...agg, description: event.target.value }
                        setAggregations(next)
                      }}
                    />
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {agg.kind}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          </GridScroller>

          <GridScroller className="rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="min-w-40 sticky left-0 bg-background">
                    Stato
                  </TableHead>
                  {aggregations.map((agg) => (
                    <TableHead
                      key={agg.id}
                      className="min-w-16 text-center"
                      title={agg.name}
                    >
                      {agg.code}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {statuses.map((status) => (
                  <TableRow key={status.statusKey}>
                    <TableCell className="sticky left-0 bg-background">
                      <div className="text-sm font-medium">{status.label}</div>
                      <div className="font-mono text-xs text-muted-foreground">
                        {status.statusKey}
                      </div>
                    </TableCell>
                    {aggregations.map((agg) => (
                      <TableCell key={agg.id} className="text-center">
                        <Checkbox
                          checked={matrix[status.statusKey]?.[agg.id] ?? false}
                          onCheckedChange={(checked) => {
                            setMatrix((prev) => ({
                              ...prev,
                              [status.statusKey]: {
                                ...prev[status.statusKey],
                                [agg.id]: !!checked,
                              },
                            }))
                          }}
                        />
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </GridScroller>
        </CardContent>
      </Card>
    </div>
  )
}
