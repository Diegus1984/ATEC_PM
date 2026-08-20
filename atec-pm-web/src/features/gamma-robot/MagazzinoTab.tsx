import * as React from "react"
import { useQuery } from "@tanstack/react-query"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { Badge } from "@/components/ui/badge"
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
import { fetchGammaComponents, fetchGammaUsage } from "@/lib/api/gamma-robot"
import type { GammaComponentDto } from "@/lib/api/types"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { formatEuro } from "./helpers"

/** Colonne opzionali del magazzino componenti (Codice e Nome sono l'identità della riga). */
const MAGAZZINO_COLUMNS: { id: string; label: string }[] = [
  { id: "categoria", label: "Categoria" },
  { id: "robot", label: "Robot" },
  { id: "vb", label: "VB €" },
]
const MAGAZZINO_COLUMNS_DEFAULT = Object.fromEntries(
  MAGAZZINO_COLUMNS.map((column) => [column.id, true])
)

export function MagazzinoTab({
  onOpenProduct,
}: {
  onOpenProduct: (productId: number) => void
}) {
  const [search, setSearch] = React.useState("")
  const [selected, setSelected] = React.useState<GammaComponentDto | null>(null)

  const [visible, setVisible] = usePersistedColumnVisibility(
    "gamma-magazzino-columns-v1",
    MAGAZZINO_COLUMNS_DEFAULT
  )
  const show = (id: string) => visible[id] ?? true
  const columnToggles = MAGAZZINO_COLUMNS.map(({ id, label }) => ({
    id,
    label,
    checked: show(id),
    onToggle: (value: boolean) =>
      setVisible((prev) => ({ ...prev, [id]: value })),
  }))

  const componentsQuery = useQuery({
    queryKey: ["gamma-robot", "components"],
    queryFn: fetchGammaComponents,
  })

  const usageQuery = useQuery({
    queryKey: ["gamma-robot", "usage", selected?.productId],
    queryFn: () => fetchGammaUsage(selected!.productId),
    enabled: selected != null,
  })

  const filter = search.trim().toLowerCase()
  const components = (componentsQuery.data ?? []).filter((c) => {
    if (!filter) return true
    return (
      c.code.toLowerCase().includes(filter) ||
      c.name.toLowerCase().includes(filter) ||
      (c.categoria ?? "").toLowerCase().includes(filter)
    )
  })

  const totConf = (usageQuery.data ?? []).reduce((s, u) => s + u.occorrenze, 0)

  return (
    <div className="grid min-h-[520px] grid-cols-1 gap-4 lg:grid-cols-[1fr_1fr]">
      <div className="flex flex-col rounded-lg border">
        <div className="flex items-center gap-2 border-b p-2">
          <Input
            placeholder="Cerca codice / nome / categoria…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="h-8"
          />
          <ColumnsMenu columns={columnToggles} />
        </div>
        <GridScroller fill>
          {componentsQuery.isLoading ? (
            <p className="p-4 text-sm text-muted-foreground">Caricamento…</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-[110px]">Codice</TableHead>
                  <TableHead>Nome</TableHead>
                  {show("categoria") && (
                    <TableHead className="w-[90px]">Categoria</TableHead>
                  )}
                  {show("robot") && (
                    <TableHead className="w-[70px] text-right">Robot</TableHead>
                  )}
                  {show("vb") && (
                    <TableHead className="w-[90px] text-right">VB €</TableHead>
                  )}
                </TableRow>
              </TableHeader>
              <TableBody>
                {components.map((c) => (
                  <TableRow
                    key={c.productId}
                    className={cn(
                      "cursor-pointer",
                      selected?.productId === c.productId && "bg-muted"
                    )}
                    onClick={() => setSelected(c)}
                    onDoubleClick={() => onOpenProduct(c.productId)}
                  >
                    <TableCell className="font-mono text-xs font-semibold">
                      {c.code}
                    </TableCell>
                    <TableCell className="text-sm">{c.name}</TableCell>
                    {show("categoria") && (
                      <TableCell className="text-xs text-muted-foreground">
                        {c.categoria ?? "—"}
                      </TableCell>
                    )}
                    {show("robot") && (
                      <TableCell className="text-right text-sm tabular-nums">
                        {c.robotCount}
                      </TableCell>
                    )}
                    {show("vb") && (
                      <TableCell className="text-right text-sm tabular-nums">
                        {c.prezzoVb != null ? formatEuro(c.prezzoVb) : "—"}
                      </TableCell>
                    )}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </GridScroller>
        <div className="border-t px-3 py-1.5 text-xs text-muted-foreground">
          {components.length} componenti
          {"  ·  doppio click = scheda prodotto"}
        </div>
      </div>

      <div className="flex flex-col rounded-lg border">
        <div className="border-b px-4 py-3">
          <h3 className="font-mono text-base font-semibold">
            {selected?.code ?? "Utilizzo"}
          </h3>
          <p className="text-sm text-muted-foreground">
            {selected
              ? `${selected.name}   ·   ${selected.robotCount} robot · ${totConf} configurazioni`
              : "Seleziona un componente a sinistra."}
          </p>
        </div>
        <GridScroller fill>
          {!selected ? (
            <p className="p-6 text-sm text-muted-foreground">
              Nessun componente selezionato.
            </p>
          ) : usageQuery.isLoading ? (
            <p className="p-6 text-sm text-muted-foreground">Caricamento…</p>
          ) : (usageQuery.data?.length ?? 0) === 0 ? (
            <p className="p-6 text-sm text-muted-foreground">
              Nessun utilizzo in distinta.
            </p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Modello</TableHead>
                  <TableHead>Controllore</TableHead>
                  <TableHead>Slot</TableHead>
                  <TableHead className="w-[70px] text-right">Conf.</TableHead>
                  <TableHead className="w-[60px]">Tipo</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(usageQuery.data ?? []).map((u, i) => (
                  <TableRow key={`${u.modello}-${u.controllore}-${i}`}>
                    <TableCell className="text-sm font-medium">
                      {u.modello}
                    </TableCell>
                    <TableCell className="text-sm">
                      {u.controllore ?? "—"}
                      {u.generazione ? (
                        <span className="text-muted-foreground">
                          {" "}
                          [{u.generazione}]
                        </span>
                      ) : null}
                    </TableCell>
                    <TableCell className="font-mono text-xs">
                      {u.slot ?? "—"}
                    </TableCell>
                    <TableCell className="text-right text-sm tabular-nums">
                      {u.occorrenze}
                    </TableCell>
                    <TableCell>
                      {u.isAlternate ? (
                        <Badge variant="outline">ALT</Badge>
                      ) : null}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </GridScroller>
      </div>
    </div>
  )
}
