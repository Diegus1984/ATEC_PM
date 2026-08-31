import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Link2, Search } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
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
import { GridScroller } from "@/components/shared/grid-scroller"
import { useConfirm } from "@/components/shared/confirm"
import { fetchCatalogByAtec, fetchCatalogByCodex } from "@/lib/api/catalog"
import { fetchCodex } from "@/lib/api/codex"
import { createDdpRow } from "@/lib/api/project-ddp"
import type { CatalogItemListItem, CodexListItem } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { notifyError } from "@/lib/toast"
import { euro } from "@/lib/format"
import { useDebounced } from "@/lib/use-debounced"
import { DDP_STATUS_VERIFY } from "./ddp-constants"
import { inserisciOfficina } from "./officina-insert"

/**
 * Picker «per codice ATEC»: cerca nel Codex (codice nuovo), mostra le alternative
 * Danea del mapping e permette di (a) scegliere subito un fornitore oppure
 * (b) inserire solo il codice ATEC con fornitore da definire (stato DO).
 *
 * #142 — i 1xx non finiscono più in Commerciale: vanno in DDP Officina (specchio di
 * DdpSmistamento, con conferma), e se il 101 ha la derivazione il pannello mostra i
 * fornitori del SUO grezzo 201 — la riga del grezzo in Commerciale la genera il motore
 * #135 dentro il POST officina, qui al più si applica la scelta del fornitore.
 */
