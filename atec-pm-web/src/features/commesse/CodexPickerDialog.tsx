import * as React from "react"
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { ChevronDown, ChevronRight, Link2, Plus } from "lucide-react"

import { ColumnFilterInput } from "@/components/shared/column-filter-input"
import { ColumnsMenu } from "@/components/shared/columns-menu"
import { useConfirm } from "@/components/shared/confirm"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
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
import { CatalogAtecAssignDialog } from "@/features/catalogo/CatalogAtecAssignDialog"
import { fetchCatalogItems } from "@/lib/api/catalog"
import {
  fetchCodexDerivati101,
  fetchCodexPickerRows,
  type CodexPickerRow,
} from "@/lib/api/codex-picker"
import { fetchCompositionChildren } from "@/lib/api/codex-compositions"
import { fetchDdpStatuses } from "@/lib/api/ddp-config"
import { createDdpRow, fetchDdpRows, updateDdpRow } from "@/lib/api/project-ddp"
import {
  addOfficinaItem,
  fetchOfficinaItems,
  importOfficinaComposition,
  updateOfficinaItem,
} from "@/lib/api/project-ddp-officina"
import type {
  CatalogItemListItem,
  CompositionChildItem,
  OfficinaImportCompositionResult,
} from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"
import { euro, dash } from "@/lib/format"
import { notifyError } from "@/lib/toast"
import { useDebounced } from "@/lib/use-debounced"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"

import { CodexGeneratePanel } from "../codex/CodexGeneratePanel"
import {
  DDP_STATUS_TO_ORDER,
  DDP_STATUS_VERIFY,
  isCommercialQtyEditable,
} from "./ddp-constants"
import { inserisciOfficina, type GrezzoScelto } from "./officina-insert"

const PAGE_SIZE = 50
const ALL_FAMILIES = "__all__"

// Tutte le famiglie generabili (401 «Materia prima» è ritirata). La tendina governa
// anche la SORGENTE: le famiglie commerciali si sfogliano dal Catalogo Danea (fornitori
// e prezzi veri, pulsante «Codifica» sugli articoli non ancora associati), le altre dal
// Codex. La DESTINAZIONE della riga però non la decide la tendina: la decide la prima
// cifra del codice, specchio di DdpSmistamento lato server (#119).
const FAMILIES: { code: string; label: string }[] = [
  { code: "101", label: "101 — Particolari a disegno" },
  { code: "201", label: "201 — Commerciale generico" },
  { code: "211", label: "211 — Commerciale elettrico" },
  { code: "221", label: "221 — Commerciale pneumatico" },
  { code: "301", label: "301 — Elementi di fissaggio" },
  { code: "501", label: "501 — Gruppo meccanico" },
  { code: "511", label: "511 — Gruppo custom" },
  { code: "601", label: "601 — Assieme meccanico" },
  { code: "701", label: "701 — Layout meccanico" },
]
const CATALOG_FAMILIES = new Set(["201", "211", "221"])

/** Punto prima delle ultime 3 cifre (stessa formattazione della pagina Codex). */
function formatCodice(codice: string): string {
  const raw = (codice ?? "").replace(/\./g, "")
  return raw.length > 3 ? `${raw.slice(0, raw.length - 3)}.${raw.slice(-3)}` : raw
}


/**
 * Specchio client di `DdpSmistamento` (server): dove finisce un codice, dalla prima
 * cifra. Serve SOLO per avvisare l'operatore prima dell'inserimento — la regola
 * autorevole resta quella del server.
 */
function destinazioneDi(atecRaw: string): "COMMERCIAL" | "OFFICINA" | "GRUPPO" | null {
  switch (atecRaw.charAt(0)) {
    case "2":
    case "3":
      return "COMMERCIAL"
    case "1":
      return "OFFICINA"
    case "5":
    case "6":
    case "7":
      return "GRUPPO"
    default:
      return null
  }
}

interface PickerColumn {
  key: string
  label: string
  align?: "right"
  /** Parametro server per il filtro per colonna (assente = colonna non filtrabile). */
  filterParam?: string
}

