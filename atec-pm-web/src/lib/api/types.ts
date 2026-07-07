/** Tipi allineati a ATEC.PM.Shared/DTOs — aggiornare con openapi-typescript quando Swagger è attivo. */

export interface ApiResponse<T> {
  success: boolean
  data?: T
  message: string
}

export interface LoginRequest {
  username: string
  password: string
}

export interface LoginResponse {
  token: string
  employeeId: number
  fullName: string
  userRole: string
  mustChangePassword: boolean
}

export interface ChangePasswordRequest {
  /** Valorizzato solo per il cambio password dalla schermata di login (senza sessione). */
  username: string
  currentPassword: string
  newPassword: string
  confirmNewPassword: string
}

export interface SessionStatusDto {
  employeeId: number
  isActive: boolean
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  hasMore: boolean
}

export interface AuthLevelDto {
  id: number
  levelValue: number
  roleName: string
  displayName: string
  sortOrder: number
}

export interface AuthFeatureDto {
  id: number
  featureKey: string
  displayName: string
  category: string
  minLevel: number
  behavior: string
}

export interface AuthFeaturesContextDto {
  userLevel: number
  features: AuthFeatureDto[]
  levels: AuthLevelDto[]
}

export interface DashboardData {
  activeProjects: number
  draftProjects: number
  completedProjects: number
  totalEmployees: number
  totalCustomers: number
  hoursThisMonth: number
  hoursThisWeek: number
  totalRevenue: number
  recentProjects: DashboardProjectRow[]
  dailyHours: DashboardDailyHoursPoint[]
}

export interface DashboardDailyHoursPoint {
  workDate: string
  hours: number
}

export interface DashboardProjectRow {
  code: string
  title: string
  customerName: string
  status: string
  hoursWorked: number
  budgetHours: number
}

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
  unit: string
  unitCost: number
  listPrice: number
  supplierId: number | null
  supplierName: string
  supplierCode: string
  manufacturer: string
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

export interface CompositionTreeNode {
  compositionId: number
  codexId: number
  catalogId: number | null
  codice: string
  descr: string
  source: string // "codex" | "catalog"
  children: CompositionTreeNode[]
}

export interface AddCompositionRequest {
  parentCodexId: number
  childCodexId: number | null
  childCatalogId: number | null
  quantity: number
}

/** Notifica real-time (SignalR `CompositionChanged`, hub `/hubs/codex`) di modifica composizione Codex. */
export interface CompositionChange {
  parentCodexId: number
  action: string // create | delete
  compositionId: number
}

export interface UserListItem {
  id: number
  fullName: string
  email: string
  userRole: string
  status: string
  hasCredentials: boolean
  username: string
  departmentCodes: string[]
  competenceCodes: string[]
}

export interface EmployeeDepartmentItem {
  id: number
  departmentId: number
  departmentCode: string
  departmentName: string
  isResponsible: boolean
  isPrimary: boolean
}

export interface EmployeeCompetenceItem {
  id: number
  departmentId: number
  departmentCode: string
  departmentName: string
  notes: string
}

export interface UserDetailDto {
  id: number
  fullName: string
  userRole: string
  username: string
  departments: EmployeeDepartmentItem[]
  competences: EmployeeCompetenceItem[]
}

export interface EmployeeSaveRequest {
  id: number
  firstName: string
  lastName: string
  email: string
  empType: string
  supplierId: number | null
  status: string
}

export interface TimesheetEntryDto {
  id: number
  employeeId: number
  projectPhaseId: number
  workDate: string
  hours: number
  entryType: string
  notes: string
  phaseDisplay: string
}

export interface ProjectListItem {
  id: number
  code: string
  title: string
  customerName: string
  pmName: string
  status: string
  priority: string
  startDate: string | null
  endDatePlanned: string | null
  revenue: number
  budgetHoursTotal: number
  linkedQuoteId: number
}

/** Richiesta crea/modifica commessa (POST /api/projects, PUT /api/projects/{id}) e
 *  shape di lettura singola commessa (GET /api/projects/{id}). */
