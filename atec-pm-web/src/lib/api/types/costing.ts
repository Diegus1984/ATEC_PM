/** Costing commessa: preventivo vs consuntivo, sezioni di costo, prezzi — allineati a ATEC.PM.Shared/DTOs. */

export interface CostSectionGroupDto {
  id: number
  name: string
  bgColor: string
  textColor: string
  sortOrder: number
  isActive: boolean
}

export interface CostSectionGroupSaveRequest {
  id: number
  name: string
  bgColor?: string
  textColor?: string
  sortOrder: number
  isActive: boolean
}

// ── Preventivo vs Consuntivo (GET /api/projects/{id}/budget-vs-actual) ──────
// Confronto a 3 colonne: Preventivato (costing) / Assegnato (fasi) / Consuntivo (timesheet).
// Tutti i campi Total*/Delta*/*Cost calcolati sono read-only dal server.
export interface BvaBudgetResourceDto {
  resourceId: number
  employeeId: number | null
  resourceName: string
  workDays: number
  hoursPerDay: number
  totalHours: number
  hourlyCost: number
  totalCost: number
  markupValue: number
  totalSale: number
  numTrips: number
  kmPerTrip: number
  costPerKm: number
  dailyFood: number
  dailyHotel: number
  allowanceDays: number
  dailyAllowance: number
  travelCost: number
  accommodationCost: number
  allowanceCost: number
  totalTravelCost: number
}

export interface BvaActualDetailDto {
  workDate: string
  phaseName: string
  entryType: string
  hours: number
  hourlyCost: number
  totalCost: number
}

export interface BvaActualEmployeeDto {
  employeeName: string
  totalHours: number
  totalCost: number
  details: BvaActualDetailDto[]
}

export interface BvaSectionDto {
  sectionId: number
  sectionName: string
  sectionType: string // IN_SEDE | DA_CLIENTE
  templateId: number | null
  budgetHours: number
  budgetCost: number
  budgetSale: number
  budgetResources: BvaBudgetResourceDto[]
  assignedHours: number
  assignedCost: number
  actualHours: number
  actualCost: number
  actualEmployees: BvaActualEmployeeDto[]
  budgetTravelCost: number
  budgetAccommodationCost: number
  budgetAllowanceCost: number
  budgetTotalTravelCost: number
  deltaHours: number
  deltaCost: number
}

export interface BvaGroupDto {
  groupName: string
  color: string
  sortOrder: number
  sections: BvaSectionDto[]
  budgetHours: number
  budgetCost: number
  assignedHours: number
  assignedCost: number
  actualHours: number
  actualCost: number
}

export interface BvaMaterialItemDto {
  id: number
  parentItemId: number | null
  description: string
  quantity: number
  unitCost: number
  markupValue: number
  itemType: string
  netCost: number
  saleCost: number
}

export interface BvaMaterialSectionDto {
  sectionId: number
  sectionName: string
  markupValue: number
  commissionMarkup: number
  items: BvaMaterialItemDto[]
  totalNetCost: number
  totalSaleCost: number
}

export interface BvaPricingDto {
  netCost: number
  contingencyPct: number
  contingencyAmount: number
  offerPrice: number
  negotiationPct: number
  negotiationAmount: number
  finalPrice: number
}

