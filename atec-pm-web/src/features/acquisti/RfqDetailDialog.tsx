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
import { rfqStatusLabel } from "./rfq-status"

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

  // Offerta vincitrice (se già scelta): senza il suo articolo Danea collegato
  // l'ordine fornitore non può essere generato, e il pulsante lo dice.
  const offertaVincitrice = rfqDetail?.offers.find((o) => o.isWinner)

  /** RDO ancora lavorabile: né aggiudicata/chiusa né annullata. Governa email,
   *  annullamento e scelta del vincitore (il server rifiuta le chiuse). */
  const rfqIsOpen =
    !!rfqDetail && rfqDetail.status !== "CLOSED" && rfqDetail.status !== "CANCELLED"

  // Il dialog è montato una volta a livello pagina: la data consegna va azzerata
  // al cambio RDO, altrimenti una consegna prevista stantia finisce nell'ordine reale.
  React.useEffect(() => {
    setExpectedDate(null)
  }, [rfqId])

  // Strada B: ordine fornitore (singola RDO) scritto direttamente in Danea (Atec_PM).
  const createOrderMutation = useMutation({
    mutationFn: (id: number) => createPurchaseRfqDaneaOrder(id, expectedDate),
    onSuccess: (updated) => {
      notifyInfo(`Ordine fornitore n. ${updated.daneaOrderNum} creato in Danea`)
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

  /** Contatore che rimonta gli input non controllati (chiave) DOPO un salvataggio
   *  fallito: senza, il campo continuerebbe a mostrare il valore digitato mentre a
   *  DB c'è quello vecchio — divergenza invisibile. */
  const [resetTick, setResetTick] = React.useState(0)

  /** Riscontro «Salvato ✓» accanto al campo appena salvato dal blur: si spegne da
   *  solo dopo ~2s. Niente toast per ogni blur: troppo rumore. */
  const [salvataggioFlash, setSalvataggioFlash] = React.useState<{
    offerId: number
    campo: "prezzo" | "note"
  } | null>(null)
  const salvataggioFlashTimer = React.useRef<ReturnType<typeof setTimeout> | null>(
    null
  )
  const mostraSalvato = React.useCallback(
    (offerId: number, campo: "prezzo" | "note") => {
      if (salvataggioFlashTimer.current) clearTimeout(salvataggioFlashTimer.current)
      setSalvataggioFlash({ offerId, campo })
      salvataggioFlashTimer.current = setTimeout(() => setSalvataggioFlash(null), 2000)
    },
    []
  )
  React.useEffect(
    () => () => {
      if (salvataggioFlashTimer.current) clearTimeout(salvataggioFlashTimer.current)
    },
    []
  )

  // 🪤 Base per il PUT completo: NON lo snapshot del render (cache react-query), che fra
  // un salvataggio e il refetch successivo è stantio — due blur ravvicinati (prezzo, poi
  // note) farebbero ripartire il secondo dal prezzo VECCHIO della closure, cancellando
  // quello appena salvato. Qui si tiene l'ultimo stato INVIATO per offerta, così i blur
  // si compongono qualunque sia la latenza del refetch.
  const offerBaseRef = React.useRef(
    new Map<
      number,
      {
        catalogItemId: number | null
        unitPrice: number | null
        validUntil: string | null
        notes: string
      }
    >()
  )
  React.useEffect(() => {
    offerBaseRef.current.clear()
  }, [rfqId])

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
    ) => {
      const base = offerBaseRef.current.get(offer.id) ?? {
        catalogItemId: offer.catalogItemId ?? null,
        unitPrice: offer.unitPrice ?? null,
        validUntil: offer.validUntil ?? null,
        notes: offer.notes ?? "",
      }
      const merged = { ...base, ...patch }
      offerBaseRef.current.set(offer.id, merged)
      return savePurchaseRfqOffer(id, offer.id, {
        supplierId: offer.supplierId,
        ...merged,
      })
    },
    []
  )

  /** Salvataggio da onBlur: l'errore NON può restare muto (l'input non controllato
   *  continuerebbe a mostrare il valore non salvato) — si avvisa, si butta la base
   *  locale e si rimonta il campo sul dato vero. */
  const saveOfferDaBlur = React.useCallback(
    (
      id: number,
      offer: PurchaseRfqOfferDto,
      patch: Parameters<typeof saveOffer>[2]
    ) => {
      saveOffer(id, offer, patch)
        .then(() => {
          if ("unitPrice" in patch) mostraSalvato(offer.id, "prezzo")
          else if ("notes" in patch) mostraSalvato(offer.id, "note")
          return refetchRfqDetail()
        })
        .catch((err) => {
          offerBaseRef.current.delete(offer.id)
          notifyError(
            `Offerta NON salvata: ${err instanceof Error ? err.message : String(err)}`
          )
          setResetTick((t) => t + 1)
          void refetchRfqDetail()
        })
    },
    [saveOffer, refetchRfqDetail, mostraSalvato]
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
      // La base locale ha già l'articolo che il server ha rifiutato: via, o il
      // prossimo blur lo rispedirebbe.
      offerBaseRef.current.delete(offerPickerTarget.id)
      notifyError(err instanceof Error ? err.message : String(err))
    } finally {
      setOfferPickerTarget(null)
    }
  }

  const handleCancelRfq = async (detail: PurchaseRfqDetail) => {
    // Anche una RDO CHIUSA (aggiudicata) ma senza ordine Danea si può annullare: è la
    // via d'uscita da un'aggiudicazione sbagliata — prima le righe restavano murate
    // (occupate dalla gara chiusa, non rimettibili in gara). Con l'ordine generato no:
    // l'ordine è irreversibile e la RDO gli fa da pezza d'appoggio.
    const eraAggiudicata = detail.status === "CLOSED"
    const ok = await confirm({
      title: eraAggiudicata ? "Annullare la RDO aggiudicata?" : "Annullare la RDO?",
      description: eraAggiudicata
        ? `La RDO #${detail.id} è già aggiudicata (senza ordine Danea): annullandola le ` +
          "righe tornano disponibili per una nuova gara. Prezzo e fornitore scritti " +
          "sulla distinta restano finché una nuova aggiudicazione non li riscrive."
        : `La RDO #${detail.id} passa in Annullata e non sarà più modificabile. ` +
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
        `Crea in Danea (archivio Atec_PM) l'ordine fornitore per ${winner?.supplierName ?? "—"}: ` +
        `${qty} × ${winner?.catalogCode || detail.atecCode} a ${euro(winner?.unitPrice ?? 0)} + IVA` +
        (expectedDate ? `, consegna prevista ${formatDateShort(expectedDate)}` : "") +
        ". L'ordine è definitivo: dopo, la RDO non si potrà più annullare da qui. " +
        "Nessuna email viene inviata al fornitore: l'invio si fa da Danea. " +
        "Le righe di distinta passano a In Ordine.",
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

    // Il codice va scritto DENTRO il ciclo, non fuori: «Codice Fornitore» deve essere il
    // codice con cui QUEL fornitore chiama l'articolo (`offer.catalogCode`), non il codice
    // della nostra riga di distinta. Prima il testo era calcolato una volta sola e mandava
    // a tutti e tre lo stesso codice — quello di casa nostra, che il fornitore non riconosce.
    const testoArticoli = (offer: PurchaseRfqOfferDto) =>
      detail.items
        .map((it) => {
          const codiceFornitore = offer.catalogCode?.trim() || it.partNumber || "N.D."
          return `• ${it.description}\n  - Codice Fornitore: ${codiceFornitore}\n  - Riferimento ATEC: ${detail.atecCode || "N.D."}\n  - Quantità: ${it.quantity}`
        })
        .join("\n\n")

    // 🪤 Il corpo NON è uguale per tutti: `offer.catalogCode` (ripetuto una volta per
    // riga) e il nome del fornitore cambiano la lunghezza — vicino alla soglia un
    // fornitore può sforare e un altro no. Le mailto si costruiscono PRIMA della
    // conferma: così la finestra promette il numero VERO di email apribili e chi
    // sfora la soglia viene nominato subito, non scoperto a finestre già aperte.
    const apribili: { offer: PurchaseRfqOfferDto; mailtoUrl: string }[] = []
    const troppoLunghe: string[] = []
    for (const offer of detail.offers) {
      const subject = `Richiesta Offerta — Commessa ${detail.projectCode}`
      const body = `Gentile ${offer.supplierName},\n\nvi chiediamo la vs. migliore offerta per la Commessa ${detail.projectCode}:\n\nArticoli richiesti:\n${testoArticoli(offer)}\n\n${detail.notes ? `Note aggiuntive:\n${detail.notes}\n\n` : ""}In attesa di un vostro riscontro, porgiamo cordiali saluti.\nATEC PM`

      const to = offer.supplierEmail?.trim()
        ? encodeURIComponent(offer.supplierEmail.trim())
        : ""
      const mailtoUrl = `mailto:${to}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`

      // Meglio nessuna mail che una mail troncata a metà lista articoli.
      if (mailtoUrl.length > MAILTO_MAX_URL) {
        troppoLunghe.push(offer.supplierName)
        continue
      }
      apribili.push({ offer, mailtoUrl })
    }

    if (apribili.length === 0) {
      notifyError(
        "Richiesta troppo lunga per l'apertura automatica di Outlook: Windows la " +
          "troncherebbe. Riduci le note della RDO oppure spezzala in più RDO con " +
          "meno articoli. Nessuna email è stata aperta e la RDO resta da inviare."
      )
      return
    }

    // Conferma preliminare: l'utente deve sapere PRIMA quante finestre Outlook si
    // aprono e per chi, che l'invio vero lo fa lui premendo Invia in ciascuna, e
    // che la RDO verrà segnata «Inviata ai fornitori». Chi è senza indirizzo in
    // anagrafica e chi resta fuori per lunghezza vanno nominati subito.
    const senzaEmail = apribili.filter(({ offer }) => !offer.supplierEmail?.trim())
    const nomiFornitori = apribili.map(({ offer }) => offer.supplierName).join(", ")
    const avvisoSenzaEmail =
      senzaEmail.length === 0
        ? ""
        : senzaEmail.length === 1
          ? ` ${senzaEmail[0].offer.supplierName} è senza indirizzo: andrà inserito a mano.`
          : ` ${senzaEmail.map(({ offer }) => offer.supplierName).join(", ")} sono senza indirizzo: andranno inseriti a mano.`
    const avvisoTroppoLunghe =
      troppoLunghe.length === 0
        ? ""
        : troppoLunghe.length === 1
          ? ` ${troppoLunghe[0]} NON verrà contattato da qui (richiesta troppo lunga per Outlook): la sua offerta resterà da inviare.`
          : ` ${troppoLunghe.join(", ")} NON verranno contattati da qui (richiesta troppo lunga per Outlook): le loro offerte resteranno da inviare.`
    const procedi = await confirm({
      title: "Comporre le email in Outlook?",
      description:
        (apribili.length === 1
          ? `Si aprirà 1 finestra di Outlook già compilata per ${nomiFornitori}.`
          : `Si apriranno ${apribili.length} finestre di Outlook già compilate, una per fornitore: ${nomiFornitori}.`) +
        " L'invio vero lo fai tu premendo Invia in ciascuna; la RDO verrà segnata " +
        "«Inviata ai fornitori»." +
        avvisoSenzaEmail +
        avvisoTroppoLunghe,
      confirmLabel: "Componi email",
    })
    if (!procedi) return

    // Si apre Outlook per OGNI fornitore apribile: se l'email non è in anagrafica
    // il destinatario resta vuoto e lo mette l'ufficio acquisti prima di inviare.
    // Offerte per cui la mail è stata davvero aperta: solo queste vanno marcate
    // come contattate (una RDO mai partita non deve risultare inviata).
    const opened: PurchaseRfqOfferDto[] = []
    for (const { offer, mailtoUrl } of apribili) {
      const position = opened.length
      if (position === 0) window.open(mailtoUrl, "_self")
      else setTimeout(() => window.open(mailtoUrl, "_self"), position * 900)
      opened.push(offer)
    }

    // Registra l'invio nel backend e avanza lo stato a SENT.
    // Se la registrazione fallisce (VPN caduta, errore server) le mail sono GIÀ aperte
    // in Outlook: l'utente deve saperlo, o la RDO resta «da inviare» con le mail partite
    // e un collega la rispedirebbe ai fornitori una seconda volta.
    try {
      await markPurchaseRfqEmailed(opened.map((o) => o.id))
    } catch (err) {
      notifyError(
        `Le email sono aperte in Outlook ma la registrazione dell'invio NON è riuscita ` +
          `(${err instanceof Error ? err.message : String(err)}): la RDO risulta ancora da ` +
          "inviare. Quando la rete torna, ripremi il pulsante e CHIUDI le finestre di " +
          "Outlook già inviate senza rispedirle."
      )
      return
    }

    onChanged()
    refetchRfqDetail()

    if (troppoLunghe.length > 0) {
      notifyError(
        `${troppoLunghe.length} fornitori NON contattati (richiesta troppo lunga per ` +
          `Outlook): ${troppoLunghe.join(", ")}. Riduci le note della RDO e ripremi ` +
          "«Ricomponi Email»: le finestre dei fornitori già contattati si possono " +
          "chiudere senza rispedire."
      )
    }
    const missing = opened.filter((o) => !o.supplierEmail?.trim()).length
    notifyInfo(
      missing > 0
        ? `Email aperte in Outlook (${missing} senza destinatario: da inserire a mano).`
        : "Email aperta in Outlook con la lista articoli nel testo."
    )
  }

  return (
    <Dialog
      open={!!rfqId}
      onOpenChange={(open) => {
        if (!open) {
          // Esc/X con il focus ancora dentro un campo prezzo/note modificato: il
          // blur forzato fa scattare l'onBlur e committa il valore, che altrimenti
          // andrebbe perso in silenzio alla chiusura.
          if (document.activeElement instanceof HTMLElement) {
            document.activeElement.blur()
          }
          onClose()
        }
      }}
    >
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
                <Badge variant="outline">{rfqStatusLabel(rfqDetail.status)}</Badge>
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
              <div className="text-[11px] text-muted-foreground">
                I prezzi e le note si salvano da soli quando esci dal campo.
              </div>
              {rfqDetail.offers.length === 0 ? (
                <div className="text-xs text-muted-foreground p-4 border rounded text-center mt-1">
                  Nessun fornitore collegato a questa gara. I fornitori vengono
                  invitati in automatico in base al codice ATEC dell'articolo: se a
                  quel codice non è associato nessun fornitore, la gara nasce vuota.
                  Associa i fornitori all'articolo nel Catalogo, poi annulla questa
                  RDO e ricreala.
                </div>
              ) : (
                <div className="space-y-3 mt-1">
                  {rfqDetail.offers.map((offer) => {
                    // Solo il prezzo VERO dell'offerta abilita l'aggiudicazione: il ripiego
                    // sul costo di riga rendeva «Scegli Vincitore» attivo su tutte e tre le
                    // offerte anche senza aver digitato niente, e si aggiudicava a un prezzo
                    // che nessun fornitore aveva proposto (ora anche il server lo rifiuta).
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

                            {/* Il prezzo è UNO per offerta (`purchase_rfq_offers.unit_price`),
                                quindi sta qui, nell'intestazione del fornitore. Stava dentro
                                l'elenco righe: con una RDO di N righe comparivano N campi
                                identici che salvavano tutti sullo stesso valore, e bastava
                                uscire da uno di quelli rimasti indietro per riscrivere in
                                silenzio il prezzo appena inserito — quello che poi finisce
                                nell'ordine Danea vero. */}
                            <div className="flex items-center gap-2 pt-1">
                              <Label
                                htmlFor={`offer-price-${offer.id}`}
                                className="text-[11px] font-semibold text-muted-foreground uppercase"
                              >
                                Prezzo unitario offerto
                              </Label>
                              <div className="relative w-32">
                                <Input
                                  id={`offer-price-${offer.id}`}
                                  // 🪤 La chiave NON deve contenere il prezzo: cambierebbe
                                  // all'atterraggio del refetch e React rimonterebbe l'input
                                  // MENTRE l'utente ci scrive dentro — testo e focus persi
                                  // senza onBlur, quindi senza salvataggio. Il valore digitato
                                  // è già quello salvato (lo scrive questo stesso campo); il
                                  // riallineamento forzato serve solo dopo un salvataggio
                                  // FALLITO, ed è il lavoro di resetTick.
                                  key={`offer-${offer.id}-price-${resetTick}`}
                                  type="number"
                                  step="0.01"
                                  defaultValue={offer.unitPrice ?? ""}
                                  placeholder="0.00"
                                  // 🪤 Bloccato all'aggiudicazione, NON alla generazione dell'ordine.
                                  // Provato e ritirato: lasciandolo aperto su una RDO già chiusa si
                                  // corregge solo `purchase_rfq_offers.unit_price`, perché il costo
                                  // sulla riga di distinta lo scrive unicamente SelectWinner, che le
                                  // RDO chiuse le rifiuta. L'ordine Danea però rilegge il prezzo
                                  // fresco: sarebbe partito col numero corretto lasciando distinta e
                                  // Bilancio su quello sbagliato, per sempre e senza dirlo a nessuno.
                                  // Un'aggiudicazione sbagliata si ripara a monte, non qui.
                                  disabled={!rfqIsOpen}
                                  className="h-8 text-xs font-mono bg-background text-right pr-6"
                                  onBlur={(e) => {
                                    // 🪤 `<input type="number">` restituisce stringa vuota per
                                    // QUALUNQUE valore che il browser non sa leggere («12.»,
                                    // separatore decimale sbagliato, incolla sporco): senza
                                    // questo controllo il campo interpreterebbe quel vuoto come
                                    // «togli il prezzo» e cancellerebbe un'offerta valida.
                                    if (e.target.validity?.badInput) {
                                      notifyError(
                                        "Prezzo non leggibile: usa il punto come separatore decimale (es. 12.50)."
                                      )
                                      return
                                    }
                                    // Campo davvero svuotato = offerta senza prezzo (si toglie
                                    // un numero messo per sbaglio).
                                    const testo = e.target.value.trim()
                                    const val = testo === "" ? null : parseFloat(testo)
                                    if (val !== null && isNaN(val)) return
                                    // Confronto con l'ultimo valore INVIATO (base locale),
                                    // non con la cache: il `?? null` conta — il server toglie
                                    // le chiavi null dal JSON, quindi un'offerta senza prezzo
                                    // arriva come undefined e un confronto stretto con null
                                    // farebbe partire un PUT a vuoto a ogni passaggio di focus.
                                    const prezzoAttuale =
                                      offerBaseRef.current.get(offer.id)?.unitPrice ??
                                      offer.unitPrice ??
                                      null
                                    if (val !== prezzoAttuale) {
                                      saveOfferDaBlur(rfqDetail.id, offer, {
                                        unitPrice: val,
                                      })
                                    }
                                  }}
                                />
                                <span className="absolute right-2 top-2 text-xs text-muted-foreground font-mono">
                                  €
                                </span>
                              </div>
                              {salvataggioFlash?.offerId === offer.id &&
                                salvataggioFlash.campo === "prezzo" && (
                                  <span className="text-[11px] font-medium text-green-700 dark:text-green-500">
                                    Salvato ✓
                                  </span>
                                )}
                            </div>
                          </div>

                          {!offer.isWinner && (
                            <Button
                              size="sm"
                              variant="outline"
                              // Niente `disabled` sul prezzo: `hasPrice` legge la cache, che si
                              // aggiorna solo dopo il salvataggio scatenato dall'onBlur — passando
                              // dal campo al pulsante il primo clic andava perso e bisognava
                              // cliccare due volte. A fermare l'aggiudicazione senza prezzo ci
                              // pensano il pre-controllo nell'onClick (base locale, aggiornata
                              // dal blur prima del click) e il server, che la rifiuta.
                              disabled={!rfqIsOpen}
                              title={!rfqIsOpen ? "RDO non più aperta" : undefined}
                              className="h-7 text-xs text-green-700 border-green-300 hover:bg-green-100 disabled:opacity-50"
                              onClick={async () => {
                                // Pre-controllo prezzo (base locale, poi cache): senza
                                // prezzo il server rifiuterebbe comunque, ma l'utente
                                // merita un messaggio chiaro PRIMA della conferma. Il
                                // pulsante resta NON disabled (vincolo anti doppio-clic
                                // documentato qui sopra).
                                const prezzoOfferta =
                                  offerBaseRef.current.get(offer.id)?.unitPrice ??
                                  offer.unitPrice ??
                                  null
                                if (prezzoOfferta == null) {
                                  notifyError(
                                    `Inserisci prima il prezzo unitario offerto da ${offer.supplierName}.`
                                  )
                                  return
                                }
                                // Il server rifiuta anche 0/negativo (RdoGuardie):
                                // meglio dirlo prima della conferma, non dopo.
                                if (prezzoOfferta <= 0) {
                                  notifyError(
                                    `Il prezzo di ${offer.supplierName} è zero o negativo: correggilo prima di scegliere il vincitore.`
                                  )
                                  return
                                }
                                if (
                                  await confirm({
                                    title: "Assegna Vincitore",
                                    description:
                                      `Aggiudicando a ${offer.supplierName} la gara si chiude: ` +
                                      "i prezzi delle altre offerte non saranno più modificabili, " +
                                      `il prezzo di ${offer.supplierName} viene scritto sulle righe ` +
                                      "di distinta e potrai generare l'ordine in Danea. Se sbagli, " +
                                      "puoi annullare la RDO finché l'ordine non è stato generato.",
                                  })
                                ) {
                                  try {
                                    const esito = await selectPurchaseRfqWinner(
                                      rfqDetail.id,
                                      offer.id
                                    )
                                    // Il testo arriva dal server («Vincitore applicato
                                    // a N righe»): qui si declina solo il singolare.
                                    const esitoLeggibile = esito.replace(
                                      /\ba 1 righe\b/,
                                      "a 1 riga"
                                    )
                                    await refetchRfqDetail()
                                    onChanged()
                                    notifyInfo(
                                      esitoLeggibile
                                        ? `Aggiudicata a ${offer.supplierName}. ${esitoLeggibile}`
                                        : `Offerta aggiudicata a ${offer.supplierName}`
                                    )
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
                              <div className="col-span-6">Articolo / Descrizione & Codice</div>
                              <div className="col-span-2 text-center">Qtà</div>
                              <div
                                className="col-span-4 text-center"
                                title="Data richiesta di consegna della riga di distinta: è unica, uguale sotto ogni fornitore."
                              >
                                Data Prev. Consegna
                              </div>
                            </div>
                            <div className="space-y-2">
                              {rfqDetail.items.map((item) => (
                                <div
                                  key={item.id}
                                  className="grid grid-cols-12 gap-3 items-center p-3 border rounded-lg bg-muted/20 text-xs"
                                >
                                  <div className="col-span-6 min-w-0">
                                    <div className="font-semibold text-foreground text-xs whitespace-normal break-words">
                                      {item.description}
                                    </div>
                                    <div className="text-[11px] text-muted-foreground font-mono mt-0.5">
                                      Cod. Fornitore:{" "}
                                      <span className="text-foreground font-medium">
                                        {item.partNumber || "N.D."}
                                      </span>
                                      {" · "}
                                      {/* Il codice ATEC per riga: se una gara è mista il server
                                          rifiuta di aggiudicarla, e da qui si vede subito quale
                                          riga è l'intrusa invece di doverlo indovinare. */}
                                      Cod. ATEC:{" "}
                                      <span className="text-foreground font-medium">
                                        {item.atecCode || "—"}
                                      </span>
                                    </div>
                                  </div>
                                  <div className="col-span-2 text-center font-mono font-bold text-sm text-foreground">
                                    {item.quantity}
                                  </div>
                                  <div className="col-span-4">
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
                            <div className="flex items-center gap-2">
                              <Label className="text-xs font-medium text-muted-foreground">
                                Note Offerta / Condizioni Fornitore
                              </Label>
                              {salvataggioFlash?.offerId === offer.id &&
                                salvataggioFlash.campo === "note" && (
                                  <span className="text-[11px] font-medium text-green-700 dark:text-green-500">
                                    Salvato ✓
                                  </span>
                                )}
                            </div>
                            <Input
                              key={`offer-${offer.id}-notes-${resetTick}`}
                              defaultValue={offer.notes ?? ""}
                              placeholder="Tempi di consegna generali, sconti, note..."
                              className="h-9 text-xs mt-1"
                              onBlur={(e) => {
                                const noteAttuali =
                                  offerBaseRef.current.get(offer.id)?.notes ??
                                  offer.notes ??
                                  ""
                                if (e.target.value !== noteAttuali) {
                                  saveOfferDaBlur(rfqDetail.id, offer, {
                                    notes: e.target.value,
                                  })
                                }
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
              rfqDetail.status !== "CANCELLED" &&
              (rfqDetail.status === "CLOSED" || rfqDetail.offers.some((o) => o.isWinner)) ? (
              // ^ mai su una ANNULLATA: l'annullo non azzera is_winner (resta come storia
              //   dell'aggiudicazione), e senza questo controllo il ramo isWinner offrirebbe
              //   un «Genera Ordine Danea» che il server rifiuta sempre.
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
                  // Senza articolo Danea sull'offerta vincitrice l'ordine fallirebbe
                  // comunque lato server: il pre-controllo nell'onClick lo spiega
                  // PRIMA della conferma. Il pulsante resta cliccabile apposta: su
                  // un disabled il title non compare mai (disabled:pointer-events-none
                  // del Button shadcn) e l'utente resterebbe senza spiegazione.
                  disabled={createOrderMutation.isPending}
                  title="ODA = Ordine D'Acquisto: crea l'ordine nel gestionale Danea"
                  onClick={() => {
                    if (!offertaVincitrice?.catalogItemId) {
                      notifyError(
                        `L'offerta vincitrice non ha un articolo Danea collegato: ` +
                          `usa il pulsante "Collega articolo" nella scheda di ` +
                          `${offertaVincitrice?.supplierName ?? "quel fornitore"}, poi rigenera l'ordine.`
                      )
                      return
                    }
                    void handleCreateSingleOrder(rfqDetail)
                  }}
                  className="gap-1 bg-green-600 hover:bg-green-700 text-white font-semibold"
                >
                  <FileCheck2 className="h-4 w-4" />
                  {createOrderMutation.isPending
                    ? "Creazione ordine…"
                    : "Genera ordine fornitore (Danea)"}
                </Button>
              </>
            ) : rfqDetail && rfqIsOpen && rfqDetail.offers.length > 0 ? (
              // RDO aperta senza vincitore: al posto del pulsante ordine si spiega
              // cosa manca perché compaia. Con zero offerte l'hint tace: lì
              // l'istruzione giusta (annulla e ricrea) la dà già il corpo.
              <span className="text-xs text-muted-foreground">
                Inserisci i prezzi e scegli il vincitore per generare l'ordine.
              </span>
            ) : null}

            {/* Annullamento: RDO aperte, più le CHIUSE senza ordine Danea (la via
                d'uscita da un'aggiudicazione sbagliata — il server rifiuta le altre).
                Qui contano SOLO i campi di testata (daneaOrderNum/IdDoc), non
                rfqOrder.exists: quello guarda anche i marcatori di riga (Rif. Danea
                scritto a mano, stato IO), e una riga ordinata fuori ciclo RDO
                nasconderebbe proprio la via d'uscita che questo pulsante apre. */}
            {rfqDetail &&
            (rfqIsOpen ||
              (rfqDetail.status === "CLOSED" &&
                rfqDetail.daneaOrderNum == null &&
                rfqDetail.daneaOrderIdDoc == null)) ? (
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