export interface ProjectSaveRequest {
  id: number
  code: string
  title: string
  customerId: number
  pmId: number
  description: string
  startDate: string | null
  endDatePlanned: string | null
  budgetTotal: number
  budgetHoursTotal: number
  revenue: number
  status: string
  priority: string
  serverPath: string
  notes: string
  createDefaultPhases: boolean
  linkedQuoteId: number
}

export interface ProjectDashboardData {
  code: string
  title: string
  customerName: string
  pmName: string
  status: string
  priority: string
  startDate: string | null
  endDatePlanned: string | null
  description: string
  serverPath: string
  notes: string
  budgetTotal: number
  budgetHoursTotal: number
  revenue: number
  hoursWorked: number
  costWorked: number
  totalPhases: number
  completedPhases: number
  materialCost: number
  totalCost: number
  departmentSummaries: DeptSummary[]
  recentEntries: RecentTimesheetEntry[]
  activeTechnicians: ActiveTechSummary[]
  weeklyHours: WeeklyHoursSummary[]
  phaseGantt: PhaseGanttItem[]
  deadlines: UpcomingDeadline[]
}

export interface ActiveTechSummary {
  employeeName: string
  departmentCode: string
  totalHours: number
  phaseCount: number
}

export interface WeeklyHoursSummary {
  year: number
  week: number
  hours: number
  weekLabel: string
}

export interface PhaseGanttItem {
  phaseId: number
  phaseName: string
  departmentCode: string
  status: string
  progressPct: number
  budgetHours: number
  hoursWorked: number
  startDate: string | null
  endDate: string | null
  sortOrder: number
}

export interface UpcomingDeadline {
  phaseName: string
  departmentCode: string
  deadline: string
  daysRemaining: number
  status: string
  progressPct: number
}

export interface CashFlowCategory {
  id: number
  name: string
  totalAmount: number
  notes: string
  sortOrder: number
  linkedSource: string | null
  isLinked: boolean
}

export interface CashFlowDataItem {
  dataType: string
  refId: number
  monthNumber: number
  numValue: number
  dateValue: string | null
}

export interface CashFlowData {
  projectId: number
  projectCode: string
  projectRevenue: number
  startDate: string | null
  paymentAmount: number
  monthCount: number
  isInitialized: boolean
  categories: CashFlowCategory[]
  dataItems: CashFlowDataItem[]
}

export interface OfficinaItem {
  id: number
  projectId: number
  partNumber: string
  description: string
  rowNumber: number
  quantity: number
  unitCost: number
  totalCost: number
  material: string
  treatment: string
  supplierName: string
  itemStatus: string
  requestedBy: string
  daneaRef: string
  dateNeeded: string | null
  destination: string
  destinationSpec: string
  notes: string
  createdAt: string | null
  updatedAt: string | null
}

export interface OfficinaItemSaveRequest {
  id: number
  projectId: number
  partNumber: string
  description: string
  quantity: number
  unitCost: number
  material: string
  treatment: string
  supplierName: string
  itemStatus: string
  requestedBy: string
  daneaRef: string
  dateNeeded: string | null
  destination: string
  destinationSpec: string
  notes: string
  expectedUpdatedAt?: string | null
}

export interface ChatListItem {
  id: number
  projectId: number
  title: string
  createdByName: string
  createdAt: string
  participantCount: number
  messageCount: number
  lastMessageAt: string | null
  lastMessagePreview: string
  unreadCount: number
}

export interface ChatMessage {
  id: number
  employeeId: number
  employeeName: string
  employeeInitials: string
  message: string
  createdAt: string
  isMine: boolean
  hasAttachment: boolean
  attachmentName: string
  attachmentPath: string
}

export interface ChatParticipant {
  id: number
  employeeId: number
  employeeName: string
}

export interface DeptSummary {
  departmentCode: string
  departmentName: string
  costingHours: number
  assignedHours: number
  hoursWorked: number
  budgetHours: number
  totalPhases: number
  completedPhases: number
}

export interface RecentTimesheetEntry {
  employeeName: string
  phaseName: string
  workDate: string
  hours: number
  entryType: string
  notes: string
}

export interface FileTreeItem {
  name: string
  isFolder: boolean
  size: number
  relativePath: string
  modified: string | null
  children: FileTreeItem[]
}

/** Voce (file o cartella) del contenuto di una sotto-cartella commessa
 *  (GET /api/projects/{id}/files?subPath=). Lista piatta, non ad albero. */
