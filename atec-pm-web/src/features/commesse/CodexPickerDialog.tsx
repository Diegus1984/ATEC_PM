import * as React from "react"
import { useInfiniteQuery, useMutation, useQueryClient } from "@tanstack/react-query"
import { Plus } from "lucide-react"

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
import { fetchCatalogByCodex, fetchCatalogItems } from "@/lib/api/catalog"
import { fetchCodex } from "@/lib/api/codex"
import { fetchCompositionChildren } from "@/lib/api/codex-compositions"
import { createDdpRow, fetchDdpRows, updateDdpRow } from "@/lib/api/project-ddp"
import {
  addOfficinaItem,
  fetchOfficinaItems,
  importOfficinaComposition,
  updateOfficinaItem,
} from "@/lib/api/project-ddp-officina"
import type {
  CatalogItemListItem,
  CodexListItem,
  OfficinaImportCompositionResult,
} from "@/lib/api/types"
import { canWriteFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"
import { euro, dash } from "@/lib/format"
import { useDebounced } from "@/lib/use-debounced"
import { usePersistedColumnVisibility } from "@/lib/use-persisted-column-visibility"

import { CodexGeneratePanel } from "../codex/CodexGeneratePanel"
import { DDP_STATUS_TO_ORDER, DDP_STATUS_VERIFY } from "./ddp-constants"

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

/** Codice ATEC di una riga Codex: il codice nuovo se ricodificata, altrimenti il codice. */
function atecDiCodex(item: CodexListItem): string {
  return (item.codiceNuovo || item.codice || "").replace(/\./g, "").trim()
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

// Vista Codex (Tutte le famiglie, 101/301/5xx/6xx/7xx): le colonne del vecchio picker
// officina + il codice fornitore, per cercare col codice commerciale anche da qui.
const CODEX_COLUMNS: PickerColumn[] = [
  { key: "codice", label: "Cod. ATEC", filterParam: "codice" },
  { key: "descr", label: "Descrizione", filterParam: "descr" },
  { key: "codeForn", label: "Cod. fornitore", filterParam: "codeForn" },
  { key: "um", label: "UM" },
  { key: "fornitore", label: "Fornitore", filterParam: "fornitore" },
  { key: "prezzoForn", label: "Costo", align: "right" },
]

type PickEntry =
  | { kind: "catalog"; item: CatalogItemListItem }
  | { kind: "codex"; item: CodexListItem }

/**
 * Picker UNICO delle due DDP di commessa (`ddpType` = la distinta da cui è aperto).
 * Tutte le famiglie sono a disposizione; ogni codice finisce nella DDP giusta per la
 * sua famiglia (specchio di DdpSmistamento, #119):
 *
 * - 2xx/3xx → riga nella DDP Commerciale (dall'Officina, con conferma);
 * - 1xx → riga nella DDP Officina (dalla Commerciale, con conferma);
 * - 5xx/6xx/7xx → import della composizione: intestazione in ENTRAMBE le distinte e
 *   componenti smistati automaticamente. Un gruppo SENZA figli non si importa
 *   (messaggio, niente riga orfana).
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

  const [visibility, setVisibility] = usePersistedColumnVisibility(
    "ddp-catalog-picker-cols-v1",
    CATALOG_COLUMNS_DEFAULTS
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
    }
  }, [open])

  // I parametri filtro delle due viste sono diversi: cambiando famiglia (e quindi
  // eventualmente vista) i filtri ripartono puliti.
  function cambiaFamiglia(next: string) {
    const vistaNext = CATALOG_FAMILIES.has(next) ? "catalog" : "codex"
    if (vistaNext !== vista) setFilters({})
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

  // ── Vista Codex: filtro server per prefisso. NB «Tutte» mostra i codici in formato
  //    ATEC (i vecchi articoli ricodificati si cercano dalla loro famiglia 2xx, che
  //    apre la vista Catalogo).
  const codexPrefixes =
    family === ALL_FAMILIES ? ["1", "2", "3", "5", "6", "7"] : [family]
  const codexQuery = useInfiniteQuery({
    queryKey: ["ddp-picker-codex", codexPrefixes, debouncedFilters],
    queryFn: ({ pageParam }) =>
      fetchCodex({
        page: pageParam,
        pageSize: PAGE_SIZE,
        codicePrefixes: codexPrefixes,
        filters: debouncedFilters,
      }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasMore ? last.page + 1 : undefined),
    enabled: open && vista === "codex",
  })

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

    // Articolo Danea di partenza: quello scelto (vista Catalogo) oppure, da un codice
    // Codex, l'UNICO associato; con zero o più d'uno la riga nasce «da definire»
    // (il fornitore è una scelta d'acquisto).
    let cat: CatalogItemListItem | null = null
    let descrizione = ""
    if (entry.kind === "catalog") {
      cat = entry.item
      descrizione = entry.item.description
    } else {
      descrizione = entry.item.descr
      try {
        const alts = await fetchCatalogByCodex(entry.item.id)
        cat = alts.length === 1 ? alts[0] : null
      } catch {
        cat = null
      }
    }
    await createDdpRow(projectId, {
      id: 0,
      projectId,
      catalogItemId: cat?.id ?? null,
      partNumber: cat?.code ?? "",
      description: cat?.description || descrizione,
      unit: cat?.unit || "PZ",
      quantity: 1,
      // Senza articolo Danea univoco vale il prezzo del Codex (stessa regola
      // dell'import server, RisolviArticoloCommerciale).
      unitCost:
        cat?.unitCost ??
        (entry.kind === "codex" ? (entry.item.prezzoForn ?? 0) : 0),
      supplierId: cat?.supplierId ?? null,
      manufacturer: cat?.manufacturer ?? "",
      itemStatus: DDP_STATUS_VERIFY,
      requestedBy,
      daneaRef: "",
      dateNeeded: null,
      destination: "",
      destinationSpec: "",
      notes: cat ? "" : "Fornitore da definire",
      ddpType: "COMMERCIAL",
      atecCode: atecRaw,
      expectedUpdatedAt: null,
    })
    return {
      code: formatCodice(atecRaw),
      testo: cat
        ? "aggiunto alla DDP Commerciale"
        : "aggiunto alla DDP Commerciale (fornitore da definire)",
    }
  }

  // ── Inserimento nella DDP OFFICINA (1xx) — il flusso del picker officina storico ──
  async function aggiungiOfficina(item: CodexListItem) {
    let existing
    try {
      existing = await fetchOfficinaItems(projectId)
    } catch {
      throw new Error(
        "Impossibile leggere la DDP Officina: inserimento annullato per non creare doppioni."
      )
    }
    const duplicate =
      existing.find(
        (r) =>
          r.partNumber === item.codice && r.itemStatus === DDP_STATUS_TO_ORDER
      ) ?? null
    if (duplicate) {
      const ok = await confirm({
        title: "Articolo già presente",
        description: `L'articolo ${item.codice} è già nella DDP Officina in stato Da Ordinare (Qtà attuale: ${duplicate.quantity}).\n\nVuoi aggiungere +1 alla quantità?`,
        confirmLabel: "Aggiungi +1",
        destructive: false,
      })
      if (!ok) return null
      // L'update officina riscrive tutti i campi editabili: ricopiati dalla riga esistente.
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
      return { code: item.codice, testo: "Qtà aggiornata" }
    }

    await addOfficinaItem(projectId, {
      id: 0,
      projectId,
      partNumber: item.codice,
      description: item.descr,
      quantity: 1,
      quantityProduced: 0,
      unitCost: item.prezzoForn,
      material: "",
      treatment: "",
      supplierName: item.fornitore,
      itemStatus: DDP_STATUS_TO_ORDER,
      requestedBy,
      daneaRef: "",
      dateNeeded: null,
      orderDate: null,
      destination: "",
      destinationSpec: "",
      notes: "",
    })
    return { code: item.codice, testo: "aggiunto alla DDP Officina" }
  }

  // ── Import di un gruppo/assieme (5xx/6xx/7xx): intestazione in entrambe le DDP,
  //    componenti smistati dal server (#119). SENZA figli non si importa. ──────────
  async function importaGruppo(item: CodexListItem) {
    const children = await fetchCompositionChildren(item.id)
    const codexChildren = children.filter((ch) => ch.source === "codex")
    if (codexChildren.length === 0) {
      throw new Error(
        `${item.codice} non ha componenti in composizione: un gruppo o assieme si importa solo coi suoi figli. Completa prima la distinta in Composizione Codex.`
      )
    }
    const pieces = codexChildren.reduce((sum, ch) => sum + ch.quantity, 0)
    const ok = await confirm({
      title: "Importare il gruppo?",
      description: `${item.codice} è un gruppo con ${codexChildren.length} componenti (${pieces} pezzi).\n\nI componenti verranno smistati automaticamente: i commerciali (2xx/3xx) nella DDP Commerciale, il resto nella DDP Officina, con l'intestazione del gruppo in entrambe. I codici già presenti sommeranno le quantità.`,
      confirmLabel: "Importa",
      destructive: false,
    })
    if (!ok) return null

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
      (r) => r.partNumber === item.codice && r.itemStatus === DDP_STATUS_TO_ORDER
    )
    if (duplicate) {
      const okPiu = await confirm({
        title: "Gruppo già presente",
        description: `${item.codice} è già nella DDP Officina in stato Da Ordinare (Qtà attuale: ${duplicate.quantity}).\n\nVuoi aggiungere +1 alla quantità?`,
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
          codexParentId: item.id,
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
      return { code: item.codice, testo: `Qtà aggiornata${compo}` }
    }

    await addOfficinaItem(projectId, {
      id: 0,
      projectId,
      partNumber: item.codice,
      description: item.descr,
      quantity: 1,
      quantityProduced: 0,
      unitCost: item.prezzoForn,
      material: "",
      treatment: "",
      supplierName: item.fornitore,
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
      codexParentId: item.id,
      requestedBy,
    })
    const multNuovo =
      imported.parentQuantity !== 1 ? ` ×${imported.parentQuantity}` : ""
    return {
      code: item.codice,
      testo: `importato${multNuovo}: ${imported.added} componenti nuovi, ${imported.updated} aggiornati${imported.skipped ? `, ${imported.skipped} saltati` : ""}`,
    }
  }

  const addMutation = useMutation({
    mutationFn: async (entry: PickEntry) => {
      setError(null)

      // Codice ATEC e destinazione: senza codice non si entra in nessuna distinta.
      let atecRaw: string
      let codexItem: CodexListItem | null = null
      if (entry.kind === "catalog") {
        atecRaw = (entry.item.atecCode || "").replace(/\./g, "").trim()
        if (!atecRaw || !entry.item.codexItemId) {
          throw new Error(
            `${entry.item.code} è senza codice Codex: codificalo (pulsante «Codifica») prima di metterlo in distinta.`
          )
        }
      } else {
        codexItem = entry.item
        atecRaw = atecDiCodex(entry.item)
        if (!atecRaw) {
          throw new Error(`${entry.item.codice}: codice non riconosciuto.`)
        }
        // Vecchio codice commerciale MAI ricodificato: la prima cifra (2/3) combacia
        // per caso col vecchio schema, ma NON è un codice ATEC — prima la ricodifica.
        if (!entry.item.codiceNuovo && /^[23]/.test(atecRaw)) {
          throw new Error(
            `${entry.item.codice} è un codice storico non ricodificato: assegnagli il codice ATEC (Ricodifica Codex) prima di metterlo in distinta.`
          )
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
    onError: (err: Error) => setError(err.message),
  })

  function handleAdd(entry: PickEntry) {
    if (addMutation.isPending) return
    addMutation.mutate(entry)
  }

  // Codice appena generato: riletto dal Codex (per codice esatto) e aggiunto subito.
  async function handleGenerated(codice: string) {
    setShowGenerate(false)
    try {
      const page = await fetchCodex({ filters: { codice }, pageSize: 50 })
      // Il server risponde col codice FORMATTATO (col punto), il Codex lo salva
      // senza: il confronto va fatto a punti tolti o non si ritrova mai.
      const raw = codice.replace(/\./g, "")
      const created = page.items.find(
        (i) => (i.codice ?? "").replace(/\./g, "") === raw
      )
      if (!created) {
        setError(`Codice ${codice} generato ma non ritrovato nel Codex.`)
        return
      }
      await codexQuery.refetch()
      addMutation.mutate({ kind: "codex", item: created })
    } catch (err) {
      setError((err as Error).message)
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

  function renderCodexCell(column: PickerColumn, item: CodexListItem) {
    switch (column.key) {
      case "codice":
        return (
          <span className="font-medium tabular-nums text-primary">
            {formatCodice(atecDiCodex(item)) || item.codice}
          </span>
        )
      case "descr":
        return (
          <span className="block max-w-[320px] truncate" title={item.descr}>
            {dash(item.descr)}
          </span>
        )
      case "codeForn":
        return dash(item.codeForn)
      case "um":
        return dash(item.um)
      case "fornitore":
        return (
          <span className="block max-w-[180px] truncate" title={item.fornitore}>
            {dash(item.fornitore)}
          </span>
        )
      case "prezzoForn":
        return <span className="tabular-nums">{euro(item.prezzoForn)}</span>
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
                Doppio clic per aggiungere (Qtà = 1). Ogni codice va nella DDP
                della sua famiglia: 2xx/3xx commerciale, 1xx officina; i gruppi
                5xx/6xx/7xx si importano coi componenti smistati in automatico.
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
                codexItems.map((item) => (
                  <TableRow
                    key={`cx-${item.id}`}
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
                        {renderCodexCell(column, item)}
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
                        <span className="sr-only">Aggiungi {item.codice}</span>
                      </Button>
                    </TableCell>
                  </TableRow>
                ))
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
