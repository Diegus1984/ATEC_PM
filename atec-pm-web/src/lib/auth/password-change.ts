import { apiPost, ApiError } from "@/lib/api/client"
import type { ApiResponse, ChangePasswordRequest } from "@/lib/api/types"

/** Allineato a ATEC.PM.Shared InitialPasswordHelper.MinPasswordLength (WPF / server PM). */
export const MIN_PASSWORD_LENGTH = 4

export function validatePasswordChange(
  currentPassword: string,
  newPassword: string,
  confirmPassword: string,
  options: { forced?: boolean } = {}
): string | null {
  if (!options.forced && !currentPassword) {
    return "Inserisci la password attuale."
  }

  if (newPassword.length < MIN_PASSWORD_LENGTH) {
    return `La nuova password deve avere almeno ${MIN_PASSWORD_LENGTH} caratteri.`
  }

  if (newPassword !== confirmPassword) {
    return "Le due password non coincidono."
  }

  if (newPassword === currentPassword) {
    return "La nuova password deve essere diversa da quella attuale."
  }

  return null
}

export async function changePasswordFromLogin(
  request: ChangePasswordRequest
): Promise<string> {
  const response = await apiPost<ApiResponse<string>>(
    "/api/auth/change-password-login",
    request
  )

  if (!response.success) {
    throw new ApiError(response.message || "Operazione non riuscita", 400)
  }

  return response.message || "Password aggiornata"
}