export interface FileItem {
  name: string
  isFolder: boolean
  size: number
  relativePath: string
  modified: string | null
}

export interface TimesheetSaveRequest {
  id: number
  employeeId: number
  projectPhaseId: number
  workDate: string
  hours: number
  entryType: string
  notes: string
}

export interface TimesheetPhaseOption {
  phaseId: number
  display: string
}

export interface TimesheetProjectOption {
  projectId: number
  display: string
}

export interface LookupItem {
  id: number
  name: string
}

// ── Gestione Risorse (Planner / Ferie) ──────────────────────────
// Allineato a ATEC.PM.Shared/DTOs/Resources_DTOs.cs (serializzazione camelCase).
// Le date arrivano come ISO datetime ("2026-06-30T00:00:00"); lato client si usa la
// porzione yyyy-MM-dd (date "pure", nessun fuso). Tipi: OP | FLEX | FERIE.

export type ResTipo = "OP" | "FLEX" | "FERIE"

export interface ResAssignmentDto {
  id: number
  employeeId: number
  employeeName: string
  tipo: ResTipo
  dataInizio: string
  dataFine: string
  projectId: number | null
  projectCode: string | null
  projectTitle: string | null
  serviceId: number | null
  serviceCod: string | null
  otherActivityId: number | null
  otherActivityDesc: string | null
  descrizione: string | null
  hasConflict: boolean
  updatedBy: number | null
  updatedByName: string | null
  updatedAt: string | null
  giorni: number
}

export interface ResAssignmentCreateRequest {
  employeeIds: number[]
  tipo: ResTipo
  dataInizio: string
  dataFine: string
  projectId?: number | null
  serviceId?: number | null
  otherActivityId?: number | null
  descrizione?: string | null
}

export interface ResAssignmentUpdateRequest {
  employeeId: number
  tipo: ResTipo
  dataInizio: string
  dataFine: string
  projectId?: number | null
  serviceId?: number | null
  otherActivityId?: number | null
  descrizione?: string | null
  /** Versione (updated_at) vista all'apertura: il server risponde 409 se cambiata. */
  expectedUpdatedAt?: string | null
}

export interface ResAssignmentChange {
  action: string // create | update | delete
  ids: number[]
}

/** SignalR "PresenceChanged": chi ha almeno un client Gantt connesso in questo momento. */
export interface PresenceSnapshot {
  onlineEmployeeIds: number[]
}

export interface ResServiceDto {
  id: number
  cod: string
  cliente: string | null
  isActive: boolean
  display: string
}

export interface ResServiceSaveRequest {
  cod: string
  cliente?: string | null
}

export interface ResOtherActivityDto {
  id: number
  descrizione: string
  isActive: boolean
}

export interface ResOtherActivitySaveRequest {
  descrizione: string
}

// ── Digest email (riepilogo modifiche piano risorse) ────────────

export interface PlanChangeLine {
  assignmentId: number
  kind: string // new | changed | deleted
  attivita: string
  periodo: string
  note: string | null
  autoreNome: string | null
}

export interface NotifyPendingEmployee {
  employeeId: number
  employeeName: string
  nuove: number
  modificate: number
  cancellate: number
  hasEmail: boolean
}

export interface NotifyPendingDto {
  totalChanges: number
  emailConfigurata: boolean
  employees: NotifyPendingEmployee[]
}

export interface NotifySendResultDto {
  emailInviate: number
  dipendentiSenzaEmail: number
  notifiedNames: string[]
  baselineCreated: boolean
  message: string
  responsabiliNotificati: number
  responsabiliNotificatiNomi: string[]
  pmNotificati: number
  pmNotificatiNomi: string[]
}

export interface SelectivePerson {
  employeeId: number
  employeeName: string
  hasEmail: boolean
  righe: PlanChangeLine[]
}

export interface SelectivePreviewDto {
  dipendenti: SelectivePerson[]
}

export interface SendSelectedRequest {
  assignmentIds: number[]
}

export interface PlanDigestSettingsDto {
  digestEnabled: boolean
  digestTime: string
  digestWeekends: boolean
  digestLastRun: string
}

