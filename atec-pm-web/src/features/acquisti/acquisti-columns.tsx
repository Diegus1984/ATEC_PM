// ── Colonne della griglia fabbisogni (una per commessa) ───────────────────
// Funzione pura: nessun hook qui dentro. Il chiamante la invoca dentro un
// useMemo, altrimenti le colonne si ricostruiscono a ogni render e le celle
// si rimontano (popover e menu aperti si chiudono da soli).

import type { ColumnDef } from "@tanstack/react-table"
import { CheckCircle2, Clock, FileCheck2, Link2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { DdpStatusMenu } from "@/features/commesse/DdpStatusMenu"
import type { AcquistiInboxItem, DdpStatusItem } from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"

import { DaneaOrderBadge, StatusFilterCombobox } from "./acquisti-ui"
import { getSmartActionSortKey, rowHasDaneaOrder, statusOf } from "./acquisti-shared"

export function buildAcquistiColumns({
  gridItems,
  statuses,
  statusMap,
  transitionMap,
  statusChangePending,
  onStatusChange,
  onAssignAtec,
  onOpenRfqDetail,
  onOpenDaneaOrder,
  onRequestRfq,
}: {
  /** Righe di QUESTA griglia: alimentano il combo di filtro della colonna Stato. */
  gridItems: AcquistiInboxItem[]
  statuses: DdpStatusItem[]
  statusMap: Map<string, DdpStatusItem>
  /** Assente = finestra stati completa (privilegio #140). */
  transitionMap: Record<string, string[]> | undefined
  statusChangePending: boolean
  onStatusChange: (item: AcquistiInboxItem, statusKey: string) => void
  onAssignAtec: (item: AcquistiInboxItem) => void
  onOpenRfqDetail: (rfqId: number) => void
  onOpenDaneaOrder: (idDoc: number) => void
  onRequestRfq: (items: AcquistiInboxItem[]) => void
}): ColumnDef<AcquistiInboxItem>[] {
  return [
    {
      id: "select",
      header: ({ table }) => (
        <Checkbox
          checked={table.getIsAllPageRowsSelected()}
          onCheckedChange={(val) => table.toggleAllPageRowsSelected(!!val)}
          aria-label="Seleziona tutte"
        />
      ),
      cell: ({ row }) => (
        <Checkbox
          checked={row.getIsSelected()}
          onCheckedChange={(val) => row.toggleSelected(!!val)}
          aria-label="Seleziona riga"
        />
      ),
      enableSorting: false,
      enableHiding: false,
    },
    {
      id: "rowNumber",
      header: "#",
      cell: ({ row }) => (
        <span className="tabular-nums font-mono text-xs opacity-80">{row.index + 1}</span>
      ),
    },
    // Stessi nomi e stesso ordine delle DDP di commessa (segnalazione #61).
    {
      accessorKey: "requestedBy",
      header: "Inserito da",
      cell: ({ row }) => <span className="text-xs">{row.original.requestedBy || "—"}</span>,
    },
    {
      accessorKey: "createdAt",
      header: "Data inserimento",
      cell: ({ row }) => (
        <span className="whitespace-nowrap text-xs">
          {formatDateShort(row.original.createdAt) || "—"}
        </span>
      ),
    },
    {
      accessorKey: "atecCode",
      header: "Cod. ATEC",
      cell: ({ row }) => {
        const item = row.original
        if (item.atecCode) {
          return <span className="font-medium tabular-nums text-xs">{item.atecCode}</span>
        }
        return (
          <button
            type="button"
            title="Assegna codice ATEC"
            className="rounded p-1 opacity-60 hover:bg-black/10 hover:opacity-100"
            onClick={(e) => {
              e.stopPropagation()
              onAssignAtec(item)
            }}
          >
            <Link2 className="size-4" />
          </button>
        )
      },
    },
    {
      accessorKey: "partNumber",
      header: "Codice",
      cell: ({ row }) => (
        <span className="font-medium text-xs">{row.original.partNumber || "—"}</span>
      ),
    },
    {
      accessorKey: "description",
      header: "Descrizione",
      cell: ({ row }) => (
        <span
          className="block max-w-[260px] whitespace-normal break-words text-xs"
          title={row.original.description}
        >
          {row.original.description || "—"}
        </span>
      ),
    },
    /* COLONNA AZIONE: l'oggetto della RDO (titolo della gara) vive qui, in tooltip.
       Nella descrizione ci deve stare l'articolo comprato, non «Richiesta offerta — …». */
    {
      id: "rfqAction",
      header: "Az.",
      enableColumnFilter: false,
      cell: ({ row }) => {
        const item = row.original
        const subject = (item.activeRfqSubject ?? "").trim()
        if (!subject) return <span className="text-xs opacity-40">—</span>
        const label = item.activeRfqId ? `RDO #${item.activeRfqId} — ${subject}` : subject
        return (
          <span className="flex justify-center" title={label} aria-label={label}>
            <FileCheck2 className="size-4 opacity-70" />
          </span>
        )
      },
    },
    {
      accessorKey: "quantity",
      header: "Qtà",
      // Filtro stretto su misura: l'input standard allargherebbe la colonna ben oltre
      // le 2-3 cifre di una quantità.
      meta: {
        filterInput: ({ value, onChange }) => (
          <Input
            value={(value as string) ?? ""}
            className="h-8 w-12 bg-background dark:bg-background"
            onChange={(event) => onChange(event.target.value)}
          />
        ),
      },
      cell: ({ row }) => (
        <span className="font-semibold text-[11px] tabular-nums">{row.original.quantity}</span>
      ),
    },
    {
      accessorKey: "unit",
      header: "UM",
      meta: {
        filterInput: ({ value, onChange }) => (
          <Input
            value={(value as string) ?? ""}
            className="h-8 w-12 bg-background dark:bg-background"
            onChange={(event) => onChange(event.target.value)}
          />
        ),
      },
      cell: ({ row }) => (
        <span className="text-[11px] uppercase opacity-80">{row.original.unit || "—"}</span>
      ),
    },
    {
      accessorKey: "supplierName",
      header: "Fornitore",
      cell: ({ row }) => (
        <span className="text-xs font-medium">{row.original.supplierName || "—"}</span>
      ),
    },
    {
      accessorKey: "manufacturer",
      header: "Produttore",
      cell: ({ row }) => (
        <span className="text-xs opacity-80">{row.original.manufacturer || "—"}</span>
      ),
    },
    {
      id: "itemStatus",
      accessorFn: (r) => statusMap.get(r.itemStatus)?.label ?? r.itemStatus ?? "",
      header: "Stato",
      meta: {
        filterInput: ({ value, onChange }) => (
          <StatusFilterCombobox
            value={(value as string) ?? ""}
            onChange={(val) => onChange(val)}
            gridItems={gridItems}
            statusMap={statusMap}
          />
        ),
      },
      cell: ({ row }) => {
        const s = statusMap.get(row.original.itemStatus)
        return (
          <div className="flex min-w-[120px] items-center gap-1">
            <span className="min-w-0 flex-1 truncate font-semibold whitespace-nowrap text-xs">
              {s ? s.label : row.original.itemStatus || "—"}
            </span>
            <DdpStatusMenu
              currentStatusKey={row.original.itemStatus}
              statuses={statuses}
              transitions={transitionMap}
              disabled={statusChangePending}
              onSelect={(statusKey) => onStatusChange(row.original, statusKey)}
            />
          </div>
        )
      },
    },
    /* COLONNA SMART ACTION: Prossimo passo in base allo stato attuale */
    {
      id: "smartAction",
      accessorFn: (r) => getSmartActionSortKey(r),
      header: "Prossimo Passo",
      cell: ({ row }) => {
        const item = row.original
        const status = statusOf(item)

        // 1) Ordine Danea già generato → badge di consultazione (clic = apre il documento).
        //    Prevale su «RDO #id»: la riga resta InActiveRfq anche a RDO chiusa/ordinata.
        if (rowHasDaneaOrder(item)) {
          return (
            <DaneaOrderBadge
              label={`In Ordine${item.daneaRef ? ` #${item.daneaRef}` : ""}`}
              idDoc={item.daneaOrderIdDoc ?? null}
              icon={CheckCircle2}
              iconClassName="size-3"
              className="inline-flex items-center gap-1 font-mono text-xs font-semibold underline-offset-2 hover:underline"
              onOpen={onOpenDaneaOrder}
            />
          )
        }

        // 2) In gara RDO (non ancora ordinata).
        if (item.inActiveRfq || status === "RO") {
          return (
            <button
              type="button"
              className="inline-flex items-center gap-1 rounded bg-black/10 hover:bg-black/20 text-current border border-black/20 px-2 py-0.5 text-xs font-medium transition-colors"
              onClick={() => {
                if (item.activeRfqId) onOpenRfqDetail(item.activeRfqId)
              }}
            >
              <Clock className="size-3" />
              RDO #{item.activeRfqId || ""}
            </button>
          )
        }

        // La RDO si può richiedere SOLO se lo stato è "DO" (Da ORDINARE).
        // Se la riga è in verifica magazzino (VER, CHEK, ecc.), non si mostra il pulsante Richiedi RDO.
        if (status === "DO") {
          return (
            <Button
              size="sm"
              variant="ghost"
              className="h-7 text-xs font-medium gap-1 bg-black/10 hover:bg-black/20 text-current border border-black/20"
              onClick={() => onRequestRfq([item])}
            >
              <FileCheck2 className="size-3.5" />
              Richiedi RDO
            </Button>
          )
        }

        return <span className="text-xs opacity-40">—</span>
      },
    },
    {
      accessorKey: "daneaRef",
      header: "Rif. Danea",
      cell: ({ row }) => (
        <span className="font-mono text-xs font-semibold">{row.original.daneaRef || "—"}</span>
      ),
    },
    {
      accessorKey: "dateNeeded",
      header: "Data Prevista",
      cell: ({ row }) => (
        <span className="whitespace-nowrap text-xs">
          {formatDateShort(row.original.dateNeeded) || "—"}
        </span>
      ),
    },
    {
      accessorKey: "destination",
      header: "Destinazione",
      cell: ({ row }) => <span className="text-xs">{row.original.destination || "—"}</span>,
    },
    {
      accessorKey: "destinationSpec",
      header: "Specifica",
      cell: ({ row }) => (
        <span className="text-xs opacity-75">{row.original.destinationSpec || "—"}</span>
      ),
    },
    {
      accessorKey: "notes",
      header: "Note",
      cell: ({ row }) => (
        <span
          className="block max-w-[120px] whitespace-normal break-words text-xs opacity-75"
          title={row.original.notes}
        >
          {row.original.notes || "—"}
        </span>
      ),
    },
    {
      accessorKey: "unitCost",
      header: "€ Unit.",
      cell: ({ row }) => (
        <span className="tabular-nums text-xs">{euro(row.original.unitCost)}</span>
      ),
    },
    {
      id: "totalCost",
      header: "€ Totale",
      cell: ({ row }) => {
        const tot = (row.original.unitCost || 0) * row.original.quantity
        return <span className="font-semibold tabular-nums text-xs">{euro(tot)}</span>
      },
    },
  ]
}