export interface BvaEconomicSummary {
  /** Derivato dalla Scheda Prezzi, o l'override imputato a mano dal PM (#35). */
  finalOfferPrice: number
  /** true = `finalOfferPrice` è imputato a mano. Va dichiarato a video. */
  finalOfferPriceIsManual: boolean
  /** Totale Ordine = somma delle righe di `orderLines`. */
  orderPrice: number
  /**
   * «Totale Costi di Vendita» ≡ `totalBudgetSaleCost`. Dal 06/08/2026 (#34) lo CALCOLA il
   * server: non è più il campo digitato a mano che c'era nel footer dell'Ordine Commessa.
   * null = la commessa non ha ancora niente di preventivato.
   */
  saleTotal: number | null
  /**
   * «Margine di Sicurezza» = Totale Ordine − Totale Costi di Vendita.
   * Si chiamava «Delta Ordine» fino al 06/08/2026. null solo se mancano entrambi i termini.
   */
  orderDelta: number | null
  /** «TOTALE COSTI» (#34): colonna Netti di tutte le sezioni + officine a costo + trasferta. */
  totalBudgetNetCost: number
  /** «TOTALE COSTI DI VENDITA» (#34): colonna Vendita di tutte le sezioni + officine + trasferta. */
  totalBudgetSaleCost: number
  totalNetCost: number
  /** % di imprevisti della Scheda Prezzi — NON è il Margine di Sicurezza, nonostante il nome. */
  contingencyAmount: number
  budgetCost: number
  budgetResourceHours: number
  budgetResourceCost: number
  budgetMaterialCost: number
  /** «Lavorazioni Officine» a preventivo: somma delle righe della finestra di calcolo. */
  budgetWorkshopCost: number
  budgetWorkshopInternalCost: number
  budgetWorkshopExternalCost: number
  /** Vendita delle lavorazioni (costo × markup): entra nel costo netto della Scheda Prezzi. */
  budgetWorkshopSale: number
  /**
   * «Spese Trasferta / indennità» a preventivo, a COSTO SECCO:
   * = budgetTravelMarkableCost + budgetAllowanceCost.
   * Il K di ricarico della #33 è stato rimosso il 06/08/2026 (#34): da allora questa voce è
   * omogenea alle altre tre e ai due lati preventivo/consuntivo.
   */
  budgetTravelCost: number
  /** Spese di trasferta: alloggio, vitto, auto, treno/aereo. */
  budgetTravelMarkableCost: number
  /** Indennità a preventivo. */
  budgetAllowanceCost: number
  actualResourceHours: number
  actualResourceCost: number
  /** Solo materiali commerciali: le officine sono voce a sé. */
  actualMaterialCost: number
  actualWorkshopCost: number
  actualWorkshopInternalCost: number
  actualWorkshopExternalCost: number
  actualWorkshopUnclassifiedCost: number
  actualTravelCost: number
  /** true quando il costo trasferta lo decide la Gestione Trasferta, non il campo a mano. */
  actualTravelFromPlan: boolean
  actualTotalCost: number
  budgetProfitability: number
  budgetProfitabilityPct: number
  profitability: number
  profitabilityPct: number
  activeTechnicians: number
  totalPhases: number
  completedPhases: number
  progressPct: number
}

/** Riga della tabella «Ordine Commessa». */
export interface ProjectOrderLineDto {
  id: number
  orderRef: string
  orderPosition: string
  amount: number | null
  sortOrder: number
  rowVersion: number
}

export interface ProjectOrderLineSaveRequest {
  orderRef: string
  orderPosition: string
  amount: number | null
  rowVersion?: number | null
  /** Solo POST: inserisci subito sotto questa riga. Assente = in coda. */
  afterLineId?: number | null
}

/** Voce del «Riepilogo Costi», con i due valori affiancati. null = «—», non 0,00 €. */
export interface BvaCostLineDto {
  key: string
  label: string
  budget: number | null
  actual: number | null
  budgetHint: string
  actualHint: string
}

export interface BudgetVsActualData {
  projectId: number
  linkedQuoteId: number
  groups: BvaGroupDto[]
  totalBudgetHours: number
  totalBudgetCost: number
  totalAssignedHours: number
  totalAssignedCost: number
  totalActualHours: number
  totalActualCost: number
  materialSections: BvaMaterialSectionDto[]
  totalMaterialNetCost: number
  totalMaterialSaleCost: number
  pricing: BvaPricingDto | null
  economic: BvaEconomicSummary | null
  orderLines: ProjectOrderLineDto[]
  costLines: BvaCostLineDto[]
  /** Dettaglio del calcolo della voce «Lavorazioni Officine» a preventivo. */
  workshopBudgetSheet: ProjectCalcSheetDto
}