export interface DigestLogEntry {
  runUtc: string
  trigger: string
  emailInviate: number
  senzaEmail: number
  esito: string
}

export interface DigestStatusDto {
  serverTimeLocal: string
  serverTimeUtc: string
  settings: PlanDigestSettingsDto
  emailConfigurata: boolean
  attivitaNelPiano: number
  dipendentiConEmail: number
  dipendentiSenzaEmail: number
  ultimeEsecuzioni: DigestLogEntry[]
}

export interface EmailSettingsDto {
  enabled: boolean
  smtpHost: string
  smtpPort: number
  security: string // auto | ssl | starttls | none
  from: string
  fromName: string
  username: string
  password: string | null
  hasPassword: boolean
  webUrl: string
}

export interface TestEmailRequest {
  toEmail: string
}

export interface MoMListItem {
  id: number
  tipo: string
  projectId: number | null
  projectCode: string | null
  projectTitle?: string | null
  title: string
  meetingDate: string | null
  inDashboard: boolean
  rev: number
  itemsCount: number
  openCount: number
  p1Count: number
  p2Count: number
  p3Count: number
  periodStart: string | null
  periodEnd: string | null
}

export interface MoMActionItem {
  id: number
  momId: number
  attivita: string
  descrizione: string | null
  azione: string | null
  priorita: number
  status: string // OPEN | STANDBY | CLOSED
  isCritical: boolean
  resp1Id: number | null
  resp1Name: string | null
  resp2Id: number | null
  resp2Name: string | null
  resp3Id: number | null
  resp3Name: string | null
  dataCheck: string | null
  dataClose: string | null
  sortOrder: number
  rowVersion: number
  responsibleNames: string[]
  responsibleIds: number[]
}

/** Riga dello storico revisioni (cambio data riunione confermato). */
export interface MoMRevision {
  rev: number
  meetingDate: string | null
  prevDate: string | null
  createdAt: string
}

export interface MoMDetail {
  id: number
  tipo: string
  projectId: number | null
  projectCode: string | null
  projectTitle: string | null
  title: string
  meetingDate: string | null
  inDashboard: boolean
  rev: number
  items: MoMActionItem[]
  revisions: MoMRevision[]
}

export interface MoMSaveRequest {
  tipo: string
  projectId: number | null
  title: string
  meetingDate: string | null
  inDashboard: boolean
}

export interface MoMActionItemSaveRequest {
  attivita: string
  descrizione: string | null
  azione: string | null
  priorita: number
  status: string
  isCritical: boolean
  resp1Id: number | null
  resp2Id: number | null
  resp3Id: number | null
  responsibleIds: number[]
  dataCheck: string | null
  dataClose: string | null
  /** Token concorrenza ottimistica: il server rifiuta se la riga è cambiata. */
  rowVersion?: number | null
}

/** Nota di acquisizione rapida (vista «Note MoM», staging personale). */
export interface MoMNote {
  id: number
  note: string
  targetMomId: number | null
}

export interface MoMNoteSaveRequest {
  note: string
  targetMomId: number | null
}

/** Payload dell'evento SignalR MoMChanged (gruppo mom-all). */
export interface MoMChange {
  momId: number
  action: string
}

export interface MoMProjectLookup {
  id: number
  code: string
  title: string
  display: string
}

// ── Check list / Attività ───────────────────────────────
// Priorità 0–3 (come nel prototipo): 0=Critica · 1=Alta · 2=Media · 3=Bassa.

/** Stato attività, allineato al MoM: aperta · in standby · chiusa (gestita). */
export type ChecklistStatus = "OPEN" | "STANDBY" | "CLOSED"

export interface ChecklistItem {
  id: number
  projectId: number | null
  groupId: number | null
  description: string
  priority: number
  dueDate: string | null // ISO date
  isCritical: boolean
  status: ChecklistStatus
  dataClose: string | null // ISO date, valorizzata quando status=CLOSED
  sortOrder: number
  rowVersion: number
  createdAt: string | null // ISO datetime, sola lettura
}

export interface ChecklistGroup {
  id: number
  name: string
  sortOrder: number
  rowVersion: number
  items: ChecklistItem[]
}

export interface ChecklistProject {
  projectId: number
  code: string
  title: string
  display: string
  items: ChecklistItem[]
}

