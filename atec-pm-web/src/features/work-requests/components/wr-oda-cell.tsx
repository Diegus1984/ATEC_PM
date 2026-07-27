// ── Cella «Dati ODA»: interna ATEC, derivata dalla DDP Officina, o manuale ──

import { DateField, ReadonlyDateField } from "@/components/shared/date-field"
import type { WorkRequest } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { InlineInput } from "./inline-fields"

function FieldLabel({ children }: { children: React.ReactNode }) {
  return (
    <span className="text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
      {children}
    </span>
  )
}

export function WrOdaCell({
  req,
  onPatch,
}: {
  req: WorkRequest
  onPatch: (id: number, field: string, value: unknown) => void
}) {
  // Lavorazione interna: fornitore fisso, n°/data ordine solo se arrivano dalla DDP.
  if (req.type === "Internal") {
    return (
      <div className="flex min-w-[11rem] flex-col gap-1.5">
        <div className="grid gap-0.5">
          <FieldLabel>Fornitore</FieldLabel>
          <span className="inline-flex h-8 w-full items-center rounded-md border border-input bg-white dark:bg-zinc-950 px-2 text-sm font-medium">
            Interna ATEC
          </span>
        </div>
        {req.ddpOfficinaItemId != null && (req.poNumber || req.poDate) ? (
          <>
            {req.poNumber ? (
              <div className="grid gap-0.5">
                <FieldLabel>N° Ordine</FieldLabel>
                <span
                  className="inline-flex h-8 w-full items-center rounded-md border border-input bg-muted/40 px-2 text-sm"
                  title="N° ordine dalla DDP Officina (Rif. Danea)"
                >
                  {req.poNumber}
                </span>
              </div>
            ) : null}
            {req.poDate ? (
              <div className="grid gap-0.5">
                <FieldLabel>Data Ordine</FieldLabel>
                <ReadonlyDateField
                  size="sm"
                  stackedWeekday
                  showIcon={false}
                  value={req.poDate}
                  className="w-full min-w-0 rounded-md shadow-none bg-muted/40"
                />
              </div>
            ) : null}
          </>
        ) : null}
      </div>
    )
  }

  // Riga collegata alla DDP Officina: i dati ordine li comanda la distinta.
  if (req.ddpOfficinaItemId != null) {
    return (
      <div className="flex min-w-[11rem] flex-col gap-1.5">
        <div className="grid gap-0.5">
          <FieldLabel>Fornitore</FieldLabel>
          <span
            className={cn(
              "inline-flex h-8 w-full items-center rounded-md border px-2 text-sm leading-tight",
              req.poSupplier
                ? "border-input bg-muted/40 font-medium"
                : "border-dashed bg-muted/20 text-muted-foreground"
            )}
            title="Fornitore dalla DDP Officina"
          >
            <span className="line-clamp-2 break-words">{req.poSupplier || "—"}</span>
          </span>
        </div>
        <div className="grid gap-0.5">
          <FieldLabel>N° Ordine</FieldLabel>
          <span
            className={cn(
              "inline-flex h-8 w-full items-center rounded-md border px-2 text-sm",
              req.poNumber
                ? "border-input bg-muted/40"
                : "border-dashed bg-muted/20 text-muted-foreground"
            )}
            title="N° ordine dalla DDP Officina (Rif. Danea)"
          >
            {req.poNumber || "—"}
          </span>
        </div>
        <div className="grid gap-0.5">
          <FieldLabel>Data Ordine</FieldLabel>
          <ReadonlyDateField
            size="sm"
            stackedWeekday
            showIcon={false}
            value={req.poDate || null}
            placeholder="—"
            className={cn(
              "w-full min-w-0 rounded-md shadow-none",
              req.poDate ? "bg-muted/40" : "border-dashed bg-muted/20"
            )}
          />
        </div>
      </div>
    )
  }

  // Lavorazione esterna manuale: tutto editabile.
  return (
    <div className="flex min-w-[11rem] flex-col gap-1.5">
      <div className="grid gap-0.5">
        <FieldLabel>Fornitore</FieldLabel>
        <InlineInput
          placeholder="—"
          value={req.poSupplier}
          onCommit={(val) => onPatch(req.id, "po_supplier", val)}
        />
      </div>
      <div className="grid gap-0.5">
        <FieldLabel>N° Ordine</FieldLabel>
        <InlineInput
          placeholder="—"
          value={req.poNumber}
          onCommit={(val) => onPatch(req.id, "po_number", val)}
        />
      </div>
      <div className="grid gap-0.5">
        <FieldLabel>Data Ordine</FieldLabel>
        <DateField
          size="sm"
          stackedWeekday
          className="w-full min-w-0 shadow-none bg-white dark:bg-zinc-950"
          placeholder="—"
          value={req.poDate || null}
          onChange={(val) => onPatch(req.id, "po_date", val)}
        />
      </div>
    </div>
  )
}
