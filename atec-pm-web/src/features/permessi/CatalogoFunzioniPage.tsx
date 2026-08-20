import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { RefreshCw, Search, TriangleAlert } from "lucide-react"

import { notifyError } from "@/lib/toast"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { isPersonPermissionEngine } from "@/lib/auth/permissions"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
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
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  fetchAuthFeatures,
  fetchAuthLevels,
  fetchRoleFeatures,
  setRoleFeature,
  updateAuthFeature,
} from "@/lib/api/auth-levels"
import type { AuthFeatureDto, FeatureAccess } from "@/lib/api/types"

const BEHAVIOR_OPTIONS = [
  { value: "HIDDEN", label: "Nascondi" },
  { value: "DISABLED", label: "Disabilita" },
  { value: "ALERT", label: "Avvisa" },
]

/**
 * Un ruolo di reparto (`accessMode = "GRANTS"`, es. AMM) non eredita niente dal livello:
 * la sua colonna è una lista bianca, e la cella gira tra i tre stati con un clic.
 */
const GRANT_CYCLE: Record<string, FeatureAccess | null> = {
  none: "FULL",
  FULL: "READ",
  READ: null,
}

function grantKey(roleName: string, featureKey: string): string {
  return `${roleName}|${featureKey}`
}

/**
 * Catalogo delle funzioni — la vecchia matrice funzioni × ruoli.
 *
 * ⚠️ Dalla Fase A **non comanda più i permessi**: scrive in `auth_role_features` e
 * `auth_features.min_level`, che il motore nuovo non legge. Resta come specchio del catalogo
 * (quali chiavi esistono, come si chiamano) e come pannello del motore VECCHIO, che è la strada
 * del rollback. I permessi delle persone si cambiano dalla loro scheda (`PermessiPage` →
 * `SchedaPersonaPage`), i template dalla pagina Master.
 *
 * 🧹 Passo 7 del rebuild: **«Nuova funzione» ed «Elimina» sono usciti**. Dal passo 2 la tabella
 * è la proiezione di `catalogo-permessi.json` e la riallinea `EnsureCatalogo` a ogni avvio: una
 * chiave creata qui non la usa nessun endpoint (resta orfana e segnalata), una cancellata torna
 * al riavvio dopo aver portato via le sue righe di `auth_role_features`. Una funzione nuova
 * nasce nel file, non in un form.
 */
