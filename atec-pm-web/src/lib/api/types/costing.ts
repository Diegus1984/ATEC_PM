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
  finalOfferPrice: number
  orderPrice: number
  totalNetCost: number
  contingencyAmount: number
  budgetCost: number
  budgetResourceHours: number
  budgetResourceCost: number
  budgetMaterialCost: number
  budgetTravelCost: number
  actualResourceHours: number
  actualResourceCost: number
  actualMaterialCost: number
  actualTravelCost: number
  actualTotalCost: number
  profitabilityPct: number
  activeTechnicians: number
  totalPhases: number
  completedPhases: number
  progressPct: number
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
