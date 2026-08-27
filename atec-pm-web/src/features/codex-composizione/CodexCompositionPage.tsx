import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  Boxes,
  Link2,
  PackageOpen,
  Pencil,
  Plus,
  RefreshCw,
  ShoppingCart,
  Trash2,
} from "lucide-react"

import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { notifyError, notifyInfo } from "@/lib/toast"
import { Button } from "@/components/ui/button"
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
import { fetchCatalogItems } from "@/lib/api/catalog"
import {
  addComposition,
  deleteComposition,
  fetchCompositionTree,
  updateCompositionQuantity,
} from "@/lib/api/codex-compositions"
import { fetchAllCodex, fetchCodex } from "@/lib/api/codex"
import { CatalogAtecAssignDialog } from "@/features/catalogo/CatalogAtecAssignDialog"
import { CodexEditDialog } from "@/features/codex/CodexEditDialog"
import type {
  CatalogItemListItem,
  CodexListItem,
  CompositionTreeNode,
} from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { euro, dash } from "@/lib/format"
import { useCodexHub } from "@/lib/signalr/use-codex-hub"
import { useDebounced } from "@/lib/use-debounced"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"
import { cn } from "@/lib/utils"

import { CodexImportDialog } from "./CodexImportDialog"
import { NewCompositeDialog } from "./NewCompositeDialog"
import { QuantityDialog } from "./QuantityDialog"
import { wildcardMatch } from "@/lib/wildcard"

// ── CONFIGURAZIONE TIPI ────────────────────────────────────

interface CompositionTypeConfig {
  code: string
  label: string
  childPrefixes: string[]
  allowCatalog: boolean
}

// Dal 25/08/2026 in composizione finiscono SOLO codici Codex: il server rifiuta
// `ChildCatalogId` (CodexController.AddComposition). `allowCatalog` non vuol più dire
// «si possono mettere articoli di catalogo», vuol dire «si può CERCARE per codice
// fornitore»: la sorgente Catalogo è una lente sul Codex, perché in officina il codice
// che si conosce è quello del fornitore (0026106), non il Codex (211240726.004).
// L'articolo aggiunto è comunque il Codex associato; quelli non ancora codificati si
// codificano al volo dalla stessa griglia.
const COMPOSITION_TYPES: CompositionTypeConfig[] = [
  { code: "501", label: "Gruppo meccanico", childPrefixes: ["1", "2", "3"], allowCatalog: true },
  // 511 = clone del 501 per i gruppi non meccanici (es. colonnina luminosa di soli 211):
  // stesse regole, stesso comportamento in DDP. Vive accanto al 501, non dentro.
  { code: "511", label: "Gruppo custom", childPrefixes: ["1", "2", "3"], allowCatalog: true },
  { code: "601", label: "Assieme meccanico", childPrefixes: ["5"], allowCatalog: false },
  { code: "701", label: "Layout meccanico", childPrefixes: ["6"], allowCatalog: false },
]

// Etichette per la combo sotto-tipo della griglia "Articoli disponibili".
// Corrette il 25/08/2026: erano rimaste quelle della codifica VECCHIA ereditata dal port
// WPF («1xx — Commerciale», «2xx — Elettrico», «3xx — Pneumatico», «4xx — Meccanico») e
// mentivano su tutta la linea rispetto alle famiglie vere del generatore Codex
// (CodexGeneratorService.GetAvailablePrefixes). Chi compila una distinta sceglie il
// componente leggendo queste etichette: devono dire il vero.
const PREFIX_LABELS: Record<string, string> = {
  "1": "1xx — Particolari a disegno",
  "2": "2xx — Commerciale (generico/elettrico/pneumatico)",
  "3": "3xx — Elementi di fissaggio",
  "5": "5xx — Gruppi (501 mecc. / 511 custom)",
  "6": "6xx — Assieme mecc.",
  "7": "7xx — Layout mecc.",
}

// Accenti di sezione.
const ACCENT_COMPOSITI = "#4F6EF7"
const ACCENT_ARTICOLI = "#D97706"
const ACCENT_COMPOSIZIONE = "#12B76A"

const ALL = "__all__"

// Colonne della sorgente Catalogo: le stesse del picker DDP («la stessa cosa»,
// richiesta 26/08/2026) — si cerca anche per codice commerciale/fornitore/produttore
// quando il codice Codex non lo si ricorda. Visibilità scelta dal menu «Colonne».
interface CatalogColumn {
  key: string
  label: string
  align?: "right"
  /** Parametro server per il filtro per colonna (assente = non filtrabile). */
  filterParam?: string
}

const CATALOG_COLUMNS: CatalogColumn[] = [
  { key: "atecCode", label: "Cod. ATEC", filterParam: "atecCode" },
  { key: "code", label: "Codice", filterParam: "code" },
  { key: "description", label: "Descrizione", filterParam: "description" },
  { key: "unit", label: "UM" },
  { key: "supplierName", label: "Fornitore", filterParam: "supplier" },
  { key: "manufacturer", label: "Produttore", filterParam: "manufacturer" },
  { key: "unitCost", label: "Costo", align: "right" },
]

const CATALOG_COLUMNS_DEFAULTS: Record<string, boolean> = Object.fromEntries(
  CATALOG_COLUMNS.map((column) => [column.key, true])
)

interface AvailableItem {
  id: number
  codice: string
  descr: string
  source: "codex" | "catalog"
  /** Solo per le righe di catalogo: codice Codex associato (Extra1), "" se da codificare. */
  atecCode?: string
  /** Solo per le righe di catalogo: id Codex da usare come figlio al posto dell'id catalogo. */
  codexId?: number | null
  /** Riga catalogo originale: serve al dialog di codifica al volo. */
  catalogItem?: CatalogItemListItem
}

interface PendingAdd {
  parentId: number
  child: AvailableItem
}

// ── HELPER ─────────────────────────────────────────────────

/** Match specifico per il codice che rimuove i punti sia dal valore che dal filtro. */
function matchCodice(codice: string | undefined, filter: string): boolean {
  const cleanCodice = (codice ?? "").replace(/\./g, "")
  const cleanFilter = filter.replace(/\./g, "")
  return wildcardMatch(cleanCodice, cleanFilter)
}

/** Inserisce un punto prima delle ultime 3 cifre (formattazione codice Codex). */
function formatCodice(codice: string): string {
  const raw = (codice ?? "").replace(/\./g, "")
  if (raw.length > 3) {
    return `${raw.slice(0, raw.length - 3)}.${raw.slice(raw.length - 3)}`
  }
  return raw
}

function getDestinationInfo(
  code: string,
  source?: string
): { label: string; dotColor: string; badgeClass: string } {
  if (source === "catalog") {
    return {
      label: "DDP Commerciale",
      dotColor: "bg-emerald-500 shadow-[0_0_4px_#10b981]",
      badgeClass:
        "bg-emerald-50 text-emerald-700 border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300 dark:border-emerald-800",
    }
  }
  const prefix = (code ?? "").charAt(0)
  if (prefix === "2") {
    return {
      label: "DDP Commerciale",
      dotColor: "bg-emerald-500 shadow-[0_0_4px_#10b981]",
      badgeClass:
        "bg-emerald-50 text-emerald-700 border-emerald-200 dark:bg-emerald-950/40 dark:text-emerald-300 dark:border-emerald-800",
    }
  }
  if (prefix === "1") {
    return {
      label: "DDP Officina",
      dotColor: "bg-sky-500 shadow-[0_0_4px_#0284c7]",
      badgeClass:
        "bg-sky-50 text-sky-700 border-sky-200 dark:bg-sky-950/40 dark:text-sky-300 dark:border-sky-800",
    }
  }
  if (prefix === "3") {
    return {
      label: "Fissaggi",
      dotColor: "bg-amber-500 shadow-[0_0_4px_#f59e0b]",
      badgeClass:
        "bg-amber-50 text-amber-700 border-amber-200 dark:bg-amber-950/40 dark:text-amber-300 dark:border-amber-800",
    }
  }
  if (["5", "6", "7"].includes(prefix)) {
    return {
      label: "Sotto-gruppo",
      dotColor: "bg-purple-500 shadow-[0_0_4px_#a855f7]",
      badgeClass:
        "bg-purple-50 text-purple-700 border-purple-200 dark:bg-purple-950/40 dark:text-purple-300 dark:border-purple-800",
    }
  }
  return {
    label: "Componente",
    dotColor: "bg-slate-400",
    badgeClass:
      "bg-slate-50 text-slate-700 border-slate-200 dark:bg-slate-800 dark:text-slate-300 dark:border-slate-700",
  }
}