export interface ChecklistBoard {
  projects: ChecklistProject[]
  groups: ChecklistGroup[]
}

export interface ChecklistItemSaveRequest {
  projectId: number | null
  groupId: number | null
  description: string
  priority: number
  dueDate: string | null
  isCritical: boolean
  status?: ChecklistStatus
  rowVersion?: number | null
}

export interface ChecklistGroupSaveRequest {
  name: string
  rowVersion?: number | null
}

export interface ChecklistInboxItem {
  id: number
  text: string
  sortOrder: number
}

export interface ChecklistInboxSaveRequest {
  text: string
}

export interface ChecklistAssignRequest {
  projectId: number | null
  groupId: number | null
}

export interface ChecklistProjectLookup {
  id: number
  code: string
  title: string
  display: string
}

/** Payload dell'evento SignalR ChecklistChanged (gruppo checklist-all + project-{id}). */
export interface ChecklistChange {
  action: string
  projectId: number | null
}

export interface DdpProjectSummary {
  projectId: number
  code: string
  customerName: string
  ddpType: string // COMMERCIAL | OFFICINA
  totalRows: number
  totalValue: number
  datedCount: number
  overdueCount: number
  deliveryStart: string | null
  deliveryEnd: string | null
  lastInsertedAt: string | null
  statusCounts?: DdpStatusCount[]
}

export interface DdpStatusCount {
  statusKey: string
  count: number
}

export interface DdpProjectDetail {
  projectId: number
  code: string
  customerName: string
  totalRows: number
  totalValue: number
  datedCount: number
  overdueCount: number
  deliveryStart: string | null
  deliveryEnd: string | null
  statusCounts: DdpStatusCount[]
}

/** Feedback Acquisti (aggregato su tutte le commesse): una riga per stato dell'aggregazione A6. */
export interface DdpFeedbackAcquistiGroup {
  projectId: number
  code: string
  customerName: string
  ddpType: string // "COMMERCIAL" | "OFFICINA"
  rows: DdpFeedbackAcquistiRow[]
}

export interface DdpFeedbackAcquistiRow {
  statusKey: string
  count: number
  note: string
  hidden: boolean
}

/** Feedback Magazzino (aggregato su tutte le commesse): righe reali negli stati dell'aggregazione A7. */
export interface DdpFeedbackMagazzinoGroup {
  projectId: number
  code: string
  customerName: string
  ddpType: string
  rows: DdpFeedbackMagazzinoRow[]
}

export interface DdpFeedbackMagazzinoRow {
  itemId: number
  requestedBy: string
  description: string
  quantity: number
  unit: string
  material: string
  treatment: string
  supplierName: string
  manufacturer: string
  itemStatus: string
  daneaRef: string
  destination: string
  destinationSpec: string
  notes: string
  hidden: boolean
}

/**
 * Riga DDP di una commessa (distinta commerciale `bom_items` o officina `ddp_officina_items`).
 * Shape unificato: le righe officina hanno gli stessi nomi proprietà (PartNumber, SupplierName…)
 * più `material`/`treatment`; i campi assenti restano ai default. Usato dalla Sintesi DDP.
 */
export interface DdpRowItem {
  id: number
  projectId: number
  rowNumber: number
  /** Articolo di catalogo collegato (solo commerciale); null se riga libera. Usato per deduplicare l'inserimento. */
  catalogItemId?: number | null
  partNumber: string
  description: string
  unit: string
  quantity: number
  unitCost: number
  totalCost: number
  supplierName: string
  manufacturer: string
  itemStatus: string
  requestedBy: string
  daneaRef: string
  dateNeeded: string | null
  destination: string
  destinationSpec: string
  notes: string
  ddpType?: string
  createdAt: string | null
  updatedAt: string | null
  // Solo officina
  material?: string
  treatment?: string
}

/** Richiesta crea/modifica riga DDP commerciale (`bom_items`).
 *  `expectedUpdatedAt` = token concorrenza ottimistica (null = nessun controllo). */