// ── Fogli di calcolo a righe ─────────────────────────────────────────────
// Il dettaglio dietro il totale di una voce di costo: prima si salvava solo il
// numero finale e il ragionamento si perdeva.

/** Chiavi dei fogli di calcolo (allineate a `CalcKeys` lato server). */
export const CALC_KEY_WORKSHOP_BUDGET = "lavorazioni.budget"

/** Sezioni del foglio «Lavorazioni Officine»: gli stessi valori di `work_type`. */
export const CALC_GROUP_EXTERNAL = "External"
export const CALC_GROUP_INTERNAL = "Internal"

export interface ProjectCalcRowDto {
  id: number
  groupKey: string
  description: string
  /** Ore (Officine interne). null nelle esterne, dove il costo è già l'importo di riga. */
  quantity: number | null
  /** Costo orario se c'è la quantità, altrimenti importo della riga. */
  unitCost: number | null
  /** Terzo fattore facoltativo (Auto = km × €/km × tratte). null vale 1. */
  multiplier: number | null
  amount: number | null
  /** Override manuale: l'importo digitato vince sul calcolo. */
  amountPinned: boolean
  /** Ricarico di VENDITA: moltiplicatore 1,000–9,999, non una percentuale. */
  markupValue: number
  /** Provenienza della riga automatica; "" = scritta a mano, non si rigenera. */
  linkedSource: string
  sortOrder: number
}

export interface ProjectCalcSheetDto {
  calcKey: string
  /** Concorrenza ottimistica dell'intero foglio. */
  rowVersion: number
  rows: ProjectCalcRowDto[]
  /** null se nessuna riga ha un importo → «—», non 0,00 €. */
  total: number | null
  saleTotal: number | null
}

export interface ProjectCalcSheetSaveRequest {
  rowVersion: number | null
  rows: ProjectCalcRowDto[]
}

/** Card della pagina /bilancio: i due KPI di redditività a consuntivo. */
export interface BilancioSummary {
  projectId: number
  code: string
  title: string
  customerName: string
  pmName: string
  status: string
  orderTotal: number
  actualResourceCost: number
  actualMaterialCost: number
  actualWorkshopCost: number
  actualTravelCost: number
  actualTotalCost: number
  /** «Consuntivo Redditività». null = non calcolabile (nessun ordine). */
  profitability: number | null
  /** «Consuntivo % Redditività». null = non calcolabile. */
  profitabilityPct: number | null
}

export interface BilancioSummaryResponse {
  /** Sotto questa percentuale (confronto stretto) la card diventa rossa. */
  thresholdPct: number
  projects: BilancioSummary[]
}

export interface BilancioSettings {
  thresholdPct: number
}

export interface CostSectionTemplateDto {
  id: number
  name: string
  sectionType: string
  groupId: number
  groupName: string
  isDefault: boolean
  isDefaultQuote: boolean
  sortOrder: number
  isActive: boolean
  departmentIds: number[]
  departmentCodes: string[]
}

export interface CostSectionTemplateSaveRequest {
  id: number
  name: string
  sectionType: string
  groupId: number
  isDefault: boolean
  isDefaultQuote: boolean
  sortOrder: number
  isActive: boolean
  departmentIds: number[]
}

// ══════════════════════════════════════════════════════════
// Commerciale — Costing preventivo IMPIANTO (Fase D5)
// Allineato a ProjectCosting_DTOs.cs. Endpoint /api/quotes/{id}/costing/*.
// I campi Total*/computed sono read-only dal server.
// ══════════════════════════════════════════════════════════
export interface ProjectCostResourceDto {
  id: number
  sectionId: number
  employeeId: number | null
  resourceName: string
  workDays: number
  hoursPerDay: number
  hourlyCost: number
  markupValue: number
  numTrips: number
  kmPerTrip: number
  costPerKm: number
  dailyFood: number
  dailyHotel: number
  allowanceDays: number
  dailyAllowance: number
  sortOrder: number
  totalHours: number
  totalCost: number
  totalSale: number
  travelTotal: number
  accommodationTotal: number
  allowanceTotal: number
}