function getItemFamilyTheme(
  code: string,
  source?: string
): {
  rowClass: string
  codePillClass: string
} {
  if (source === "catalog") {
    return {
      rowClass:
        "bg-orange-50/70 hover:bg-orange-100/70 border-l-[5px] border-l-orange-500 dark:bg-orange-950/20 dark:hover:bg-orange-900/30 dark:border-l-orange-400",
      codePillClass:
        "bg-background text-orange-800 dark:text-orange-300 border-orange-300 dark:border-orange-700 shadow-[0_0_6px_rgba(234,88,12,0.18)]",
    }
  }
  const prefix = (code ?? "").charAt(0)
  if (prefix === "1") {
    return {
      rowClass:
        "bg-sky-50/70 hover:bg-sky-100/70 border-l-[5px] border-l-sky-500 dark:bg-sky-950/20 dark:hover:bg-sky-900/30 dark:border-l-sky-400",
      codePillClass:
        "bg-background text-sky-800 dark:text-sky-300 border-sky-300 dark:border-sky-700 shadow-[0_0_6px_rgba(2,132,199,0.18)]",
    }
  }
  if (prefix === "2") {
    return {
      rowClass:
        "bg-emerald-50/70 hover:bg-emerald-100/70 border-l-[5px] border-l-emerald-500 dark:bg-emerald-950/20 dark:hover:bg-emerald-900/30 dark:border-l-emerald-400",
      codePillClass:
        "bg-background text-emerald-800 dark:text-emerald-300 border-emerald-300 dark:border-emerald-700 shadow-[0_0_6px_rgba(16,185,129,0.18)]",
    }
  }
  if (prefix === "3") {
    return {
      rowClass:
        "bg-amber-50/70 hover:bg-amber-100/70 border-l-[5px] border-l-amber-500 dark:bg-amber-950/20 dark:hover:bg-amber-900/30 dark:border-l-amber-400",
      codePillClass:
        "bg-background text-amber-800 dark:text-amber-300 border-amber-300 dark:border-amber-700 shadow-[0_0_6px_rgba(245,158,11,0.18)]",
    }
  }
  if (prefix === "5") {
    return {
      rowClass:
        "bg-purple-50/70 hover:bg-purple-100/70 border-l-[5px] border-l-purple-500 dark:bg-purple-950/20 dark:hover:bg-purple-900/30 dark:border-l-purple-400",
      codePillClass:
        "bg-background text-purple-800 dark:text-purple-300 border-purple-300 dark:border-purple-700 shadow-[0_0_6px_rgba(168,85,247,0.18)]",
    }
  }
  if (prefix === "6") {
    return {
      rowClass:
        "bg-teal-50/70 hover:bg-teal-100/70 border-l-[5px] border-l-teal-500 dark:bg-teal-950/20 dark:hover:bg-teal-900/30 dark:border-l-teal-400",
      codePillClass:
        "bg-background text-teal-800 dark:text-teal-300 border-teal-300 dark:border-teal-700 shadow-[0_0_6px_rgba(13,148,136,0.18)]",
    }
  }
  if (prefix === "7") {
    return {
      rowClass:
        "bg-rose-50/70 hover:bg-rose-100/70 border-l-[5px] border-l-rose-500 dark:bg-rose-950/20 dark:hover:bg-rose-900/30 dark:border-l-rose-400",
      codePillClass:
        "bg-background text-rose-800 dark:text-rose-300 border-rose-300 dark:border-rose-700 shadow-[0_0_6px_rgba(225,29,72,0.18)]",
    }
  }
  return {
    rowClass:
      "bg-muted/40 hover:bg-muted/60 border-l-[5px] border-l-slate-400 dark:border-l-slate-500",
    codePillClass:
      "bg-background text-foreground border-border shadow-[0_0_6px_rgba(0,0,0,0.06)]",
  }
}

function renderDestinationBadge(code: string, source?: string) {
  const dest = getDestinationInfo(code, source)
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-semibold border tracking-tight shrink-0",
        dest.badgeClass
      )}
    >
      <span className={cn("size-1.5 rounded-full shrink-0", dest.dotColor)} />
      {dest.label}
    </span>
  )
}

function getRootPrefixBadgeClass(prefix: string): { bg: string; border: string; pill: string } {
  if (prefix === "5") {
    return {
      bg: "bg-amber-500",
      border:
        "border-amber-300/80 border-l-amber-500 dark:border-amber-800/60 dark:border-l-amber-400 from-amber-500/10 via-amber-500/5",
      pill: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300 border-amber-200 dark:border-amber-800",
    }
  }
  if (prefix === "6") {
    return {
      bg: "bg-teal-600",
      border:
        "border-teal-300/80 border-l-teal-500 dark:border-teal-800/60 dark:border-l-teal-400 from-teal-500/10 via-teal-500/5",
      pill: "bg-teal-100 text-teal-800 dark:bg-teal-950 dark:text-teal-300 border-teal-200 dark:border-teal-800",
    }
  }
  if (prefix === "7") {
    return {
      bg: "bg-rose-600",
      border:
        "border-rose-300/80 border-l-rose-500 dark:border-rose-800/60 dark:border-l-rose-400 from-rose-500/10 via-rose-500/5",
      pill: "bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-300 border-rose-200 dark:border-rose-800",
    }
  }
  return {
    bg: "bg-primary",
    border: "border-primary/30 border-l-primary from-primary/10 via-primary/5",
    pill: "bg-primary/10 text-primary border-primary/20",
  }
}

/**
 * Validazione gerarchia lato client (anteprima del drop). Il server riapplica le
 * stesse regole all'aggiunta. Dal 25/08/2026 una riga di catalogo vale per il suo
 * codice Codex associato: se non ce l'ha non entra in distinta (si codifica prima,
 * col pulsante «Codifica» della stessa griglia), perché un figlio senza codice Codex
 * non saprebbe nemmeno in quale DDP finire.
 */
function validateDropLocal(targetCodice: string, child: AvailableItem): string | null {
  // Servono ENTRAMBI: il codice per la regola gerarchica, l'id per scrivere il figlio.
  if (child.source === "catalog" && (!child.atecCode || !child.codexId))
    return "Articolo senza codice Codex: codificalo (201/211/221) prima di metterlo in distinta"
  const target = targetCodice.charAt(0)
  const childPrefix = (child.source === "catalog" ? child.atecCode! : child.codice).charAt(0)
  switch (target) {
    case "5":
      // 4xx fuori dal 25/08/2026: famiglia 401 «Materia prima» ritirata.
      return ["1", "2", "3"].includes(childPrefix) ? null : "5xx accetta solo 1xx-3xx"
    case "6":
      return childPrefix === "5" ? null : "6xx accetta solo 5xx"
    case "7":
      return childPrefix === "6" ? null : "7xx accetta solo 6xx"
    default:
      return "Questo nodo non può contenere figli"
  }
}

