/** Commerciale: preventivi e catalogo preventivi — allineati a ATEC.PM.Shared/DTOs. */

// ══════════════════════════════════════════════════════════
// Commerciale — Catalogo Preventivi (Fase D)
// Listini → Gruppi → Categorie (albero, parentId) → Prodotti → Varianti.
// Allineato a ATEC.PM.Shared/DTOs/Quote_DTOs.cs. Prezzo vendita variante = cost * markup.
// ══════════════════════════════════════════════════════════
export interface QuotePriceListDto {
  id: number
  name: string
  currency: string
  locale: string
  isActive: boolean
  sortOrder: number
  groupCount: number
}

export interface QuotePriceListSaveDto {
  name: string
  currency: string
  locale: string
  isActive: boolean
  sortOrder: number
}

export interface QuoteProductVariantDto {
  id: number
  productId: number
  code: string
  name: string
  costPrice: number
  markupValue: number
  /** Calcolato server-side: costPrice * markupValue. */
  sellPrice: number
  sortOrder: number
}

export interface QuoteProductDto {
  id: number
  categoryId: number
  categoryName: string
  groupName: string
  itemType: string // "product" | "content"
  code: string
  name: string
  /** HTML/RTF; può contenere <img src="/uploads/cms/products/..."> relativi. */
  descriptionRtf: string
  imagePath: string
  attachmentPath: string
  autoInclude: boolean
  sortOrder: number
  isActive: boolean
  variants: QuoteProductVariantDto[]
}

export interface QuoteCategoryDto {
  id: number
  groupId: number
  parentId: number | null
  groupName: string
  name: string
  description: string
  sortOrder: number
  isActive: boolean
  productCount: number
  children: QuoteCategoryDto[]
  products: QuoteProductDto[]
}

export interface QuoteGroupDto {
  id: number
  priceListId: number | null
  priceListName: string
  name: string
  description: string
  sortOrder: number
  isActive: boolean
  categoryCount: number
  productCount: number
  categories: QuoteCategoryDto[]
}

export interface QuoteCatalogTreeDto {
  groups: QuoteGroupDto[]
  totalGroups: number
  totalCategories: number
  totalProducts: number
}

export interface QuoteGroupSaveDto {
  priceListId: number | null
  name: string
  description: string
  sortOrder: number
  isActive: boolean
}

export interface QuoteCategorySaveDto {
  groupId: number
  parentId: number | null
  name: string
  description: string
  sortOrder: number
  isActive: boolean
}

export interface CategoryMoveRequest {
  newParentId: number | null
  newGroupId: number
}

export interface ProductMoveRequest {
  categoryId: number
}

export interface QuoteProductVariantSaveDto {
  /** 0 = nuova variante, >0 = aggiorna esistente. */
  id: number
  code: string
  name: string
  costPrice: number
  markupValue: number
  sortOrder: number
}

export interface QuoteProductSaveDto {
  categoryId: number
  itemType: string
  code: string
  name: string
  descriptionRtf: string
  imagePath: string
  attachmentPath: string
  autoInclude: boolean
  sortOrder: number
  isActive: boolean
  variants: QuoteProductVariantSaveDto[]
}

// Import catalogo (POST /api/quote-catalog/import): listini→gruppi→categorie→prodotti→varianti.
export interface QuoteCatalogImportVariant {
  code: string
  name: string
  description: string
  costPrice: number
  markupValue: number
}

export interface QuoteCatalogImportProduct {
  code: string
  name: string
  itemType: string
  description: string
  position: string
  variants: QuoteCatalogImportVariant[]
}

export interface QuoteCatalogImportCategory {
  name: string
  products: QuoteCatalogImportProduct[]
}

export interface QuoteCatalogImportGroup {
  name: string
  categories: QuoteCatalogImportCategory[]
}

export interface QuoteCatalogImportListino {
  name: string
  currency: string
  locale: string
  groups: QuoteCatalogImportGroup[]
}

export interface QuoteCatalogImportDto {
  priceLists: QuoteCatalogImportListino[]
}

// ══════════════════════════════════════════════════════════
// Commerciale — Preventivi (Fase D)
// quoteType: "SERVICE" | "IMPIANTO" (IMPIANTO consente costing + conversione a commessa).
// status: draft|sent|negotiation|accepted|rejected|expired|superseded|converted.
// ══════════════════════════════════════════════════════════
export interface QuoteItemDto {
  id: number
  quoteId: number
  productId: number | null
  variantId: number | null
  itemType: string // product | content | section | ...
  code: string
  name: string
  descriptionRtf: string
  unit: string
  quantity: number
  costPrice: number
  sellPrice: number
  discountPct: number
  vatPct: number
  lineTotal: number
  lineProfit: number
  sortOrder: number
  isActive: boolean
  isConfirmed: boolean
  parentItemId: number | null
  isAutoInclude: boolean
}

export interface QuoteDto {
  id: number
  quoteNumber: string
  title: string
  customerId: number
  customerName: string
  contactName1: string
  contactName2: string
  contactName3: string
  deliveryDays: number
  validityDays: number
  paymentType: string
  language: string
  status: string // vedi enum sopra
  quoteType: string // SERVICE | IMPIANTO
  revision: number
  parentQuoteId: number | null
  priceListId: number | null
  priceListName: string
  groupId: number | null
  groupName: string
  subtotal: number
  discountPct: number
  discountAbs: number
  vatTotal: number
  total: number
  totalWithVat: number
  /** Solo IMPIANTO (dal costing). */
  costTotal: number
  profit: number
  showItemPrices: boolean
  showSummary: boolean
  showSummaryPrices: boolean
  hideQuantities: boolean
  notesInternal: string
  notesQuote: string
  projectId: number | null
  projectCode: string
  assignedTo: number | null
  assignedToName: string
  createdBy: number
  createdByName: string
  createdAt: string
  updatedAt: string
  sentAt: string | null
  acceptedAt: string | null
  convertedAt: string | null
  items: QuoteItemDto[]
}

export interface QuoteSaveDto {
  quoteType: string // SERVICE | IMPIANTO
  priceListId: number | null
  title: string
  customerId: number
  contactName1: string
  contactName2: string
  contactName3: string
  deliveryDays: number
  validityDays: number
  paymentType: string
  language: string
  groupId: number | null
  discountPct: number
  discountAbs: number
  showItemPrices: boolean
  showSummary: boolean
  showSummaryPrices: boolean
  hideQuantities: boolean
  notesInternal: string
  notesQuote: string
  assignedTo: number | null
}

export interface QuoteItemSaveDto {
  productId: number | null
  variantId: number | null
  itemType: string
  code: string
  name: string
  descriptionRtf: string
  unit: string
  quantity: number
  costPrice: number
  sellPrice: number
  discountPct: number
  vatPct: number
  sortOrder: number
  isActive: boolean
  isConfirmed: boolean
  parentItemId: number | null
  isAutoInclude: boolean
}

export interface QuoteStatusChangeDto {
  newStatus: string
  notes: string
}

export interface QuoteConvertDto {
  pmId: number
}

export interface QuoteStatsDto {
  totalQuotes: number
  quotesDraft: number
  quotesSent: number
  quotesAccepted: number
  quotesRejected: number
  quotesConverted: number
  totalValue: number
  totalProfit: number
  conversionRate: number
  avgProfit: number
}
