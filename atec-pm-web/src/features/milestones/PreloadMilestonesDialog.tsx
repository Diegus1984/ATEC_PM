import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { CheckSquare, ListChecks, Square } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  fetchActiveActivityCatalog,
  seedMilestonesFromCatalog,
} from "@/lib/api/milestones"
import { notifyError, notifySuccess } from "@/lib/toast"

interface PreloadMilestonesDialogProps {
  open: boolean
  projectId: number
  onClose: () => void
  onPreloaded: () => void
}

export function PreloadMilestonesDialog({
  open,
  projectId,
  onClose,
  onPreloaded,
}: PreloadMilestonesDialogProps) {
  const [selectedIds, setSelectedIds] = React.useState<Set<number>>(new Set())

  const query = useQuery({
    queryKey: ["activity-catalog", "active"],
    queryFn: fetchActiveActivityCatalog,
    enabled: open,
  })

  // Popola la selezione iniziale con tutti gli item attivi
  React.useEffect(() => {
    if (query.data && query.data.length > 0) {
      setSelectedIds(new Set(query.data.map((item) => item.id)))
    }
  }, [query.data])

  const items = React.useMemo(() => query.data ?? [], [query.data])

  const toggleAll = (select: boolean) => {
    if (select) {
      setSelectedIds(new Set(items.map((i) => i.id)))
    } else {
      setSelectedIds(new Set())
    }
  }

  const toggleItem = (id: number) => {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  const seedMutation = useMutation({
    mutationFn: () => seedMilestonesFromCatalog(projectId, Array.from(selectedIds)),
    onSuccess: (count) => {
      notifySuccess(`${count} milestone precaricate con successo.`)
      onPreloaded()
      onClose()
    },
    onError: (err) => {
      notifyError(err, "Impossibile precaricare le milestone dal catalogo")
    },
  })

  const allSelected = items.length > 0 && selectedIds.size === items.length

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <ListChecks className="size-5 text-primary" />
            Precarica milestone da catalogo
          </DialogTitle>
          <DialogDescription>
            Seleziona le attività standard dall'anagrafica da aggiungere come
            milestone per questa commessa.
          </DialogDescription>
        </DialogHeader>

        {query.isLoading ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            Caricamento attività da catalogo…
          </p>
        ) : query.isError ? (
          <p className="py-6 text-center text-sm text-destructive">
            Errore durante il caricamento del catalogo attività.
          </p>
        ) : items.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            Nessuna attività disponibile nell'anagrafica catalogo.
          </p>
        ) : (
          <div className="space-y-3">
            <div className="flex items-center justify-between border-b pb-2">
              <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Voci disponibili ({selectedIds.size} di {items.length} selezionate)
              </span>
              <div className="flex items-center gap-2">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-7 text-xs"
                  onClick={() => toggleAll(!allSelected)}
                >
                  {allSelected ? (
                    <>
                      <Square className="mr-1 size-3.5" /> Deseleziona tutti
                    </>
                  ) : (
                    <>
                      <CheckSquare className="mr-1 size-3.5" /> Seleziona tutti
                    </>
                  )}
                </Button>
              </div>
            </div>

            <div className="max-h-72 overflow-y-auto space-y-1 pr-1">
              {items.map((item) => {
                const checked = selectedIds.has(item.id)
                return (
                  <div
                    key={item.id}
                    className="flex items-center gap-3 rounded-md px-2.5 py-1.5 hover:bg-accent/50 cursor-pointer select-none transition-colors"
                    onClick={() => toggleItem(item.id)}
                  >
                    <Checkbox
                      checked={checked}
                      onCheckedChange={() => toggleItem(item.id)}
                      onClick={(e) => e.stopPropagation()}
                    />
                    <span className="text-sm font-medium leading-none">
                      {item.label}
                    </span>
                  </div>
                )
              })}
            </div>
          </div>
        )}

        <DialogFooter className="gap-2 sm:gap-0">
          <Button type="button" variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            type="button"
            disabled={selectedIds.size === 0 || seedMutation.isPending}
            onClick={() => seedMutation.mutate()}
          >
            {seedMutation.isPending ? "Precaricamento…" : `Precarica (${selectedIds.size})`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