export function AtecPickerDialog({
  open,
  projectId,
  onClose,
  onAdded,
}: {
  open: boolean
  projectId: number
  onClose: () => void
  onAdded: () => void
}) {
  const requestedBy = getSession()?.user.fullName ?? ""
  const confirm = useConfirm()
  const [searchInput, setSearchInput] = React.useState("")
  const search = useDebounced(searchInput.trim(), 300)
  const [selected, setSelected] = React.useState<CodexListItem | null>(null)
  const [message, setMessage] = React.useState<string | null>(null)
  const [error, setError] = React.useState<string | null>(null)

  // #142 — smistamento per famiglia, specchio di DdpSmistamento: i 1xx sono particolari
  // d'officina. Se il 101 ha la derivazione, le alternative fornitore sono del SUO 201.
  const selectedRaw = (selected?.codiceNuovo ?? "").replace(/\./g, "")
  const isLavorato = selectedRaw.startsWith("1")
  const derivazioneCodexId = isLavorato ? selected?.refCommercialeCodexId ?? null : null
  const derivazioneCodice = isLavorato ? selected?.refCommercialeCodice ?? "" : ""

  React.useEffect(() => {
    if (open) {
      setSearchInput("")
      setSelected(null)
      setMessage(null)
      setError(null)
    }
  }, [open])

  const codexQuery = useQuery({
    queryKey: ["atec-picker-codex", search],
    queryFn: () =>
      fetchCodex({
        page: 1,
        pageSize: 40,
        search: search || undefined,
        newCodeState: "done",
      }),
    enabled: open,
  })

  const altsQuery = useQuery({
    queryKey: [
      "atec-picker-alts",
      selected?.id,
      selected?.codiceNuovo,
      derivazioneCodexId,
    ],
    queryFn: async () => {
      if (!selected) return [] as CatalogItemListItem[]
      // #142: per un 101 con derivazione le alternative sono gli articoli del SUO 201.
      if (derivazioneCodexId != null) return fetchCatalogByCodex(derivazioneCodexId)
      // 101 senza derivazione: non si compra — niente alternative da mostrare.
      if (isLavorato) return [] as CatalogItemListItem[]
      if (selected.id > 0) return fetchCatalogByCodex(selected.id)
      return fetchCatalogByAtec(selected.codiceNuovo)
    },
    enabled: open && !!selected?.codiceNuovo,
  })

  const addMutation = useMutation({
    mutationFn: async (opts: {
      atecCode: string
      description: string
      catalog?: CatalogItemListItem | null
      tbd?: boolean
    }): Promise<string | null> => {
      setError(null)

      // #142 — 1xx: riga in DDP OFFICINA (con conferma); il grezzo 201, se c'è la
      // derivazione, lo genera il motore #135 dentro il POST — qui si applica al più
      // la scelta del fornitore fatta nel pannello.
      if (isLavorato) {
        const conGrezzo = derivazioneCodice.length > 0
        const scoperto =
          conGrezzo && !altsQuery.isLoading && (altsQuery.data?.length ?? 0) === 0
        const ok = await confirm({
          title: "Particolare d'officina",
          description:
            `${opts.atecCode} è un particolare a disegno (1xx): la riga andrà nella DDP Officina.` +
            (conGrezzo
              ? `\nIl suo grezzo ${derivazioneCodice} comparirà nella DDP Commerciale` +
                (opts.catalog
                  ? ` con fornitore ${opts.catalog.supplierName || "scelto"}.`
                  : scoperto
                    ? " (da associare a un articolo commerciale)."
                    : " (fornitore da definire).")
              : "") +
            `\n\nVuoi continuare?`,
          confirmLabel: "Inserisci",
          destructive: false,
        })
        if (!ok) return null
        const esito = await inserisciOfficina({
          projectId,
          codiceAtec: opts.atecCode,
          descrizione: opts.description,
          // Il costo della riga officina è la LAVORAZIONE: il materiale sta sul grezzo.
          unitCost: 0,
          supplierName: "",
          requestedBy,
          confirm,
          notaScheda: ' — la trovi nella scheda "DDP Officina" di questa commessa',
          grezzo: conGrezzo
            ? {
                codice: derivazioneCodice,
                catalogItemId: opts.catalog?.id ?? null,
                fornitoreNome: opts.catalog?.supplierName ?? "",
                scoperto,
              }
            : null,
        })
        return esito ? `✓ ${esito.code} ${esito.testo}` : null
      }

      const cat = opts.catalog
      await createDdpRow(projectId, {
        id: 0,
        projectId,
        catalogItemId: cat?.id ?? null,
        partNumber: cat?.code ?? "",
        description: cat?.description || opts.description,
        unit: cat?.unit || "PZ",
        quantity: 1,
        unitCost: cat?.unitCost ?? 0,
        supplierId: cat?.supplierId ?? null,
        manufacturer: cat?.manufacturer ?? "",
        itemStatus: DDP_STATUS_VERIFY,
        requestedBy,
        daneaRef: "",
        dateNeeded: null,
        destination: "",
        destinationSpec: "",
        notes: opts.tbd ? "Fornitore da definire" : "",
        ddpType: "COMMERCIAL",
        atecCode: opts.atecCode,
        expectedUpdatedAt: null,
      })
      return `✓ ${opts.atecCode} aggiunto`
    },
    onSuccess: (msg) => {
      if (!msg) return
      setMessage(msg)
      onAdded()
    },
    // Oltre alla riga nel dialogo, il toast: come nel picker gemello
    // (CodexPickerDialog) gli errori bloccanti non devono passare inosservati.
    onError: (err: Error) => {
      setError(err.message)
      notifyError(err.message)
    },
  })

  const items = codexQuery.data?.items ?? []
  const alts = altsQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      {/* #128: la finestra era stretta e le due griglie finivano a scroll orizzontale —
          larghezza piena e altezza minima vera, così codici e fornitori si leggono. */}
      <DialogContent className="flex max-h-[90vh] min-h-[60vh] flex-col gap-3 overflow-hidden sm:max-w-6xl">
        <DialogHeader>
          <DialogTitle>Aggiungi per codice ATEC</DialogTitle>
          <DialogDescription>
            Cerca il codice ATEC dell&apos;articolo; se ha già fornitori
            collegati scegline uno, altrimenti inserisci la riga e il fornitore
            si deciderà dopo.
          </DialogDescription>
        </DialogHeader>

        <div className="relative">
          <Search className="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
          <Input
            value={searchInput}
            placeholder="Cerca codice nuovo, descrizione…"
            className="pl-8"
            onChange={(e) => setSearchInput(e.target.value)}
          />
        </div>

        <div className="grid min-h-0 flex-1 gap-3 md:grid-cols-[5fr_6fr]">
          <GridScroller fill className="rounded-lg border">
            <Table>
              <TableHeader className="bg-muted/50">
                <TableRow>
                  <TableHead>Cod. ATEC</TableHead>
                  <TableHead>Descrizione</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {items.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={2} className="text-center text-muted-foreground">
                      {codexQuery.isLoading
                        ? "Caricamento…"
                        : "Nessun codice nuovo trovato."}
                    </TableCell>
                  </TableRow>
                ) : (
                  items.map((item) => (
                    <TableRow
                      key={item.id}
                      className={
                        selected?.id === item.id
                          ? "cursor-pointer bg-accent"
                          : "cursor-pointer"
                      }
                      onClick={() => setSelected(item)}
                    >
                      <TableCell className="font-medium tabular-nums text-primary">
                        {item.codiceNuovo || item.codice}
                      </TableCell>
                      <TableCell className="max-w-[200px] truncate">
                        {item.descr || "—"}
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </GridScroller>

          <div className="flex min-h-0 flex-col gap-2 overflow-auto rounded-lg border p-2">
            {!selected ? (
              <p className="p-4 text-sm text-muted-foreground">
                Seleziona un codice ATEC a sinistra.
              </p>
            ) : (
              <>
                <div className="flex items-center justify-between gap-2 px-1">
                  <div>
                    <p className="font-medium tabular-nums text-primary">
                      {selected.codiceNuovo}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {selected.descr || "—"}
                    </p>
                    {derivazioneCodice ? (
                      <p className="text-xs font-medium text-amber-700 dark:text-amber-300">
                        Fornitori del grezzo {derivazioneCodice} (derivazione)
                      </p>
                    ) : null}
                  </div>
                  <Button
                    size="sm"
                    variant="outline"
                    title={
                      isLavorato
                        ? derivazioneCodice
                          ? "Riga 101 in DDP Officina; il grezzo nasce in DDP Commerciale con fornitore da definire"
                          : "Riga 101 in DDP Officina (nessun grezzo: manca la derivazione)"
                        : "La riga entra in distinta con fornitore da definire"
                    }
                    disabled={addMutation.isPending || !selected.codiceNuovo}
                    onClick={() =>
                      addMutation.mutate({
                        atecCode: selected.codiceNuovo,
                        description: selected.descr,
                        tbd: true,
                      })
                    }
                  >
                    <Link2 />
                    {isLavorato ? "Inserisci (Officina)" : "Inserisci senza fornitore"}
                  </Button>
                </div>
                <GridScroller className="rounded-md border">
                <Table>
                  <TableHeader className="bg-muted/50">
                    <TableRow>
                      <TableHead>Fornitore</TableHead>
                      <TableHead>Codice</TableHead>
                      <TableHead>Produttore</TableHead>
                      <TableHead className="text-right">Costo</TableHead>
                      <TableHead />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {altsQuery.isLoading ? (
                      <TableRow>
                        <TableCell colSpan={5} className="text-muted-foreground">
                          Caricamento alternative…
                        </TableCell>
                      </TableRow>
                    ) : alts.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={5} className="text-muted-foreground">
                          {isLavorato && derivazioneCodice
                            ? `Il grezzo ${derivazioneCodice} non è associato a nessun articolo commerciale: se inserisci, la riga del grezzo resterà BLOCCATA (bordo lampeggiante) finché non associ l'articolo (Codex → Articoli Danea, icona catena).`
                            : isLavorato
                              ? "Particolare a disegno senza derivazione: la riga va in DDP Officina e qui non servono fornitori. Se questo 101 si ricava da un commerciale, compila la derivazione nella scheda Codex."
                              : "Questo codice non ha ancora fornitori collegati. Puoi inserire la riga senza fornitore, oppure collegare gli articoli dei fornitori dal Catalogo (icona catena)."}
                        </TableCell>
                      </TableRow>
                    ) : (
                      alts.map((alt) => (
                        <TableRow key={alt.id}>
                          <TableCell className="max-w-[180px] truncate">
                            {alt.supplierName || "—"}
                          </TableCell>
                          <TableCell className="font-medium">{alt.code}</TableCell>
                          <TableCell className="max-w-[140px] truncate">
                            {alt.manufacturer || "—"}
                          </TableCell>
                          <TableCell className="text-right tabular-nums">
                            {euro(alt.unitCost)}
                          </TableCell>
                          <TableCell className="text-right">
                            <Button
                              size="sm"
                              disabled={addMutation.isPending}
                              onClick={() =>
                                addMutation.mutate({
                                  atecCode: selected.codiceNuovo,
                                  description: selected.descr,
                                  catalog: alt,
                                })
                              }
                            >
                              Scegli
                            </Button>
                          </TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
                </GridScroller>
              </>
            )}
          </div>
        </div>

        {message ? <p className="text-sm text-green-700">{message}</p> : null}
        {error ? <p className="text-sm text-destructive">{error}</p> : null}

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Chiudi
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
