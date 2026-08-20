import type { ColumnDef } from "@tanstack/react-table"
import { Link } from "react-router-dom"

import { Checkbox } from "@/components/ui/checkbox"
import { DdpInlineDateCell } from "@/features/commesse/DdpInlineDateCell"
import { DdpInlineTextCell } from "@/features/commesse/DdpInlineTextCell"
import type { DdpStatusItem, WorkshopRow } from "@/lib/api/types"
import { dateToIso, formatDateShort } from "@/lib/date-iso"
import { cn } from "@/lib/utils"

import { isDataRichiestaScaduta, type WorkshopView } from "./workshop-shared"

export const WORKSHOP_COLUMN_LABELS: Record<string, string> = {
  project: "Commessa",
  partNumber: "Codice",
  description: "Descrizione",
  quantity: "Qtà",
  quantityProduced: "Prodotti",
  material: "Materiale",
  treatment: "Trattamento",
  itemStatus: "Stato",
  requestDate: "Data Richiesta",
  destination: "Destinazione",
  destinationSpec: "Specifica",
  daneaRef: "Rif. Danea",
  orderDate: "Data Ordine",
  deliveredAt: "Consegnato il",
  notes: "Note",
  ultra: "Urgente",
}

export type WorkshopPatch = {
  onRequestDate: (row: WorkshopRow, value: string | null) => void
  onNotes: (row: WorkshopRow, value: string) => void
  onUltraCritical: (row: WorkshopRow, value: boolean) => void
  disabled?: boolean
}

/**
 * Colonne di «Lavorazioni Officine» (#83). Tutto quello che arriva dalla distinta è **testo**,
 * non un campo: la segnalazione lo chiede esplicitamente («non consentire variazione da
 * Lavorazioni a DDP officine»), e mostrarlo come casella scrivibile che poi non salva sarebbe
 * peggio che mostrarlo fermo. Le tre eccezioni scrivibili — Data Richiesta, Note, Urgente —
 * sono quelle che questa pagina possiede davvero.
 */
