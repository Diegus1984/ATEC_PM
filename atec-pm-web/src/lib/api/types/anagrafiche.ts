/** Anagrafiche: clienti, fornitori, catalogo articoli — allineati a ATEC.PM.Shared/DTOs. */

export interface CustomerListItem {
  id: number
  companyName: string
  contactName: string
  email: string
  pec: string
  phone: string
  cell: string
  vatNumber: string
  fiscalCode: string
  sdiCode: string
  address: string
  notes: string
  paymentTerms: string
  isActive: boolean
}

export interface CustomerSaveRequest {
  id: number
  companyName: string
  contactName: string
  email: string
  pec: string
  phone: string
  cell: string
  address: string
  vatNumber: string
  fiscalCode: string
  paymentTerms: string
  sdiCode: string
  notes: string
  isActive: boolean
}

/**
 * Il fornitore come lo mostra una **combo** (`GET /api/suppliers/lookup`): ragione sociale,
 * referente (che le combo scrivono come sottotitolo) e flag attivo. Niente email, telefono,
 * P.IVA o codice fiscale — ed è per questo che l'endpoint resta aperto a tutti.
 */
export interface SupplierLookupItem {
  id: number
  companyName: string
  contactName: string
  isActive: boolean
}

export interface SupplierListItem {
  id: number
  companyName: string
  contactName: string
  email: string
  phone: string
  vatNumber: string
  fiscalCode: string
  isActive: boolean
}

export interface SupplierSaveRequest {
  id: number
  companyName: string
  contactName: string
  email: string
  phone: string
  address: string
  vatNumber: string
  fiscalCode: string
  notes: string
  isActive: boolean
}

export interface CatalogItemListItem {
  id: number
  code: string
  description: string
  category: string
  subcategory: string
  unit: string
  unitCost: number | null
  listPrice: number | null
  supplierId: number | null
  supplierName: string
  supplierCode: string
  manufacturer: string
  /** Mapping Danea↔ATEC (Extra1): codice NUOVO Codex associato, "" = non associato. */
  atecCode: string
  codexItemId: number | null
  easyfattId: number | null
}

export interface CatalogItemSaveRequest {
  id: number
  code: string
  description: string
  category: string
  subcategory: string
  unit: string
  unitCost: number
  listPrice: number
  supplierId: number | null
  supplierCode: string
  manufacturer: string
  barcode: string
  notes: string
  isActive: boolean
}