export function CatalogoFunzioniPage() {
  const queryClient = useQueryClient()
  const [search, setSearch] = React.useState("")
  const [features, setFeatures] = React.useState<AuthFeatureDto[]>([])

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

  const roleFeaturesQuery = useQuery({
    queryKey: ["auth-role-features"],
    queryFn: fetchRoleFeatures,
  })

  const levels = React.useMemo(
    () =>
      (levelsQuery.data ?? [])
        .slice()
        .sort((a, b) => a.sortOrder - b.sortOrder),
    [levelsQuery.data]
  )

  /** Ruoli della gerarchia: sono gli unici che possono fare da «livello minimo». */
  const hierarchyLevels = React.useMemo(
    () => levels.filter((level) => level.accessMode !== "GRANTS"),
    [levels]
  )

  /** Ruoli di reparto: colonne a lista bianca, modificabili con un clic. */
  const deptRoles = React.useMemo(
    () => levels.filter((level) => level.accessMode === "GRANTS"),
    [levels]
  )

  /** "RUOLO|chiave" → READ/FULL */
  const grants = React.useMemo(() => {
    const map = new Map<string, FeatureAccess>()
    for (const row of roleFeaturesQuery.data ?? []) {
      map.set(grantKey(row.roleName, row.featureKey), row.access)
    }
    return map
  }, [roleFeaturesQuery.data])

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["auth-features"] })

  const grantMutation = useMutation({
    mutationFn: setRoleFeature,
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: ["auth-role-features"] }),
    onError: (err: Error) => notifyError(err),
  })

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

  return (
    <div className="space-y-4">
      {isPersonPermissionEngine() ? (
        <Alert variant="destructive">
          <TriangleAlert />
          <AlertTitle>Questa matrice non comanda più i permessi</AlertTitle>
          <AlertDescription>
            I permessi sono passati sulla <strong>persona</strong>: chi vede cosa lo dice una
            riga per dipendente, non più il livello del ruolo. Quello che si salva qui — livello
            minimo, comportamento, concessioni per ruolo — non ha più effetto su nessuno: resta
            solo come configurazione del motore vecchio. Per cambiare i permessi di qualcuno c'è
            la <strong>scheda della persona</strong>; per i pacchetti, la pagina{" "}
            <strong>Master / Template</strong>.
          </AlertDescription>
        </Alert>
      ) : null}
      <Alert>
        <TriangleAlert />
        <AlertTitle>L'elenco delle funzioni si scrive nel catalogo, non qui</AlertTitle>
        <AlertDescription>
          Quali funzioni esistono e come si chiamano lo dice{" "}
          <code>catalogo-permessi.json</code>: il server riallinea questa tabella a ogni avvio.
          Una funzione nuova si aggiunge lì (i test stampano la voce pronta se un endpoint usa
          una chiave che non c'è) e compare qui da sola.
        </AlertDescription>
      </Alert>
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Catalogo funzioni</CardTitle>
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
                  void roleFeaturesQuery.refetch()
                }}
              >
                <RefreshCw />
                Aggiorna
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {/* Legenda livelli */}
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            <span>Gerarchia ruoli:</span>
            {hierarchyLevels.map((level) => (
              <Badge key={level.id} variant="outline">
                {level.levelValue} · {level.displayName}
              </Badge>
            ))}
            <span>— ✓ = accesso consentito</span>
          </div>

          {deptRoles.length > 0 ? (
            <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span>Reparti (fuori gerarchia, vedono solo ciò che è concesso):</span>
              {deptRoles.map((role) => (
                <Badge key={role.id} variant="secondary">
                  {role.displayName}
                </Badge>
              ))}
              <span>
                — clic sulla cella per girare tra · (niente), ✓ (completo) e 👁 (sola
                lettura)
              </span>
            </div>
          ) : null}

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

          <GridScroller className="rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Categoria</TableHead>
                  <TableHead>Funzione</TableHead>
                  <TableHead>Codice</TableHead>
                  <TableHead className="w-40">Livello minimo</TableHead>
                  {hierarchyLevels.map((level) => (
                    <TableHead
                      key={level.id}
                      className="w-14 text-center"
                      title={level.displayName}
                    >
                      {level.displayName}
                    </TableHead>
                  ))}
                  {deptRoles.map((role) => (
                    <TableHead
                      key={role.id}
                      className="w-14 text-center"
                      title={`${role.displayName} — concessioni per ruolo`}
                    >
                      {role.displayName}
                    </TableHead>
                  ))}
                  <TableHead className="w-36">Se non autorizzato</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.length === 0 ? (
                  <TableRow>
                    <TableCell
                      colSpan={5 + hierarchyLevels.length + deptRoles.length}
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
                            {hierarchyLevels.map((level) => (
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
                      {hierarchyLevels.map((level) => (
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
                      {deptRoles.map((role) => {
                        const access =
                          grants.get(grantKey(role.roleName, feature.featureKey)) ??
                          null
                        return (
                          <TableCell key={role.id} className="text-center">
                            <button
                              type="button"
                              className="w-full rounded px-1 py-0.5 hover:bg-muted"
                              title={
                                access === "FULL"
                                  ? `${role.displayName}: accesso completo`
                                  : access === "READ"
                                    ? `${role.displayName}: sola lettura`
                                    : `${role.displayName}: nessun accesso`
                              }
                              onClick={() =>
                                grantMutation.mutate({
                                  roleName: role.roleName,
                                  featureKey: feature.featureKey,
                                  access: GRANT_CYCLE[access ?? "none"],
                                })
                              }
                            >
                              {access === "FULL" ? (
                                <span className="font-semibold text-emerald-600">
                                  ✓
                                </span>
                              ) : access === "READ" ? (
                                <span className="text-amber-600">👁</span>
                              ) : (
                                <span className="text-muted-foreground/40">·</span>
                              )}
                            </button>
                          </TableCell>
                        )
                      })}
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
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </GridScroller>
        </CardContent>
      </Card>
    </div>
  )
}
