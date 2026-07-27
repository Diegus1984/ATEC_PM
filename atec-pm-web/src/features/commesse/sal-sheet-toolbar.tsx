// ── Testata del foglio SAL: dati commessa, avanzamento incasso, comandi ────

import { RefreshCw } from "lucide-react"

import { ColumnsMenu } from "@/components/shared/columns-menu"
import { Button } from "@/components/ui/button"
import { CardHeader } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { SalIncassoProgress } from "@/features/sal/SalIncassoProgress"
import type { SalHeaderSaveRequest } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { Stat } from "./sal-sheet-fields"

export function SalSheetToolbar({
  projectCode,
  projectTitle,
  customerName,
  po,
  setPo,
  rifOfferta,
  setRifOfferta,
  valore,
  setValore,
  onSaveHeader,
  canSeeEconomics,
  totalPerc,
  paidPerc,
  columnToggles,
  onSeedTemplate,
  seedDisabled,
  onRefresh,
  isFetching,
}: {
  projectCode?: string
  projectTitle?: string
  customerName: string
  po: string
  setPo: (v: string) => void
  rifOfferta: string
  setRifOfferta: (v: string) => void
  valore: string
  setValore: (v: string) => void
  onSaveHeader: (fields: Partial<SalHeaderSaveRequest>) => void
  canSeeEconomics: boolean
  totalPerc: number
  paidPerc: number
  columnToggles: {
    id: string
    label: string
    checked: boolean
    onToggle: (checked: boolean) => void
  }[]
  onSeedTemplate: () => void
  seedDisabled: boolean
  onRefresh: () => void
  isFetching: boolean
}) {
  return (
    <CardHeader className="flex flex-row flex-wrap items-center gap-6 border-b bg-muted/30 py-3">
      <div className="flex flex-wrap items-center gap-4">
        {projectCode ? (
          <Stat label="N° Commessa">
            <span className="text-sm font-bold bg-muted px-1.5 py-0.5 rounded border">
              {projectCode}
            </span>
          </Stat>
        ) : null}
        {projectTitle ? (
          <Stat label="Descrizione">
            <span className="block max-w-56 truncate" title={projectTitle}>
              {projectTitle}
            </span>
          </Stat>
        ) : null}
        <div className="flex flex-col gap-1">
          <label className="text-[10px] uppercase font-bold tracking-wider text-muted-foreground">
            Cliente
          </label>
          <div
            className="flex h-8 w-56 items-center rounded-md border border-input bg-muted/40 px-2.5 text-sm font-medium text-foreground truncate"
            title={customerName}
          >
            {customerName}
          </div>
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-[10px] uppercase font-bold tracking-wider text-muted-foreground">
            PO - Ordine
          </label>
          <Input
            value={po}
            onChange={(e) => setPo(e.target.value)}
            onBlur={() => onSaveHeader({ po })}
            onKeyDown={(e) => {
              if (e.key === "Enter") e.currentTarget.blur()
            }}
            placeholder="PO cliente..."
            className="h-8 w-40 shadow-none"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-[10px] uppercase font-bold tracking-wider text-muted-foreground">
            Riferimento Offerta
          </label>
          <Input
            value={rifOfferta}
            onChange={(e) => setRifOfferta(e.target.value)}
            onBlur={() => onSaveHeader({ rifOfferta })}
            onKeyDown={(e) => {
              if (e.key === "Enter") e.currentTarget.blur()
            }}
            placeholder="Offerta ATEC..."
            className="h-8 w-44 shadow-none"
          />
        </div>
        {canSeeEconomics && (
          <div className="flex flex-col gap-1">
            <label className="text-[10px] uppercase font-bold tracking-wider text-muted-foreground">
              Importo Ordine (€)
            </label>
            <Input
              type="number"
              value={valore}
              onChange={(e) => setValore(e.target.value)}
              onBlur={() =>
                onSaveHeader({ valore: valore === "" ? null : Number(valore) })
              }
              onKeyDown={(e) => {
                if (e.key === "Enter") e.currentTarget.blur()
              }}
              placeholder="Valore (€)..."
              className="h-8 w-40 text-sm text-right shadow-none"
            />
          </div>
        )}
      </div>

      <div className="flex flex-row gap-6 items-center">
        <SalIncassoProgress percTotal={totalPerc} percPaid={paidPerc} />
      </div>

      <div className="ml-auto flex items-center gap-2">
        <ColumnsMenu columns={columnToggles} />
        {canSeeEconomics && (
          <Button
            variant="outline"
            size="sm"
            onClick={onSeedTemplate}
            disabled={seedDisabled}
            title="Precarica i 6 step SAL standard (15/15/10/20/20/20)"
          >
            Precarica modello standard
          </Button>
        )}
        <Button variant="outline" size="sm" onClick={onRefresh} disabled={isFetching}>
          <RefreshCw className={cn("size-3.5 mr-1.5", isFetching && "animate-spin")} />
          Aggiorna
        </Button>
      </div>
    </CardHeader>
  )
}
