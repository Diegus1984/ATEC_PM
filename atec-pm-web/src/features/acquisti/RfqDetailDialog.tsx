// ── Dialog dettaglio RDO: offerte, prezzi, vincitore, email, ordine Danea ──

import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"
import { Ban, Building2, CheckCircle2, FileCheck2, Package, Send } from "lucide-react"

import { CatalogItemPickerDialog } from "@/components/shared/catalog-item-picker-dialog"
import { useConfirm } from "@/components/shared/confirm"
import { DateField } from "@/components/shared/date-field"
import { Badge } from "@/components/ui/badge"
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
import { Label } from "@/components/ui/label"
import {
  cancelPurchaseRfq,
  createPurchaseRfqDaneaOrder,
  fetchPurchaseRfq,
  markPurchaseRfqEmailed,
  savePurchaseRfqOffer,
  selectPurchaseRfqWinner,
} from "@/lib/api/purchase-rfqs"
import type {
  CatalogItemListItem,
  PurchaseRfqDetail,
  PurchaseRfqOfferDto,
} from "@/lib/api/types"
import { formatDateShort } from "@/lib/date-iso"
import { euro } from "@/lib/format"
import { notifyError, notifyInfo } from "@/lib/toast"
import { cn } from "@/lib/utils"

import { DaneaOrderBadge } from "./acquisti-ui"
import { rfqDaneaOrder } from "./acquisti-shared"

/** Windows tronca (o ignora del tutto) le URL mailto troppo lunghe, e lo fa in
 *  silenzio: la richiesta offerta partirebbe con la lista articoli tagliata a
 *  metà. Oltre questa soglia si preferisce non aprire affatto la mail. */
const MAILTO_MAX_URL = 1800