export function buildWorkshopColumns(
  view: WorkshopView,
  patch: WorkshopPatch,
  statusMap: Map<string, DdpStatusItem>
): ColumnDef<WorkshopRow>[] {
  const esterne = view === "esterne"
  /** Oggi secondo il browser: le colonne si ricostruiscono a ogni cambio vista/patch. */
  const oggi = dateToIso(new Date())

  const columns: ColumnDef<WorkshopRow>[] = [
    {
      id: "project",
      header: "Commessa",
      accessorFn: (r) => `${r.projectCode} ${r.projectTitle}`.trim(),
      cell: ({ row }) => {
        const r = row.original
        if (!r.projectId) {
          return <span className="text-xs text-muted-foreground">— riga manuale —</span>
        }
        return (
          <div className="flex flex-col">
            <Link
              to={`/commesse/${r.projectId}/ddp_officina?item=${r.source === "DDP" ? r.id : ""}`}
              className="font-medium text-primary hover:underline"
            >
              {r.projectCode || "—"}
            </Link>
            <span className="line-clamp-1 text-xs text-muted-foreground">
              {r.customerName || r.projectTitle}
            </span>
          </div>
        )
      },
    },
    {
      accessorKey: "partNumber",
      header: "Codice",
      cell: ({ row }) => (
        <span className="font-mono text-xs font-semibold">
          {row.original.partNumber || "—"}
        </span>
      ),
    },
    {
      accessorKey: "description",
      header: "Descrizione",
      cell: ({ row }) => (
        <span className="line-clamp-2 max-w-[240px] text-sm">
          {row.original.description || "—"}
        </span>
      ),
    },
    {
      accessorKey: "quantity",
      header: "Qtà",
      cell: ({ row }) => (
        <span className="tabular-nums text-sm">{row.original.quantity}</span>
      ),
    },
    {
      accessorKey: "quantityProduced",
      header: "Prodotti",
      cell: ({ row }) => (
        <span className="tabular-nums text-sm">
          {row.original.quantityProduced}
          <span className="text-muted-foreground">/{row.original.quantity}</span>
        </span>
      ),
    },
    {
      accessorKey: "material",
      header: "Materiale",
      cell: ({ row }) => (
        <span className="line-clamp-1 max-w-[140px] text-sm">
          {row.original.material || "—"}
        </span>
      ),
    },
    {
      accessorKey: "treatment",
      header: "Trattamento",
      cell: ({ row }) => (
        <span className="line-clamp-1 max-w-[140px] text-sm">
          {row.original.treatment || "—"}
        </span>
      ),
    },
    {
      accessorKey: "itemStatus",
      header: "Stato",
      cell: ({ row }) => {
        const r = row.original
        if (r.source === "MANUAL") {
          return <span className="text-xs text-muted-foreground">manuale</span>
        }
        const st = statusMap.get(r.itemStatus)
        return (
          <span
            className="inline-flex items-center rounded-md border px-1.5 py-0.5 text-[11px] font-medium"
            style={
              st
                ? { backgroundColor: st.colorBg, color: st.colorFg, borderColor: st.colorFg }
                : undefined
            }
          >
            {st?.label ?? r.itemStatus}
          </span>
        )
      },
    },
    {
      accessorKey: "requestDate",
      header: "Data Richiesta",
      // Le righe senza data richiesta vanno in fondo, non in cima: non hanno una scadenza,
      // non sono le più urgenti (stesso criterio dell'ordine che manda il server).
      sortingFn: (a, b) => {
        const da = a.original.requestDate || ""
        const db = b.original.requestDate || ""
        if (da === db) return 0
        if (da === "") return 1
        if (db === "") return -1
        return da < db ? -1 : 1
      },
      cell: ({ row }) => {
        const r = row.original
        // #84: scaduta = grassetto rosso, misurata su oggi del browser (`isDataRichiestaScaduta`).
        // Il «+N gg» resta quello del server, che è il conteggio dei giorni veri di ritardo.
        const scaduta = isDataRichiestaScaduta(r.requestDate, oggi)
        const late = (r.daysLate ?? 0) > 0
        return (
          <div className="flex flex-col gap-0.5">
            <DdpInlineDateCell
              value={r.requestDate || null}
              disabled={patch.disabled}
              onChange={(value) => patch.onRequestDate(r, value)}
              className={
                scaduta ? "font-bold text-rose-600 hover:text-rose-600" : undefined
              }
            />
            {late ? (
              <span className="text-[10px] font-medium text-rose-600">
                +{r.daysLate} gg
              </span>
            ) : null}
          </div>
        )
      },
    },
    {
      accessorKey: "destination",
      header: "Destinazione",
      cell: ({ row }) => (
        <span className="line-clamp-1 max-w-[140px] text-sm">
          {row.original.destination || "—"}
        </span>
      ),
    },
    {
      accessorKey: "destinationSpec",
      header: "Specifica",
      cell: ({ row }) => (
        <span className="line-clamp-1 max-w-[140px] text-sm">
          {row.original.destinationSpec || "—"}
        </span>
      ),
    },
  ]

  if (esterne) {
    columns.push(
      {
        accessorKey: "daneaRef",
        header: "Rif. Danea",
        cell: ({ row }) => (
          <span className="font-mono text-xs">{row.original.daneaRef || "—"}</span>
        ),
      },
      {
        accessorKey: "orderDate",
        header: "Data Ordine",
        cell: ({ row }) => (
          <span className="text-sm tabular-nums">
            {row.original.orderDate ? formatDateShort(row.original.orderDate) : "—"}
          </span>
        ),
      },
      {
        accessorKey: "deliveredAt",
        header: "Consegnato il",
        cell: ({ row }) => (
          <span className="text-sm tabular-nums">
            {row.original.deliveredAt ? formatDateShort(row.original.deliveredAt) : "—"}
          </span>
        ),
      }
    )
  }

  columns.push({
    accessorKey: "notes",
    header: "Note",
    cell: ({ row }) => (
      <DdpInlineTextCell
        value={row.original.notes ?? ""}
        disabled={patch.disabled}
        placeholder="—"
        multiline
        onCommit={(value) => patch.onNotes(row.original, value)}
      />
    ),
  })

  // «Segnala ultra critica»: la segnalazione la tiene sulle sole interne — per le esterne
  // dice, testualmente, che non serve né la spunta né la pagina Urgenze.
  if (view !== "esterne") {
    columns.push({
      id: "ultra",
      header: "Urgente",
      accessorFn: (r) => (r.isUltraCritical ? 1 : 0),
      cell: ({ row }) => {
        const r = row.original
        return (
          <div
            className="flex items-center justify-center"
            onClick={(e) => e.stopPropagation()}
            onDoubleClick={(e) => e.stopPropagation()}
          >
            <Checkbox
              checked={r.isUltraCritical}
              disabled={patch.disabled}
              className={cn(r.isUltraCritical && "border-rose-500 data-[state=checked]:bg-rose-500")}
              onCheckedChange={(value) => patch.onUltraCritical(r, value === true)}
              aria-label="Segnala ultra critica"
            />
          </div>
        )
      },
    })
  }

  return columns
}
