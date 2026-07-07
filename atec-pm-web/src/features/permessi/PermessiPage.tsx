import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Plus, RefreshCw, Search, Trash2 } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { notifyError } from "@/lib/toast"
import { RowActionsMenu } from "@/components/shared/row-actions"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  createAuthFeature,
  deleteAuthFeature,
  fetchAuthFeatures,
  fetchAuthLevels,
  updateAuthFeature,
} from "@/lib/api/auth-levels"
import type { AuthFeatureDto, AuthLevelDto } from "@/lib/api/types"


const BEHAVIOR_OPTIONS = [
  { value: "HIDDEN", label: "Nascondi" },
  { value: "DISABLED", label: "Disabilita" },
  { value: "ALERT", label: "Avvisa" },
]

export function PermessiPage() {
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const [search, setSearch] = React.useState("")
  const [features, setFeatures] = React.useState<AuthFeatureDto[]>([])
  const [addOpen, setAddOpen] = React.useState(false)

  const levelsQuery = useQuery({
    queryKey: ["auth-levels"],
    queryFn: fetchAuthLevels,
  })

  const featuresQuery = useQuery({
    queryKey: ["auth-features"],
    queryFn: fetchAuthFeatures,
  })

  React.useEffect(() => {
    if (featuresQuery.data) {
      setFeatures(featuresQuery.data)
    }
  }, [featuresQuery.data])

  const levels = React.useMemo(
    () =>
      (levelsQuery.data ?? [])
        .slice()
        .sort((a, b) => a.sortOrder - b.sortOrder),
    [levelsQuery.data]
  )

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["auth-features"] })

  const updateMutation = useMutation({
    mutationFn: ({
      id,
      minLevel,
      behavior,
    }: {
      id: number
      minLevel: number
      behavior: string
    }) => updateAuthFeature(id, { minLevel, behavior }),
    onError: (err: Error) => {
      notifyError(err)
      void invalidate()
    },
  })

  const deleteMutation = useMutation({
    mutationFn: deleteAuthFeature,
    onSuccess: () => invalidate(),
    onError: (err: Error) => notifyError(err),
  })

  function patchFeature(id: number, patch: Partial<AuthFeatureDto>) {
    setFeatures((prev) =>
      prev.map((feature) =>
        feature.id === id ? { ...feature, ...patch } : feature
      )
    )
    const target = features.find((feature) => feature.id === id)
    if (!target) return
    const merged = { ...target, ...patch }
    updateMutation.mutate({
      id,
      minLevel: merged.minLevel,
      behavior: merged.behavior,
    })
  }

  async function handleDelete(feature: AuthFeatureDto) {
    const ok = await confirm({
      title: "Elimina funzione",
      description: `Eliminare la funzione "${feature.displayName}"?`,
      confirmLabel: "Elimina",
    })
    if (ok) {
      deleteMutation.mutate(feature.id)
    }
  }

  const rows = React.useMemo(() => {
    const term = search.trim().toLowerCase()
    const sorted = features
      .slice()
      .sort(
        (a, b) =>
          a.category.localeCompare(b.category) ||
          a.displayName.localeCompare(b.displayName)
      )
    if (!term) return sorted
    return sorted.filter((feature) =>
      [feature.displayName, feature.category, feature.featureKey]
        .join(" ")
        .toLowerCase()
        .includes(term)
    )
  }, [features, search])

  const categories = React.useMemo(
    () =>
      Array.from(new Set(features.map((feature) => feature.category)))
        .filter(Boolean)
        .sort(),
    [features]
  )

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Permessi</CardTitle>
              <CardDescription>
                Livello minimo e comportamento per ogni funzione ({features.length})
              </CardDescription>
            </div>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  void levelsQuery.refetch()
                  void featuresQuery.refetch()
                }}
              >
                <RefreshCw />
                Aggiorna
              </Button>
              <Button size="sm" onClick={() => setAddOpen(true)}>
                <Plus />
                Nuova funzione
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {/* Legenda livelli */}
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            <span>Gerarchia ruoli:</span>
            {levels.map((level) => (
              <Badge key={level.id} variant="outline">
                {level.levelValue} · {level.displayName}
              </Badge>
            ))}
            <span>— ✓ = accesso consentito</span>
          </div>

          <div className="relative max-w-sm">
            <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
            <Input
              value={search}
              placeholder="Cerca funzione, categoria, codice…"
              className="pl-8"
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>

          {featuresQuery.isLoading || levelsQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">Caricamento…</p>
          ) : null}
          {featuresQuery.isError ? (
            <p className="text-sm text-destructive">
              {(featuresQuery.error as Error).message}
            </p>
          ) : null}

          <div className="overflow-x-auto rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Categoria</TableHead>
                  <TableHead>Funzione</TableHead>
                  <TableHead>Codice</TableHead>
                  <TableHead className="w-40">Livello minimo</TableHead>
                  {levels.map((level) => (
                    <TableHead
                      key={level.id}
                      className="w-14 text-center"
                      title={level.displayName}
                    >
                      {level.displayName}
                    </TableHead>
                  ))}
                  <TableHead className="w-36">Se non autorizzato</TableHead>
                  <TableHead className="w-12" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.length === 0 ? (
                  <TableRow>
                    <TableCell
                      colSpan={6 + levels.length}
                      className="h-24 text-center text-muted-foreground"
                    >
                      Nessuna funzione.
                    </TableCell>
                  </TableRow>
                ) : (
                  rows.map((feature) => (
                    <TableRow key={feature.id}>
                      <TableCell>
                        <Badge variant="secondary">{feature.category}</Badge>
                      </TableCell>
                      <TableCell className="font-medium">
                        {feature.displayName}
                      </TableCell>
                      <TableCell className="font-mono text-xs text-muted-foreground">
                        {feature.featureKey}
                      </TableCell>
                      <TableCell>
                        <Select
                          value={String(feature.minLevel)}
                          onValueChange={(value) =>
                            patchFeature(feature.id, {
                              minLevel: Number(value),
                            })
                          }
                        >
                          <SelectTrigger size="sm" className="w-full">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {levels.map((level) => (
                              <SelectItem
                                key={level.id}
                                value={String(level.levelValue)}
                              >
                                {level.levelValue} · {level.displayName}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </TableCell>
                      {levels.map((level) => (
                        <TableCell key={level.id} className="text-center">
                          {level.levelValue >= feature.minLevel ? (
                            <span className="font-semibold text-emerald-600">
                              ✓
                            </span>
                          ) : (
                            <span className="text-muted-foreground/40">·</span>
                          )}
                        </TableCell>
                      ))}
                      <TableCell>
                        <Select
                          value={feature.behavior}
                          onValueChange={(value) =>
                            patchFeature(feature.id, { behavior: value })
                          }
                        >
                          <SelectTrigger size="sm" className="w-full">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {BEHAVIOR_OPTIONS.map((option) => (
                              <SelectItem key={option.value} value={option.value}>
                                {option.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </TableCell>
                      <TableCell>
                        <div className="flex justify-end">
                          <RowActionsMenu
                            actions={[
                              {
                                label: "Elimina",
                                icon: Trash2,
                                destructive: true,
                                onClick: () => {
                                  void handleDelete(feature)
                                },
                              },
                            ]}
                          />
                        </div>
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>

      <AddFeatureDialog
        open={addOpen}
        levels={levels}
        categories={categories}
        onClose={() => setAddOpen(false)}
        onSaved={async () => {
          setAddOpen(false)
          await invalidate()
        }}
      />
    </div>
  )
}

function AddFeatureDialog({
  open,
  levels,
  categories,
  onClose,
  onSaved,
}: {
  open: boolean
  levels: AuthLevelDto[]
  categories: string[]
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [displayName, setDisplayName] = React.useState("")
  const [featureKey, setFeatureKey] = React.useState("")
  const [category, setCategory] = React.useState("navigation")
  const [minLevel, setMinLevel] = React.useState("0")
  const [error, setError] = React.useState<string | null>(null)

  const categoryOptions = React.useMemo(() => {
    const base = ["navigation", "action", "financial", "admin", "report"]
    return Array.from(new Set([...categories, ...base])).sort()
  }, [categories])

  React.useEffect(() => {
    if (!open) return
    setDisplayName("")
    setFeatureKey("")
    setCategory(categories[0] ?? "navigation")
    setMinLevel(String(levels[0]?.levelValue ?? 0))
    setError(null)
  }, [open, categories, levels])

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!displayName.trim() || !featureKey.trim()) {
        throw new Error("Nome e codice interno sono obbligatori.")
      }
      await createAuthFeature({
        featureKey: featureKey.trim().toLowerCase(),
        displayName: displayName.trim(),
        category,
        minLevel: Number(minLevel),
        behavior: "HIDDEN",
      })
    },
    onSuccess: onSaved,
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Nuova funzione</DialogTitle>
        </DialogHeader>
        <div className="space-y-4">
          <div className="grid gap-2">
            <Label>Nome funzione</Label>
            <Input
              value={displayName}
              autoFocus
              placeholder="Es. Crea ordine"
              onChange={(event) => setDisplayName(event.target.value)}
            />
          </div>
          <div className="grid gap-2">
            <Label>Codice interno</Label>
            <Input
              value={featureKey}
              className="font-mono"
              placeholder="area.nome_funzione — es. nav.mia_pagina"
              onChange={(event) => setFeatureKey(event.target.value)}
            />
            <p className="text-xs text-muted-foreground">
              Formato: area.nome_funzione (es. nav.mia_pagina, action.crea_ordine).
            </p>
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div className="grid gap-2">
              <Label>Categoria</Label>
              <Select value={category} onValueChange={setCategory}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {categoryOptions.map((option) => (
                    <SelectItem key={option} value={option}>
                      {option}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="grid gap-2">
              <Label>Livello minimo</Label>
              <Select value={minLevel} onValueChange={setMinLevel}>
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {levels.map((level) => (
                    <SelectItem key={level.id} value={String(level.levelValue)}>
                      {level.levelValue} · {level.displayName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={!displayName.trim() || !featureKey.trim() || saveMutation.isPending}
          >
            Crea
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