export function RfqDetailDialog({
  rfqId,
  onClose,
  onChanged,
  onUpdateRow,
  onOpenDaneaOrder,
}: {
  rfqId: number | null
  onClose: () => void
  /** Invalidazione delle liste di pagina (inbox + elenco RDO). */
  onChanged: () => void
  /** Scrive su una riga della distinta (costo unitario / data prevista). */
  onUpdateRow: (data: {
    projectId: number
    rowId: number
    unitCost?: number
    dateNeeded?: string | null
  }) => void
  onOpenDaneaOrder: (idDoc: number) => void
}) {
  const confirm = useConfirm()
  const [expectedDate, setExpectedDate] = React.useState<string | null>(null)
  // Picker articolo Danea: offerta di cui si sta scegliendo l'articolo di catalogo.
  const [offerPickerTarget, setOfferPickerTarget] =
    React.useState<PurchaseRfqOfferDto | null>(null)

  const { data: rfqDetail, refetch: refetchRfqDetail } = useQuery({
    queryKey: ["purchase-rfq-detail", rfqId],
    queryFn: () => (rfqId ? fetchPurchaseRfq(rfqId) : null),
    enabled: rfqId != null,
  })

  // Ordine Danea della RDO aperta: un solo punto di verità per badge e pulsante ODA.
  const rfqOrder = React.useMemo(
    () => (rfqDetail ? rfqDaneaOrder(rfqDetail) : null),
    [rfqDetail]
  )

  /** RDO ancora lavorabile: né aggiudicata/chiusa né annullata. Governa email,
   *  annullamento e scelta del vincitore (il server rifiuta le chiuse). */
  const rfqIsOpen =
    !!rfqDetail && rfqDetail.status !== "CLOSED" && rfqDetail.status !== "CANCELLED"

  // Prezzo di ripiego per l'aggiudicazione: primo costo di riga valorizzato della
  // RDO. Uguale per tutte le offerte, quindi calcolato una volta sola.
  const rfqFallbackUnitCost = React.useMemo(
    () => rfqDetail?.items.find((it) => (it.unitCost ?? 0) > 0)?.unitCost,
    [rfqDetail]
  )

  // Il dialog è montato una volta a livello pagina: la data consegna va azzerata
  // al cambio RDO, altrimenti una consegna prevista stantia finisce nell'ordine reale.
  React.useEffect(() => {
    setExpectedDate(null)
  }, [rfqId])

  // Strada B: ordine fornitore (singola RDO) scritto direttamente in Danea (Atec_PM).
  const createOrderMutation = useMutation({
    mutationFn: (id: number) => createPurchaseRfqDaneaOrder(id, expectedDate),
    onSuccess: (updated) => {
      notifyInfo(`Ordine fornitore n. ${updated.daneaOrderNum} creato in Danea (Atec_PM)`)
      void refetchRfqDetail()
      onChanged()
    },
    onError: (err: Error) => notifyError(err.message),
  })

  // Annullamento RDO: il server lo rifiuta sulle RDO già chiuse (con ordine), quindi
  // il pulsante compare solo su DRAFT/SENT. Le righe distinta non vengono toccate.
  const cancelRfqMutation = useMutation({
    mutationFn: (id: number) => cancelPurchaseRfq(id),
    onSuccess: () => {
      notifyInfo("RDO annullata")
      onChanged()
      onClose()
    },
    onError: (err: Error) => notifyError(err.message),
  })

  /**
   * Salvataggio offerta con TUTTI i campi. Il PUT server sovrascrive senza COALESCE:
   * mandare solo il prezzo azzerava `catalogItemId` (l'articolo Danea), e senza quello
   * la generazione dell'ordine fallisce. Unico punto di salvataggio delle offerte.
   */
  const saveOffer = React.useCallback(
    (
      id: number,
      offer: PurchaseRfqOfferDto,
      patch: Partial<{
        catalogItemId: number | null
        unitPrice: number | null
        validUntil: string | null
        notes: string
      }>
    ) =>
      savePurchaseRfqOffer(id, offer.id, {
        supplierId: offer.supplierId,
        catalogItemId: offer.catalogItemId,
        unitPrice: offer.unitPrice,
        validUntil: offer.validUntil,
        notes: offer.notes ?? "",
        ...patch,
      }),
    []
  )

  const handlePickCatalogItem = async (item: CatalogItemListItem) => {
    if (!offerPickerTarget || !rfqDetail) return
    try {
      await saveOffer(rfqDetail.id, offerPickerTarget, { catalogItemId: item.id })
      await refetchRfqDetail()
      onChanged()
      notifyInfo(
        `Articolo ${item.code} collegato all'offerta ${offerPickerTarget.supplierName}`
      )
    } catch (err) {
      notifyError(err instanceof Error ? err.message : String(err))
    } finally {
      setOfferPickerTarget(null)
    }
  }

  const handleCancelRfq = async (detail: PurchaseRfqDetail) => {
    const ok = await confirm({
      title: "Annullare la RDO?",
      description:
        `La RDO #${detail.id} passa in Annullata e non sarà più modificabile. ` +
        "Le righe distinta non vengono toccate: restano da ordinare e potrai rimetterle in gara.",
      confirmLabel: "Annulla RDO",
      destructive: true,
    })
    if (ok) cancelRfqMutation.mutate(detail.id)
  }

  const handleCreateSingleOrder = async (detail: PurchaseRfqDetail) => {
    const winner = detail.offers.find((o) => o.isWinner)
    const qty = detail.items.reduce((sum, i) => sum + i.quantity, 0)
    const ok = await confirm({
      title: "Generare l'ordine fornitore in Danea?",
      description:
        `Crea in Atec_PM l'ordine per ${winner?.supplierName ?? "—"}: ` +
        `${qty} × ${winner?.catalogCode || detail.atecCode} a ${euro(winner?.unitPrice ?? 0)} + IVA` +
        (expectedDate ? `, consegna prevista ${formatDateShort(expectedDate)}` : "") +
        ". Le righe distinta passano a In ordine.",
      confirmLabel: "Genera ordine",
    })
    if (ok) createOrderMutation.mutate(detail.id)
  }

  // Richiesta offerta: una mailto per fornitore in gara (la mailto non trasporta
  // HTML, quindi la lista articoli è testo semplice). Outlook si apre già
  // compilato — destinatario, oggetto e corpo — e l'utente controlla e invia.
  const handleSendMailto = async (detail: PurchaseRfqDetail) => {
    if (!detail || !detail.offers || detail.offers.length === 0) {
      notifyError("Nessun fornitore in gara per questa RDO.")
      return
    }

    // Si apre Outlook per OGNI fornitore in gara: se l'email non è in anagrafica
    // il destinatario resta vuoto e lo mette l'ufficio acquisti prima di inviare.
    const itemsText = detail.items
      .map(
        (it) =>
          `• ${it.description}\n  - Codice Fornitore: ${it.partNumber || "N.D."}\n  - Riferimento ATEC: ${detail.atecCode || "N.D."}\n  - Quantità: ${it.quantity}`
      )
      .join("\n\n")

    // Offerte per cui la mail è stata davvero aperta: solo queste vanno marcate
    // come contattate (una RDO mai partita non deve risultare inviata).
    const opened: PurchaseRfqOfferDto[] = []
    for (const offer of detail.offers) {
      const subject = `Richiesta Offerta — Commessa ${detail.projectCode}`
      const body = `Gentile ${offer.supplierName},\n\nvi chiediamo la vs. migliore offerta per la Commessa ${detail.projectCode}:\n\nArticoli richiesti:\n${itemsText}\n\n${detail.notes ? `Note aggiuntive:\n${detail.notes}\n\n` : ""}In attesa di un vostro riscontro, porgiamo cordiali saluti.\nATEC PM`

      const to = offer.supplierEmail?.trim()
        ? encodeURIComponent(offer.supplierEmail.trim())
        : ""
      const mailtoUrl = `mailto:${to}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`

      // Meglio nessuna mail che una mail troncata a metà lista articoli.
      if (mailtoUrl.length > MAILTO_MAX_URL) continue

      const position = opened.length
      if (position === 0) window.open(mailtoUrl, "_self")
      else setTimeout(() => window.open(mailtoUrl, "_self"), position * 900)
      opened.push(offer)
    }

    // Il corpo è lo stesso per tutti i fornitori: se sfora, sfora per tutti.
    if (opened.length === 0) {
      notifyError(
        "Richiesta troppo lunga per l'apertura automatica di Outlook: Windows la " +
          "troncherebbe. Riduci le note della RDO oppure spezzala in più RDO con " +
          "meno articoli. Nessuna email è stata aperta e la RDO resta da inviare."
      )
      return
    }

    // Registra l'invio nel backend e avanza lo stato a SENT
    await markPurchaseRfqEmailed(opened.map((o) => o.id))

    onChanged()
    refetchRfqDetail()

    const missing = opened.filter((o) => !o.supplierEmail?.trim()).length
    notifyInfo(
      missing > 0
        ? `Email aperte in Outlook (${missing} senza destinatario: da inserire a mano).`
        : "Email aperta in Outlook con la lista articoli nel testo."
    )
  }

  return (
    <Dialog open={!!rfqId} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-6xl sm:w-[1150px] max-w-[95vw]">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-lg font-bold">
            <FileCheck2 className="h-5 w-5 text-primary" />
            Dettaglio RDO #{rfqId}
          </DialogTitle>
          <DialogDescription className="text-xs">
            Gestisci le offerte dei fornitori e assegna l'offerta vincente.
          </DialogDescription>
        </DialogHeader>

        {rfqDetail && (
          <div className="space-y-4 py-2 text-xs max-h-[75vh] overflow-y-auto pr-1">
            <div className="bg-muted/40 p-3 rounded border space-y-2">
              <div className="flex items-center justify-between">
                <div>
                  <div className="font-bold text-sm text-foreground">
                    RDO #{rfqDetail.id} — Commessa {rfqDetail.projectCode}
                  </div>
                  <div className="text-muted-foreground text-xs">
                    Codice ATEC:{" "}
                    <span className="font-mono font-semibold">{rfqDetail.atecCode}</span> —{" "}
                    {rfqDetail.description}
                  </div>
                </div>
                <Badge variant="outline">{rfqDetail.status}</Badge>
              </div>
              {rfqDetail.items && rfqDetail.items.length > 0 && (
                <div className="pt-2 border-t space-y-1">
                  <Label className="text-[11px] font-semibold text-muted-foreground uppercase">
                    Articoli inclusi in questa gara ({rfqDetail.items.length})
                  </Label>
                  <div className="space-y-1 max-h-32 overflow-y-auto pr-1">
                    {rfqDetail.items.map((it) => (
                      <div
                        key={it.id}
                        className="flex items-center justify-between text-xs py-0.5 border-b last:border-0"
                      >
                        <span className="font-medium text-foreground">
                          {it.description}{" "}
                          <span className="font-mono text-muted-foreground">
                            ({it.partNumber || "Cod. N.D."})
                          </span>
                        </span>
                        <Badge variant="secondary" className="font-mono text-[10px]">
                          {it.quantity} pz
                        </Badge>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>

            <div>
              <Label className="font-semibold text-foreground uppercase">
                Offerte Ricevute dai Fornitori
              </Label>
              {rfqDetail.offers.length === 0 ? (
                <div className="text-xs text-muted-foreground p-4 border rounded text-center mt-1">
                  Nessun fornitore contattato per questa RDO.
                </div>
              ) : (
                <div className="space-y-3 mt-1">
                  {rfqDetail.offers.map((offer) => {
                    const effectivePrice = offer.unitPrice ?? rfqFallbackUnitCost
                    const hasPrice = effectivePrice != null && effectivePrice > 0
                    return (
                      <div
                        key={offer.id}
                        className={cn(
                          "p-3 border rounded-lg flex flex-col gap-2 bg-card",
                          offer.isWinner && "border-green-500 bg-green-50/50 dark:bg-green-950/20"
                        )}
                      >
                        <div className="flex items-start justify-between">
                          <div className="space-y-1">
                            <div className="flex items-center gap-2">
                              <Building2 className="h-4 w-4 text-muted-foreground" />
                              <span className="font-bold text-sm">{offer.supplierName}</span>
                              {offer.isWinner && (
                                <Badge className="bg-green-600 text-white text-[10px] gap-1">
                                  <CheckCircle2 className="h-3 w-3" /> VINCITORE
                                </Badge>
                              )}
                            </div>

                            {/* Articolo Danea dell'offerta: senza questo l'ordine fornitore
                                non può essere generato, quindi la mancanza è evidenziata. */}
                            <div className="flex items-center gap-1.5 pl-6 text-[11px]">
                              <Package className="h-3 w-3 text-muted-foreground" />
                              {offer.catalogItemId ? (
                                <span className="text-muted-foreground">
                                  Articolo Danea:{" "}
                                  <span className="font-mono font-semibold text-foreground">
                                    {offer.catalogCode || `#${offer.catalogItemId}`}
                                  </span>
                                </span>
                              ) : (
                                <span className="font-medium text-amber-700 dark:text-amber-500">
                                  Nessun articolo Danea — l'ordine non può partire
                                </span>
                              )}
                              <Button
                                size="sm"
                                variant="ghost"
                                className="h-5 px-1.5 text-[11px] underline-offset-2 hover:underline"
                                onClick={() => setOfferPickerTarget(offer)}
                              >
                                {offer.catalogItemId ? "Cambia" : "Collega articolo"}
                              </Button>
                            </div>
                          </div>

                          {!offer.isWinner && (
                            <Button
                              size="sm"
                              variant="outline"
                              disabled={!hasPrice || !rfqIsOpen}
                              title={
                                !rfqIsOpen
                                  ? "RDO non più aperta"
                                  : !hasPrice
                                    ? "Inserisci prima il prezzo dell'offerta"
                                    : undefined
                              }
                              className="h-7 text-xs text-green-700 border-green-300 hover:bg-green-100 disabled:opacity-50"
                              onClick={async () => {
                                if (!hasPrice) {
                                  notifyError(
                                    "Inserisci prima il prezzo dell'offerta, poi scegli il vincitore."
                                  )
                                  return
                                }
                                if (
                                  await confirm({
                                    title: "Assegna Vincitore",
                                    description: `Confermi la scelta di ${offer.supplierName} come vincitore?`,
                                  })
                                ) {
                                  try {
                                    if (offer.unitPrice == null && effectivePrice) {
                                      await saveOffer(rfqDetail.id, offer, {
                                        unitPrice: effectivePrice,
                                      })
                                    }
                                    await selectPurchaseRfqWinner(rfqDetail.id, offer.id)
                                    await refetchRfqDetail()
                                    onChanged()
                                    notifyInfo(`Offerta aggiudicata a ${offer.supplierName}`)
                                  } catch (err) {
                                    notifyError(
                                      err instanceof Error ? err.message : String(err)
                                    )
                                  }
                                }
                              }}
                            >
                              Scegli Vincitore
                            </Button>
                          )}
                        </div>

                        <div className="space-y-3 pt-2 border-t">
                          <div className="space-y-2">
                            <div className="grid grid-cols-12 gap-3 text-[11px] font-semibold text-muted-foreground uppercase px-1">
                              <div className="col-span-5">Articolo / Descrizione & Codice</div>
                              <div className="col-span-1 text-center">Qtà</div>
                              <div className="col-span-3 text-center">Prezzo Unitario €</div>
                              <div className="col-span-3 text-center">Data Prev. Consegna</div>
                            </div>
                            <div className="space-y-2">
                              {rfqDetail.items.map((item) => (
                                <div
                                  key={item.id}
                                  className="grid grid-cols-12 gap-3 items-center p-3 border rounded-lg bg-muted/20 text-xs"
                                >
                                  <div className="col-span-5 min-w-0">
                                    <div className="font-semibold text-foreground text-xs whitespace-normal break-words">
                                      {item.description}
                                    </div>
                                    <div className="text-[11px] text-muted-foreground font-mono mt-0.5">
                                      Cod. Fornitore:{" "}
                                      <span className="text-foreground font-medium">
                                        {item.partNumber || "N.D."}
                                      </span>
                                    </div>
                                  </div>
                                  <div className="col-span-1 text-center font-mono font-bold text-sm text-foreground">
                                    {item.quantity}
                                  </div>
                                  <div className="col-span-3">
                                    <div className="relative">
                                      <Input
                                        type="number"
                                        step="0.01"
                                        defaultValue={item.unitCost ?? ""}
                                        placeholder="0.00"
                                        className="h-9 text-xs font-mono bg-background text-right pr-6"
                                        onBlur={(e) => {
                                          const val = parseFloat(e.target.value)
                                          if (!isNaN(val)) {
                                            onUpdateRow({
                                              projectId: item.projectId,
                                              rowId: item.bomItemId,
                                              unitCost: val,
                                            })
                                            saveOffer(rfqDetail.id, offer, {
                                              unitPrice: val,
                                            }).then(() => refetchRfqDetail())
                                          }
                                        }}
                                      />
                                      <span className="absolute right-2.5 top-2.5 text-xs text-muted-foreground font-mono">
                                        €
                                      </span>
                                    </div>
                                  </div>
                                  <div className="col-span-3">
                                    <DateField
                                      value={item.dateNeeded ? item.dateNeeded.slice(0, 10) : null}
                                      onChange={(val) => {
                                        onUpdateRow({
                                          projectId: item.projectId,
                                          rowId: item.bomItemId,
                                          dateNeeded: val,
                                        })
                                      }}
                                      showWeekday={true}
                                      size="sm"
                                      placeholder="Scegli data..."
                                      className="h-9 w-full bg-background text-xs"
                                    />
                                  </div>
                                </div>
                              ))}
                            </div>
                          </div>

                          <div>
                            <Label className="text-xs font-medium text-muted-foreground">
                              Note Offerta / Condizioni Fornitore
                            </Label>
                            <Input
                              defaultValue={offer.notes ?? ""}
                              placeholder="Tempi di consegna generali, sconti, note..."
                              className="h-9 text-xs mt-1"
                              onBlur={(e) => {
                                saveOffer(rfqDetail.id, offer, {
                                  notes: e.target.value,
                                }).then(() => refetchRfqDetail())
                              }}
                            />
                          </div>
                        </div>
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          </div>
        )}

        <DialogFooter className="items-center sm:justify-between">
          <div className="flex items-center gap-2">
            {/* Ordine Danea: badge se già generato, altrimenti pulsante (solo con vincitore) */}
            {rfqOrder?.exists ? (
              <DaneaOrderBadge
                label={`Ordine Danea n. ${rfqOrder.num ?? rfqOrder.idDoc ?? "Registrato"}`}
                idDoc={rfqOrder.idDoc}
                icon={FileCheck2}
                iconClassName="size-4 text-teal-600"
                className="inline-flex items-center gap-1.5 text-sm font-bold text-teal-700 underline-offset-2 hover:underline bg-teal-50 dark:bg-teal-950/40 px-3 py-1.5 rounded-lg border border-teal-200"
                onOpen={onOpenDaneaOrder}
              />
            ) : rfqDetail &&
              (rfqDetail.status === "CLOSED" || rfqDetail.offers.some((o) => o.isWinner)) ? (
              <>
                <DateField
                  value={expectedDate}
                  onChange={setExpectedDate}
                  size="sm"
                  placeholder="Consegna prevista"
                  className="h-8 w-40"
                />
                <Button
                  size="sm"
                  disabled={createOrderMutation.isPending}
                  onClick={() => void handleCreateSingleOrder(rfqDetail)}
                  className="gap-1 bg-green-600 hover:bg-green-700 text-white font-semibold"
                >
                  <FileCheck2 className="h-4 w-4" />
                  {createOrderMutation.isPending
                    ? "Creazione ordine…"
                    : "Genera Ordine Danea (ODA)"}
                </Button>
              </>
            ) : null}

            {/* Annullamento: solo su RDO ancora aperte (il server rifiuta le chiuse). */}
            {rfqDetail && rfqIsOpen ? (
              <Button
                size="sm"
                variant="destructive"
                disabled={cancelRfqMutation.isPending}
                onClick={() => void handleCancelRfq(rfqDetail)}
                className="gap-1"
              >
                <Ban className="h-3.5 w-3.5" />
                {cancelRfqMutation.isPending ? "Annullamento…" : "Annulla RDO"}
              </Button>
            ) : null}
          </div>
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={onClose}>
              Chiudi
            </Button>
            {rfqDetail && rfqIsOpen && (
              <Button
                size="sm"
                variant={rfqDetail.status === "SENT" ? "outline" : "default"}
                onClick={() => void handleSendMailto(rfqDetail)}
                className="gap-1"
              >
                <Send className="h-3.5 w-3.5" />
                {rfqDetail.status === "SENT"
                  ? "Ricomponi Email (Outlook)"
                  : "Componi Email (Outlook)"}
              </Button>
            )}
          </div>
        </DialogFooter>

        {/* Picker articolo Danea: ANNIDATO nel dialog RDO — da fratello, chiudendosi
            faceva chiudere anche la RDO sottostante (stack dei layer Radix). */}
        <CatalogItemPickerDialog
          open={offerPickerTarget !== null}
          onClose={() => setOfferPickerTarget(null)}
          onSelect={(item) => void handlePickCatalogItem(item)}
          selectedId={offerPickerTarget?.catalogItemId ?? null}
          title={`Articolo Danea — ${offerPickerTarget?.supplierName ?? ""}`}
          description="Scegli l'articolo di catalogo che il fornitore fornirà: è quello che finisce nell'ordine Danea. Il filtro Fornitore è precompilato, cancellalo per cercare in tutto il catalogo."
          initialFilters={
            offerPickerTarget?.supplierName
              ? { supplier: offerPickerTarget.supplierName }
              : undefined
          }
        />
      </DialogContent>
    </Dialog>
  )
}
