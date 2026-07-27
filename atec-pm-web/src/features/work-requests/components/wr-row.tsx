// ── Riga della griglia lavorazioni (celle editabili + menu azioni) ─────────

import { Check, Flag, RotateCcw, Trash2 } from "lucide-react"

import { DateField } from "@/components/shared/date-field"
import { RowActionsMenu, type RowAction } from "@/components/shared/row-actions"
import { Button } from "@/components/ui/button"
import { TableCell, TableRow } from "@/components/ui/table"
import { priorityRowClass } from "@/features/checklist/checklist-utils"
import type { WorkRequest } from "@/lib/api/types"
import { formatWorkRequestProjectLabel } from "@/lib/system-projects"
import { cn } from "@/lib/utils"

import { InlineTextarea } from "./inline-fields"
import { WrOdaCell } from "./wr-oda-cell"
import {
  WrDeliveredSelect,
  WrPrioritySelect,
  WrTreatmentSelect,
  WrTypeSelect,
} from "./wr-selects"
import type { WrVisibleColumns } from "../wr-shared"

const CELL_CLASS = "whitespace-normal py-2 !align-top"

export function WorkRequestRow({
  req,
  index,
  columns,
  highlighted,
  onPatch,
  onTypeChange,
  onOpenRfq,
  onConfirmDraft,
  onDelete,
}: {
  req: WorkRequest
  index: number
  columns: WrVisibleColumns
  highlighted: boolean
  onPatch: (id: number, field: string, value: unknown) => void
  onTypeChange: (req: WorkRequest, type: string) => void
  onOpenRfq: (req: WorkRequest) => void
  onConfirmDraft: (id: number) => void
  onDelete: (req: WorkRequest) => void
}) {
  const actions: RowAction[] = []
  if (req.isStaging) {
    actions.push({
      label: "Conferma bozza",
      icon: Check,
      onClick: () => onConfirmDraft(req.id),
    })
  }
  if (req.hasTreatment && !req.isTreatmentConfirmed) {
    actions.push({
      label: "Conferma trattamento",
      icon: Check,
      onClick: () => onPatch(req.id, "is_treatment_confirmed", true),
    })
  }
  if (req.hasTreatment && req.isTreatmentConfirmed) {
    actions.push({
      // Riporta la lavorazione nel Rapporto Trattamenti (es. secondo giro
      // di trattamento); il server azzera anche treatment_confirmed_at
      label: "Riapri trattamento",
      icon: RotateCcw,
      onClick: () => onPatch(req.id, "is_treatment_confirmed", false),
    })
  }
  actions.push({
    label: req.isUltraCritical ? "Rimuovi ultra-critica" : "Segna ultra-critica",
    icon: Flag,
    onClick: () => onPatch(req.id, "is_ultra_critical", !req.isUltraCritical),
  })
  actions.push({
    label: "Elimina lavorazione",
    icon: Trash2,
    destructive: true,
    separatorBefore: true,
    onClick: () => onDelete(req),
  })

  return (
    <TableRow
      data-row-id={String(req.id)}
      className={cn(
        "border-0 transition-colors",
        highlighted && "ring-2 ring-inset ring-destructive",
        req.isDelivered
          ? "bg-muted/30 opacity-70"
          : req.isUltraCritical
            ? priorityRowClass(req.priority ?? 2, true)
            : req.priority !== null
              ? priorityRowClass(req.priority, false)
              : undefined
      )}
    >
      <TableCell
        className={cn(CELL_CLASS, "text-center font-mono text-xs text-muted-foreground")}
      >
        {index + 1}
      </TableCell>

      {/* Commessa */}
      {columns.project && (
        <TableCell className={cn(CELL_CLASS, "font-semibold")}>
          {formatWorkRequestProjectLabel(req.projectCode, req.projectName)}
        </TableCell>
      )}

      {/* Data Richiesta */}
      {columns.requestDate && (
        <TableCell className={CELL_CLASS}>
          <DateField
            size="sm"
            stackedWeekday
            className="w-full min-w-0 shadow-none bg-white dark:bg-zinc-950"
            value={req.requestDate || null}
            onChange={(val) => onPatch(req.id, "request_date", val)}
          />
        </TableCell>
      )}

      {/* Descrizione lavorazione: derivata dalla DDP Officina (sola lettura,
          badge DDP) per le righe collegate; editabile al blur per le manuali. */}
      {columns.description && (
        <TableCell className={CELL_CLASS}>
          {req.ddpOfficinaItemId != null ? (
            <div className="flex items-start gap-1.5 px-2 py-1.5">
              <span
                className="mt-0.5 inline-flex shrink-0 rounded-sm bg-sky-100 px-1 py-0.5 text-[9px] font-bold uppercase tracking-wide text-sky-800 dark:bg-sky-950 dark:text-sky-300"
                title="Generata dalla DDP Officina: descrizione, data disponibilità e trattamento seguono la distinta"
              >
                DDP
              </span>
              <span
                className={cn(
                  "text-sm",
                  req.isUltraCritical && !req.isDelivered && "font-semibold text-red-800",
                  req.isDelivered && "text-muted-foreground line-through"
                )}
              >
                {req.description || "—"}
              </span>
            </div>
          ) : (
            <InlineTextarea
              value={req.description}
              placeholder="Descrizione lavorazione"
              variant="relaxed"
              className={cn(
                req.isUltraCritical && !req.isDelivered && "font-semibold text-red-800",
                req.isDelivered && "text-muted-foreground line-through"
              )}
              onCommit={(val) => onPatch(req.id, "description", val)}
            />
          )}
        </TableCell>
      )}

      {/* Tipo lavorazione (Interna/Esterna) */}
      {columns.type && (
        <TableCell className={CELL_CLASS}>
          <WrTypeSelect value={req.type} onChange={(val) => onTypeChange(req, val)} />
        </TableCell>
      )}

      {/* RDO (Richieste di Offerta) */}
      {columns.rfqs && (
        <TableCell className={CELL_CLASS}>
          {req.type === "Internal" ? (
            <span className="text-sm text-muted-foreground">—</span>
          ) : (
            <div className="flex min-w-[12rem] flex-col gap-1.5">
              {req.rfqs.length > 0 ? (
                <ul className="space-y-1.5 w-full">
                  {req.rfqs.map((q, qIdx) => (
                    <li key={qIdx} className="flex items-center w-full">
                      <span
                        className="inline-flex h-8 w-full items-center justify-between rounded-md border border-input bg-white dark:bg-zinc-950 px-2 text-sm leading-tight font-medium"
                        title={q.supplier}
                      >
                        <span className="truncate">{q.supplier}</span>
                        {q.ok ? (
                          <span
                            className="shrink-0 ml-2 font-bold text-emerald-600 text-sm"
                            title="Offerta accettata"
                          >
                            ✓
                          </span>
                        ) : null}
                      </span>
                    </li>
                  ))}
                </ul>
              ) : (
                <span className="text-sm text-muted-foreground">Nessuna offerta</span>
              )}
              <Button
                size="sm"
                variant="default"
                className="h-8 shrink-0 w-full text-sm font-medium"
                onClick={() => onOpenRfq(req)}
              >
                Gestisci RDO
              </Button>
            </div>
          )}
        </TableCell>
      )}

      {/* Dati ODA (Ordine di Acquisto) */}
      {columns.oda && (
        <TableCell className={CELL_CLASS}>
          <WrOdaCell req={req} onPatch={onPatch} />
        </TableCell>
      )}

      {/* Priorità (P0/P1/P2 o nessuna) */}
      {columns.priority && (
        <TableCell className={CELL_CLASS}>
          <WrPrioritySelect
            value={req.priority}
            onChange={(priority) => onPatch(req.id, "priority", priority)}
          />
        </TableCell>
      )}

      {/* Data Disponibilità (dalla Data prev. cons. della DDP per le collegate) */}
      {columns.availabilityDate && (
        <TableCell className={CELL_CLASS}>
          <DateField
            size="sm"
            stackedWeekday
            disabled={req.ddpOfficinaItemId != null}
            className="w-full min-w-0 shadow-none bg-white dark:bg-zinc-950"
            value={req.availabilityDate || null}
            onChange={(val) => onPatch(req.id, "availability_date", val)}
          />
        </TableCell>
      )}

      {/* Note Generali (salvate al blur) */}
      {columns.notes && (
        <TableCell className={CELL_CLASS}>
          <InlineTextarea
            value={req.notes}
            placeholder="—"
            variant="relaxed"
            onCommit={(val) => onPatch(req.id, "notes", val)}
          />
        </TableCell>
      )}

      {/* Stato consegna */}
      {columns.status && (
        <TableCell className={CELL_CLASS}>
          <WrDeliveredSelect
            value={req.isDelivered}
            onChange={(delivered) => onPatch(req.id, "is_delivered", delivered)}
          />
        </TableCell>
      )}

      {/* Richiesta Trattamenti (dal campo Trattamento della DDP per le collegate) */}
      {columns.treatment && (
        <TableCell className={CELL_CLASS}>
          <div className="space-y-1">
            {req.ddpOfficinaItemId != null ? (
              <span
                className="inline-flex px-2 text-sm font-medium"
                title="Deriva dal campo Trattamento della riga DDP Officina"
              >
                {req.hasTreatment ? "Sì" : "No"}
              </span>
            ) : (
              <WrTreatmentSelect
                value={req.hasTreatment}
                onChange={(hasTreatment) =>
                  onPatch(req.id, "has_treatment", hasTreatment)
                }
              />
            )}
            {req.hasTreatment && req.isTreatmentConfirmed ? (
              <span className="block text-[10px] font-medium text-emerald-600">
                ✓ Confermato
              </span>
            ) : null}
          </div>
        </TableCell>
      )}

      {/* Data / note trattamento: solo se Trattamento = Sì */}
      {columns.treatmentDate && (
        <TableCell className={CELL_CLASS}>
          {req.hasTreatment ? (
            <DateField
              size="sm"
              stackedWeekday
              className="w-full min-w-0 shadow-none bg-white dark:bg-zinc-950"
              value={req.treatmentDate || null}
              onChange={(val) => onPatch(req.id, "treatment_date", val)}
            />
          ) : (
            <span className="px-2 text-sm text-muted-foreground">—</span>
          )}
        </TableCell>
      )}

      {columns.treatmentNotes && (
        <TableCell className={CELL_CLASS}>
          {req.hasTreatment ? (
            <InlineTextarea
              value={req.treatmentNotes}
              placeholder="Note per il trattamento..."
              onCommit={(val) => onPatch(req.id, "treatment_notes", val)}
            />
          ) : (
            <span className="px-2 text-sm text-muted-foreground">—</span>
          )}
        </TableCell>
      )}

      {/* Azioni riga (menu ⋮) */}
      <TableCell className={cn(CELL_CLASS, "text-right")}>
        <RowActionsMenu size="icon-sm" triggerClassName="size-7" actions={actions} />
      </TableCell>
    </TableRow>
  )
}