export interface ProjectCostResourceSaveRequest {
  id: number
  sectionId: number
  employeeId: number | null
  resourceName: string
  workDays: number
  hoursPerDay: number
  hourlyCost: number
  markupValue: number
  numTrips: number
  kmPerTrip: number
  costPerKm: number
  dailyFood: number
  dailyHotel: number
  allowanceDays: number
  dailyAllowance: number
  sortOrder: number
}

export interface ProjectCostSectionDto {
  id: number
  projectId: number
  templateId: number | null
  name: string
  sectionType: string // IN_SEDE | DA_CLIENTE
  groupName: string
  groupColor: string
  sortOrder: number
  isEnabled: boolean
  contingencyPct: number
  marginPct: number
  contingencyPinned: boolean
  marginPinned: boolean
  isShadowed: boolean
  departmentIds: number[]
  resources: ProjectCostResourceDto[]
  totalHours: number
  totalCost: number
  totalSale: number
  totalTravel: number
}

export interface ProjectMaterialItemDto {
  id: number
  sectionId: number
  parentItemId: number | null
  productId: number | null
  variantId: number | null
  code: string
  description: string
  descriptionRtf: string | null
  quantity: number
  unitCost: number
  markupValue: number
  itemType: string
  sortOrder: number
  contingencyPct: number
  marginPct: number
  contingencyPinned: boolean
  marginPinned: boolean
  isShadowed: boolean
  isActive: boolean
  totalCost: number
  totalSale: number
}

export interface ProjectMaterialItemSaveRequest {
  id: number
  sectionId: number
  parentItemId: number | null
  productId: number | null
  variantId: number | null
  code: string
  description: string
  descriptionRtf: string | null
  quantity: number
  unitCost: number
  markupValue: number
  itemType: string
  sortOrder: number
  isActive: boolean
}

export interface ProjectMaterialSectionDto {
  id: number
  projectId: number
  categoryId: number | null
  name: string
  markupValue: number
  commissionMarkup: number
  sortOrder: number
  isEnabled: boolean
  items: ProjectMaterialItemDto[]
  totalCost: number
  totalSale: number
}

export interface ProjectPricingDto {
  id: number
  projectId: number
  contingencyPct: number
  negotiationMarginPct: number
  travelMarkup: number
  allowanceMarkup: number
}

export interface ProjectCostingData {
  projectId: number
  costSections: ProjectCostSectionDto[]
  materialSections: ProjectMaterialSectionDto[]
  pricing: ProjectPricingDto
  isInitialized: boolean
}

export interface AvailableCostTemplatesDto {
  groups: CostSectionGroupDto[]
  templates: CostSectionTemplateDto[]
}

export interface EmployeeCostLookup {
  id: number
  fullName: string
  departmentCode: string
  hourlyCost: number
  defaultMarkup: number
}

export interface PricingDistributionRow {
  id: number
  offerId: number
  sectionType: string // COST | MATERIAL
  sectionId: number
  sectionName: string
  saleAmount: number
  contingencyPct: number
  marginPct: number
  contingencyAmount: number
  marginAmount: number
  clientPrice: number
}

export interface SectionDistributionDto {
  contingencyPct: number
  marginPct: number
  contingencyPinned: boolean
  marginPinned: boolean
  isShadowed: boolean
}

export interface RebalanceRequest {
  fixedRowId: number
  field: string // contingency | margin
  newValue: number
}

export interface BatchDistributionItem {
  id: number
  contingencyPct: number
  marginPct: number
  contingencyPinned: boolean
  marginPinned: boolean
  isShadowed: boolean
}

export interface BatchDistributionRequest {
  sections?: BatchDistributionItem[]
  materialItems?: BatchDistributionItem[]
}

export interface SectionDepartmentsRequest {
  departmentIds: number[]
}