export interface BomItemSaveRequest {
  id: number
  projectId: number
  catalogItemId: number | null
  partNumber: string
  description: string
  unit: string
  quantity: number
  unitCost: number
  supplierId: number | null
  manufacturer: string
  itemStatus: string
  requestedBy: string
  daneaRef: string
  dateNeeded: string | null
  destination: string
  destinationSpec: string
  notes: string
  ddpType: string
  expectedUpdatedAt: string | null
}

/** Notifica real-time (SignalR `DdpChanged`) di modifica distinta di una commessa. */
export interface DdpChange {
  projectId: number
  action: string // create | update | delete
  itemId: number
  ddpType: string // COMMERCIAL | OFFICINA
}

/** Notifica real-time (SignalR `DocumentsChanged`) di modifica ai documenti di una commessa. */
export interface DocumentsChange {
  projectId: number
  action: string // upload | create | rename | move | delete
}

/** Notifica real-time (SignalR `ChatChanged`) su una chat di commessa. */
export interface ChatChange {
  projectId: number
  chatId: number
  action: string // create | message | delete_message | delete_chat
}

export interface BackupFileInfo {
  fileName: string
  sizeMB: number
  date: string
}

export interface DdpDestinationItem {
  id: number
  name: string
  sortOrder: number
  isActive: boolean
}

export interface DdpDestinationSaveRequest {
  id: number
  name: string
  sortOrder: number
  isActive: boolean
}

// Voce del catalogo "Anagrafica attività" (elenco globale delle attività standard di progetto).
export interface ActivityCatalogItem {
  id: number
  label: string
  sortOrder: number
  isActive: boolean
}

export interface ActivityCatalogSaveRequest {
  id: number
  label: string
  sortOrder: number
  isActive: boolean
}

// Milestone = riga di pianificazione di una commessa (copia snapshot dal catalogo attività).
export interface Milestone {
  id: number
  projectId: number
  descrizione: string
  dataInizio: string | null
  dataFine: string | null
  avanzamento: number | null
  note: string
  evidenza: boolean
  spento: boolean
  sortOrder: number
  rowVersion: number
  sourceCatalogId: number | null
}

/** Riepilogo per-commessa delle milestone attive (GET /api/milestones/summary):
 *  conteggi di stato calcolati sulle sole righe non spente. Alimenta i pallini +
 *  conteggio della sidebar PM globale senza rompere il lazy-load delle card. */
export interface MilestoneSummary {
  projectId: number
  code: string
  title: string
  active: number
  late: number
  current: number
  done: number
}

export interface MilestoneSaveRequest {
  descrizione: string
  dataInizio: string | null
  dataFine: string | null
  avanzamento: number | null
  note: string
  evidenza: boolean
  spento: boolean
  rowVersion: number | null
}

export interface SalRow {
  id: number
  projectId: number
  step: string
  perc: number | null
  condizione: string
  dataFatt: string | null
  stato: string
  sortOrder: number
  rowVersion: number
}

export interface SalHeader {
  projectId: number
  cliente: string
  valore: number | null
  rowVersion: number
}

export interface SalBundle {
  header: SalHeader
  rows: SalRow[]
}

export interface SalHeaderSaveRequest {
  cliente: string
  valore: number | null
  rowVersion: number | null
}

export interface SalRowSaveRequest {
  step: string
  perc: number | null
  condizione: string
  dataFatt: string | null
  stato: string
  rowVersion: number | null
}

export interface SalReorderRequest {
  ids: number[]
}

export interface SalCondition {
  id: number
  label: string
  sortOrder: number
  isActive: boolean
}

export interface SalConditionSaveRequest {
  label: string
}

export interface SalProspettoRow {
  projectId: number
  code: string
  cliente: string
  step: string
  perc: number | null
  condizione: string
  dataFatt: string | null
  importo: number | null
  ord: number
  alert: string
}

export interface SalSummary {
  projectId: number
  code: string
  title: string
  total: number
  open: number
  warn: number
  pre: number
}




export interface DdpStatusItem {
  id: number
  statusKey: string
  label: string
  colorBg: string
  colorFg: string
  sortOrder: number
  isActive: boolean
}

export interface DdpStatusSaveRequest {
  id: number
  label: string
  colorBg: string
  colorFg: string
  sortOrder: number
  isActive: boolean
}

export interface DdpAggregation {
  id: number
  code: string
  name: string
  description: string
  kind: string
  sortOrder: number
  isActive: boolean
  statusKeys: string[]
}

