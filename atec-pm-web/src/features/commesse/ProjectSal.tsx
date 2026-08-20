import * as React from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"

import { useConfirm } from "@/components/shared/confirm"
import { Card, CardContent } from "@/components/ui/card"
import {
  Table,
  TableBody,
  TableCell,
  TableFooter,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  createSalRow,
  fetchPaymentStatesActive,
  fetchSal,
  fetchSalConditions,
  fetchSapCausaliActive,
  reorderSalRows,
  saveSalHeader,
  seedSalTemplate,
} from "@/lib/api/sal"
import type { SalHeaderSaveRequest } from "@/lib/api/types"
import { canAccessFeature, canWriteFeature } from "@/lib/auth/permissions"
import { euro } from "@/lib/format"
import { useGlobalSalHub, useSalHub } from "@/lib/signalr/use-sal-hub"
import { notifyError } from "@/lib/toast"
import { cn } from "@/lib/utils"
import { fmtSalPct } from "@/features/sal/SalIncassoProgress"
import { toIso } from "@/features/risorse/planner-logic"

import { NewSalRowComponent } from "./sal-new-row"
import { SalRowComponent } from "./sal-row"
import { SalSheetHead } from "./sal-sheet-head"
import {
  emptyRowRequest,
  SAL_COL_ORDER,
  SAL_ECONOMICS_COLS,
  SAL_HIDEABLE_COLS,
  SAL_VISIBILITY_STORAGE_KEY,
  type DropHint,
  type SalColId,
} from "./sal-sheet-shared"
import { printSalSheet } from "./sal-sheet-print"
import { SalSheetToolbar } from "./sal-sheet-toolbar"
import {
  salIsPagata,
  salPagamentoStyle,
  salRowClass,
  salRowVisualState,
} from "./sal-utils"