function countComponents(node: CompositionTreeNode): number {
  return node.children.reduce((acc, child) => acc + 1 + countComponents(child), 0)
}

/** Pezzi totali = somma delle quantità di tutte le righe (senza esplosione ricorsiva). */
function countPieces(node: CompositionTreeNode): number {
  return node.children.reduce((acc, child) => acc + child.quantity + countPieces(child), 0)
}

/** Clona l'albero applicando la nuova quantità alla riga indicata (update ottimistico). */
function patchQuantity(
  node: CompositionTreeNode,
  compositionId: number,
  quantity: number
): CompositionTreeNode {
  return {
    ...node,
    quantity: node.compositionId === compositionId ? quantity : node.quantity,
    children: node.children.map((child) => patchQuantity(child, compositionId, quantity)),
  }
}

// ── ALBERO E TABELLE COMPOSIZIONE ──────────────────────────

interface TreeCtx {
  canEdit: boolean
  dragItem: React.MutableRefObject<AvailableItem | null>
  hoverKey: string | null
  setHoverKey: (key: string | null) => void
  /** Aggiunge `child` sotto il nodo `parentCodexId`; valida contro `parentCodice`. */
  onDropTo: (parentCodexId: number, parentCodice: string, child: AvailableItem) => void
  onRemove: (node: CompositionTreeNode) => void
  /** Apre il dialog quantità (click sul numero dello stepper). */
  onEditQuantity: (node: CompositionTreeNode) => void
  /** Incrementa/decrementa la quantità di una riga (freccette ▲/▼, delta ±1). */
  onChangeQuantity: (node: CompositionTreeNode, delta: number) => void
}

function RootHeaderCard({ node }: { node: CompositionTreeNode }) {
  const prefix = node.codice.charAt(0)
  const typeLabel = PREFIX_LABELS[prefix] ?? "Composito"
  const theme = getRootPrefixBadgeClass(prefix)
  const totalComponents = countComponents(node)
  const totalPieces = countPieces(node)

  return (
    <div
      className={cn(
        "bg-gradient-to-r to-transparent border border-l-[5px] rounded-lg p-3 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 shadow-2xs",
        theme.border
      )}
    >
      <div className="flex items-center gap-3 min-w-0">
        <div
          className={cn(
            "size-9 rounded-lg text-white flex items-center justify-center font-mono font-bold text-sm shadow-xs shrink-0",
            theme.bg
          )}
        >
          {node.codice.slice(0, 3)}
        </div>
        <div className="min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="font-mono font-bold text-base sm:text-lg text-foreground tracking-tight">
              {formatCodice(node.codice)}
            </span>
            <span
              className={cn(
                "px-2.5 py-0.5 text-[10px] sm:text-[11px] font-semibold rounded-full border",
                theme.pill
              )}
            >
              {typeLabel}
            </span>
          </div>
          <div
            className="text-xs sm:text-sm font-medium text-muted-foreground truncate"
            title={node.descr}
          >
            {node.descr || "—"}
          </div>
        </div>
      </div>

      <div className="flex items-center gap-2 shrink-0 self-end sm:self-center">
        <div className="bg-background/90 border border-border px-2.5 py-1 rounded-md text-xs flex items-center gap-1.5 shadow-2xs">
          <span className="text-muted-foreground">Voci:</span>
          <span className="font-semibold text-foreground">{totalComponents}</span>
        </div>
        <div className="bg-background/90 border border-border px-2.5 py-1 rounded-md text-xs flex items-center gap-1.5 shadow-2xs">
          <span className="text-muted-foreground">Tot. Pezzi:</span>
          <span className="font-bold text-emerald-600 dark:text-emerald-400">{totalPieces}</span>
        </div>
      </div>
    </div>
  )
}

function ComponentRow({
  node,
  depth,
  ctx,
}: {
  node: CompositionTreeNode
  depth: number
  ctx: TreeCtx
}) {
  const isCodex = node.source !== "catalog"
  const droppable = ctx.canEdit && isCodex
  const key = `node-${node.compositionId}-${node.codexId}`
  const display = isCodex ? formatCodice(node.codice) : node.codice
  const familyTheme = getItemFamilyTheme(node.codice, node.source)

  const dndHandlers = droppable
    ? {
        onDragOver: (event: React.DragEvent) => {
          const item = ctx.dragItem.current
          if (!item || validateDropLocal(node.codice, item) !== null) return
          event.preventDefault()
          event.stopPropagation()
          ctx.setHoverKey(key)
        },
        onDrop: (event: React.DragEvent) => {
          event.preventDefault()
          event.stopPropagation()
          const item = ctx.dragItem.current
          ctx.setHoverKey(null)
          if (!item || validateDropLocal(node.codice, item) !== null) return
          ctx.onDropTo(node.codexId, node.codice, item)
        },
      }
    : {}

  return (
    <>
      <tr
        {...dndHandlers}
        className={cn(
          "transition-colors group",
          familyTheme.rowClass,
          ctx.hoverKey === key && "ring-2 ring-primary ring-inset bg-primary/15"
        )}
      >
        <td className="py-2.5 px-3 font-mono">
          <div
            className="flex items-center gap-1.5"
            style={{ paddingLeft: depth > 0 ? `${depth * 14}px` : undefined }}
          >
            {depth > 0 && (
              <span className="text-muted-foreground/60 text-xs font-sans select-none">↳</span>
            )}
            <span
              className={cn(
                "px-2.5 py-0.5 rounded-md font-mono font-bold text-xs sm:text-[13px] border tracking-tight shrink-0 transition-shadow",
                familyTheme.codePillClass
              )}
            >
              {display}
            </span>
          </div>
        </td>
        <td className="py-2.5 px-3 text-foreground font-medium text-xs sm:text-[13px] whitespace-normal break-words">
          {node.descr || "—"}
        </td>
        <td className="py-2.5 px-3">
          {renderDestinationBadge(node.codice, node.source)}
        </td>
        <td className="py-2.5 px-3 text-center">
          {ctx.canEdit ? (
            <div className="inline-flex items-center border border-border rounded-md bg-background/90 shadow-2xs overflow-hidden">
              <button
                type="button"
                disabled={node.quantity <= 1}
                onClick={() => ctx.onChangeQuantity(node, -1)}
                className="px-2 py-0.5 text-xs text-muted-foreground hover:bg-muted hover:text-foreground disabled:opacity-30 disabled:pointer-events-none transition-colors font-bold"
                title="Diminuisci quantità (-1)"
              >
                -
              </button>
              <button
                type="button"
                onClick={() => ctx.onEditQuantity(node)}
                className="px-2.5 py-0.5 font-mono font-bold text-foreground text-xs sm:text-[13px] min-w-6 text-center hover:bg-muted/50 transition-colors border-x border-border/60"
                title="Clicca per impostare la quantità esatta"
              >
                {node.quantity}
              </button>
              <button
                type="button"
                onClick={() => ctx.onChangeQuantity(node, 1)}
                className="px-2 py-0.5 text-xs text-muted-foreground hover:bg-muted hover:text-foreground transition-colors font-bold"
                title="Aumenta quantità (+1)"
              >
                +
              </button>
            </div>
          ) : (
            <span className="px-2 py-0.5 font-mono text-xs sm:text-[13px] font-bold text-muted-foreground">
              ×{node.quantity}
            </span>
          )}
        </td>
        <td className="py-2.5 px-2 text-center">
          {ctx.canEdit && (
            <Button
              variant="ghost"
              size="icon-sm"
              className="size-7 text-muted-foreground/50 opacity-60 group-hover:opacity-100 hover:text-destructive hover:bg-destructive/10 transition-all"
              title="Rimuovi il componente dalla distinta"
              onClick={() => ctx.onRemove(node)}
            >
              <Trash2 className="size-3.5" />
            </Button>
          )}
        </td>
      </tr>

      {/* Sotto-figli gerarchici (per gruppi annidati, es. 601 -> 501 -> 201) */}
      {node.children &&
        node.children.length > 0 &&
        [...node.children]
          .sort((a, b) => a.codice.localeCompare(b.codice))
          .map((child) => (
            <ComponentRow
              key={`nested-${child.compositionId}-${child.codexId}`}
              node={child}
              depth={depth + 1}
              ctx={ctx}
            />
          ))}
    </>
  )
}

