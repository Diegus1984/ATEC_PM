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
