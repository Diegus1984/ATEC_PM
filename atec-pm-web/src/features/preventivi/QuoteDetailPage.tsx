import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { useNavigate, useParams, useSearchParams } from "react-router-dom"
import {
  ArrowLeft,
  ChevronDown,
  ChevronRight,
  Copy,
  FileDown,
  Pencil,
  Plus,
  Trash2,
} from "lucide-react"

import { useConfirm } from "@/components/shared/confirm"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import {
  addQuoteLocalVariant,
  deleteQuoteItem,
  cloneQuoteItem,
  fetchQuote,
  fetchQuotePdf,
  reloadAutoIncludes,
  updateQuote,
  updateQuoteField,
  updateQuoteItem,
  updateQuoteItemField,
} from "@/lib/api/quotes"
import type { QuoteDto, QuoteItemDto, QuoteSaveDto } from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { notifyError } from "@/lib/toast"

import { AddLocalVariantDialog, type LocalVariantValues } from "./AddLocalVariantDialog"
import { AddQuoteItemDialog } from "./AddQuoteItemDialog"
import { CostingTree } from "./CostingTree"
import { RtfEditDialog } from "./RtfEditDialog"
import { quoteStatusLabel, quoteStatusMeta } from "./quote-status"
import { quoteTypeLabel } from "./quote-type"