function CompositionGroupTable({
  title,
  icon: Icon,
  items,
  groupKey,
  onDrop,
  onDragOver,
  ctx,
}: {
  title: string
  icon: React.ComponentType<{ className?: string }>
  items: CompositionTreeNode[]
  groupKey: string
  onDrop: (event: React.DragEvent) => void
  onDragOver: (groupKey: string) => (event: React.DragEvent) => void
  ctx: TreeCtx
}) {
  const totalPieces = items.reduce((acc, c) => acc + c.quantity + countPieces(c), 0)

  return (
    <div className="border border-border rounded-lg overflow-hidden shadow-2xs mb-3 bg-card">
      <div
        onDragOver={onDragOver(groupKey)}
        onDrop={onDrop}
        className={cn(
          "bg-muted/40 border-b border-border px-3 py-2 flex items-center justify-between text-xs transition-colors",
          ctx.hoverKey === groupKey && "ring-2 ring-primary ring-inset bg-primary/10"
        )}
      >
        <div className="flex items-center gap-2 font-semibold text-foreground">
          <Icon className="size-4 text-indigo-600 dark:text-indigo-400" />
          <span>{title}</span>
        </div>
        <span className="text-[11px] text-muted-foreground font-medium">
          {items.length} {items.length === 1 ? "voce" : "voci"} · {totalPieces}{" "}
          {totalPieces === 1 ? "pezzo" : "pezzi"}
        </span>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-xs text-left border-collapse">
          <thead>
            <tr className="bg-muted/20 border-b border-border text-[10px] font-semibold text-muted-foreground uppercase tracking-wider">
              <th className="py-2 px-3 w-36">Cod. ATEC</th>
              <th className="py-2 px-3">Descrizione Componente</th>
              <th className="py-2 px-3 w-36">Destinazione</th>
              <th className="py-2 px-3 w-28 text-center">Quantità</th>
              <th className="py-2 px-2 w-9 text-center"></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border/60">
            {items.map((child) => (
              <ComponentRow
                key={`comp-${child.compositionId}-${child.codexId}`}
                node={child}
                depth={0}
                ctx={ctx}
              />
            ))}
          </tbody>
        </table>
      </div>

      {/* Dropzone attiva in fondo alla tabella */}
      {ctx.canEdit && (
        <div
          onDragOver={onDragOver(`${groupKey}-bottom`)}
          onDrop={onDrop}
          className={cn(
            "p-2 bg-muted/10 border-t border-dashed border-border text-center transition-colors",
            ctx.hoverKey === `${groupKey}-bottom` &&
              "ring-2 ring-primary ring-inset bg-primary/10"
          )}
        >
          <div className="border border-dashed border-border/80 hover:border-primary/50 hover:bg-primary/5 transition-all rounded-md py-2 px-3 flex items-center justify-center gap-2 text-xs text-muted-foreground cursor-pointer">
            <Plus className="size-3.5 text-muted-foreground/70" />
            <span>
              Trascina qui un articolo da sinistra oppure fai <strong>doppio clic</strong> nell'elenco
            </span>
          </div>
        </div>
      )}
    </div>
  )
}

function CompositionTreeView({
  node,
  ctx,
}: {
  node: CompositionTreeNode
  ctx: TreeCtx
}) {
  const codex = node.children
    .filter((c) => c.source !== "catalog")
    .sort((a, b) => a.codice.localeCompare(b.codice))
  const catalog = node.children
    .filter((c) => c.source === "catalog")
    .sort((a, b) => a.codice.localeCompare(b.codice))

  function groupDrop(event: React.DragEvent) {
    event.preventDefault()
    event.stopPropagation()
    const item = ctx.dragItem.current
    ctx.setHoverKey(null)
    if (!item || validateDropLocal(node.codice, item) !== null) return
    ctx.onDropTo(node.codexId, node.codice, item)
  }

  function groupDragOver(groupKey: string) {
    return (event: React.DragEvent) => {
      const item = ctx.dragItem.current
      if (!item || validateDropLocal(node.codice, item) !== null) return
      event.preventDefault()
      event.stopPropagation()
      ctx.setHoverKey(groupKey)
    }
  }

  return (
    <div className="space-y-3">
      {/* Testata Padre in Evidenza */}
      <RootHeaderCard node={node} />

      {/* Se non ci sono figli */}
      {node.children.length === 0 ? (
        <div
          onDragOver={groupDragOver("empty-root")}
          onDrop={groupDrop}
          className={cn(
            "p-8 text-center border-2 border-dashed border-border rounded-lg bg-muted/10 transition-colors",
            ctx.hoverKey === "empty-root" &&
              "ring-2 ring-primary ring-inset bg-primary/10 border-primary"
          )}
        >
          <PackageOpen className="size-8 text-muted-foreground/50 mx-auto mb-2" />
          <p className="text-sm font-medium text-foreground">Nessun componente in distinta</p>
          <p className="text-xs text-muted-foreground mt-1">
            {ctx.canEdit
              ? "Trascina qui un articolo disponibile da sinistra o fai doppio clic nell'elenco per aggiungerlo."
              : "Nessun componente associato a questo composito."}
          </p>
        </div>
      ) : (
        <>
          {/* Tabella Componenti Codex */}
          {codex.length > 0 && (
            <CompositionGroupTable
              title="Componenti Codex"
              icon={Boxes}
              items={codex}
              groupKey="group-codex"
              onDrop={groupDrop}
              onDragOver={groupDragOver}
              ctx={ctx}
            />
          )}

          {/* Tabella Componenti Commerciali */}
          {catalog.length > 0 && (
            <CompositionGroupTable
              title="Componenti commerciali"
              icon={ShoppingCart}
              items={catalog}
              groupKey="group-catalog"
              onDrop={groupDrop}
              onDragOver={groupDragOver}
              ctx={ctx}
            />
          )}
        </>
      )}
    </div>
  )
}

// ── PAGINA ─────────────────────────────────────────────────

