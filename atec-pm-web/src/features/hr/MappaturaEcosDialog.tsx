import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Link2, RefreshCw } from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { GridScroller } from "@/components/shared/grid-scroller"
import {
  LookupCombobox,
  type LookupComboboxOption,
} from "@/components/shared/lookup-combobox"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  fetchHrBadges,
  fetchHrMapping,
  importHrPunches,
  saveHrMapping,
} from "@/lib/api/hr"
import { notifyError, notifySuccess } from "@/lib/toast"

/**
 * Mappatura dipendenti ↔ codici Ecos (`employees.ecos_empl_code`): senza questo ponte le
 * timbrature non sanno di chi sono. I suggerimenti arrivano VIVI dai badge Ecos; se le
 * credenziali non sono configurate sul server, il codice si scrive a mano.
 *
 * Dopo aver collegato una persona nuova serve il reimport completo: le sue timbrature
 * passate erano state scartate come «non abbinate» e il cursore è già avanti.
 */
export function MappaturaEcosDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const mappaturaQuery = useQuery({
    queryKey: ["hr-mapping"],
    queryFn: fetchHrMapping,
    enabled: open,
  })
  const badgesQuery = useQuery({
    queryKey: ["hr-badges"],
    queryFn: fetchHrBadges,
    enabled: open,
  })

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["hr-mapping"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-status"] })
    void queryClient.invalidateQueries({ queryKey: ["hr-timesheet"] })
  }

  const salva = useMutation({
    mutationFn: ({ employeeId, codice }: { employeeId: number; codice: string | null }) =>
      saveHrMapping(employeeId, codice),
    onSuccess: () => {
      notifySuccess("Mappatura aggiornata")
      invalidate()
    },
    onError: (e) => {
      notifyError((e as Error).message)
      invalidate()
    },
  })

  const reimporta = useMutation({
    mutationFn: () => importHrPunches(true),
    onSuccess: (esito) => {
      notifySuccess(esito.message)
      invalidate()
    },
    onError: (e) => notifyError((e as Error).message),
  })

  /**
   * Il reimport completo non è un semplice «aggiorna»: riallinea lo storico alla
   * mappatura di ADESSO. Se un codice è stato spostato di persona, anni di presenze
   * cambiano proprietario; e le timbrature cancellate su Ecos vengono tolte anche qui.
   * Merita una conferma, non un clic distratto.
   */
  async function chiediEReimporta() {
    const ok = await confirm({
      title: "Reimportare tutto lo storico?",
      description:
        "Le timbrature verranno riassegnate secondo i collegamenti attuali e quelle nel frattempo cancellate su Ecos verranno tolte anche qui. Controlla che i codici in elenco siano quelli giusti.",
      confirmLabel: "Reimporta",
      destructive: false,
    })
    if (ok) reimporta.mutate()
  }

  const configurato = badgesQuery.data?.configured ?? false
  // 🪤 «Non configurato» e «Ecos non ha risposto» sono due cose diverse: con l'errore
  // travestito da credenziali mancanti si finiva a digitare i codici a mano — e un codice
  // digitato storto fa scartare le timbrature di quella persona a ogni import.
  const erroreBadge = badgesQuery.error as Error | null

  const opzioniBadge: LookupComboboxOption<string>[] = React.useMemo(() => {
    const dalVivo = (badgesQuery.data?.badges ?? [])
      .filter((b) => b.isActive)
      .map((b) => ({
        id: b.emplCode,
        name: `${b.emplCode} — ${b.name}`,
      }))
    // I codici già salvati che Ecos non elenca più (persona rimossa di là, codice messo
    // a mano) devono restare visibili: senza, la riga sembrerebbe scollegata.
    const noti = new Set(dalVivo.map((o) => o.id))
    const orfani = (mappaturaQuery.data ?? [])
      .map((r) => r.ecosEmplCode)
      .filter((codice): codice is string => !!codice && !noti.has(codice))
      .map((codice) => ({
        id: codice,
        name: codice,
        hint: "Non presente fra i badge Ecos in forza",
      }))
    const map = new Map<string, LookupComboboxOption<string>>()
    for (const opt of [...dalVivo, ...orfani]) {
      if (!map.has(opt.id)) {
        map.set(opt.id, opt)
      }
    }
    return Array.from(map.values())
  }, [badgesQuery.data, mappaturaQuery.data])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Link2 className="size-4" />
            Collega i dipendenti a Ecos
          </DialogTitle>
          <DialogDescription>
            Il codice Ecos (EmplCode) dice a chi appartengono le timbrature.
            {!configurato &&
              !badgesQuery.isLoading &&
              !erroreBadge &&
              " Credenziali Ecos non configurate sul server: il codice si inserisce a mano."}
          </DialogDescription>
        </DialogHeader>

        {erroreBadge && (
          <p className="text-sm text-destructive">
            Badge non caricati da Ecos: {erroreBadge.message}. I codici si possono
            inserire a mano, ma controllali sul portale prima di salvare.
          </p>
        )}

        {mappaturaQuery.isLoading ? (
          <p className="text-sm text-muted-foreground">Caricamento…</p>
        ) : mappaturaQuery.error ? (
          <p className="text-sm text-destructive">
            {(mappaturaQuery.error as Error).message}
          </p>
        ) : (
          <GridScroller className="rounded-lg border" scrollerClassName="max-h-[50vh]">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Dipendente</TableHead>
                  <TableHead className="w-[260px]">Codice Ecos</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(mappaturaQuery.data ?? []).map((riga) => (
                  <TableRow key={riga.employeeId}>
                    <TableCell>{riga.name}</TableCell>
                    <TableCell>
                      {configurato ? (
                        <LookupCombobox<string>
                          options={opzioniBadge}
                          value={riga.ecosEmplCode ?? null}
                          onValueChange={(codice) =>
                            salva.mutate({ employeeId: riga.employeeId, codice })
                          }
                          placeholder="—"
                          noneLabel="— scollega —"
                          disabled={salva.isPending}
                          className="w-full"
                        />
                      ) : (
                        <CodiceAManoInput
                          valore={riga.ecosEmplCode ?? ""}
                          inCorso={salva.isPending}
                          onSalva={(codice) =>
                            salva.mutate({
                              employeeId: riga.employeeId,
                              codice: codice.trim() || null,
                            })
                          }
                        />
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </GridScroller>
        )}

        <div className="flex items-center justify-between gap-2">
          <p className="text-xs text-muted-foreground">
            Hai collegato qualcuno di nuovo? Le sue timbrature passate erano state
            scartate: serve il reimport completo.
          </p>
          <Button
            variant="outline"
            size="sm"
            disabled={reimporta.isPending}
            onClick={() => void chiediEReimporta()}
          >
            <RefreshCw className="mr-1 size-3.5" />
            {reimporta.isPending ? "Reimport in corso…" : "Reimporta tutto"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  )
}

/** Input col salvataggio esplicito: senza tendina badge non c'è un evento «scelta». */
function CodiceAManoInput({
  valore,
  inCorso,
  onSalva,
}: {
  valore: string
  inCorso: boolean
  onSalva: (codice: string) => void
}) {
  const [testo, setTesto] = React.useState(valore)
  React.useEffect(() => setTesto(valore), [valore])
  return (
    <div className="flex items-center gap-1">
      <Input
        value={testo}
        onChange={(e) => setTesto(e.target.value)}
        placeholder="EmplCode"
        className="h-8"
      />
      <Button
        variant="outline"
        size="sm"
        disabled={inCorso || testo.trim() === (valore ?? "").trim()}
        onClick={() => onSalva(testo)}
      >
        Salva
      </Button>
    </div>
  )
}
