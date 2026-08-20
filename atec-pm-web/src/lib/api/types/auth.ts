/** Autenticazione, sessione e feature abilitate — allineati a ATEC.PM.Shared/DTOs. */

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

export interface AuthLevelDto {
  id: number
  levelValue: number
  roleName: string
  displayName: string
  sortOrder: number
  /**
   * `LEVEL` = ruolo della gerarchia (vede tutto ciò che sta al suo livello o sotto);
   * `GRANTS` = ruolo di reparto (es. AMM): vede SOLO le funzioni concesse.
   */
  accessMode: string
}

/** Concessione di una funzione a un singolo ruolo. */
export interface AuthRoleFeatureDto {
  roleName: string
  featureKey: string
  access: FeatureAccess
}

/** `FULL` = lettura e scrittura, `READ` = sola consultazione. */
export type FeatureAccess = "FULL" | "READ"

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
  /** Modalità del ruolo dell'utente: `LEVEL` o `GRANTS`. */
  accessMode: string
  /** Concessioni del ruolo dell'utente: chiave funzione → `READ`/`FULL`. */
  grants: Record<string, FeatureAccess>
  /**
   * Quale motore dei permessi sta rispondendo: `NEW` = le righe sulla PERSONA
   * (`employee_feature_access`), `LEGACY` = livelli e liste di ruolo.
   *
   * Serve perché le due cose si somigliano da fuori ma si amministrano in posti diversi: con
   * `NEW` acceso la matrice funzioni × ruoli della pagina «Permessi» scrive su tabelle che il
   * motore non legge più, quindi salvare lì non cambia niente per nessuno. Senza questo campo
   * l'unico modo di accorgersene era provare e non vedere effetti.
   */
  permissionsEngine?: string
}
