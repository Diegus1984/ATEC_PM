import * as React from "react"
import { Link, useNavigate, useParams, useLocation } from "react-router-dom"
import {
  ArrowLeft,
  CircleCheck,
  EllipsisVertical,
  FileSpreadsheet,
  FileText,
  FileType,
  Plus,
  Printer,
  RefreshCw,
  Trash2,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { PageErrorAlert } from "@/components/shared/page-error-alert"
import { notifyError } from "@/lib/toast"
import { DateField, ReadonlyDateField } from "@/components/shared/date-field"
import { type MultiSelectOption } from "@/components/shared/multi-select"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group"
import {
  addMoMItem,
  deleteMoMItem,
  fetchMoMDetail,
  fetchMoMEmployees,
  fetchMoMWildcards,
  reorderMoMItems,
  updateMoM,
  updateMoMItem,
} from "@/lib/api/mom"
import { canWriteFeature } from "@/lib/auth/permissions"
import { getSession } from "@/lib/auth/session"
import { useMoMHub } from "@/lib/signalr/use-mom-hub"
import { cn } from "@/lib/utils"
import type {
  LookupItem,
  MoMActionItem,
  MoMActionItemSaveRequest,
  MoMRevision,
} from "@/lib/api/types"

import {
  exportMoMCsv,
  exportMoMExcel,
  exportMoMWord,
  printMoM,
} from "./mom-export"
import { MoMClosedProgress } from "./mom-closed-progress"
import { MOM_CONDITION_STYLE } from "./mom-palette"
import { MoMSheet, type SheetFocusRequest } from "./mom-sheet"
import {
  type FilterKey,
  type HeaderState,
  type SheetSort,
  buildSaveRequest,
  formatDate,
  headerTitle,
  isOverdue,
  matchesFilter,
  orderRows,
  reorderWithinBucket,
  todayIso,
} from "./mom-detail-shared"
import { useDeferredItemOrder } from "@/lib/use-deferred-item-order"

// Riga nuova: priorità 3 (bassa) di default, nessun campo obbligatorio oltre al testo inserito.
function blankRowRequest(
  attivita: string,
  azione: string | null = null
): MoMActionItemSaveRequest {
  return {
    attivita,
    descrizione: null,
    azione,
    priorita: 3,
    status: "OPEN",
    isCritical: false,
    resp1Id: null,
    resp2Id: null,
    resp3Id: null,
    responsibleIds: [],
    dataCheck: null,
    dataClose: null,
  }
}

export function MoMDetailPage() {
  const params = useParams<{ id: string }>()
  const navigate = useNavigate()
  const location = useLocation()
  const confirm = useConfirm()
  const momId = Number(params.id)
  const myId = getSession()?.user.employeeId ?? null
  /**
   * Funzione concessa in sola lettura: il verbale si apre, si legge e si esporta,
   * ma non si tocca. Qui non basta togliere i pulsanti: il foglio salva da solo
   * mentre si scrive, quindi in sola lettura le celle diventano testo e l'autosave
   * non parte proprio (vedi `patchRow` ed `enqueueSave`). Senza quelle guardie
   * l'utente digiterebbe, la PUT tornerebbe 403 e il testo andrebbe perso in
   * silenzio al primo ricaricamento.
   */
  const readOnly = !canWriteFeature("nav.mom")

  const [header, setHeader] = React.useState<HeaderState | null>(null)
  const [revisions, setRevisions] = React.useState<MoMRevision[]>([])
  const [rows, setRows] = React.useState<MoMActionItem[]>([])
  const [employees, setEmployees] = React.useState<LookupItem[]>([])
  const [wildcards, setWildcards] = React.useState<LookupItem[]>([])
  const [loading, setLoading] = React.useState(true)
  const [loadError, setLoadError] = React.useState<string | null>(null)
  const [status, setStatus] = React.useState("")
  const [filter, setFilter] = React.useState<FilterKey>("tutte")
  const [sort, setSort] = React.useState<SheetSort>({ field: null, dir: 1 })
  const [adding, setAdding] = React.useState(false)
  const [focusReq, setFocusReq] = React.useState<SheetFocusRequest | null>(null)
  const [selectedRowIds, setSelectedRowIds] = React.useState<Set<number>>(
    () => new Set()
  )
  /** «Aggiorna» riordina le righe; le modifiche a priorità/data non spostano subito. */
  const [layoutEpoch, setLayoutEpoch] = React.useState(0)
  /**
   * Righe appena aggiunte: restano in fondo al foglio finché non si preme
   * «Aggiorna» o non si riordina a mano (segnalazione #47). Serve nel caso in cui
   * era attivo un ordinamento per colonna: l'aggiunta lo disattiva e senza questo
   * elenco il riordino conseguente rimetterebbe la riga nuova a metà tabella,
   * dentro la sua fascia di priorità, invece che sotto a tutte.
   */
  const pinnedNewIdsRef = React.useRef<Set<number>>(new Set())

  const today = todayIso()
  const containerRef = React.useRef<HTMLDivElement | null>(null)

  const backUrl =
    location.state?.fromProject && header?.projectId
      ? `/commesse/${header.projectId}/mom`
      : "/mom"
  const backUrlRef = React.useRef(backUrl)
  backUrlRef.current = backUrl

  // Autosave: stato più recente + debounce per riga + coda seriale di salvataggio.
  const rowsRef = React.useRef<MoMActionItem[]>([])
  rowsRef.current = rows
  const timersRef = React.useRef(new Map<number, number>())
  const saveQueue = React.useRef<Promise<void>>(Promise.resolve())
  const inFlightRef = React.useRef(0)
  const pendingRemoteRef = React.useRef(false)
  /** Finestra in cui ignorare l'echo SignalR delle proprie scritture. */
  const ignoreRemoteUntilRef = React.useRef(0)

  const applyDetail = React.useCallback(
    (detail: Awaited<ReturnType<typeof fetchMoMDetail>>) => {
      setHeader({
        id: detail.id,
        tipo: detail.tipo,
        projectId: detail.projectId,
        projectCode: detail.projectCode,
        title: detail.title,
        meetingDate: detail.meetingDate,
        inDashboard: detail.inDashboard,
        rev: detail.rev,
      })
      setRevisions(detail.revisions)
      // Solo i valori: l'ordine a video resta congelato fino ad «Aggiorna»
      // (segnalazione #43). Se qui si bumpasse layoutEpoch, ogni autosave — e il
      // proprio echo SignalR — riposizionerebbe le righe come prima della Check List.
      setRows(detail.items)
    },
    []
  )

  const reload = React.useCallback(async () => {
    try {
      const detail = await fetchMoMDetail(momId)
      applyDetail(detail)
    } catch (err) {
      setStatus(`Errore: ${(err as Error).message}`)
    }
  }, [momId, applyDetail])

  React.useEffect(() => {
    let cancelled = false
    async function load() {
      if (!Number.isFinite(momId)) return
      setLoading(true)
      setLoadError(null)
      try {
        const [detail, emps, wilds] = await Promise.all([
          fetchMoMDetail(momId),
          fetchMoMEmployees(),
          fetchMoMWildcards(),
        ])
        if (cancelled) return
        applyDetail(detail)
        pinnedNewIdsRef.current.clear()
        // Primo caricamento: applica subito l'ordine di default (useDeferredItemOrder
        // con orderIds vuoto lo farebbe già; l'epoch serve se si riapre lo stesso MoM).
        setLayoutEpoch((n) => n + 1)
        setEmployees(emps)
        setWildcards(wilds)
      } catch (err) {
        if (!cancelled) setLoadError((err as Error).message)
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [momId, applyDetail])

  const globalNameById = React.useMemo(() => {
    const map = new Map<number, string>()
    for (const item of employees) map.set(item.id, item.name)
    for (const item of wildcards) map.set(item.id, item.name)
    return map
  }, [employees, wildcards])

  const poolFor = React.useCallback(
    (item: MoMActionItem): MultiSelectOption[] => {
      const pool: MultiSelectOption[] = employees.map((emp) => ({
        id: emp.id,
        name: emp.name,
      }))
      const present = new Set(pool.map((option) => option.id))
      const add = (id: number | null, name: string | null) => {
        if (id == null || present.has(id)) return
        pool.push({ id, name: name || globalNameById.get(id) || `#${id}` })
        present.add(id)
      }
      if (item.responsibleIds.length > 0) {
        item.responsibleIds.forEach((id, index) =>
          add(id, item.responsibleNames[index] ?? null)
        )
      } else {
        add(item.resp1Id, item.resp1Name)
        add(item.resp2Id, item.resp2Name)
        add(item.resp3Id, item.resp3Name)
      }
      return pool
    },
    [employees, globalNameById]
  )

  // ── Autosave ───────────────────────────────────────────────

  const isEditing = React.useCallback(() => {
    const el = document.activeElement
    return (
      !!el &&
      !!containerRef.current?.contains(el) &&
      (el.tagName === "TEXTAREA" || el.tagName === "INPUT")
    )
  }, [])

  const maybeRemoteRefresh = React.useCallback(() => {
    if (
      pendingRemoteRef.current &&
      timersRef.current.size === 0 &&
      inFlightRef.current === 0 &&
      !isEditing()
    ) {
      pendingRemoteRef.current = false
      void reload()
    }
  }, [isEditing, reload])

  const enqueueSave = React.useCallback(
    (id: number) => {
      // Guardia sulla CODA, non solo sull'interfaccia: qui passa ogni salvataggio
      // (debounce, flush al blur, patch immediate). In sola lettura la coda resta
      // ferma, così non parte nessuna PUT destinata a tornare 403.
      if (readOnly) return
      inFlightRef.current += 1
      saveQueue.current = saveQueue.current.then(async () => {
        const row = rowsRef.current.find((r) => r.id === id)
        if (!row) {
          inFlightRef.current -= 1
          return
        }
        setStatus("Salvataggio…")
        try {
          await updateMoMItem(id, buildSaveRequest(row))
          // Il server notifica anche noi: senza questa finestra il reload
          // riscriverebbe le righe a ogni autosave (priorità/data comprese).
          ignoreRemoteUntilRef.current = Date.now() + 1500
          setRows((prev) =>
            prev.map((r) =>
              r.id === id ? { ...r, rowVersion: row.rowVersion + 1 } : r
            )
          )
          setStatus("Salvato")
        } catch (err) {
          const message = (err as Error).message
          if (message.includes("CONFLITTO")) {
            setStatus("Riga modificata da un altro utente — ricarico il foglio…")
            await reload()
          } else {
            setStatus(`Errore: ${message}`)
            notifyError(message)
          }
        } finally {
          inFlightRef.current -= 1
          maybeRemoteRefresh()
        }
      })
    },
    [maybeRemoteRefresh, readOnly, reload]
  )

  const scheduleSave = React.useCallback(
    (id: number, immediate: boolean) => {
      const existing = timersRef.current.get(id)
      if (existing) window.clearTimeout(existing)
      if (immediate) {
        timersRef.current.delete(id)
        enqueueSave(id)
        return
      }
      timersRef.current.set(
        id,
        window.setTimeout(() => {
          timersRef.current.delete(id)
          enqueueSave(id)
        }, 600)
      )
    },
    [enqueueSave]
  )

  const patchRow = React.useCallback(
    (
      item: MoMActionItem,
      patch: Partial<MoMActionItem>,
      opts?: { immediate?: boolean }
    ) => {
      // In sola lettura la riga non cambia nemmeno a video: se la aggiornassimo qui
      // l'utente vedrebbe la modifica «presa» mentre in banca dati non arriva nulla.
      if (readOnly) return
      // Cambio responsabili: risolve subito i nomi per la visualizzazione/export.
      if (patch.responsibleIds) {
        patch.responsibleNames = patch.responsibleIds.map(
          (id) => globalNameById.get(id) ?? `#${id}`
        )
        patch.resp1Name = patch.responsibleNames[0] ?? null
        patch.resp2Name = patch.responsibleNames[1] ?? null
        patch.resp3Name = patch.responsibleNames[2] ?? null
      }
      setRows((prev) =>
        prev.map((r) => (r.id === item.id ? { ...r, ...patch } : r))
      )
      scheduleSave(item.id, opts?.immediate === true)
    },
    [globalNameById, readOnly, scheduleSave]
  )

  const flushRow = React.useCallback(
    (item: MoMActionItem) => {
      const timer = timersRef.current.get(item.id)
      if (timer == null) return
      window.clearTimeout(timer)
      timersRef.current.delete(item.id)
      enqueueSave(item.id)
    },
    [enqueueSave]
  )

  // ── Realtime: refetch quando un altro utente modifica questa MoM ──
  useMoMHub(
    Number.isFinite(momId),
    React.useCallback(
      (change) => {
        if (change.momId !== momId) return
        // Echo delle proprie scritture: i valori locali sono già aggiornati.
        if (Date.now() < ignoreRemoteUntilRef.current) return
        pendingRemoteRef.current = true
        maybeRemoteRefresh()
      },
      [momId, maybeRemoteRefresh]
    )
  )

  // ── Righe: aggiunta / eliminazione / riordino ──────────────

  const addRow = React.useCallback(
    async (
      focusField: "attivita" | "descrizione" | "azione" = "attivita",
      opts?: { attivita?: string }
    ) => {
      if (adding || !header || readOnly) return
      const attivita = opts?.attivita?.trim() ?? ""
      setAdding(true)
      try {
        const newId = await addMoMItem(momId, blankRowRequest(attivita))
        ignoreRemoteUntilRef.current = Date.now() + 1500
        const maxSort = rowsRef.current.reduce(
          (max, r) => Math.max(max, r.sortOrder),
          -1
        )
        const newRow: MoMActionItem = {
          id: newId,
          momId,
          attivita,
          descrizione: null,
          azione: null,
          priorita: 3,
          status: "OPEN",
          isCritical: false,
          resp1Id: null,
          resp1Name: null,
          resp2Id: null,
          resp2Name: null,
          resp3Id: null,
          resp3Name: null,
          dataCheck: null,
          dataClose: null,
          sortOrder: maxSort + 1,
          rowVersion: 0,
          responsibleNames: [],
          responsibleIds: [],
        }
        pinnedNewIdsRef.current.add(newId)
        setRows((prev) => [...prev, newRow])
        setFilter("tutte")
        if (!attivita) {
          setFocusReq({ rowId: newId, field: focusField })
        }
        setSort((prev) => {
          if (prev.field != null) {
            setStatus(
              "Nuova riga aggiunta in fondo (ordinamento per colonna disattivato)."
            )
          } else {
            setStatus("Nuova riga aggiunta.")
          }
          return { field: null, dir: 1 }
        })
      } catch (err) {
        setStatus(`Errore: ${(err as Error).message}`)
        notifyError(err)
      } finally {
        setAdding(false)
      }
    },
    [adding, header, momId, readOnly]
  )

  const deleteRow = React.useCallback(
    async (item: MoMActionItem) => {
      if (readOnly) return
      const ok = await confirm({
        title: "Eliminare la riga",
        description: "La riga selezionata verrà rimossa dalla MoM.",
        confirmLabel: "Elimina",
      })
      if (!ok) return
      try {
        await deleteMoMItem(item.id)
        ignoreRemoteUntilRef.current = Date.now() + 1500
        setRows((prev) => prev.filter((row) => row.id !== item.id))
        setSelectedRowIds((prev) => {
          if (!prev.has(item.id)) return prev
          const next = new Set(prev)
          next.delete(item.id)
          return next
        })
        setStatus("Riga eliminata")
      } catch (err) {
        notifyError(err)
      }
    },
    [confirm, readOnly]
  )

  const deleteSelectedRows = React.useCallback(async () => {
    const ids = [...selectedRowIds]
    if (ids.length === 0 || readOnly) return
    const ok = await confirm({
      title: ids.length === 1 ? "Eliminare la riga" : `Eliminare ${ids.length} righe`,
      description:
        ids.length === 1
          ? "La riga selezionata verrà rimossa dalla MoM."
          : `Verranno rimosse ${ids.length} righe dal verbale.`,
      confirmLabel: "Elimina",
    })
    if (!ok) return
    try {
      await Promise.all(ids.map((id) => deleteMoMItem(id)))
      ignoreRemoteUntilRef.current = Date.now() + 1500
      setRows((prev) => prev.filter((row) => !selectedRowIds.has(row.id)))
      setSelectedRowIds(new Set())
      setStatus(
        ids.length === 1 ? "Riga eliminata" : `${ids.length} righe eliminate`
      )
    } catch (err) {
      notifyError(err)
      void reload()
    }
  }, [selectedRowIds, confirm, readOnly, reload])

  const handleReorder = React.useCallback(
    (dragId: number, targetId: number, after: boolean): boolean => {
      // Il riordino scrive (`reorderMoMItems`): in sola lettura non si sposta nulla.
      if (readOnly) return false
      const renumbered = reorderWithinBucket(
        rowsRef.current,
        dragId,
        targetId,
        after
      )
      if (!renumbered) {
        setStatus(
          "Puoi riordinare solo attività con la stessa priorità e stato (aperta, critica, stand-by, chiusa)."
        )
        return false
      }
      setRows(renumbered)
      // Riordino esplicito dell'utente: le righe nuove perdono il posto fisso in fondo.
      pinnedNewIdsRef.current.clear()
      setLayoutEpoch((n) => n + 1)
      setSort((prev) => {
        setStatus(
          prev.field != null
            ? "Ordine aggiornato (ordinamento per colonna disattivato)."
            : "Ordine aggiornato nella fascia di priorità."
        )
        return { field: null, dir: 1 }
      })
      ignoreRemoteUntilRef.current = Date.now() + 1500
      reorderMoMItems(momId, renumbered.map((row) => row.id)).catch(
        (err: Error) => {
          notifyError(err)
          void reload()
        }
      )
      return true
    },
    [momId, readOnly, reload]
  )

  // ── Data riunione: cambio con conferma → nuova revisione (v9) ──

  const changeMeetingDate = React.useCallback(
    async (value: string | null) => {
      // Cambiare la data riunione crea una revisione: è una scrittura a tutti gli effetti.
      if (!header || readOnly) return
      const prevDay = header.meetingDate ? header.meetingDate.slice(0, 10) : null
      if (prevDay && value && prevDay !== value) {
        const ok = await confirm({
          title: "Conferma aggiornamento MoM",
          description: `La data riunione cambia da ${formatDate(prevDay)} a ${formatDate(
            value
          )}. Confermando, la MoM passa alla revisione ${header.rev + 1}. Procedere?`,
          confirmLabel: "Conferma",
        })
        if (!ok) return
      }
      try {
        const rev = await updateMoM(header.id, {
          tipo: header.tipo,
          projectId: header.projectId,
          title: header.title,
          meetingDate: value,
          inDashboard: header.inDashboard,
        })
        const bumped = rev !== header.rev
        setHeader({ ...header, meetingDate: value, rev })
        if (bumped) {
          setRevisions((prev) => [
            ...prev,
            {
              rev,
              meetingDate: value,
              prevDate: prevDay,
              createdAt: new Date().toISOString(),
            },
          ])
          setStatus(
            `Revisione ${rev} registrata · data riunione ${formatDate(value)}`
          )
        } else {
          setStatus("Salvato")
        }
      } catch (err) {
        notifyError(err)
      }
    },
    [header, confirm, readOnly]
  )

  // ── Vista: ordine congelato fino ad «Aggiorna» / click colonna / drag ──

  const sortItems = React.useCallback(
    (list: MoMActionItem[]) => {
      const pinned = pinnedNewIdsRef.current
      if (pinned.size === 0) return orderRows(list, sort)
      const fresh = list.filter((row) => pinned.has(row.id))
      if (fresh.length === 0) return orderRows(list, sort)
      const rest = list.filter((row) => !pinned.has(row.id))
      return [...orderRows(rest, sort), ...fresh]
    },
    [sort]
  )
  const sortResortKey =
    sort.field == null ? "manual" : `${sort.field}:${sort.dir}`
  const orderedRows = useDeferredItemOrder(
    rows,
    sortItems,
    layoutEpoch,
    sortResortKey
  )
  const displayRows = React.useMemo(
    () =>
      orderedRows.filter((row) => matchesFilter(row, filter, myId, today)),
    [orderedRows, filter, myId, today]
  )

  const handleRefreshLayout = React.useCallback(() => {
    // Solo «Aggiorna» rimette in gioco le righe nuove tenute in fondo.
    pinnedNewIdsRef.current.clear()
    setLayoutEpoch((n) => n + 1)
    setStatus("Ordine aggiornato secondo priorità e date.")
  }, [])

  React.useEffect(() => {
    setSelectedRowIds(new Set())
  }, [filter])

  const toggleRowSelected = React.useCallback((id: number, selected: boolean) => {
    setSelectedRowIds((prev) => {
      const next = new Set(prev)
      if (selected) next.add(id)
      else next.delete(id)
      return next
    })
  }, [])

  const toggleAllRowsSelected = React.useCallback(
    (selected: boolean) => {
      setSelectedRowIds((prev) => {
        const next = new Set(prev)
        for (const row of displayRows) {
          if (selected) next.add(row.id)
          else next.delete(row.id)
        }
        return next
      })
    },
    [displayRows]
  )

  const summary = React.useMemo(() => {
    const total = rows.length
    const closed = rows.filter((row) => row.status === "CLOSED").length
    const critiche = rows.filter(
      (row) => row.isCritical && row.status !== "CLOSED"
    ).length
    const scadute = rows.filter((row) => isOverdue(row, today)).length
    const standby = rows.filter((row) => row.status === "STANDBY").length
    return {
      total,
      closed,
      daFare: total - closed,
      critiche,
      scadute,
      standby,
    }
  }, [rows, today])

  const addRowRef = React.useRef(addRow)
  addRowRef.current = addRow

  React.useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      // Ctrl+N aggiunge una riga: in sola lettura la scorciatoia resta muta
      // (l'Escape per tornare indietro invece serve sempre).
      if (event.key === "n" && (event.ctrlKey || event.metaKey) && !readOnly) {
        event.preventDefault()
        void addRowRef.current("attivita")
        return
      }
      if (event.key === "Escape" && !event.defaultPrevented) {
        const tag = (event.target as HTMLElement | null)?.tagName
        if (tag === "INPUT" || tag === "TEXTAREA") return
        event.preventDefault()
        navigate(backUrlRef.current)
      }
    }
    window.addEventListener("keydown", onKeyDown)
    return () => window.removeEventListener("keydown", onKeyDown)
  }, [navigate, readOnly])

  if (loading) {
    return <p className="text-sm text-muted-foreground">Caricamento…</p>
  }
  if (loadError) {
    return <PageErrorAlert message={loadError} />
  }
  if (!header) return null

  const FILTERS: { key: FilterKey; label: string }[] = [
    { key: "tutte", label: "Tutte" },
    { key: "aperte", label: "Aperte" },
    { key: "critiche", label: "Critiche" },
    { key: "scadute", label: "Scadute" },
    ...(myId != null ? [{ key: "mie" as FilterKey, label: "Mie" }] : []),
  ]

  const revTooltip =
    revisions.length > 0
      ? revisions
          .map(
            (entry) =>
              `Rev. ${entry.rev}: ${formatDate(entry.prevDate)} → ${formatDate(
                entry.meetingDate
              )}`
          )
          .join("\n")
      : "Nessuna revisione registrata"

  const exportArgs = {
    title: headerTitle(header),
    tipoBadge: header.tipo,
    meetingDate: header.meetingDate,
    rev: header.rev,
  }

  return (
    <div ref={containerRef} className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-3">
          <Button variant="outline" size="sm" asChild>
            <Link to={backUrl}>
              <ArrowLeft />
              {location.state?.fromProject ? "Commessa" : "Riepilogo"}
            </Link>
          </Button>
          <span className="text-base font-semibold">{headerTitle(header)}</span>
          <Badge variant="outline" className="text-[10px] font-bold">
            {header.tipo}
          </Badge>
          <span className="inline-flex items-center gap-2 rounded-lg border bg-muted/50 py-1 pl-3 pr-1.5">
            <span className="text-xs font-semibold text-muted-foreground">
              Riunione
            </span>
            {readOnly ? (
              <ReadonlyDateField
                value={header.meetingDate}
                size="sm"
                className="w-52 bg-background"
              />
            ) : (
              <DateField
                value={header.meetingDate}
                onChange={(value) => void changeMeetingDate(value)}
                size="sm"
                className="w-52 bg-background"
              />
            )}
            <Badge title={revTooltip} className="font-bold">
              Rev. {header.rev}
            </Badge>
          </span>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={handleRefreshLayout}
            title="Riordina le righe secondo priorità, stato e date aggiornate"
          >
            <RefreshCw />
            Aggiorna
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => printMoM({ ...exportArgs, items: displayRows })}
          >
            <Printer />
            Stampa
          </Button>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="sm">
                Esporta
                <EllipsisVertical />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem
                onClick={() => exportMoMWord({ ...exportArgs, items: displayRows })}
              >
                <FileType />
                Word
              </DropdownMenuItem>
              <DropdownMenuItem
                onClick={() =>
                  exportMoMExcel({ ...exportArgs, items: displayRows })
                }
              >
                <FileSpreadsheet />
                Excel
              </DropdownMenuItem>
              <DropdownMenuItem
                onClick={() => exportMoMCsv({ ...exportArgs, items: displayRows })}
              >
                <FileText />
                CSV
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
          {!readOnly && selectedRowIds.size > 0 ? (
            <Button
              variant="destructive"
              size="sm"
              onClick={() => void deleteSelectedRows()}
            >
              <Trash2 />
              Elimina ({selectedRowIds.size})
            </Button>
          ) : null}
          {!readOnly && (
            <Button size="sm" onClick={() => void addRow()} disabled={adding}>
              <Plus />
              Nuova riga
            </Button>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-x-5 gap-y-2 rounded-md bg-muted/50 px-4 py-2.5">
        <span className="inline-flex items-center gap-1.5 text-sm">
          <span
            className={cn(
              "size-2 rounded-full",
              MOM_CONDITION_STYLE.critical.dotClass
            )}
          />
          {summary.critiche} critiche
        </span>
        <span className="inline-flex items-center gap-1.5 text-sm">
          <span
            className={cn(
              "size-2 rounded-full",
              MOM_CONDITION_STYLE.overdue.dotClass
            )}
          />
          {summary.scadute} scadute
        </span>
        <span className="inline-flex items-center gap-1.5 text-sm">
          <span
            className={cn(
              "size-2 rounded-full",
              MOM_CONDITION_STYLE.standby.dotClass
            )}
          />
          {summary.standby} stand-by
        </span>
        <span className="inline-flex items-center gap-1.5 text-sm">
          <span className="size-2 rounded-full bg-blue-500" />
          {summary.daFare} da fare
        </span>
        <span className="inline-flex items-center gap-1.5 text-sm text-muted-foreground">
          <CircleCheck className="size-4 text-green-600" />
          {summary.closed} chiuse
        </span>
        <MoMClosedProgress
          itemsCount={summary.total}
          openCount={summary.daFare}
          className="min-w-32 flex-1"
          trackClassName="bg-background"
        />
      </div>

      <ToggleGroup
        type="single"
        value={filter}
        onValueChange={(value) => setFilter((value || "tutte") as FilterKey)}
        variant="outline"
        size="sm"
        className="flex-wrap"
      >
        {FILTERS.map((entry) => (
          <ToggleGroupItem key={entry.key} value={entry.key}>
            {entry.label}
          </ToggleGroupItem>
        ))}
      </ToggleGroup>

      <MoMSheet
        displayRows={displayRows}
        allRows={rows}
        today={today}
        sort={sort}
        poolFor={poolFor}
        focusRequest={focusReq}
        onFocusHandled={() => setFocusReq(null)}
        onSortChange={setSort}
        onPatch={patchRow}
        onFlush={flushRow}
        onAddRow={(field, opts) => void addRow(field, opts)}
        onDelete={(item) => void deleteRow(item)}
        onReorder={handleReorder}
        readOnly={readOnly}
        emptyMessage={
          filter === "tutte"
            ? readOnly
              ? "Nessuna azione in questo verbale."
              : "Aggiungi la prima riga con «Nuova attività…» in fondo (o Ctrl+N)."
            : "Nessuna azione per questo filtro."
        }
        selectedRowIds={selectedRowIds}
        onToggleRowSelected={toggleRowSelected}
        onToggleAllRowsSelected={toggleAllRowsSelected}
      />

      {status ? (
        <p className="text-xs text-muted-foreground">{status}</p>
      ) : null}
    </div>
  )
}