// Vista Catalogo (famiglie commerciali): visibilità scelta dal menu «Colonne».
const CATALOG_COLUMNS: PickerColumn[] = [
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

// Vista Codex (Tutte le famiglie, 101/301/5xx/6xx/7xx): righe dall'endpoint dedicato
// /api/codex/picker (#128) — un abbinamento Danea = una riga, così codice articolo e
// produttore si CERCANO e si SCELGONO direttamente anche da qui.
const CODEX_COLUMNS: PickerColumn[] = [
  { key: "codice", label: "Cod. ATEC", filterParam: "codice" },
  { key: "descr", label: "Descrizione", filterParam: "descr" },
  { key: "articolo", label: "Cod. articolo", filterParam: "articolo" },
  { key: "fornitore", label: "Fornitore", filterParam: "fornitore" },
  { key: "produttore", label: "Produttore", filterParam: "produttore" },
  { key: "um", label: "UM" },
  { key: "costo", label: "Costo", align: "right" },
]

type PickEntry =
  | { kind: "catalog"; item: CatalogItemListItem }
  | { kind: "codex"; item: CodexPickerRow }
  // #142: riga della sezione «Lavorati con grezzo commerciale» (vista 2xx) —
  // codiceAtec è il 101, articolo/fornitore sono dell'abbinamento del SUO grezzo.
  | { kind: "derivato101"; item: CodexPickerRow }

/**
 * Picker UNICO delle due DDP di commessa (`ddpType` = la distinta da cui è aperto).
 * Tutte le famiglie sono a disposizione; ogni codice finisce nella DDP giusta per la
 * sua famiglia (specchio di DdpSmistamento, #119):
 *
 * - 2xx/3xx → riga nella DDP Commerciale (dall'Officina, con conferma);
 * - 1xx → riga nella DDP Officina (dalla Commerciale, con conferma);
 * - 5xx/6xx/7xx → import della composizione: componenti smistati automaticamente e
 *   intestazione SOLO nelle distinte dove il gruppo ha componenti (fix 26/08/2026:
 *   un gruppo di soli commerciali non lascia più il padre orfano in Officina).
 *   Un gruppo SENZA figli non si importa (messaggio, niente riga orfana).
 *
 * Doppio clic o «+» = Qtà 1; se il codice è già in distinta nello stato d'ingresso
 * propone +1. «Nuovo codice Codex» genera un codice e lo aggiunge subito. Resta
 * aperto per inserimenti multipli, come sempre.
 */
export function CodexPickerDialog({
  open,
  projectId,
  ddpType,
  onClose,
  onAdded,
}: {
  open: boolean
  projectId: number
  /** La DDP da cui il picker è aperto: decide solo gli AVVISI, non la destinazione. */
  ddpType: "COMMERCIAL" | "OFFICINA"
  onClose: () => void
  /** Invocato dopo ogni inserimento: il parent ricarica la griglia. */
  onAdded: () => void
}) {
  const confirm = useConfirm()
  const queryClient = useQueryClient()
  const requestedBy = getSession()?.user.fullName ?? ""
  const canAssignAtec = canWriteFeature("action.assign_atec_code")
  // Le chiavi che /api/codex/reserve|confirm accettano davvero (CodexController):
  // senza una di queste il pulsante «Nuovo codice Codex» regalerebbe solo un 403.
  const canGenerate =
    canWriteFeature("action.manage_codex") ||
    canWriteFeature("action.assign_atec_code") ||
    canWriteFeature("project.ddp_officina")

  const [family, setFamily] = React.useState(ALL_FAMILIES)
  const [filters, setFilters] = React.useState<Record<string, string>>({})
  const debouncedFilters = useDebounced(filters, 300)
  const [addedCount, setAddedCount] = React.useState(0)
  const [message, setMessage] = React.useState<string | null>(null)
  const [error, setError] = React.useState<string | null>(null)
  const [showGenerate, setShowGenerate] = React.useState(false)
  // Codifica al volo dell'articolo Danea senza codice ATEC (vista Catalogo).
  const [atecTarget, setAtecTarget] = React.useState<CatalogItemListItem | null>(null)
  // Gruppi (5xx/6xx/7xx) aperti col chevron: id → figli (o "loading"). La chiave
  // assente = riga chiusa; i figli si caricano alla prima apertura e restano in cache.
  const [expandedGroups, setExpandedGroups] = React.useState<
    Record<number, CompositionChildItem[] | "loading">
  >({})

  const [visibility, setVisibility] = usePersistedColumnVisibility(
    "ddp-catalog-picker-cols-v1",
    CATALOG_COLUMNS_DEFAULTS
  )

  // Etichette leggibili degli stati DDP: stessa queryKey delle pagine DDP, quindi
  // la mappa arriva dalla cache di chi ha aperto il picker. Serve a non mostrare
  // mai all'utente il codice grezzo dello stato (es. «ORD»).
  const statusesQuery = useQuery({
    queryKey: ["ddp-statuses"],
    queryFn: fetchDdpStatuses,
  })
  const statusMap = React.useMemo(
    () => new Map((statusesQuery.data ?? []).map((s) => [s.statusKey, s])),
    [statusesQuery.data]
  )

  const vista: "catalog" | "codex" = CATALOG_FAMILIES.has(family)
    ? "catalog"
    : "codex"
  const visibleColumns =
    vista === "catalog"
      ? CATALOG_COLUMNS.filter((column) => visibility[column.key] !== false)
      : CODEX_COLUMNS

  // Reset alla riapertura: niente residui dell'ultima sessione di inserimento
  // (la scelta delle colonne invece resta: è una preferenza, non un filtro).
  React.useEffect(() => {
    if (open) {
      setFamily(ALL_FAMILIES)
      setFilters({})
      setAddedCount(0)
      setMessage(null)
      setError(null)
      setShowGenerate(false)
      setExpandedGroups({})
    }
  }, [open])

  // Chevron dei gruppi: apre/chiude e carica i figli diretti alla prima apertura.
  async function toggleGroup(row: CodexPickerRow) {
    const id = row.codexId
    if (expandedGroups[id] !== undefined) {
      setExpandedGroups((prev) => {
        const next = { ...prev }
        delete next[id]
        return next
      })
      return
    }
    setExpandedGroups((prev) => ({ ...prev, [id]: "loading" }))
    try {
      const children = await fetchCompositionChildren(id)
      setExpandedGroups((prev) =>
        id in prev
          ? { ...prev, [id]: children.filter((ch) => ch.source === "codex") }
          : prev
      )
    } catch (err) {
      setExpandedGroups((prev) => {
        const next = { ...prev }
        delete next[id]
        return next
      })
      setError((err as Error).message)
    }
  }

  /** Badge della DDP in cui finirà un componente (anteprima dello smistamento). */
  function renderDestinationBadge(codice: string) {
    const dest = destinazioneDi((codice ?? "").replace(/\./g, ""))
    switch (dest) {
      case "COMMERCIAL":
        return (
          <span className="inline-flex items-center gap-1.5 rounded-full border border-sky-200 bg-sky-50 px-2 py-0.5 text-[11px] font-medium text-sky-700 dark:border-sky-800 dark:bg-sky-950/40 dark:text-sky-300">
            <span className="size-1.5 rounded-full bg-sky-500" />
            DDP Commerciale
          </span>
        )
      case "OFFICINA":
        return (
          <span className="inline-flex items-center gap-1.5 rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-700 dark:border-amber-800 dark:bg-amber-950/40 dark:text-amber-300">
            <span className="size-1.5 rounded-full bg-amber-500" />
            DDP Officina
          </span>
        )
      case "GRUPPO":
        return (
          <span className="inline-flex items-center gap-1.5 rounded-full border border-purple-200 bg-purple-50 px-2 py-0.5 text-[11px] font-medium text-purple-700 dark:border-purple-800 dark:bg-purple-950/40 dark:text-purple-300">
            <span className="size-1.5 rounded-full bg-purple-500" />
            Sotto-gruppo (Officina)
          </span>
        )
      default:
        return null
    }
  }

  // I parametri filtro delle due viste sono diversi: cambiando famiglia (e quindi
  // eventualmente vista) i filtri ripartono puliti. Se qualcosa era digitato,
  // l'azzeramento va DETTO, o l'elenco che cambia sembra un capriccio del programma.
  function cambiaFamiglia(next: string) {
    const vistaNext = CATALOG_FAMILIES.has(next) ? "catalog" : "codex"
    if (vistaNext !== vista) {
      if (Object.keys(filters).length > 0) {
        setError(null)
        setMessage(
          "Filtri azzerati: cambiando famiglia cambia l'archivio di ricerca."
        )
      }
      setFilters({})
    }
    setFamily(next)
  }

  const setColumnFilter = React.useCallback((param: string, value: string) => {
    setFilters((prev) => {
      const next = { ...prev }
      if (value) next[param] = value
      else delete next[param]
      return next
    })
  }, [])

  // ── Vista Catalogo: la tendina famiglia filtra il codice ATEC; il filtro digitato
  //    in colonna vince (un solo LIKE per colonna lato server).
  const catalogFilters = React.useMemo(() => {
    const merged = { ...debouncedFilters }
    if (vista === "catalog" && !merged.atecCode && family !== ALL_FAMILIES) {
      merged.atecCode = `${family}*`
    }
    return merged
  }, [debouncedFilters, family, vista])

  const catalogQuery = useInfiniteQuery({
    queryKey: ["catalog-picker", catalogFilters],
    queryFn: ({ pageParam }) =>
      fetchCatalogItems({
        page: pageParam,
        pageSize: PAGE_SIZE,
        filters: catalogFilters,
      }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasMore ? last.page + 1 : undefined),
    enabled: open && vista === "catalog",
  })

  // ── Vista Codex: endpoint dedicato (#128) — i prefissi valgono sul codice ATEC
  //    effettivo, quindi anche i vecchi ricodificati compaiono nella loro famiglia,
  //    e ogni abbinamento Danea è una riga con codice articolo e produttore cercabili.
  const codexPrefixes =
    family === ALL_FAMILIES ? ["1", "2", "3", "5", "6", "7"] : [family]
  const codexQuery = useInfiniteQuery({
    queryKey: ["ddp-picker-codex", codexPrefixes, debouncedFilters],
    queryFn: ({ pageParam }) =>
      fetchCodexPickerRows({
        page: pageParam,
        pageSize: PAGE_SIZE,
        codicePrefixes: codexPrefixes,
        filters: debouncedFilters,
      }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasMore ? last.page + 1 : undefined),
    enabled: open && vista === "codex",
  })

  // ── #142: lavorati 101 col grezzo commerciale, in sezione dedicata della vista 2xx.
  //    I filtri di colonna del Catalogo si traducono sulle chiavi dell'endpoint derivati
  //    (il codice ATEC digitato filtra il 101, articolo/fornitore filtrano il grezzo).
  const derivatiFilters = React.useMemo(() => {
    const f: Record<string, string> = {}
    if (debouncedFilters.atecCode) f.codice = debouncedFilters.atecCode
    if (debouncedFilters.description) f.descr = debouncedFilters.description
    if (debouncedFilters.code) f.articolo = debouncedFilters.code
    if (debouncedFilters.supplier) f.fornitore = debouncedFilters.supplier
    if (debouncedFilters.manufacturer) f.produttore = debouncedFilters.manufacturer
    return f
  }, [debouncedFilters])
  const derivatiQuery = useQuery({
    queryKey: ["ddp-picker-derivati-101", derivatiFilters],
    queryFn: () =>
      fetchCodexDerivati101({ pageSize: 100, filters: derivatiFilters }),
    enabled: open && vista === "catalog",
  })
  const derivati = derivatiQuery.data?.items ?? []
  const derivatiTotal = derivatiQuery.data?.totalCount ?? 0

  const query = vista === "catalog" ? catalogQuery : codexQuery
  const catalogItems = React.useMemo(
    () => catalogQuery.data?.pages.flatMap((p) => p.items) ?? [],
    [catalogQuery.data]
  )
  const codexItems = React.useMemo(
    () => codexQuery.data?.pages.flatMap((p) => p.items) ?? [],
    [codexQuery.data]
  )
  const rowCount = vista === "catalog" ? catalogItems.length : codexItems.length
  const totalCount = query.data?.pages[0]?.totalCount ?? 0

  // ── Inserimento nella DDP COMMERCIALE (2xx/3xx) ─────────────────────────────
  async function aggiungiCommerciale(entry: PickEntry, atecRaw: string) {
    // Dedup per codice ATEC sulle righe in stato d'ingresso (VER). Se la lettura
    // fallisce si ANNULLA: inserire alla cieca creerebbe doppioni silenziosi.
    let existing
    try {
      existing = await fetchDdpRows(projectId, "COMMERCIAL")
    } catch {
      throw new Error(
        "Impossibile leggere la DDP Commerciale: inserimento annullato per non creare doppioni."
      )
    }
    const duplicate =
      existing.find(
        (r) =>
          (r.atecCode ?? "").replace(/\./g, "") === atecRaw &&
          r.itemStatus === DDP_STATUS_VERIFY
      ) ?? null
    if (duplicate) {
      const ok = await confirm({
        title: "Codice già presente",
        description: `Il codice ${formatCodice(atecRaw)} è già nella DDP Commerciale in stato Verificare magazzino (Qtà attuale: ${duplicate.quantity}).\n\nVuoi aggiungere +1 alla quantità?`,
        confirmLabel: "Aggiungi +1",
        destructive: false,
      })
      if (!ok) return null
      await updateDdpRow(projectId, duplicate.id, {
        id: duplicate.id,
        projectId,
        catalogItemId: duplicate.catalogItemId ?? null,
        partNumber: duplicate.partNumber,
        description: duplicate.description,
        unit: duplicate.unit,
        quantity: duplicate.quantity + 1,
        unitCost: duplicate.unitCost,
        supplierId: null,
        manufacturer: duplicate.manufacturer,
        itemStatus: duplicate.itemStatus,
        requestedBy: duplicate.requestedBy,
        daneaRef: duplicate.daneaRef,
        dateNeeded: duplicate.dateNeeded,
        destination: duplicate.destination,
        destinationSpec: duplicate.destinationSpec ?? "",
        notes: duplicate.notes,
        ddpType: "COMMERCIAL",
        expectedUpdatedAt: null,
      })
      return { code: formatCodice(atecRaw), testo: "Qtà aggiornata" }
    }

    // Articolo Danea di partenza: nella vista Catalogo è la riga scelta; nella vista
    // Codex ogni riga È già un abbinamento preciso (#128) — un codice senza articolo
    // nasce «da definire» col prezzo del Codex (come l'import server).
    let art: {
      catalogItemId: number | null
      partNumber: string
      description: string
      unit: string
      unitCost: number
      supplierId: number | null
      manufacturer: string
    }
    if (entry.kind === "catalog") {
      const i = entry.item
      art = {
        catalogItemId: i.id,
        partNumber: i.code,
        description: i.description,
        unit: i.unit || "PZ",
        unitCost: i.unitCost ?? 0,
        supplierId: i.supplierId,
        manufacturer: i.manufacturer,
      }
    } else {
      const r = entry.item
      art = r.catalogItemId
        ? {
            catalogItemId: r.catalogItemId,
            partNumber: r.codiceArticolo,
            description: r.descr,
            unit: r.unitArticolo || "PZ",
            unitCost: r.costoArticolo ?? r.prezzoCodex ?? 0,
            supplierId: r.supplierId,
            manufacturer: r.produttore,
          }
        : {
            catalogItemId: null,
            partNumber: "",
            description: r.descr,
            unit: "PZ",
            unitCost: r.prezzoCodex ?? 0,
            supplierId: null,
            manufacturer: "",
          }
    }
    const daDefinire = art.catalogItemId == null
    await createDdpRow(projectId, {
      id: 0,
      projectId,
      catalogItemId: art.catalogItemId,
      partNumber: art.partNumber,
      description: art.description,
      unit: art.unit,
      quantity: 1,
      unitCost: art.unitCost,
      supplierId: art.supplierId,
      manufacturer: art.manufacturer,
      itemStatus: DDP_STATUS_VERIFY,
      requestedBy,
      daneaRef: "",
      dateNeeded: null,
      destination: "",
      destinationSpec: "",
      notes: daDefinire ? "Fornitore da definire" : "",
      ddpType: "COMMERCIAL",
      atecCode: atecRaw,
      expectedUpdatedAt: null,
    })
    // Riga smistata dall'ALTRA distinta: si dice anche DOVE ritrovarla.
    const notaScheda =
      ddpType === "OFFICINA"
        ? ' — la trovi nella scheda "DDP Commerciali" di questa commessa'
        : ""
    return {
      code: formatCodice(atecRaw),
      testo: daDefinire
        ? `aggiunto alla DDP Commerciale (fornitore da definire)${notaScheda}`
        : `aggiunto alla DDP Commerciale${notaScheda}`,
    }
  }

  // ── Inserimento nella DDP OFFICINA (1xx) — il flusso storico vive in
  //    `officina-insert.ts` (#142: lo condivide col picker «per codice ATEC»).
  //    Con `grezzo` la riga nasce a costo 0 e senza fornitore: il materiale sta
  //    sulla riga del grezzo in Commerciale, che genera il motore #135.
  async function aggiungiOfficina(
    item: CodexPickerRow,
    grezzo?: GrezzoScelto | null
  ) {
    return inserisciOfficina({
      projectId,
      codiceAtec: item.codiceAtec,
      descrizione: item.descr,
      unitCost: grezzo ? 0 : item.prezzoCodex ?? 0,
      supplierName: grezzo ? "" : item.fornitoreCodex,
      requestedBy,
      confirm,
      // Riga smistata dall'ALTRA distinta: si dice anche DOVE ritrovarla.
      notaScheda:
        ddpType === "COMMERCIAL"
          ? ' — la trovi nella scheda "DDP Officina" di questa commessa'
          : "",
      grezzo: grezzo ?? null,
    })
  }

  // ── Gruppo di SOLI componenti commerciali (fix 26/08/2026): nella DDP Officina non
  //    entra NIENTE — niente padre orfano che sporcherebbe Lavorazioni Officine (vista
  //    Esterne), i KPI del Gestore e il Bilancio (OfficinaParentDedup non lo scarta).
  //    L'intestazione vive solo nella Commerciale (la crea il server alla prima riga
  //    commerciale) ed è lei il «padre che comanda»: un cambio quantità passa da
  //    ComposizioneDdp.PropagaQuantita come per gli altri gruppi. ──────────────────
  async function importaGruppoSoloCommerciale(item: CodexPickerRow) {
    const codeVis = formatCodice(item.codiceAtec)
    const parentKey = (item.codiceAtec ?? "").replace(/\./g, "").trim()
    let existing: Awaited<ReturnType<typeof fetchDdpRows>>
    try {
      existing = await fetchDdpRows(projectId, "COMMERCIAL")
    } catch {
      throw new Error(
        "Impossibile leggere la DDP Commerciale: import annullato per non raddoppiare le quantità."
      )
    }
    const header =
      existing.find(
        (r) =>
          (r.partNumber ?? "").replace(/\./g, "").trim() === parentKey &&
          r.parentBomItemId == null
      ) ?? null

    if (header) {
      // Intestazione già in distinta: +1 solo negli stati a quantità libera (VER/DO),
      // negli altri il server rifiuterebbe comunque la nuova quantità.
      if (!isCommercialQtyEditable(header.itemStatus)) {
        // Etichetta leggibile dello stato (fallback al codice se la mappa non c'è).
        const statoLabel =
          statusMap.get(header.itemStatus)?.label ?? header.itemStatus
        throw new Error(
          `${codeVis} è già nella DDP Commerciale in stato ${statoLabel}: lì la quantità è bloccata, gestiscilo dalla distinta.`
        )
      }
      const okPiu = await confirm({
        title: "Gruppo già presente",
        description: `${codeVis} è già nella DDP Commerciale (Qtà attuale: ${header.quantity}).\n\nVuoi aggiungere +1 alla quantità?`,
        confirmLabel: "Aggiungi +1",
        destructive: false,
      })
      if (!okPiu) return null
      await updateDdpRow(projectId, header.id, {
        id: header.id,
        projectId,
        catalogItemId: header.catalogItemId ?? null,
        partNumber: header.partNumber,
        description: header.description,
        unit: header.unit,
        quantity: header.quantity + 1,
        unitCost: header.unitCost,
        supplierId: null,
        manufacturer: header.manufacturer,
        itemStatus: header.itemStatus,
        requestedBy: header.requestedBy,
        daneaRef: header.daneaRef,
        dateNeeded: header.dateNeeded,
        destination: header.destination,
        destinationSpec: header.destinationSpec ?? "",
        notes: header.notes,
        ddpType: "COMMERCIAL",
        expectedUpdatedAt: null,
      })
      // Figli mai collegati (import interrotto a metà): si completa ora — il server
      // usa la quantità dell'intestazione come moltiplicatore.
      const hasLinkedChildren = existing.some(
        (r) => r.parentBomItemId === header.id
      )
      let imported: OfficinaImportCompositionResult | null = null
      if (!hasLinkedChildren) {
        imported = await importOfficinaComposition(projectId, {
          codexParentId: item.codexId,
          requestedBy,
        })
      }
      const mult =
        imported && imported.parentQuantity !== 1
          ? ` ×${imported.parentQuantity}`
          : ""
      const compo = imported
        ? ` + componenti importati${mult} (${imported.added} nuovi, ${imported.updated} aggiornati)`
        : " (componenti allineati all'intestazione)"
      return { code: codeVis, testo: `Qtà aggiornata${compo}` }
    }

    const imported = await importOfficinaComposition(projectId, {
      codexParentId: item.codexId,
      requestedBy,
    })
    // Aperto dall'Officina qui non compare nulla: si dice DOVE è finito il gruppo.
    const notaScheda =
      ddpType === "OFFICINA"
        ? ' — la trovi nella scheda "DDP Commerciali" di questa commessa'
        : ""
    return {
      code: codeVis,
      testo: `importato nella sola DDP Commerciale: ${imported.added} componenti nuovi, ${imported.updated} aggiornati${imported.skipped ? `, ${imported.skipped} saltati` : ""}${notaScheda}`,
    }
  }

  // ── Import di un gruppo/assieme (5xx/6xx/7xx): componenti smistati dal server
  //    (#119) e intestazione solo dove il gruppo ha componenti. SENZA figli non si
  //    importa. ──────────
  async function importaGruppo(item: CodexPickerRow) {
    const codeVis = formatCodice(item.codiceAtec)
    const children = await fetchCompositionChildren(item.codexId)
    const codexChildren = children.filter((ch) => ch.source === "codex")
    if (codexChildren.length === 0) {
      throw new Error(
        `${codeVis} non ha componenti in composizione: un gruppo o assieme si importa solo coi suoi figli. Completa prima la distinta in Composizione Codex.`
      )
    }
    const pieces = codexChildren.reduce((sum, ch) => sum + ch.quantity, 0)

    // Specchio di DdpSmistamento (prima cifra): 2xx/3xx → commerciale, tutto il resto
    // (1xx, sotto-gruppi 5xx/6xx/7xx, famiglie ignote) → officina.
    const haFigliOfficina = codexChildren.some((ch) => {
      const key = (ch.childCodice ?? "").replace(/\./g, "").trim()
      return key.length > 0 && !/^[23]/.test(key)
    })

    const smistamento = haFigliOfficina
      ? "I componenti verranno smistati automaticamente: i commerciali (2xx/3xx) nella DDP Commerciale, il resto nella DDP Officina, con l'intestazione del gruppo dove ha componenti. I codici già presenti sommeranno le quantità."
      : "I componenti sono tutti commerciali (2xx/3xx): finiranno nella DDP Commerciale con l'intestazione del gruppo. Nella DDP Officina non entrerà nulla."
    const ok = await confirm({
      title: "Importare il gruppo?",
      description: `${codeVis} è un gruppo con ${codexChildren.length} componenti (${pieces} pezzi).\n\n${smistamento}`,
      confirmLabel: "Importa",
      destructive: false,
    })
    if (!ok) return null

    if (!haFigliOfficina) {
      return importaGruppoSoloCommerciale(item)
    }

    // Riga del padre in DDP Officina (è il «padre che comanda»): se c'è già in Da
    // Ordinare propone +1, e se i componenti sono già collegati il server li ha già
    // riallineati — un secondo import sommerebbe un set doppio. Per lo stesso motivo,
    // se la lettura fallisce si ANNULLA: importare alla cieca moltiplica le quantità.
    let imported: OfficinaImportCompositionResult | null = null
    let existing: Awaited<ReturnType<typeof fetchOfficinaItems>>
    try {
      existing = await fetchOfficinaItems(projectId)
    } catch {
      throw new Error(
        "Impossibile leggere la DDP Officina: import annullato per non raddoppiare le quantità."
      )
    }
    const duplicate = existing.find(
      (r) =>
        r.partNumber === item.codiceAtec &&
        r.itemStatus === DDP_STATUS_TO_ORDER
    )
    if (duplicate) {
      const okPiu = await confirm({
        title: "Gruppo già presente",
        description: `${codeVis} è già nella DDP Officina in stato Da Ordinare (Qtà attuale: ${duplicate.quantity}).\n\nVuoi aggiungere +1 alla quantità?`,
        confirmLabel: "Aggiungi +1",
        destructive: false,
      })
      if (!okPiu) return null
      await updateOfficinaItem(projectId, duplicate.id, {
        id: duplicate.id,
        projectId,
        partNumber: duplicate.partNumber,
        description: duplicate.description,
        quantity: duplicate.quantity + 1,
        quantityProduced: duplicate.quantityProduced ?? 0,
        unitCost: duplicate.unitCost,
        material: duplicate.material,
        treatment: duplicate.treatment,
        supplierName: duplicate.supplierName,
        itemStatus: duplicate.itemStatus,
        requestedBy: duplicate.requestedBy,
        daneaRef: duplicate.daneaRef,
        dateNeeded: duplicate.dateNeeded,
        orderDate: duplicate.orderDate,
        destination: duplicate.destination,
        destinationSpec: duplicate.destinationSpec ?? "",
        notes: duplicate.notes,
        expectedUpdatedAt: null,
      })
      const hasLinkedChildren = existing.some(
        (r) => r.parentOfficinaItemId === duplicate.id
      )
      if (!hasLinkedChildren) {
        imported = await importOfficinaComposition(projectId, {
          codexParentId: item.codexId,
          requestedBy,
        })
      }
      const mult =
        imported && imported.parentQuantity !== 1
          ? ` ×${imported.parentQuantity}`
          : ""
      const compo = imported
        ? ` + componenti smistati${mult} (${imported.added} nuovi, ${imported.updated} aggiornati)`
        : " (componenti allineati al padre)"
      return { code: codeVis, testo: `Qtà aggiornata${compo}` }
    }

    await addOfficinaItem(projectId, {
      id: 0,
      projectId,
      partNumber: item.codiceAtec,
      description: item.descr,
      quantity: 1,
      quantityProduced: 0,
      unitCost: item.prezzoCodex ?? 0,
      material: "",
      treatment: "",
      supplierName: item.fornitoreCodex,
      itemStatus: DDP_STATUS_TO_ORDER,
      requestedBy,
      daneaRef: "",
      dateNeeded: null,
      orderDate: null,
      destination: "",
      destinationSpec: "",
      notes: "",
    })
    imported = await importOfficinaComposition(projectId, {
      codexParentId: item.codexId,
      requestedBy,
    })
    const multNuovo =
      imported.parentQuantity !== 1 ? ` ×${imported.parentQuantity}` : ""
    return {
      code: codeVis,
      testo: `importato${multNuovo}: ${imported.added} componenti nuovi, ${imported.updated} aggiornati${imported.skipped ? `, ${imported.skipped} saltati` : ""}`,
    }
  }

  const addMutation = useMutation({
    mutationFn: async (entry: PickEntry) => {
      setError(null)

      // ── #142: lavorato con grezzo commerciale — riga 101 in Officina, il grezzo
      //    lo genera il motore #135; qui al più si applica la scelta del fornitore.
      if (entry.kind === "derivato101") {
        const item = entry.item
        const grezzoVis = formatCodice(item.grezzoCodice ?? "")
        const scoperto = item.catalogItemId == null
        const ok = await confirm({
          title: "Lavorato con grezzo commerciale",
          description:
            `${formatCodice(item.codiceAtec)} è un particolare a disegno (1xx): la riga andrà nella DDP Officina.\n` +
            (scoperto
              ? `Il suo grezzo ${grezzoVis} comparirà nella DDP Commerciale DA ASSOCIARE a un articolo commerciale (resterà bloccato finché non lo associ).`
              : `Il suo grezzo ${grezzoVis} comparirà nella DDP Commerciale con fornitore ${item.fornitoreNome || "da definire"}.`) +
            `\n\nVuoi continuare?`,
          confirmLabel: "Inserisci",
          destructive: false,
        })
        if (!ok) return null
        return aggiungiOfficina(item, {
          codice: item.grezzoCodice ?? "",
          catalogItemId: item.catalogItemId,
          fornitoreNome: item.fornitoreNome,
          scoperto,
        })
      }

      // Codice ATEC e destinazione: senza codice non si entra in nessuna distinta.
      let atecRaw: string
      let codexItem: CodexPickerRow | null = null
      if (entry.kind === "catalog") {
        atecRaw = (entry.item.atecCode || "").replace(/\./g, "").trim()
        if (!atecRaw || !entry.item.codexItemId) {
          throw new Error(
            `${entry.item.code} è senza codice Codex: codificalo (pulsante «Codifica») prima di metterlo in distinta.`
          )
        }
      } else {
        // I codici storici commerciali non ricodificati non arrivano qui: li esclude
        // già l'endpoint del picker (vedi CodexPickerController).
        codexItem = entry.item
        atecRaw = (entry.item.codiceAtec ?? "").replace(/\./g, "").trim()
        if (!atecRaw) {
          throw new Error(`${entry.item.codiceAtec}: codice non riconosciuto.`)
        }
      }

      const dest = destinazioneDi(atecRaw)
      if (dest === null) {
        throw new Error(
          `${formatCodice(atecRaw)}: famiglia non gestita nelle DDP (la 4xx è ritirata).`
        )
      }

      // ── Gruppi e assiemi: import con smistamento, da entrambe le DDP ──────────
      if (dest === "GRUPPO") {
        // Dalla vista Catalogo non possono arrivare 5xx: qui c'è sempre il Codex.
        if (!codexItem) throw new Error("Un gruppo si aggiunge dalla vista Codex.")
        return importaGruppo(codexItem)
      }

      // ── Avviso incrociato: l'oggetto finirà nell'ALTRA distinta ───────────────
      if (dest !== ddpType) {
        const daA =
          dest === "OFFICINA"
            ? `${formatCodice(atecRaw)} è un particolare d'officina (1xx): la riga NON finirà qui, ma nella DDP Officina.`
            : `${formatCodice(atecRaw)} è un codice commerciale (${atecRaw.charAt(0)}xx): la riga NON finirà qui, ma nella DDP Commerciale.`
        const ok = await confirm({
          title: "Distinta diversa",
          description: `${daA}\n\nVuoi inserirlo comunque?`,
          confirmLabel: "Inserisci",
          destructive: false,
        })
        if (!ok) return null
      }

      if (dest === "OFFICINA") {
        // I 1xx vivono solo nel Codex (un particolare a disegno non è un articolo Danea).
        if (!codexItem) throw new Error("Un particolare 1xx si aggiunge dalla vista Codex.")
        return aggiungiOfficina(codexItem)
      }
      return aggiungiCommerciale(entry, atecRaw)
    },
    onSuccess: (result) => {
      if (!result) return
      setAddedCount((n) => n + 1)
      setMessage(`✓ ${result.code}: ${result.testo}`)
      onAdded()
    },
    // Errore bloccante: oltre alla riga nel footer (facile da non vedere) anche
    // il toast rosso standard dell'app.
    onError: (err: Error) => {
      setError(err.message)
      notifyError(err)
    },
  })

  function handleAdd(entry: PickEntry) {
    if (addMutation.isPending) return
    addMutation.mutate(entry)
  }

  // Codice appena generato: riletto dal picker (per codice esatto) e aggiunto subito.
  async function handleGenerated(codice: string) {
    setShowGenerate(false)
    try {
      // Il server risponde col codice FORMATTATO (col punto), il DB lo salva senza:
      // il confronto va fatto a punti tolti o non si ritrova mai.
      const raw = codice.replace(/\./g, "")
      const page = await fetchCodexPickerRows({
        filters: { codice: raw },
        pageSize: 50,
      })
      const created = page.items.find(
        (r) => (r.codiceAtec ?? "").replace(/\./g, "") === raw
      )
      if (!created) {
        const msg = `Codice ${codice} generato ma non ritrovato nel Codex.`
        setError(msg)
        notifyError(msg)
        return
      }
      await codexQuery.refetch()
      addMutation.mutate({ kind: "codex", item: created })
    } catch (err) {
      setError((err as Error).message)
      notifyError(err)
    }
  }

  function renderCatalogCell(column: PickerColumn, item: CatalogItemListItem) {
    switch (column.key) {
      case "atecCode":
        return item.atecCode && item.codexItemId ? (
          <span
            className="font-medium tabular-nums text-primary"
            title="Codice Codex associato: è questo che entra in distinta"
          >
            {formatCodice(item.atecCode)}
          </span>
        ) : (
          <Button
            size="sm"
            variant="outline"
            className="h-6 px-2 text-xs"
            disabled={!canAssignAtec}
            title={
              canAssignAtec
                ? "Associa (o crea) il codice Codex di questo articolo"
                : "Serve il permesso di codifica"
            }
            onClick={(event) => {
              event.stopPropagation()
              setAtecTarget(item)
            }}
          >
            <Link2 className="size-3" />
            Codifica
          </Button>
        )
      case "code":
        return <span className="font-medium">{item.code}</span>
      case "description":
        return (
          <span className="block max-w-[320px] truncate" title={item.description}>
            {dash(item.description)}
          </span>
        )
      case "unit":
        return dash(item.unit)
      case "supplierName":
        return (
          <span className="block max-w-[180px] truncate" title={item.supplierName}>
            {dash(item.supplierName)}
          </span>
        )
      case "manufacturer":
        return dash(item.manufacturer)
      case "unitCost":
        return <span className="tabular-nums">{euro(item.unitCost)}</span>
      default:
        return null
    }
  }

  function renderCodexCell(column: PickerColumn, row: CodexPickerRow) {
    switch (column.key) {
      case "codice":
        return (
          <span className="font-medium tabular-nums text-primary">
            {formatCodice(row.codiceAtec)}
          </span>
        )
      case "descr":
        return (
          <span className="block max-w-[320px] truncate" title={row.descr}>
            {dash(row.descr)}
          </span>
        )
      case "articolo":
        return (
          <span className="font-medium" title={row.codiceArticolo}>
            {dash(row.codiceArticolo)}
          </span>
        )
      case "fornitore":
        return (
          <span
            className="block max-w-[180px] truncate"
            title={row.fornitoreNome || row.fornitoreCodex}
          >
            {dash(row.fornitoreNome || row.fornitoreCodex)}
          </span>
        )
      case "produttore":
        return dash(row.produttore)
      case "um":
        return dash(row.unitArticolo || row.umCodex)
      case "costo":
        return (
          <span className="tabular-nums">
            {euro(row.costoArticolo ?? row.prezzoCodex)}
          </span>
        )
      default:
        return null
    }
  }

  // Scroll infinito: avvicinandosi al fondo carica la pagina successiva.
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = query
  const handleScroll = React.useCallback(
    (event: React.UIEvent<HTMLDivElement>) => {
      const el = event.currentTarget
      if (
        hasNextPage &&
        !isFetchingNextPage &&
        el.scrollHeight - el.scrollTop - el.clientHeight < 300
      ) {
        void fetchNextPage()
      }
    },
    [hasNextPage, isFetchingNextPage, fetchNextPage]
  )

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="flex max-h-[88vh] flex-col gap-4 sm:max-w-5xl">
        <DialogHeader>
          <div className="flex items-start justify-between gap-4">
            <div className="space-y-1.5">
              <DialogTitle>
                Aggiungi alla distinta —{" "}
                {ddpType === "COMMERCIAL" ? "DDP Commerciale" : "DDP Officina"}
              </DialogTitle>
              <DialogDescription>
                Doppio clic per aggiungere (Qtà = 1). Gli articoli da comprare
                (commerciali, famiglie 2xx/3xx) vanno nella DDP Commerciale; i
                particolari a disegno (1xx) nella DDP Officina; i gruppi
                (5xx/6xx/7xx) vengono scomposti nei loro componenti in
                automatico. Ci pensa il programma in base al codice.
              </DialogDescription>
            </div>
            {canGenerate ? (
              <Button
                size="sm"
                variant="outline"
                className="shrink-0"
                onClick={() => setShowGenerate((v) => !v)}
              >
                <Plus />
                Nuovo codice Codex
              </Button>
            ) : null}
          </div>
        </DialogHeader>

        {showGenerate ? (
          <CodexGeneratePanel
            onClose={() => setShowGenerate(false)}
            onGenerated={handleGenerated}
          />
        ) : null}

        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm text-muted-foreground">Famiglia:</span>
          <Select value={family} onValueChange={cambiaFamiglia}>
            <SelectTrigger size="sm" className="w-64">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_FAMILIES}>Tutte le famiglie</SelectItem>
              {FAMILIES.map((f) => (
                <SelectItem key={f.code} value={f.code}>
                  {f.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {/* La tendina cambia anche la SORGENTE della ricerca: senza dirlo,
              l'elenco che cambia faccia sembra un errore. */}
          <span className="text-xs text-muted-foreground">
            {vista === "catalog"
              ? "Ricerca nel Catalogo articoli (fornitori e prezzi Danea)"
              : "Ricerca nell'archivio Codex"}
          </span>
          {vista === "catalog" ? (
            <ColumnsMenu
              className="ml-auto"
              modal={false}
              columns={CATALOG_COLUMNS.map((column) => ({
                id: column.key,
                label: column.label,
                checked: visibility[column.key] !== false,
                onToggle: (checked) =>
                  setVisibility((prev) => ({ ...prev, [column.key]: checked })),
              }))}
            />
          ) : null}
        </div>

        {/* #142: i lavorati 101 con grezzo commerciale si scelgono anche dal lato
            acquisti — un gesto inserisce la COPPIA (101 in Officina, grezzo qui). */}
        {vista === "catalog" && derivati.length > 0 ? (
          <div className="shrink-0 rounded-lg border border-amber-200 dark:border-amber-800">
            <div className="flex items-center justify-between gap-2 border-b border-amber-200 bg-amber-50/60 px-3 py-1.5 text-xs dark:border-amber-800 dark:bg-amber-950/30">
              <span className="font-semibold text-amber-800 dark:text-amber-300">
                Lavorati con grezzo commerciale (derivazione)
              </span>
              <span className="text-muted-foreground">
                un inserimento = riga 101 in Officina + grezzo in Commerciale
              </span>
            </div>
            <div className="max-h-44 overflow-auto">
              <table className="w-full text-left text-xs">
                <thead>
                  <tr className="border-b border-border/40 bg-muted/20 text-[11px] font-medium text-muted-foreground">
                    <th className="px-3 py-1.5 font-medium">Cod. ATEC (101)</th>
                    <th className="px-3 py-1.5 font-medium">Descrizione</th>
                    <th className="px-3 py-1.5 font-medium">Grezzo (201)</th>
                    <th className="px-3 py-1.5 font-medium">Cod. articolo</th>
                    <th className="px-3 py-1.5 font-medium">Fornitore</th>
                    <th className="px-3 py-1.5 text-right font-medium">Costo</th>
                    <th className="w-12 px-3 py-1.5" />
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/30">
                  {derivati.map((row) => (
                    <tr
                      key={`drv-${row.codexId}-${row.catalogItemId ?? 0}`}
                      className="cursor-pointer transition-colors hover:bg-muted/30"
                      onDoubleClick={() =>
                        handleAdd({ kind: "derivato101", item: row })
                      }
                    >
                      <td className="px-3 py-1.5 font-mono font-medium tabular-nums text-primary">
                        {formatCodice(row.codiceAtec)}
                      </td>
                      <td
                        className="max-w-[260px] truncate px-3 py-1.5"
                        title={row.descr}
                      >
                        {dash(row.descr)}
                      </td>
                      <td className="px-3 py-1.5 font-mono tabular-nums">
                        {formatCodice(row.grezzoCodice ?? "")}
                      </td>
                      {row.catalogItemId == null ? (
                        <td colSpan={2} className="px-3 py-1.5">
                          <span className="inline-flex items-center gap-1.5 rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-700 dark:border-amber-800 dark:bg-amber-950/40 dark:text-amber-300">
                            <span className="size-1.5 rounded-full bg-amber-500" />
                            grezzo da associare a un articolo
                          </span>
                        </td>
                      ) : (
                        <>
                          <td className="px-3 py-1.5 font-medium" title={row.codiceArticolo}>
                            {dash(row.codiceArticolo)}
                          </td>
                          <td
                            className="max-w-[160px] truncate px-3 py-1.5"
                            title={row.fornitoreNome}
                          >
                            {dash(row.fornitoreNome)}
                          </td>
                        </>
                      )}
                      <td className="px-3 py-1.5 text-right tabular-nums">
                        {euro(row.costoArticolo ?? row.prezzoCodex)}
                      </td>
                      <td className="px-3 py-1.5 text-right">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          title="Inserisci: riga 101 in Officina + grezzo in Commerciale"
                          disabled={addMutation.isPending}
                          onClick={() =>
                            handleAdd({ kind: "derivato101", item: row })
                          }
                        >
                          <Plus />
                          <span className="sr-only">
                            Aggiungi {row.codiceAtec}
                          </span>
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {derivatiTotal > derivati.length ? (
                <p className="px-3 py-1.5 text-[11px] text-muted-foreground">
                  … e altri {derivatiTotal - derivati.length}: restringi coi filtri.
                </p>
              ) : null}
            </div>
          </div>
        ) : null}

        <GridScroller fill className="rounded-lg border" onScroll={handleScroll}>
          <Table>
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                {visibleColumns.map((column) => (
                  <TableHead
                    key={column.key}
                    className={column.align === "right" ? "text-right" : undefined}
                  >
                    {column.label}
                  </TableHead>
                ))}
                <TableHead className="w-12" />
              </TableRow>
              <TableRow className="hover:bg-transparent">
                {visibleColumns.map((column) => (
                  <TableHead key={column.key} className="h-auto px-2 py-2 align-middle">
                    {column.filterParam ? (
                      <ColumnFilterInput
                        value={filters[column.filterParam] ?? ""}
                        onChange={(value) => setColumnFilter(column.filterParam!, value)}
                      />
                    ) : null}
                  </TableHead>
                ))}
                <TableHead className="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {query.isError ? (
                <TableRow>
                  <TableCell
                    colSpan={visibleColumns.length + 1}
                    className="h-24 text-center text-destructive"
                  >
                    {(query.error as Error).message || "Errore nel caricamento."}
                  </TableCell>
                </TableRow>
              ) : query.isLoading ? (
                <TableRow>
                  <TableCell
                    colSpan={visibleColumns.length + 1}
                    className="h-24 text-center text-muted-foreground"
                  >
                    Caricamento…
                  </TableCell>
                </TableRow>
              ) : rowCount === 0 ? (
                <TableRow>
                  <TableCell
                    colSpan={visibleColumns.length + 1}
                    className="h-24 text-center text-muted-foreground"
                  >
                    {vista === "catalog"
                      ? "Nessun articolo corrisponde ai filtri. Se l'articolo non è ancora codificato, si codifica dal Catalogo o dalla Ricodifica Codex."
                      : "Nessun codice corrisponde ai filtri."}
                  </TableCell>
                </TableRow>
              ) : vista === "catalog" ? (
                catalogItems.map((item) => {
                  const codificato = Boolean(item.atecCode && item.codexItemId)
                  return (
                    <TableRow
                      key={`cat-${item.id}`}
                      className="cursor-pointer"
                      onDoubleClick={() => handleAdd({ kind: "catalog", item })}
                    >
                      {visibleColumns.map((column) => (
                        <TableCell
                          key={column.key}
                          className={
                            column.align === "right" ? "text-right" : undefined
                          }
                        >
                          {renderCatalogCell(column, item)}
                        </TableCell>
                      ))}
                      <TableCell className="text-right">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          title={
                            codificato
                              ? "Aggiungi alla distinta"
                              : "Senza codice Codex: prima la codifica"
                          }
                          disabled={addMutation.isPending || !codificato}
                          onClick={() => handleAdd({ kind: "catalog", item })}
                        >
                          <Plus />
                          <span className="sr-only">Aggiungi {item.code}</span>
                        </Button>
                      </TableCell>
                    </TableRow>
                  )
                })
              ) : (
                codexItems.map((item) => {
                  const isGruppo = /^[567]/.test(item.codiceAtec)
                  const figli = expandedGroups[item.codexId]
                  return (
                    <React.Fragment
                      key={`cx-${item.codexId}-${item.catalogItemId ?? 0}`}
                    >
                      <TableRow
                        className="cursor-pointer"
                        onDoubleClick={() => handleAdd({ kind: "codex", item })}
                      >
                        {visibleColumns.map((column) => (
                          <TableCell
                            key={column.key}
                            className={
                              column.align === "right" ? "text-right" : undefined
                            }
                          >
                            {column.key === "codice" ? (
                              <span className="flex items-center gap-1">
                                {isGruppo ? (
                                  <button
                                    type="button"
                                    className="shrink-0 rounded p-0.5 text-muted-foreground hover:bg-muted hover:text-foreground"
                                    title={
                                      figli !== undefined
                                        ? "Nascondi i componenti"
                                        : "Mostra i componenti"
                                    }
                                    onClick={(event) => {
                                      event.stopPropagation()
                                      void toggleGroup(item)
                                    }}
                                    onDoubleClick={(event) =>
                                      event.stopPropagation()
                                    }
                                  >
                                    {figli !== undefined ? (
                                      <ChevronDown className="size-4" />
                                    ) : (
                                      <ChevronRight className="size-4" />
                                    )}
                                  </button>
                                ) : (
                                  <span className="inline-block w-5 shrink-0" />
                                )}
                                {renderCodexCell(column, item)}
                              </span>
                            ) : (
                              renderCodexCell(column, item)
                            )}
                          </TableCell>
                        ))}
                        <TableCell className="text-right">
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            title="Aggiungi alla distinta"
                            disabled={addMutation.isPending}
                            onClick={() => handleAdd({ kind: "codex", item })}
                          >
                            <Plus />
                            <span className="sr-only">
                              Aggiungi {item.codiceAtec}
                            </span>
                          </Button>
                        </TableCell>
                      </TableRow>
                      {figli !== undefined ? (
                        <TableRow className="bg-muted/15 hover:bg-muted/15">
                          <TableCell
                            colSpan={visibleColumns.length + 1}
                            className="p-2.5 pl-8 pr-4"
                          >
                            {figli === "loading" ? (
                              <div className="flex items-center gap-2 py-2 px-3 text-xs text-muted-foreground">
                                <span className="size-2 animate-pulse rounded-full bg-primary" />
                                Caricamento componenti…
                              </div>
                            ) : figli.length === 0 ? (
                              <div className="rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-xs text-destructive">
                                Nessun componente in composizione: questo gruppo non è importabile finché non ha i figli.
                              </div>
                            ) : (
                              <div className="rounded-lg border border-border/80 bg-card shadow-2xs overflow-hidden">
                                <div className="flex items-center justify-between border-b border-border/70 bg-muted/40 px-3.5 py-1.5 text-xs">
                                  <div className="flex items-center gap-2">
                                    <span className="size-1.5 rounded-full bg-primary" />
                                    <span className="font-semibold text-foreground">
                                      Distinta componenti gruppo
                                    </span>
                                    <span className="rounded bg-muted px-1.5 py-0.5 font-mono text-[10px] font-medium text-muted-foreground">
                                      {figli.length} articol{figli.length === 1 ? "o" : "i"} •{" "}
                                      {figli.reduce((acc, c) => acc + c.quantity, 0)} pezz{figli.reduce((acc, c) => acc + c.quantity, 0) === 1 ? "o" : "i"} tot.
                                    </span>
                                  </div>
                                  <span className="text-[11px] text-muted-foreground">
                                    Smistamento automatico all&apos;importazione
                                  </span>
                                </div>
                                <table className="w-full text-xs text-left">
                                  <thead>
                                    <tr className="border-b border-border/40 bg-muted/20 text-[11px] font-medium text-muted-foreground">
                                      <th className="py-1.5 px-3.5 text-left font-medium w-40">Cod. ATEC</th>
                                      <th className="py-1.5 px-3 text-left font-medium">Descrizione componente</th>
                                      <th className="py-1.5 px-3 text-center font-medium w-20">Qtà</th>
                                      <th className="py-1.5 px-3.5 text-right font-medium w-48">Destinazione DDP</th>
                                    </tr>
                                  </thead>
                                  <tbody className="divide-y divide-border/30">
                                    {figli.map((f) => (
                                      <tr key={f.id} className="hover:bg-muted/30 transition-colors">
                                        <td className="py-1.5 px-3.5 font-mono font-medium tabular-nums text-foreground">
                                          {formatCodice(f.childCodice)}
                                        </td>
                                        <td className="py-1.5 px-3 text-foreground/80 font-normal truncate max-w-[360px]" title={f.childDescr}>
                                          {f.childDescr || "—"}
                                        </td>
                                        <td className="py-1.5 px-3 text-center">
                                          <span className="inline-block rounded bg-muted px-2 py-0.5 font-mono text-[11px] font-medium tabular-nums text-foreground">
                                            ×{f.quantity}
                                          </span>
                                        </td>
                                        <td className="py-1.5 px-3.5 text-right">
                                          {renderDestinationBadge(f.childCodice)}
                                        </td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </div>
                            )}
                          </TableCell>
                        </TableRow>
                      ) : null}
                    </React.Fragment>
                  )
                })
              )}
            </TableBody>
          </Table>
          {query.isFetchingNextPage ? (
            <p className="py-2 text-center text-sm text-muted-foreground">
              Caricamento…
            </p>
          ) : null}
        </GridScroller>

        <DialogFooter className="sm:justify-between">
          <div className="flex flex-1 items-center gap-3 text-sm">
            <span className="text-muted-foreground">
              {totalCount > 0 ? `${rowCount} di ${totalCount} articoli` : ""}
            </span>
            {error ? (
              <span className="text-destructive">{error}</span>
            ) : message ? (
              <span className="font-medium text-primary">{message}</span>
            ) : null}
          </div>
          <div className="flex items-center gap-3">
            {addedCount > 0 ? (
              <span className="text-sm font-semibold tabular-nums">
                ✓ {addedCount} aggiunt{addedCount === 1 ? "o" : "i"}
              </span>
            ) : null}
            <Button variant="outline" onClick={onClose}>
              Chiudi
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>

      {/* Codifica al volo: stesso dialog del Catalogo Articoli. Dopo il salvataggio
          l'elenco si ricarica e l'articolo diventa aggiungibile. */}
      <CatalogAtecAssignDialog
        item={atecTarget}
        onClose={() => setAtecTarget(null)}
        onSaved={() => {
          setAtecTarget(null)
          void queryClient.invalidateQueries({ queryKey: ["catalog-picker"] })
        }}
      />
    </Dialog>
  )
}