export function CodexCompositionPage() {
  const queryClient = useQueryClient()


  const confirm = useConfirm()
  const canEdit = canWriteFeature("action.edit_codex_composition")
  // Creare il composito è creare un articolo Codex: chiave del Codex, non della
  // composizione (stessa che governa «Genera Codice» nella pagina Codex Articoli).
  const canCreateComposite = canWriteFeature("action.manage_codex")

  const [typeCode, setTypeCode] = React.useState("501")
  const [selectedParent, setSelectedParent] = React.useState<CodexListItem | null>(null)

  // Filtri griglia superiore (Compositi) e inferiore (Articoli disponibili).
  const [compCode, setCompCode] = React.useState("")
  const [compDescr, setCompDescr] = React.useState("")
  const [availCode, setAvailCode] = React.useState("")
  const [availDescr, setAvailDescr] = React.useState("")

  // Combo sotto-tipo + sorgente della griglia inferiore.
  const [childType, setChildType] = React.useState(ALL)
  const [source, setSource] = React.useState<"codex" | "catalog">("codex")
  // Filtri per colonna della sorgente Catalogo (la sorgente Codex tiene i due
  // campi Codice/Descrizione di sempre) + visibilità colonne persistita.
  const [catFilters, setCatFilters] = React.useState<Record<string, string>>({})
  const [catVisibility, setCatVisibility] = usePersistedColumnVisibility(
    "codex-comp-catalog-cols-v1",
    CATALOG_COLUMNS_DEFAULTS
  )
  const visibleCatalogColumns = CATALOG_COLUMNS.filter(
    (column) => catVisibility[column.key] !== false
  )
  // Sorgente Catalogo: si vede TUTTO il catalogo, codificato e non. Filtrare via i non
  // codificati sembrava pulito ma nascondeva proprio le righe su cui si deve agire:
  // quelle senza codice mostrano il pulsante «Codifica» e si sistemano sul posto.
  const [atecTarget, setAtecTarget] = React.useState<CatalogItemListItem | null>(null)
  // Creazione di un composito nuovo dalla pagina stessa: senza, una famiglia vuota
  // («Nessun composito 511») sarebbe un vicolo cieco.
  const [newComposite, setNewComposite] = React.useState(false)
  // Rinomina della descrizione di un composito (stesso dialog della pagina Codex).
  const [editComposite, setEditComposite] = React.useState<CodexListItem | null>(null)

  // Stato drag&drop.
  const dragItem = React.useRef<AvailableItem | null>(null)
  const [hoverKey, setHoverKey] = React.useState<string | null>(null)
  const [pendingAdd, setPendingAdd] = React.useState<PendingAdd | null>(null)
  // Riga di cui si sta modificando la quantità (badge ×N).
  const [editQty, setEditQty] = React.useState<CompositionTreeNode | null>(null)

  // Import distinta da file STEP: il pulsante apre direttamente il selettore file,
  // il composito padre si ricava dalla radice del file (nessuna selezione richiesta).
  const importFileRef = React.useRef<HTMLInputElement>(null)
  const [importFile, setImportFile] = React.useState<File | null>(null)
  // Composito da selezionare dopo un cambio tipo programmato (post-import STEP):
  // l'effect su typeCode azzererebbe la selezione appena fatta.
  const pendingSelectRef = React.useRef<CodexListItem | null>(null)

  const activeType =
    COMPOSITION_TYPES.find((type) => type.code === typeCode) ?? COMPOSITION_TYPES[0]
  const allowCatalog = activeType.allowCatalog
  const effectiveSource: "codex" | "catalog" = allowCatalog ? source : "codex"

  // Reset al cambio tipo (come CmbType_SelectionChanged nel WPF). Se il cambio è
  // stato innescato dall'import STEP, seleziona il composito importato anziché azzerare.
  React.useEffect(() => {
    setSelectedParent(pendingSelectRef.current)
    pendingSelectRef.current = null
    setChildType(ALL)
    setSource("codex")
    setCatFilters({})
  }, [typeCode])

  // Compositi del tipo selezionato: filtrati server-side per prefisso (es. 501%),
  // così TUTTI i 5xx/6xx/7xx vengono caricati (prima ci si fermava a 10k righe e i
  // 501, ordinati dopo 1xx/2xx, restavano fuori → "nessun composito 501"). Il filtro
  // per codice/descrizione è poi applicato client-side con i jolly, come nel WPF.
  const compositesQuery = useQuery({
    queryKey: ["codex-by-prefix", typeCode],
    queryFn: () => fetchAllCodex({ codicePrefixes: [typeCode] }),
  })

  const treeQuery = useQuery({
    queryKey: ["composition-tree", selectedParent?.id],
    queryFn: () => fetchCompositionTree(selectedParent!.id),
    enabled: selectedParent != null,
  })

  // Articoli disponibili: ricerca server-side paginata (i prefissi figli possono
  // contare decine di migliaia di righe → niente caricamento client-side completo).
  // I jolly su codice/descrizione sono applicati dal server (stesso helper LIKE).
  const childPrefixes = childType === ALL ? activeType.childPrefixes : [childType]
  const debAvailCode = useDebounced(availCode.trim(), 300)
  const debAvailDescr = useDebounced(availDescr.trim(), 300)

  const codexAvailableQuery = useQuery({
    queryKey: ["codex-available", childPrefixes, debAvailCode, debAvailDescr],
    queryFn: () =>
      fetchCodex({
        codicePrefixes: childPrefixes,
        filters: { codice: debAvailCode, descr: debAvailDescr },
        pageSize: 100,
      }),
    enabled: effectiveSource === "codex",
  })

  const debCatFilters = useDebounced(catFilters, 300)
  const catalogQuery = useQuery({
    queryKey: ["catalog-available", debCatFilters],
    queryFn: () =>
      fetchCatalogItems({
        filters: debCatFilters,
        pageSize: 100,
      }),
    enabled: effectiveSource === "catalog",
  })

  // Real-time: quando un altro utente modifica una composizione, ricarica l'albero
  // aperto (hub /hubs/codex). connectionIdRef serve per la self-exclusion sulle mutazioni.
  const connectionIdRef = useCodexHub(() => {
    void queryClient.invalidateQueries({ queryKey: ["composition-tree"] })
  })

  const addMutation = useMutation({
    mutationFn: (vars: { parentId: number; child: AvailableItem; quantity: number }) =>
      // Una riga di catalogo entra in distinta col suo CODEX associato, mai come
      // `childCatalogId` (il server lo rifiuta): la sorgente Catalogo è solo un modo
      // di cercare per codice fornitore.
      addComposition(
        {
          parentCodexId: vars.parentId,
          childCodexId:
            vars.child.source === "codex" ? vars.child.id : vars.child.codexId ?? null,
          childCatalogId: null,
          quantity: vars.quantity,
        },
        connectionIdRef.current
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["composition-tree"] }),
    onError: (err: Error) => notifyError(err),
  })

  const deleteMutation = useMutation({
    mutationFn: (compositionId: number) =>
      deleteComposition(compositionId, connectionIdRef.current),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["composition-tree"] }),
    onError: (err: Error) => notifyError(err),
  })

  const quantityMutation = useMutation({
    mutationFn: (vars: { compositionId: number; quantity: number }) =>
      updateCompositionQuantity(vars.compositionId, vars.quantity, connectionIdRef.current),
    // Update ottimistico: i click rapidi su ▲/▼ partono dal valore già aggiornato in cache.
    onMutate: async (vars) => {
      await queryClient.cancelQueries({ queryKey: ["composition-tree"] })
      queryClient.setQueryData<CompositionTreeNode>(
        ["composition-tree", selectedParent?.id],
        (old) => (old ? patchQuantity(old, vars.compositionId, vars.quantity) : old)
      )
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["composition-tree"] }),
    onError: (err: Error) => {
      notifyError(err)
      void queryClient.invalidateQueries({ queryKey: ["composition-tree"] })
    },
  })

  // Compositi (griglia superiore): l'insieme è già ristretto al prefisso del tipo
  // lato server; qui si applica solo il filtro jolly client-side (come il WPF).
  const composites = React.useMemo(() => {
    const items = compositesQuery.data ?? []
    return items
      .filter((item) => matchCodice(item.codice, compCode))
      .filter((item) => wildcardMatch(item.descr, compDescr))
      .sort((a, b) => a.codice.localeCompare(b.codice))
  }, [compositesQuery.data, compCode, compDescr])

  // Articoli disponibili (griglia inferiore): risultati server-side (Codex o Catalogo).
  const available = React.useMemo<AvailableItem[]>(() => {
    if (effectiveSource === "catalog") {
      return (catalogQuery.data?.items ?? []).map((item) => ({
        id: item.id,
        codice: item.code,
        descr: item.description,
        source: "catalog" as const,
        atecCode: (item.atecCode || "").replace(/\./g, "").trim(),
        codexId: item.codexItemId,
        catalogItem: item,
      }))
    }
    return (codexAvailableQuery.data?.items ?? []).map((item) => ({
      id: item.id,
      codice: item.codice,
      descr: item.descr,
      source: "codex" as const,
    }))
  }, [effectiveSource, catalogQuery.data, codexAvailableQuery.data])

  const availableTotal =
    effectiveSource === "catalog"
      ? catalogQuery.data?.totalCount ?? 0
      : codexAvailableQuery.data?.totalCount ?? 0
  const availableLoading =
    effectiveSource === "catalog" ? catalogQuery.isLoading : codexAvailableQuery.isLoading
  // Il colSpan delle righe di stato va calcolato, non cablato (regola BLOCKS-RULES §6):
  // sulla sorgente Catalogo dipende dalle colonne accese nel menu «Colonne».
  const availableColSpan =
    effectiveSource === "catalog" ? Math.max(visibleCatalogColumns.length, 1) : 2

  async function handleRemove(node: CompositionTreeNode) {
    const ok = await confirm({
      title: "Rimuovi componente",
      description: `Rimuovere "${node.codice} — ${node.descr}" dalla composizione?`,
      confirmLabel: "Rimuovi",
    })
    if (ok) deleteMutation.mutate(node.compositionId)
  }

  // Drop su un nodo/radice → apre il dialog quantità (validazione già fatta a monte).
  function handleDropTo(parentCodexId: number, _parentCodice: string, child: AvailableItem) {
    setPendingAdd({ parentId: parentCodexId, child })
  }

  // Celle della sorgente Catalogo: stesse colonne del picker DDP («la stessa cosa»).
  function renderCatalogCell(column: CatalogColumn, item: AvailableItem) {
    const cat = item.catalogItem
    switch (column.key) {
      case "atecCode":
        return item.atecCode && item.codexId ? (
          <span
            className="font-mono text-primary"
            title="Codice Codex associato: è questo che entra in distinta"
          >
            {formatCodice(item.atecCode)}
          </span>
        ) : (
          <Button
            size="sm"
            variant="outline"
            className="h-6 px-2 text-xs"
            disabled={!canEdit}
            onClick={(event) => {
              event.stopPropagation()
              setAtecTarget(cat ?? null)
            }}
          >
            <Link2 className="size-3" />
            Codifica
          </Button>
        )
      case "code":
        return <span className="font-mono font-medium">{item.codice}</span>
      case "description":
        return (
          <span
            className="block max-w-[320px] truncate text-muted-foreground"
            title={item.descr}
          >
            {dash(item.descr)}
          </span>
        )
      case "unit":
        return dash(cat?.unit)
      case "supplierName":
        return (
          <span className="block max-w-[160px] truncate" title={cat?.supplierName}>
            {dash(cat?.supplierName)}
          </span>
        )
      case "manufacturer":
        return dash(cat?.manufacturer)
      case "unitCost":
        return <span className="tabular-nums">{euro(cat?.unitCost ?? null)}</span>
      default:
        return null
    }
  }

  // Doppio click su un articolo → aggiunta rapida (quantità 1) alla radice, come il WPF.
  function handleQuickAdd(child: AvailableItem) {
    if (!canEdit || !selectedParent) return
    // Stessa guardia del drop: senza codice Codex l'articolo non entra in distinta.
    const error = validateDropLocal(selectedParent.codice, child)
    if (error) {
      notifyInfo(error)
      return
    }
    addMutation.mutate({ parentId: selectedParent.id, child, quantity: 1 })
  }

  const treeData = treeQuery.data
  const componentCount = treeData ? countComponents(treeData) : 0
  const pieceCount = treeData ? countPieces(treeData) : 0
  const statusText = selectedParent
    ? treeData
      ? `${componentCount} componenti · ${pieceCount} pezzi nella composizione`
      : "Caricamento composizione…"
    : `${composites.length} compositi ${typeCode} trovati`

  const ctx: TreeCtx = {
    canEdit,
    dragItem,
    hoverKey,
    setHoverKey,
    onDropTo: handleDropTo,
    onRemove: handleRemove,
    onEditQuantity: setEditQty,
    onChangeQuantity: (node, delta) => {
      const next = node.quantity + delta
      if (next >= 1) {
        quantityMutation.mutate({ compositionId: node.compositionId, quantity: next })
      }
    },
  }

  // Drop sull'area albero (spazio vuoto / fuori dai nodi) → aggiunta alla radice.
  const rootDroppable = canEdit && selectedParent != null
  function rootDragOver(event: React.DragEvent) {
    if (!rootDroppable || !selectedParent) return
    const item = dragItem.current
    if (!item || validateDropLocal(selectedParent.codice, item) !== null) return
    event.preventDefault()
    setHoverKey("root")
  }
  function rootDrop(event: React.DragEvent) {
    if (!rootDroppable || !selectedParent) return
    event.preventDefault()
    const item = dragItem.current
    setHoverKey(null)
    if (!item || validateDropLocal(selectedParent.codice, item) !== null) return
    handleDropTo(selectedParent.id, selectedParent.codice, item)
  }

  return (
    <div className="space-y-4">
      {/* Pagina a tutta altezza (pattern ConfigSectionsPage): il Card riempie la
          finestra e le tre griglie si spartiscono lo spazio, invece delle vecchie
          altezze fisse da 26rem che lasciavano mezzo schermo vuoto sui monitor grandi. */}
      <Card className="flex h-[calc(100vh-7rem)] flex-col">
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <CardTitle>Composizione Codex</CardTitle>
              <CardDescription>
                Distinta dei compositi meccanici (5xx/6xx/7xx) — trascina gli
                articoli disponibili sull'albero o fai doppio click.
              </CardDescription>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-sm text-muted-foreground">Tipo:</span>
              <Select value={typeCode} onValueChange={setTypeCode}>
                <SelectTrigger className="w-56">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {COMPOSITION_TYPES.map((type) => (
                    <SelectItem key={type.code} value={type.code}>
                      {type.code} — {type.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => {
                    void compositesQuery.refetch()
                    if (selectedParent) void treeQuery.refetch()
                    if (effectiveSource === "catalog") void catalogQuery.refetch()
                    else void codexAvailableQuery.refetch()
                  }}
                  disabled={compositesQuery.isFetching}
                >
                  <RefreshCw className={compositesQuery.isFetching ? "animate-spin" : ""} />
                  Aggiorna
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => importFileRef.current?.click()}
                  disabled={!canEdit}
                  title="Importa la distinta dal file STEP dell'assieme"
                >
                  Importa
                </Button>
                <input
                  ref={importFileRef}
                  type="file"
                  accept=".step,.stp,.STEP,.STP"
                  className="hidden"
                  onChange={(event) => {
                    const file = event.target.files?.[0]
                    if (file) setImportFile(file)
                    // Permette di riselezionare lo stesso file.
                    event.target.value = ""
                  }}
                />
            </div>
          </div>
        </CardHeader>

        <CardContent className="flex min-h-0 flex-1 flex-col">
          <div className="grid min-h-0 flex-1 gap-4 lg:grid-cols-2">
            {/* ── SINISTRA: Compositi + Articoli disponibili ── */}
            <div className="flex min-h-0 flex-col gap-4">
              {/* Griglia superiore: Compositi. Sotto lg restano le altezze fisse:
                  in colonna singola le righe flex-1 collasserebbero a zero. */}
              <div className="flex h-[26rem] min-h-0 flex-col overflow-hidden rounded-md border lg:h-auto lg:flex-1">
                <div className="flex items-center gap-2 border-b bg-muted/40 px-3 py-2">
                  <span
                    className="text-xs font-semibold tracking-wide"
                    style={{ color: ACCENT_COMPOSITI }}
                  >
                    COMPOSITI
                  </span>
                  {canCreateComposite ? (
                    <Button
                      size="sm"
                      variant="outline"
                      className="ml-auto h-7 px-2 text-xs"
                      onClick={() => setNewComposite(true)}
                    >
                      <Plus className="size-3.5" />
                      Nuovo {typeCode}
                    </Button>
                  ) : null}
                </div>
                <div className="flex gap-2 border-b p-2">
                  <Input
                    value={compCode}
                    placeholder="Codice"
                    className="h-8 w-32"
                    onChange={(event) => setCompCode(event.target.value)}
                  />
                  <Input
                    value={compDescr}
                    placeholder="Descrizione"
                    className="h-8 flex-1"
                    onChange={(event) => setCompDescr(event.target.value)}
                  />
                </div>
                <GridScroller fill>
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead className="w-40">Codice</TableHead>
                        <TableHead>Descrizione</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {compositesQuery.isLoading ? (
                        <TableRow>
                          <TableCell
                            colSpan={2}
                            className="h-24 text-center text-muted-foreground"
                          >
                            Caricamento…
                          </TableCell>
                        </TableRow>
                      ) : composites.length === 0 ? (
                        <TableRow>
                          <TableCell colSpan={2} className="h-24 text-center">
                            <div className="flex flex-col items-center gap-2">
                              <span className="text-muted-foreground">
                                Nessun composito {typeCode}.
                              </span>
                              {canCreateComposite ? (
                                <Button
                                  size="sm"
                                  variant="outline"
                                  onClick={() => setNewComposite(true)}
                                >
                                  <Plus className="size-4" />
                                  Crea il primo {typeCode}
                                </Button>
                              ) : null}
                            </div>
                          </TableCell>
                        </TableRow>
                      ) : (
                        composites.map((item) => (
                          <TableRow
                            key={item.id}
                            data-state={
                              selectedParent?.id === item.id ? "selected" : undefined
                            }
                            className="group cursor-pointer"
                            onClick={() => setSelectedParent(item)}
                          >
                            <TableCell className="font-mono font-medium">
                              {item.codice}
                            </TableCell>
                            <TableCell className="whitespace-normal break-words text-muted-foreground">
                              <span className="flex items-start justify-between gap-2">
                                <span>{item.descr || "—"}</span>
                                {canCreateComposite ? (
                                  <Button
                                    variant="ghost"
                                    size="icon-sm"
                                    className="shrink-0 opacity-0 focus-visible:opacity-100 group-hover:opacity-100"
                                    title="Rinomina la descrizione"
                                    onClick={(event) => {
                                      event.stopPropagation()
                                      setEditComposite(item)
                                    }}
                                  >
                                    <Pencil className="size-3.5" />
                                  </Button>
                                ) : null}
                              </span>
                            </TableCell>
                          </TableRow>
                        ))
                      )}
                    </TableBody>
                  </Table>
                </GridScroller>
              </div>

              {/* Griglia inferiore: Articoli disponibili */}
              <div className="flex h-[26rem] min-h-0 flex-col overflow-hidden rounded-md border lg:h-auto lg:flex-1">
                <div className="flex flex-wrap items-center gap-2 border-b bg-muted/40 px-3 py-2">
                  <span
                    className="text-xs font-semibold tracking-wide"
                    style={{ color: ACCENT_ARTICOLI }}
                  >
                    ARTICOLI DISPONIBILI
                  </span>
                  <Select
                    value={childType}
                    onValueChange={setChildType}
                    disabled={effectiveSource === "catalog"}
                  >
                    <SelectTrigger size="sm" className="h-7 w-40">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={ALL}>Tutti</SelectItem>
                      {activeType.childPrefixes.map((prefix) => (
                        <SelectItem key={prefix} value={prefix}>
                          {PREFIX_LABELS[prefix] ?? `${prefix}xx`}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <Select
                    value={effectiveSource}
                    onValueChange={(value) => setSource(value as "codex" | "catalog")}
                    disabled={!allowCatalog}
                  >
                    <SelectTrigger size="sm" className="h-7 w-28">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="codex">Codex</SelectItem>
                      <SelectItem value="catalog">Catalogo</SelectItem>
                    </SelectContent>
                  </Select>
                  {effectiveSource === "catalog" ? (
                    <ColumnsMenu
                      className="ml-auto h-7 px-2 text-xs"
                      columns={CATALOG_COLUMNS.map((column) => ({
                        id: column.key,
                        label: column.label,
                        checked: catVisibility[column.key] !== false,
                        onToggle: (checked) =>
                          setCatVisibility((prev) => ({
                            ...prev,
                            [column.key]: checked,
                          })),
                      }))}
                    />
                  ) : null}
                </div>
                {effectiveSource === "codex" ? (
                  <div className="flex gap-2 border-b p-2">
                    <Input
                      value={availCode}
                      placeholder="Codice"
                      className="h-8 w-32"
                      onChange={(event) => setAvailCode(event.target.value)}
                    />
                    <Input
                      value={availDescr}
                      placeholder="Descrizione"
                      className="h-8 flex-1"
                      onChange={(event) => setAvailDescr(event.target.value)}
                    />
                  </div>
                ) : null}
                <GridScroller fill>
                  <Table>
                    <TableHeader>
                      {effectiveSource === "catalog" ? (
                        <>
                          <TableRow className="hover:bg-transparent">
                            {visibleCatalogColumns.map((column) => (
                              <TableHead
                                key={column.key}
                                className={
                                  column.align === "right" ? "text-right" : undefined
                                }
                              >
                                {column.label}
                              </TableHead>
                            ))}
                          </TableRow>
                          <TableRow className="hover:bg-transparent">
                            {visibleCatalogColumns.map((column) => (
                              <TableHead
                                key={column.key}
                                className="h-auto px-2 py-2 align-middle"
                              >
                                {column.filterParam ? (
                                  <ColumnFilterInput
                                    value={catFilters[column.filterParam] ?? ""}
                                    onChange={(value) =>
                                      setCatFilters((prev) => {
                                        const next = { ...prev }
                                        if (value) next[column.filterParam!] = value
                                        else delete next[column.filterParam!]
                                        return next
                                      })
                                    }
                                  />
                                ) : null}
                              </TableHead>
                            ))}
                          </TableRow>
                        </>
                      ) : (
                        <TableRow>
                          <TableHead className="w-36">Codice</TableHead>
                          <TableHead>Descrizione</TableHead>
                        </TableRow>
                      )}
                    </TableHeader>
                    <TableBody>
                      {availableLoading ? (
                        <TableRow>
                          <TableCell
                            colSpan={availableColSpan}
                            className="h-24 text-center text-muted-foreground"
                          >
                            Caricamento…
                          </TableCell>
                        </TableRow>
                      ) : available.length === 0 ? (
                        <TableRow>
                          <TableCell
                            colSpan={availableColSpan}
                            className="h-24 text-center text-muted-foreground"
                          >
                            Nessun articolo.
                          </TableCell>
                        </TableRow>
                      ) : (
                        available.map((item) => {
                          // Senza codice Codex non è trascinabile: prima si codifica.
                          const trascinabile =
                            canEdit && !(item.source === "catalog" && !item.codexId)
                          return (
                            <TableRow
                              key={`${item.source}-${item.id}`}
                              draggable={trascinabile}
                              onDragStart={(event) => {
                                dragItem.current = item
                                event.dataTransfer.effectAllowed = "copy"
                                event.dataTransfer.setData("text/plain", item.codice)
                              }}
                              onDragEnd={() => {
                                dragItem.current = null
                                setHoverKey(null)
                              }}
                              onDoubleClick={() => handleQuickAdd(item)}
                              title={
                                trascinabile
                                  ? "Trascina sull'albero o doppio click per aggiungere"
                                  : undefined
                              }
                              className={cn(
                                trascinabile && "cursor-grab active:cursor-grabbing"
                              )}
                            >
                              {effectiveSource === "catalog" ? (
                                visibleCatalogColumns.map((column) => (
                                  <TableCell
                                    key={column.key}
                                    className={
                                      column.align === "right"
                                        ? "text-right"
                                        : undefined
                                    }
                                  >
                                    {renderCatalogCell(column, item)}
                                  </TableCell>
                                ))
                              ) : (
                                <>
                                  <TableCell className="font-mono font-medium">
                                    {item.codice}
                                  </TableCell>
                                  <TableCell className="whitespace-normal break-words text-muted-foreground">
                                    {item.descr || "—"}
                                  </TableCell>
                                </>
                              )}
                            </TableRow>
                          )
                        })
                      )}
                    </TableBody>
                  </Table>
                </GridScroller>
                {available.length > 0 ? (
                  <div className="border-t px-3 py-1 text-[11px] text-muted-foreground">
                    {availableTotal > available.length
                      ? `Primi ${available.length} di ${availableTotal} — affina la ricerca`
                      : `${available.length} articoli`}
                  </div>
                ) : null}
              </div>
            </div>

            {/* ── DESTRA: Composizione (albero) ── */}
            <div className="flex h-[26rem] min-h-0 flex-col overflow-hidden rounded-md border lg:h-auto">
              <div className="flex items-center justify-between gap-2 border-b bg-muted/40 px-3 py-2">
                <span
                  className="text-xs font-semibold tracking-wide"
                  style={{ color: ACCENT_COMPOSIZIONE }}
                >
                  DISTINTA COMPOSIZIONE
                  {selectedParent ? ` — ${formatCodice(selectedParent.codice)}` : ""}
                </span>
                {selectedParent && (
                  <span className="text-[11px] text-muted-foreground hidden sm:inline">
                    Trascina articoli o fai doppio clic a sinistra
                  </span>
                )}
              </div>
              <div
                onDragOver={rootDragOver}
                onDrop={rootDrop}
                className={cn(
                  "min-h-0 flex-1 overflow-y-auto p-3 transition-colors",
                  hoverKey === "root" && "ring-2 ring-inset ring-primary bg-primary/5"
                )}
              >
                {!selectedParent ? (
                  <div className="flex h-full flex-col items-center justify-center text-center text-sm text-muted-foreground p-8">
                    <PackageOpen className="size-10 text-muted-foreground/40 mb-2" />
                    <p className="font-medium text-foreground">Nessun composito selezionato</p>
                    <p className="text-xs text-muted-foreground mt-1 max-w-xs">
                      Seleziona un composito dalla tabella in alto a sinistra per visualizzarne o modificarne la distinta.
                    </p>
                  </div>
                ) : treeQuery.isLoading ? (
                  <div className="flex h-40 items-center justify-center text-sm text-muted-foreground">
                    <RefreshCw className="size-4 animate-spin mr-2" />
                    Caricamento distinta…
                  </div>
                ) : treeQuery.isError ? (
                  <div className="p-4 text-sm text-destructive bg-destructive/10 rounded-md">
                    {(treeQuery.error as Error).message}
                  </div>
                ) : treeData ? (
                  <CompositionTreeView node={treeData} ctx={ctx} />
                ) : null}
              </div>
            </div>
          </div>

          {/* ── STATUS BAR ── */}
          <div className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t pt-2 text-xs">
            <span className="text-muted-foreground">{statusText}</span>
            <span className="italic text-muted-foreground/80">
              Filtri: abc = contiene · abc* = inizia con · *abc = finisce con —
              Trascina gli articoli disponibili sull'albero
            </span>
          </div>
        </CardContent>
      </Card>
      <CodexImportDialog
        file={importFile}
        connRef={connectionIdRef}
        onSuccess={(parent) => {
          setImportFile(null)
          // Mostra subito la distinta importata: seleziona il composito, cambiando
          // tipo (501/601/701) se il file era di un tipo diverso da quello attivo.
          const prefix = parent.codice.replace(/\./g, "").slice(0, 3)
          if (prefix !== typeCode && COMPOSITION_TYPES.some((type) => type.code === prefix)) {
            pendingSelectRef.current = parent
            setTypeCode(prefix)
          } else {
            setSelectedParent(parent)
          }
          void queryClient.invalidateQueries({ queryKey: ["composition-tree"] })
        }}
        onCancel={() => setImportFile(null)}
      />

      <QuantityDialog
        open={pendingAdd != null}
        childCodice={pendingAdd?.child.codice ?? ""}
        onCancel={() => setPendingAdd(null)}
        onConfirm={(quantity) => {
          // Chiude subito il dialog (come il modale WPF) e poi aggiunge: niente
          // doppio inserimento se l'utente preme Invio due volte.
          const add = pendingAdd
          setPendingAdd(null)
          if (add) {
            addMutation.mutate({ parentId: add.parentId, child: add.child, quantity })
          }
        }}
      />

      <QuantityDialog
        open={editQty != null}
        mode="edit"
        childCodice={editQty?.codice ?? ""}
        initialQuantity={editQty?.quantity ?? 1}
        onCancel={() => setEditQty(null)}
        onConfirm={(quantity) => {
          const node = editQty
          setEditQty(null)
          if (node && quantity !== node.quantity) {
            quantityMutation.mutate({ compositionId: node.compositionId, quantity })
          }
        }}
      />

      <NewCompositeDialog
        open={newComposite}
        typeCode={typeCode}
        typeLabel={activeType.label}
        onClose={() => setNewComposite(false)}
        onCreated={async (created) => {
          setNewComposite(false)
          // Filtri azzerati: il composito appena creato deve comparire, anche se
          // l'elenco era filtrato su altro.
          setCompCode("")
          setCompDescr("")
          await queryClient.invalidateQueries({ queryKey: ["codex-by-prefix", typeCode] })
          const lista = queryClient.getQueryData<CodexListItem[]>([
            "codex-by-prefix",
            typeCode,
          ])
          const nuovo = lista?.find((item) => item.id === created.id)
          if (nuovo) setSelectedParent(nuovo)
        }}
      />

      {/* Rinomina descrizione: stesso dialog della pagina Codex Articoli, stesso
          permesso del server (action.manage_codex, PUT /api/codex/{id}). */}
      <CodexEditDialog
        item={editComposite}
        onClose={() => setEditComposite(null)}
        onSaved={async () => {
          setEditComposite(null)
          // La descrizione compare anche nella radice dell'albero aperto.
          await queryClient.invalidateQueries({ queryKey: ["codex-by-prefix", typeCode] })
          void queryClient.invalidateQueries({ queryKey: ["composition-tree"] })
        }}
      />

      {/* Codifica al volo: stesso dialog del Catalogo Articoli (cerca un Codex esistente
          o ne crea uno generico 201/211/221). Dopo il salvataggio l'elenco si ricarica e
          l'articolo compare tra i «Con codice Codex», pronto da trascinare. */}
      <CatalogAtecAssignDialog
        item={atecTarget}
        onClose={() => setAtecTarget(null)}
        onSaved={() => {
          setAtecTarget(null)
          void queryClient.invalidateQueries({ queryKey: ["catalog-available"] })
        }}
      />
    </div>
  )
}