export interface DdpAggregationSaveRequest {
  id: number
  name: string
  description: string
  statusKeys: string[]
}

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

export interface FieldUpdateRequest {
  field: string
  value: string | null
}

export interface PhaseTemplateDto {
  id: number
  name: string
  category: string
  costSectionTemplateId: number | null
  costSectionName: string
  sortOrder: number
  isDefault: boolean
}

export interface PhaseTemplateSaveRequest {
  name: string
  category: string
  costSectionTemplateId: number | null
  sortOrder: number
  isDefault: boolean
}

export interface DepartmentSaveRequest {
  id: number
  code: string
  name: string
  hourlyCost: number
  defaultMarkup: number
  sortOrder: number
  isActive: boolean
}

// ── Fasi di commessa + assegnazioni (GET /api/phases/project/{id}) ──────────

export interface PhaseAssignmentDto {
  id: number
  projectPhaseId: number
  employeeId: number
  employeeName: string
  assignRole: string
  plannedHours: number
  hoursWorked: number
}

export interface PhaseListItem {
  id: number
  name: string
  category: string
  budgetHours: number
  budgetCost: number
  status: string
  progressPct: number
  sortOrder: number
  hoursWorked: number
  assignments: PhaseAssignmentDto[]
  phaseTemplateId: number
  customName: string
  costSectionName: string
  costSectionTemplateId: number | null
  isLocal: boolean
}

export interface BulkPhaseRequest {
  projectId: number
  templateIds: number[]
}

export interface LocalPhaseRequest {
  projectId: number
  costSectionTemplateId: number | null
  name: string
  departmentId: number | null
}

// ── Preventivo vs Consuntivo (GET /api/projects/{id}/budget-vs-actual) ──────
// Confronto a 3 colonne: Preventivato (costing) / Assegnato (fasi) / Consuntivo (timesheet).
// Tutti i campi Total*/Delta*/*Cost calcolati sono read-only dal server.

export interface BvaBudgetResourceDto {
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

export interface DepartmentDto {
  id: number
  code: string
  name: string
  hourlyCost: number
  defaultMarkup: number
  sortOrder: number
  isActive: boolean
}

export interface TemplateFolderNode {
  id: number
  parentId: number | null
  name: string
  sortOrder: number
  children: TemplateFolderNode[]
  files: TemplateFileItem[]
}

export interface TemplateFileItem {
  id: number
  fileName: string
  fileSize: number
  uploadedAt: string
}

// ── Codex ────────────────────────────────────────────────

export interface CodexListItem {
  id: number
  codice: string
  codeForn: string
  fornitore: string
  prezzoForn: number
  iva: string
  produttore: string
  data: string
  descr: string
  note: string
  categoria: string
  barcode: string
  tipologia: string
  extra1: string
  extra2: string
  extra3: string
  codeProd: string
  spec: string
  oper: number
  um: string
  ubicazione: string
  codexforn: string
}

export interface CodexSyncStatus {
  isSyncing: boolean
  lastSync: string | null
  totalRows: number
  lastError: string | null
}

export interface CodexPrefix {
  codice: string
  descrizione: string
}

export interface CodexReservationResult {
  codice: string
  reservationId: number
}

export interface CodexGeneratedCode {
  codice: string
  id: number
}

export interface AddCodexReferenceRequest {
  sourceCodexId: number
  refCodexId: number
  /** "201" (commerciale) o "401" (materia prima). */
  refType: string
}

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

// ── Notifiche ───────────────────────────────────────────
// Allineate a ATEC.PM.Shared/DTOs/Notification_DTOs.cs

export interface NotificationListItem {
  /** Id del recipient (notification_recipients.id) — usato per read/delete. */
  id: number
  notificationId: number
  notificationType: string
  severity: string // INFO | WARNING | ERROR | SUCCESS | ALARM
  title: string
  message: string
  referenceType: string // PROJECT | PHASE | BOM | ""
  referenceId: number
  referenceLabel: string
  projectId: number | null
  projectCode: string
  createdByName: string
  isRead: boolean
  readAt: string | null
  createdAt: string
}

export interface NotificationBadge {
  unreadCount: number
}