function fmt2(value: number): string {
  return value.toLocaleString("it-IT", { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}
function parseDecimal(value: string): number {
  const n = Number((value ?? "").replace(",", "."))
  return Number.isFinite(n) ? n : 0
}

interface ProductGroup {
  parent: QuoteItemDto
  variants: QuoteItemDto[]
}

function buildGroups(items: QuoteItemDto[]): ProductGroup[] {
  const sorted = [...items].sort((a, b) => a.sortOrder - b.sortOrder)
  const parents = sorted.filter((i) => i.parentItemId == null)
  return parents.map((parent) => ({
    parent,
    variants: sorted.filter((i) => i.parentItemId === parent.id),
  }))
}

/** QuoteItemSaveDto da un item esistente, con override. */
function itemDto(item: QuoteItemDto, patch: Partial<QuoteItemDto>): import("@/lib/api/types").QuoteItemSaveDto {
  const merged = { ...item, ...patch }
  return {
    productId: merged.productId,
    variantId: merged.variantId,
    itemType: merged.itemType,
    code: merged.code,
    name: merged.name,
    descriptionRtf: merged.descriptionRtf,
    unit: merged.unit,
    quantity: merged.quantity,
    costPrice: merged.costPrice,
    sellPrice: merged.sellPrice,
    discountPct: merged.discountPct,
    vatPct: merged.vatPct,
    sortOrder: merged.sortOrder,
    isActive: merged.isActive,
    isConfirmed: merged.isConfirmed,
    parentItemId: merged.parentItemId,
    isAutoInclude: merged.isAutoInclude,
  }
}

// ── PAGINA ─────────────────────────────────────────────────

export function QuoteDetailPage() {
  const params = useParams()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const confirm = useConfirm()

  const quoteId = Number.parseInt(params.id ?? "0", 10)
  const readOnly = searchParams.get("readonly") === "1"
  const role = getSession()?.user.userRole ?? ""
  const canSeeCosts = role === "ADMIN" || role === "PM"

  const quoteQuery = useQuery({
    queryKey: ["quote", quoteId],
    queryFn: () => fetchQuote(quoteId),
    enabled: quoteId > 0,
  })
  const quote = quoteQuery.data

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ["quote", quoteId] })
  }

  // Carica i contenuti automatici dal catalogo una sola volta, se assenti (come il WPF).
  const autoReloadedRef = React.useRef(false)
  React.useEffect(() => {
    if (readOnly || autoReloadedRef.current || !quote) return
    autoReloadedRef.current = true
    if (!quote.items.some((i) => i.isAutoInclude)) {
      reloadAutoIncludes(quoteId)
        .then(() => invalidate())
        .catch(() => {})
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [quote, readOnly, quoteId])

  // Dialoghi.
  const [addProductOpen, setAddProductOpen] = React.useState(false)
  const [addVariantFor, setAddVariantFor] = React.useState<{ parentId: number; name: string } | null>(null)
  const [rtfEdit, setRtfEdit] = React.useState<{ itemId: number; title: string; html: string } | null>(null)

  // ── Mutazioni item ──
  const saveItem = useMutation({
    mutationFn: (vars: { itemId: number; dto: import("@/lib/api/types").QuoteItemSaveDto }) =>
      updateQuoteItem(quoteId, vars.itemId, vars.dto),
    onSuccess: invalidate,
    onError: (err: Error) => notifyError(err),
  })
  const deleteItem = useMutation({
    mutationFn: (itemId: number) => deleteQuoteItem(quoteId, itemId),
    onSuccess: invalidate,
    onError: (err: Error) => notifyError(err),
  })
  const cloneItem = useMutation({
    mutationFn: (itemId: number) => cloneQuoteItem(quoteId, itemId),
    onSuccess: invalidate,
    onError: (err: Error) => notifyError(err),
  })
  const addVariant = useMutation({
    mutationFn: (vars: { parentId: number; values: LocalVariantValues }) =>
      addQuoteLocalVariant(quoteId, vars.parentId, {
        productId: null,
        variantId: null,
        itemType: "product",
        code: vars.values.code,
        name: vars.values.name,
        descriptionRtf: "",
        unit: vars.values.unit,
        quantity: vars.values.quantity,
        costPrice: vars.values.costPrice,
        sellPrice: vars.values.sellPrice,
        discountPct: 0,
        vatPct: 22,
        sortOrder: 0,
        isActive: true,
        isConfirmed: false,
        parentItemId: vars.parentId,
        isAutoInclude: false,
      }),
    onSuccess: invalidate,
    onError: (err: Error) => notifyError(err),
  })

  async function handleRemoveProduct(group: ProductGroup) {
    const ok = await confirm({
      title: "Rimuovi prodotto",
      description: `Rimuovere '${group.parent.name}' e tutte le sue varianti?`,
      confirmLabel: "Rimuovi",
      destructive: true,
    })
    if (!ok) return
    await deleteQuoteItem(quoteId, group.parent.id)
    for (const v of group.variants) {
      await deleteQuoteItem(quoteId, v.id)
    }
    invalidate()
  }

  async function handleRemoveAutoInclude(group: ProductGroup) {
    const ok = await confirm({
      title: "Rimuovi contenuto",
      description: `Rimuovere '${group.parent.name}'?`,
      confirmLabel: "Rimuovi",
      destructive: true,
    })
    if (ok) deleteItem.mutate(group.parent.id)
  }

  function saveRtf(itemId: number, html: string) {
    void updateQuoteItemField(quoteId, itemId, "description_rtf", html)
      .then(invalidate)
      .catch((err: Error) => notifyError(err))
    setRtfEdit(null)
  }

  async function handleReloadAutoIncludes() {
    try {
      await reloadAutoIncludes(quoteId)
      invalidate()
    } catch (err) {
      notifyError(err)
    }
  }

  async function handlePdf() {
    try {
      const blob = await fetchQuotePdf(quoteId)
      window.open(URL.createObjectURL(blob), "_blank")
    } catch (err) {
      notifyError(err)
    }
  }

  if (quoteQuery.isLoading) {
    return <p className="p-6 text-sm text-muted-foreground">Caricamento preventivo…</p>
  }
  if (!quote) {
    return <p className="p-6 text-sm text-destructive">Preventivo non trovato.</p>
  }

  const groups = buildGroups(quote.items)
  const serviceProducts = groups.filter((g) => !g.parent.isAutoInclude)
  const autoIncludes = groups.filter((g) => g.parent.isAutoInclude)
  const isPlant = quote.quoteType === "IMPIANTO"
  const meta = quoteStatusMeta(quote.status)
  const statusText = readOnly
    ? quote.status === "converted"
      ? "CONVERTITO — SOLA LETTURA"
      : "SUPERATA — SOLA LETTURA"
    : quoteStatusLabel(quote.status).toUpperCase()

  return (
    <div className="space-y-4">
      <Button variant="ghost" size="sm" className="text-muted-foreground" onClick={() => navigate("/preventivi")}>
        <ArrowLeft className="size-4" />
        Indietro
      </Button>

      {/* Header */}
      <Card>
        <CardContent className="flex flex-wrap items-center justify-between gap-3 py-4">
          <div className="space-y-1">
            <h1 className="text-lg font-bold">
              {quote.quoteNumber} — {quote.title}
            </h1>
            <div className="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
              <span
                className="rounded-full px-2.5 py-0.5 text-xs font-bold"
                style={meta && !readOnly ? { backgroundColor: meta.bg, color: meta.fg } : { backgroundColor: "#E5E7EB", color: "#6B7280" }}
              >
                {statusText}
              </span>
              <span className="font-medium text-[#2563EB]">{quote.customerName}</span>
              <span className="text-xs">di {quote.createdByName}</span>
              <span className="text-xs">{new Date(quote.createdAt).toLocaleDateString("it-IT")}</span>
              <span
                className="rounded-full px-2.5 py-0.5 text-[11px] font-bold"
                style={{ backgroundColor: isPlant ? "#FFF7ED" : "#F0FDF4", color: isPlant ? "#EA580C" : "#059669" }}
              >
                {quoteTypeLabel(quote.quoteType)}
              </span>
            </div>
          </div>
          <Button variant="outline" size="sm" onClick={handlePdf}>
            <FileDown className="size-4" />
            PDF
          </Button>
        </CardContent>
      </Card>

      {/* Pannello info condiviso */}
      <InfoPanel quote={quote} readOnly={readOnly} onSaved={invalidate} />

      {isPlant ? (
        <Card>
          <CardContent className="space-y-4 py-4">
            {canSeeCosts ? (
              <CostingTree quoteId={quoteId} readOnly={readOnly} />
            ) : (
              <p className="text-sm text-muted-foreground">
                Dati economici riservati (PM/ADMIN).
              </p>
            )}
            <AutoIncludesPanel
              autoIncludes={autoIncludes}
              readOnly={readOnly}
              onReload={handleReloadAutoIncludes}
              onEditRtf={(g) =>
                setRtfEdit({ itemId: g.parent.id, title: g.parent.name, html: g.parent.descriptionRtf })
              }
              onRemove={handleRemoveAutoInclude}
            />
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="space-y-4 py-4">
            <div className="flex items-center justify-between">
              <h2 className="font-bold">Prodotti &amp; Contenuti</h2>
              {!readOnly ? (
                <Button size="sm" onClick={() => setAddProductOpen(true)}>
                  <Plus className="size-4" />
                  Aggiungi prodotto
                </Button>
              ) : null}
            </div>

            {/* Prodotti */}
            {serviceProducts.length === 0 ? (
              <p className="py-6 text-center text-sm text-muted-foreground">
                Nessun prodotto aggiunto.
              </p>
            ) : (
              <div className="space-y-3">
                {serviceProducts.map((group) => (
                  <ServiceProductCard
                    key={group.parent.id}
                    group={group}
                    readOnly={readOnly}
                    onSaveVariant={(itemId, dto) => saveItem.mutate({ itemId, dto })}
                    onDeleteVariant={(itemId) => deleteItem.mutate(itemId)}
                    onAddVariant={() => setAddVariantFor({ parentId: group.parent.id, name: group.parent.name })}
                    onRemoveProduct={() => handleRemoveProduct(group)}
                    onCloneProduct={() => cloneItem.mutate(group.parent.id)}
                    onEditRtf={() =>
                      setRtfEdit({ itemId: group.parent.id, title: group.parent.name, html: group.parent.descriptionRtf })
                    }
                    onRenameProduct={(name) =>
                      void updateQuoteItemField(quoteId, group.parent.id, "name", name).then(invalidate)
                    }
                  />
                ))}
              </div>
            )}

            {/* Contenuti automatici */}
            <div className="rounded-md border">
              <div className="flex items-center justify-between border-b bg-[#F0FDF4] px-3 py-2">
                <span className="text-xs font-bold text-[#166534]">
                  CONTENUTI AUTOMATICI{autoIncludes.length > 0 ? ` (${autoIncludes.length})` : ""}
                </span>
                {!readOnly ? (
                  <Button variant="outline" size="sm" onClick={handleReloadAutoIncludes}>
                    Ricarica dal catalogo
                  </Button>
                ) : null}
              </div>
              {autoIncludes.length === 0 ? (
                <p className="px-3 py-3 text-sm text-muted-foreground">Nessun contenuto automatico.</p>
              ) : (
                autoIncludes.map((group) => (
                  <div key={group.parent.id} className="flex items-center gap-2 border-b px-3 py-2 last:border-b-0">
                    <span className="flex-1 truncate text-sm italic text-muted-foreground">{group.parent.name}</span>
                    {!readOnly ? (
                      <>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          title="Modifica contenuto"
                          onClick={() =>
                            setRtfEdit({ itemId: group.parent.id, title: group.parent.name, html: group.parent.descriptionRtf })
                          }
                        >
                          <Pencil className="size-3.5" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          className="text-destructive hover:bg-destructive/10"
                          title="Rimuovi"
                          onClick={() => handleRemoveAutoInclude(group)}
                        >
                          <Trash2 className="size-3.5" />
                        </Button>
                      </>
                    ) : null}
                  </div>
                ))
              )}
            </div>

            {/* Sconto + totali */}
            <ServiceTotals quote={quote} readOnly={readOnly} onSaved={invalidate} />
          </CardContent>
        </Card>
      )}

      {/* Dialoghi */}
      <AddQuoteItemDialog
        open={addProductOpen}
        quoteId={quoteId}
        existingProductIds={new Set(quote.items.map((i) => i.productId).filter((x): x is number => x != null))}
        onClose={() => setAddProductOpen(false)}
        onItemAdded={invalidate}
      />
      <AddLocalVariantDialog
        open={addVariantFor != null}
        productName={addVariantFor?.name ?? ""}
        onClose={() => setAddVariantFor(null)}
        onAdd={(values) => {
          if (addVariantFor) addVariant.mutate({ parentId: addVariantFor.parentId, values })
          setAddVariantFor(null)
        }}
      />
      <RtfEditDialog
        open={rtfEdit != null}
        title={rtfEdit?.title ?? ""}
        initialHtml={rtfEdit?.html ?? ""}
        onClose={() => setRtfEdit(null)}
        onSave={(html) => {
          if (rtfEdit) saveRtf(rtfEdit.itemId, html)
        }}
      />
    </div>
  )
}

// ── Contenuti automatici (clausole) ────────────────────────

function AutoIncludesPanel({
  autoIncludes,
  readOnly,
  onReload,
  onEditRtf,
  onRemove,
}: {
  autoIncludes: ProductGroup[]
  readOnly: boolean
  onReload: () => void
  onEditRtf: (group: ProductGroup) => void
  onRemove: (group: ProductGroup) => void
}) {
  return (
    <div className="rounded-md border">
      <div className="flex items-center justify-between border-b bg-[#F0FDF4] px-3 py-2">
        <span className="text-xs font-bold text-[#166534]">
          CONTENUTI AUTOMATICI{autoIncludes.length > 0 ? ` (${autoIncludes.length})` : ""}
        </span>
        {!readOnly ? (
          <Button variant="outline" size="sm" onClick={onReload}>
            Ricarica dal catalogo
          </Button>
        ) : null}
      </div>
      {autoIncludes.length === 0 ? (
        <p className="px-3 py-3 text-sm text-muted-foreground">Nessun contenuto automatico.</p>
      ) : (
        autoIncludes.map((group) => (
          <div key={group.parent.id} className="flex items-center gap-2 border-b px-3 py-2 last:border-b-0">
            <span className="flex-1 truncate text-sm italic text-muted-foreground">{group.parent.name}</span>
            {!readOnly ? (
              <>
                <Button variant="ghost" size="icon-sm" title="Modifica contenuto" onClick={() => onEditRtf(group)}>
                  <Pencil className="size-3.5" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  className="text-destructive hover:bg-destructive/10"
                  title="Rimuovi"
                  onClick={() => onRemove(group)}
                >
                  <Trash2 className="size-3.5" />
                </Button>
              </>
            ) : null}
          </div>
        ))
      )}
    </div>
  )
}

// ── Pannello info (auto-save) ──────────────────────────────

function InfoPanel({
  quote,
  readOnly,
  onSaved,
}: {
  quote: QuoteDto
  readOnly: boolean
  onSaved: () => void
}) {
  const [title, setTitle] = React.useState(quote.title)
  const [c1, setC1] = React.useState(quote.contactName1)
  const [c2, setC2] = React.useState(quote.contactName2)
  const [c3, setC3] = React.useState(quote.contactName3)
  const [delivery, setDelivery] = React.useState(String(quote.deliveryDays))
  const [validity, setValidity] = React.useState(String(quote.validityDays))
  const [payment, setPayment] = React.useState(quote.paymentType)
  const [notesInternal, setNotesInternal] = React.useState(quote.notesInternal)
  const [notesQuote, setNotesQuote] = React.useState(quote.notesQuote)
  const [pdf, setPdf] = React.useState({
    showItemPrices: quote.showItemPrices,
    showSummary: quote.showSummary,
    showSummaryPrices: quote.showSummaryPrices,
    hideQuantities: quote.hideQuantities,
  })

  function buildDto(overrides?: Partial<QuoteSaveDto>): QuoteSaveDto {
    return {
      quoteType: quote.quoteType,
      priceListId: quote.priceListId,
      title: title.trim(),
      customerId: quote.customerId,
      contactName1: c1.trim(),
      contactName2: c2.trim(),
      contactName3: c3.trim(),
      deliveryDays: Number.parseInt(delivery, 10) || 0,
      validityDays: Number.parseInt(validity, 10) || 60,
      paymentType: payment.trim(),
      language: quote.language,
      groupId: quote.groupId,
      discountPct: quote.discountPct,
      discountAbs: quote.discountAbs,
      showItemPrices: pdf.showItemPrices,
      showSummary: pdf.showSummary,
      showSummaryPrices: pdf.showSummaryPrices,
      hideQuantities: pdf.hideQuantities,
      notesInternal,
      notesQuote,
      assignedTo: quote.assignedTo,
      ...overrides,
    }
  }

  function save(overrides?: Partial<QuoteSaveDto>) {
    if (readOnly) return
    void updateQuote(quote.id, buildDto(overrides)).then(onSaved).catch((err: Error) => notifyError(err))
  }

  const fieldCls = "bg-muted/40"

  return (
    <Card>
      <CardContent className="grid gap-4 py-4 md:grid-cols-3">
        <div className="grid gap-1.5">
          <Label className="text-xs">Titolo *</Label>
          <Input className={fieldCls} value={title} readOnly={readOnly} onChange={(e) => setTitle(e.target.value)} onBlur={() => save()} />
        </div>
        <div className="grid gap-1.5">
          <Label className="text-xs">Cliente</Label>
          <Input value={quote.customerName} readOnly className="font-semibold text-[#2563EB]" />
        </div>
        <div className="grid gap-1.5">
          <Label className="text-xs">Template</Label>
          <Input value={quoteTypeLabel(quote.quoteType)} readOnly />
        </div>

        <div className="grid gap-1.5">
          <Label className="text-xs">Referente 1</Label>
          <Input className={fieldCls} value={c1} readOnly={readOnly} onChange={(e) => setC1(e.target.value)} onBlur={() => save()} />
        </div>
        <div className="grid gap-1.5">
          <Label className="text-xs">Referente 2</Label>
          <Input className={fieldCls} value={c2} readOnly={readOnly} onChange={(e) => setC2(e.target.value)} onBlur={() => save()} />
        </div>
        <div className="grid gap-1.5">
          <Label className="text-xs">Referente 3</Label>
          <Input className={fieldCls} value={c3} readOnly={readOnly} onChange={(e) => setC3(e.target.value)} onBlur={() => save()} />
        </div>

        <div className="grid gap-1.5">
          <Label className="text-xs">Tempi consegna (gg)</Label>
          <Input className={fieldCls} inputMode="numeric" value={delivery} readOnly={readOnly} onChange={(e) => setDelivery(e.target.value)} onBlur={() => save()} />
        </div>
        <div className="grid gap-1.5">
          <Label className="text-xs">Validità offerta (gg)</Label>
          <Input className={fieldCls} inputMode="numeric" value={validity} readOnly={readOnly} onChange={(e) => setValidity(e.target.value)} onBlur={() => save()} />
        </div>
        <div className="grid gap-1.5">
          <Label className="text-xs">Tipo pagamento</Label>
          <Input className={fieldCls} value={payment} readOnly={readOnly} onChange={(e) => setPayment(e.target.value)} onBlur={() => save()} />
        </div>

        {/* Note */}
        <div className="grid gap-1.5 md:col-span-1">
          <Label className="text-xs font-bold">NOTE AD USO INTERNO</Label>
          <Textarea className={fieldCls} rows={4} value={notesInternal} readOnly={readOnly} onChange={(e) => setNotesInternal(e.target.value)} onBlur={() => save()} />
        </div>
        <div className="grid gap-1.5 md:col-span-1">
          <Label className="text-xs font-bold">NOTE SU PREVENTIVO</Label>
          <Textarea className={fieldCls} rows={4} value={notesQuote} readOnly={readOnly} onChange={(e) => setNotesQuote(e.target.value)} onBlur={() => save()} />
        </div>

        {/* Opzioni PDF */}
        <div className="grid gap-2 md:col-span-1">
          <Label className="text-xs font-bold">OPZIONI STAMPA</Label>
          {([
            ["showItemPrices", "Prezzi sulle pagine articoli"],
            ["showSummary", "Attiva il riepilogo finale"],
            ["showSummaryPrices", "Prezzi articoli nel riepilogo"],
            ["hideQuantities", "Nascondi dettagli nel riepilogo"],
          ] as const).map(([key, label]) => (
            <label key={key} className="flex items-center gap-2 text-sm">
              <Checkbox
                checked={pdf[key]}
                disabled={readOnly}
                onCheckedChange={(v) => {
                  const next = { ...pdf, [key]: v === true }
                  setPdf(next)
                  save({
                    showItemPrices: next.showItemPrices,
                    showSummary: next.showSummary,
                    showSummaryPrices: next.showSummaryPrices,
                    hideQuantities: next.hideQuantities,
                  })
                }}
              />
              {label}
            </label>
          ))}
        </div>
      </CardContent>
    </Card>
  )
}

// ── Sconto + totali SERVICE ────────────────────────────────

function ServiceTotals({
  quote,
  readOnly,
  onSaved,
}: {
  quote: QuoteDto
  readOnly: boolean
  onSaved: () => void
}) {
  const [discount, setDiscount] = React.useState(quote.discountPct.toFixed(2))
  const discountAmount = (quote.subtotal * quote.discountPct) / 100 + quote.discountAbs

  function saveDiscount() {
    if (readOnly) return
    void updateQuoteField(quote.id, "discount_pct", String(parseDecimal(discount)))
      .then(onSaved)
      .catch((err: Error) => notifyError(err))
  }

  const Row = ({ label, value, strong, color }: { label: string; value: string; strong?: boolean; color?: string }) => (
    <div className="flex items-center justify-between gap-6">
      <span className={strong ? "font-bold" : "text-muted-foreground"} style={color ? { color } : undefined}>
        {label}
      </span>
      <span className={strong ? "font-bold tabular-nums" : "tabular-nums"} style={color ? { color } : undefined}>
        {value}
      </span>
    </div>
  )

  return (
    <div className="flex flex-wrap items-start justify-between gap-6 border-t pt-4">
      <div className="flex items-center gap-2">
        <span className="text-sm text-muted-foreground">Sconto sul TOTALE:</span>
        <Input
          className="h-8 w-20 text-right"
          inputMode="decimal"
          value={discount}
          readOnly={readOnly}
          onChange={(e) => setDiscount(e.target.value)}
          onBlur={saveDiscount}
        />
        <span className="text-sm text-muted-foreground">%</span>
      </div>
      <div className="w-72 space-y-1 text-sm">
        <Row label="TOTALE" value={`${fmt2(quote.subtotal)} €`} />
        <Row label="TOTALE IVA" value={`${fmt2(quote.vatTotal)} €`} />
        <Row label="SCONTO" value={discountAmount > 0 ? `-${fmt2(discountAmount)} €` : "0,00 €"} color="#DC2626" />
        <div className="border-t pt-1">
          <Row label="TOTALE IMPONIBILE" value={`${fmt2(quote.total)} €`} strong />
        </div>
        <Row label="TOTALE IVA INCLUSA" value={`${fmt2(quote.totalWithVat)} €`} />
        <div className="border-t pt-1">
          <Row label="TOTALE COSTI AZIENDALI" value={`${fmt2(quote.costTotal)} €`} strong color="#2563EB" />
        </div>
        <Row label="UTILE" value={`${fmt2(quote.profit)} €`} strong color="#2563EB" />
      </div>
    </div>
  )
}

// ── Card prodotto SERVICE ──────────────────────────────────

function ServiceProductCard({
  group,
  readOnly,
  onSaveVariant,
  onDeleteVariant,
  onAddVariant,
  onRemoveProduct,
  onCloneProduct,
  onEditRtf,
  onRenameProduct,
}: {
  group: ProductGroup
  readOnly: boolean
  onSaveVariant: (itemId: number, dto: import("@/lib/api/types").QuoteItemSaveDto) => void
  onDeleteVariant: (itemId: number) => void
  onAddVariant: () => void
  onRemoveProduct: () => void
  onCloneProduct: () => void
  onEditRtf: () => void
  onRenameProduct: (name: string) => void
}) {
  const [expanded, setExpanded] = React.useState(true)
  const [name, setName] = React.useState(group.parent.name)
  React.useEffect(() => setName(group.parent.name), [group.parent.name])

  const isContent = group.parent.itemType === "content"
  const total = group.variants.filter((v) => v.isActive).reduce((acc, v) => acc + v.lineTotal, 0)

  return (
    <div className="rounded-md border">
      <div className="flex items-center gap-2 border-b bg-muted/40 px-3 py-2">
        <span
          className="rounded px-1.5 py-0.5 text-[10px] font-bold text-white"
          style={{ backgroundColor: isContent ? "#7C3AED" : "#2563EB" }}
        >
          {isContent ? "Cont." : "Prod."}
        </span>
        <Input
          value={name}
          readOnly={readOnly}
          className="h-7 flex-1 border-transparent bg-transparent font-semibold focus-visible:border-input"
          onChange={(e) => setName(e.target.value)}
          onBlur={() => name !== group.parent.name && onRenameProduct(name)}
        />
        {total > 0 ? <span className="font-bold tabular-nums">{fmt2(total)}€</span> : null}
        {!readOnly ? (
          <>
            <Button variant="ghost" size="icon-sm" title="Aggiungi variante" onClick={onAddVariant}>
              <Plus className="size-3.5" />
            </Button>
            <Button variant="ghost" size="icon-sm" title="Modifica descrizione" onClick={onEditRtf}>
              <Pencil className="size-3.5" />
            </Button>
            <Button variant="ghost" size="icon-sm" title="Duplica prodotto" onClick={onCloneProduct}>
              <Copy className="size-3.5" />
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              className="text-destructive hover:bg-destructive/10"
              title="Rimuovi prodotto"
              onClick={onRemoveProduct}
            >
              <Trash2 className="size-3.5" />
            </Button>
          </>
        ) : null}
        <Button variant="ghost" size="icon-sm" title="Espandi" onClick={() => setExpanded((v) => !v)}>
          {expanded ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}
        </Button>
      </div>

      {expanded ? (
        <div>
          <div className="grid grid-cols-[40px_1fr_70px_100px_100px_70px_110px_36px] gap-1 border-b bg-muted/20 px-3 py-1 text-[10px] font-bold text-muted-foreground">
            <span />
            <span>DESCRIZIONE</span>
            <span className="text-right">QTÀ</span>
            <span className="text-right">COSTO UNIT.</span>
            <span className="text-right">COSTO TOT.</span>
            <span className="text-center">K</span>
            <span className="text-right">VENDITA</span>
            <span />
          </div>
          {group.variants.map((variant) => (
            <VariantRow
              key={variant.id}
              variant={variant}
              readOnly={readOnly}
              onSave={(dto) => onSaveVariant(variant.id, dto)}
              onDelete={() => onDeleteVariant(variant.id)}
            />
          ))}
          {group.variants.length === 0 ? (
            <p className="px-3 py-2 text-xs text-muted-foreground">Nessuna variante.</p>
          ) : null}
        </div>
      ) : null}
    </div>
  )
}

// ── Riga variante editabile ────────────────────────────────

function VariantRow({
  variant,
  readOnly,
  onSave,
  onDelete,
}: {
  variant: QuoteItemDto
  readOnly: boolean
  onSave: (dto: import("@/lib/api/types").QuoteItemSaveDto) => void
  onDelete: () => void
}) {
  const initialMarkup = variant.costPrice > 0 ? variant.sellPrice / variant.costPrice : 1
  const [name, setName] = React.useState(variant.name)
  const [qty, setQty] = React.useState(String(variant.quantity))
  const [cost, setCost] = React.useState(variant.costPrice.toFixed(2))
  const [markup, setMarkup] = React.useState(initialMarkup.toFixed(3))
  const [active, setActive] = React.useState(variant.isActive)

  function save(overrides?: Partial<QuoteItemDto>) {
    if (readOnly) return
    const q = parseDecimal(qty)
    const c = parseDecimal(cost)
    const k = parseDecimal(markup)
    onSave(
      itemDto(variant, {
        name,
        quantity: q,
        costPrice: c,
        sellPrice: c * k,
        isActive: active,
        ...overrides,
      })
    )
  }

  const costTot = parseDecimal(cost) * parseDecimal(qty)

  return (
    <div
      className="grid grid-cols-[40px_1fr_70px_100px_100px_70px_110px_36px] items-center gap-1 border-b px-3 py-1.5 last:border-b-0"
      style={active ? undefined : { opacity: 0.5 }}
    >
      <Checkbox
        checked={active}
        disabled={readOnly}
        onCheckedChange={(v) => {
          const next = v === true
          setActive(next)
          save({ isActive: next })
        }}
      />
      <Input
        value={name}
        readOnly={readOnly}
        className="h-7 border-transparent bg-transparent text-xs focus-visible:border-input"
        onChange={(e) => setName(e.target.value)}
        onBlur={() => save()}
      />
      <Input
        value={qty}
        readOnly={readOnly}
        className="h-7 text-right text-xs"
        onChange={(e) => setQty(e.target.value)}
        onBlur={() => save()}
      />
      <Input
        value={cost}
        readOnly={readOnly}
        className="h-7 text-right text-xs"
        onChange={(e) => setCost(e.target.value)}
        onBlur={() => save()}
      />
      <span className="text-right text-xs text-muted-foreground tabular-nums">{fmt2(costTot)}</span>
      <Input
        value={markup}
        readOnly={readOnly}
        className="h-7 text-center text-xs"
        onChange={(e) => setMarkup(e.target.value)}
        onBlur={() => save()}
      />
      <span className="text-right text-xs font-semibold tabular-nums text-[#059669]">
        {active ? `${fmt2(variant.lineTotal)}€` : "—"}
      </span>
      {!readOnly ? (
        <Button
          variant="ghost"
          size="icon-sm"
          className="text-destructive hover:bg-destructive/10"
          title="Rimuovi variante"
          onClick={onDelete}
        >
          <Trash2 className="size-3.5" />
        </Button>
      ) : (
        <span />
      )}
    </div>
  )
}