export function ProjectSal({
  projectId,
  projectCode,
  projectTitle,
}: {
  projectId: number
  projectCode?: string
  projectTitle?: string
}) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  // Foglio SAL di una commessa CHIUSA: resta modificabile solo a chi ha la chiave
  // `action.sal_edit_closed` (prima era il livello ADMIN). È una scrittura, quindi
  // `canWriteFeature`; alimenta il prop `isAdmin` di `sal-row` (che sblocca le righe).
  const canEditClosedSal = canWriteFeature("action.sal_edit_closed")
  // Importi visibili con la funzione `sal.economics` (livello 2: PM/ADMIN come prima).
  const canSeeEconomics = canAccessFeature("sal.economics")
  // Funzione concessa in sola lettura (tecnico del reparto Contabilità): il foglio si
  // consulta ma non si tocca. A respingere davvero le scritture è l'API.
  const readOnly = !canWriteFeature("project.sal")

  const queryKey = React.useMemo(() => ["sal", "project", projectId], [projectId])
  const query = useQuery({
    queryKey,
    queryFn: () => fetchSal(projectId),
    enabled: projectId > 0,
  })

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey })
  }, [queryClient, queryKey])

  useSalHub(projectId > 0, invalidate, projectId)

  // Realtime anagrafiche: un cambio ai cataloghi SAL da un altro client (colori stati
  // pagamento, nuove condizioni/causali) deve riflettersi anche qui nel tab commessa.
  // Invalida SOLO le query dei cataloghi: ["sal","project",...] è già coperta da
  // useSalHub qui sopra — rifarla qui significherebbe doppio refetch su ogni evento.
  const invalidateLookups = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: ["sal", "conditions"] })
    void queryClient.invalidateQueries({ queryKey: ["sal", "sap-causali"] })
    void queryClient.invalidateQueries({ queryKey: ["sal", "payment-states"] })
  }, [queryClient])

  useGlobalSalHub(projectId > 0, invalidateLookups)

  // Anagrafiche attive: Condizioni di pagamento, Causali Conto SAP, Stati Pagamento
  const conditionsQuery = useQuery({
    queryKey: ["sal", "conditions", "active"],
    queryFn: () => fetchSalConditions(true),
  })
  const sapCausaliQuery = useQuery({
    queryKey: ["sal", "sap-causali", "active"],
    queryFn: fetchSapCausaliActive,
  })
  const paymentStatesQuery = useQuery({
    queryKey: ["sal", "payment-states", "active"],
    queryFn: fetchPaymentStatesActive,
  })

  const bundle = query.data
  const header = bundle?.header
  const rows = React.useMemo(() => bundle?.rows ?? [], [bundle])

  // Avanzamento incasso v10: Σ% delle righe con Pagamento = «Pagata» (non più su stato)
  const { totalPerc, paidPerc } = React.useMemo(() => {
    const tot = rows.reduce((acc, r) => acc + Number(r.perc ?? 0), 0)
    const paid = rows.reduce(
      (acc, r) => acc + (salIsPagata(r.pagamento) ? Number(r.perc ?? 0) : 0),
      0
    )
    return { totalPerc: tot, paidPerc: paid }
  }, [rows])

  // Totali economici footer (D4: importo = valore×perc/100, iva = importo×%IVA/100)
  const sums = React.useMemo(() => {
    let importo = 0
    let iva = 0
    let hasAny = false
    for (const r of rows) {
      if (header?.valore == null || r.perc == null) continue
      const imp = header.valore * (Number(r.perc) / 100)
      const i = imp * ((r.ivaPerc ?? 0) / 100)
      importo += imp
      iva += i
      hasAny = true
    }
    return { importo, iva, tot: importo + iva, hasAny }
  }, [rows, header])

  // Σ% «esatto» al netto del rumore floating point (verde solo se = 100,00)
  const percOk = Math.abs(totalPerc - 100) < 0.005

  // Stato interno per l'editing dell'header (cliente = sola lettura dalla commessa)
  const [valore, setValore] = React.useState("")
  const [po, setPo] = React.useState("")
  const [rifOfferta, setRifOfferta] = React.useState("")

  const customerName = header?.customerName?.trim() || "—"

  // Sync header → input con deps PRIMITIVE (non l'oggetto header: a ogni refetch
  // cambia identità anche a dati invariati e resetterebbe i campi mentre l'utente
  // digita). Dentro l'effect si aggiornano solo i campi realmente cambiati lato
  // server, tracciati nell'ultimo valore sincronizzato.
  const headerValore = header?.valore
  const headerPo = header?.po
  const headerRifOfferta = header?.rifOfferta
  const lastSyncedHeader = React.useRef<{
    valore?: number | null
    po?: string
    rifOfferta?: string
  }>({})

  React.useEffect(() => {
    const prev = lastSyncedHeader.current
    if (prev.valore !== headerValore)
      setValore(
        headerValore === null || headerValore === undefined ? "" : String(headerValore)
      )
    if (prev.po !== headerPo) setPo(headerPo || "")
    if (prev.rifOfferta !== headerRifOfferta) setRifOfferta(headerRifOfferta || "")
    lastSyncedHeader.current = {
      valore: headerValore,
      po: headerPo,
      rifOfferta: headerRifOfferta,
    }
  }, [headerValore, headerPo, headerRifOfferta])

  const handleSaveHeader = async (updatedFields: Partial<SalHeaderSaveRequest>) => {
    if (!header) return
    try {
      // Contratto null-preserve: po/rifOfferta SEMPRE stringhe esplicite (anche vuote)
      const payload: SalHeaderSaveRequest = {
        valore:
          updatedFields.valore !== undefined
            ? updatedFields.valore
            : valore === ""
              ? null
              : Number(valore),
        po: updatedFields.po !== undefined ? updatedFields.po : po,
        rifOfferta:
          updatedFields.rifOfferta !== undefined ? updatedFields.rifOfferta : rifOfferta,
        rowVersion: header.rowVersion,
      }
      await saveSalHeader(projectId, payload)
      invalidate()
    } catch (err) {
      notifyError(err as Error)
      invalidate()
    }
  }

  const handleSeedTemplate = async () => {
    try {
      await seedSalTemplate(projectId)
      invalidate()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  // ── Drag & Drop ───────────────────────────────────────────────
  const dragIdRef = React.useRef<number | null>(null)
  const [grabId, setGrabId] = React.useState<number | null>(null)
  const [draggingId, setDraggingId] = React.useState<number | null>(null)
  const [dropHint, setDropHint] = React.useState<DropHint | null>(null)

  const clearDrag = () => {
    dragIdRef.current = null
    setGrabId(null)
    setDraggingId(null)
    setDropHint(null)
  }

  const handleReorder = async (dragId: number, targetId: number, after: boolean) => {
    if (dragId === targetId) return
    const ids = rows.map((r) => r.id)
    const fromIdx = ids.indexOf(dragId)
    if (fromIdx === -1) return
    ids.splice(fromIdx, 1)
    const targetIdx = ids.indexOf(targetId)
    if (targetIdx === -1) return
    ids.splice(after ? targetIdx + 1 : targetIdx, 0, dragId)

    try {
      await reorderSalRows(projectId, ids)
      invalidate()
    } catch (err) {
      notifyError(err as Error)
      invalidate()
    }
  }

  const handleInsertRow = async (index: number, where: "above" | "below") => {
    try {
      const newId = await createSalRow(projectId, emptyRowRequest())
      const ids = rows.map((r) => r.id)
      ids.splice(where === "below" ? index + 1 : index, 0, newId)
      await reorderSalRows(projectId, ids)
      invalidate()
    } catch (err) {
      notifyError(err as Error)
    }
  }

  const todayIso = React.useMemo(() => toIso(new Date()), [])

  // ── Visibilità colonne dal menu «Colonne» (true = nascosta), persistita come
  // nelle DataTableCard; chiave versionata (vedi gotcha localStorage). ──
  const [hiddenCols, setHiddenCols] = React.useState<Record<string, boolean>>(() => {
    try {
      const saved = localStorage.getItem(SAL_VISIBILITY_STORAGE_KEY)
      if (saved) return JSON.parse(saved)
    } catch (e) {
      console.error("Failed to load column visibility from localStorage", e)
    }
    return {}
  })

  React.useEffect(() => {
    try {
      localStorage.setItem(SAL_VISIBILITY_STORAGE_KEY, JSON.stringify(hiddenCols))
    } catch (e) {
      console.error("Failed to save column visibility to localStorage", e)
    }
  }, [hiddenCols])

  const showCol = React.useCallback(
    (id: SalColId) => hiddenCols[id] !== true,
    [hiddenCols]
  )

  const columnToggles = SAL_HIDEABLE_COLS.filter(
    (c) => canSeeEconomics || !SAL_ECONOMICS_COLS.has(c.id)
  ).map((c) => ({
    id: c.id,
    label: c.label,
    checked: showCol(c.id),
    onToggle: (checked: boolean) =>
      setHiddenCols((prev) => ({ ...prev, [c.id]: !checked })),
  }))

  // Colonne effettivamente renderizzate, in ordine canonico (footer + colSpan).
  const visibleColIds = SAL_COL_ORDER.filter(
    (id) => (canSeeEconomics || !SAL_ECONOMICS_COLS.has(id)) && showCol(id)
  )

  if (query.isLoading) {
    return (
      <Card className="py-8 text-center text-sm text-muted-foreground">
        Caricamento dati SAL…
      </Card>
    )
  }

  if (query.isError) {
    return (
      <Card className="p-4 border-destructive/20 bg-destructive/10 text-destructive text-sm font-medium">
        {(query.error as Error).message}
      </Card>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <Card className="overflow-hidden py-0">
        <SalSheetToolbar
          projectCode={projectCode}
          projectTitle={projectTitle}
          customerName={customerName}
          po={po}
          setPo={setPo}
          rifOfferta={rifOfferta}
          setRifOfferta={setRifOfferta}
          valore={valore}
          setValore={setValore}
          onSaveHeader={(fields) => void handleSaveHeader(fields)}
          canSeeEconomics={canSeeEconomics}
          totalPerc={totalPerc}
          paidPerc={paidPerc}
          columnToggles={columnToggles}
          onSeedTemplate={() => void handleSeedTemplate()}
          seedDisabled={rows.length > 0}
          onRefresh={() => void query.refetch()}
          isFetching={query.isFetching}
          onPrintPdf={
            bundle
              ? () =>
                  printSalSheet(bundle, {
                    projectCode: projectCode || `P${projectId}`,
                    projectTitle,
                    canSeeEconomics,
                  })
              : undefined
          }
          readOnly={readOnly}
        />

        <CardContent className="p-3">
          {rows.length > 0 && Math.floor(totalPerc) > 100 && (
            <div className="mb-3 px-3 py-2.5 rounded-lg border border-red-700 bg-red-600 text-white text-xs flex items-center justify-between font-semibold shadow-sm">
              <span>
                🚨 <strong>ATTENZIONE:</strong> il totale delle percentuali degli step SAL è{" "}
                <span className="text-yellow-300 underline font-extrabold">{fmtSalPct(totalPerc)}%</span>. Supera il 100% di{" "}
                <span className="text-yellow-300 underline font-extrabold">{fmtSalPct(totalPerc - 100)}%</span>.
              </span>
            </div>
          )}

          {rows.length === 0 ? (
            <p className="mb-3 text-sm text-muted-foreground text-center py-6">
              Nessun pagamento SAL pianificato per questa commessa. Precarica il modello
              standard o aggiungi uno step qui sotto.
            </p>
          ) : null}

          {/* Tabella larga v10: scroller standard (righe dentro la griglia,
              intestazione ferma, barra orizzontale in alto). */}
          <GridScroller className="rounded-lg border">
            <Table className="border-separate border-spacing-y-1.5">
              <TableHeader className="bg-muted/40">
                <SalSheetHead showCol={showCol} canSeeEconomics={canSeeEconomics} />
              </TableHeader>
              <TableBody>
                {rows.map((row, index) => {
                  // Tinta riga: colore configurato in anagrafica (style inline,
                  // stati attivi: quelli storici/disattivati restano neutri) >
                  // classi cablate di fallback (Pagata verde / Parz. rossa /
                  // emessa gialla / warn-pre). Mai entrambe (doppia tinta).
                  const paymentStates = paymentStatesQuery.data ?? []
                  const importo =
                    header && header.valore != null && row.perc != null
                      ? header.valore * (row.perc / 100)
                      : null

                  return (
                    <SalRowComponent
                      key={row.id}
                      row={row}
                      index={index}
                      rowBg={salRowClass(salRowVisualState(row, todayIso, paymentStates))}
                      rowStyle={salPagamentoStyle(row.pagamento, paymentStates)}
                      isDragging={draggingId === row.id}
                      isDropOver={dropHint?.id === row.id}
                      dropHint={dropHint}
                      importo={importo}
                      activeConditions={conditionsQuery.data ?? []}
                      sapCausali={sapCausaliQuery.data ?? []}
                      paymentStates={paymentStates}
                      onMutated={invalidate}
                      onInsert={(where) => void handleInsertRow(index, where)}
                      onConfirm={confirm}
                      grabId={grabId}
                      setGrabId={setGrabId}
                      setDraggingId={setDraggingId}
                      setDropHint={setDropHint}
                      dragIdRef={dragIdRef}
                      handleReorder={handleReorder}
                      clearDrag={clearDrag}
                      canSeeEconomics={canSeeEconomics}
                      isAdmin={canEditClosedSal}
                      isProjectClosed={header?.isProjectClosed ?? false}
                      readOnly={readOnly}
                      showCol={showCol}
                    />
                  )
                })}

                {canSeeEconomics && !readOnly && (
                  <NewSalRowComponent
                    projectId={projectId}
                    onMutated={invalidate}
                    colSpan={visibleColIds.length - 1}
                  />
                )}
              </TableBody>

              {rows.length > 0 && (
                <TableFooter className="bg-transparent">
                  {/* Una cella per ogni colonna visibile (ordine canonico): i totali
                      restano allineati qualunque sia la selezione del menu «Colonne». */}
                  <TableRow className="border-0 bg-muted/40 hover:bg-muted/40">
                    {visibleColIds.map((id) => {
                      switch (id) {
                        case "iva":
                          return (
                            <TableCell
                              key={id}
                              className="py-2 pr-4 text-right text-sm font-bold text-muted-foreground"
                            >
                              {sums.hasAny ? euro(sums.iva) : "—"}
                            </TableCell>
                          )
                        case "totIva":
                          return (
                            <TableCell
                              key={id}
                              className="py-2 pr-4 text-right text-sm font-bold text-foreground"
                            >
                              {sums.hasAny ? euro(sums.tot) : "—"}
                            </TableCell>
                          )
                        case "step":
                          return (
                            <TableCell
                              key={id}
                              className="py-2 text-sm font-bold uppercase tracking-wide text-muted-foreground"
                            >
                              Totali
                            </TableCell>
                          )
                        case "perc":
                          return (
                            <TableCell
                              key={id}
                              className={cn(
                                "py-2 text-center text-sm font-bold",
                                percOk ? "text-emerald-600" : "text-amber-600"
                              )}
                              title={
                                percOk
                                  ? "Pianificazione completa (100%)"
                                  : "Il totale delle percentuali non è 100%"
                              }
                            >
                              {fmtSalPct(totalPerc)}%
                            </TableCell>
                          )
                        case "importo":
                          return (
                            <TableCell
                              key={id}
                              className="py-2 pr-4 text-right text-sm font-bold text-foreground"
                            >
                              {sums.hasAny ? euro(sums.importo) : "—"}
                            </TableCell>
                          )
                        default:
                          return <TableCell key={id} className="py-2" />
                      }
                    })}
                  </TableRow>
                </TableFooter>
              )}
            </Table>
          </GridScroller>
        </CardContent>
      </Card>
    </div>
  )
}
