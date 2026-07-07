import { toast } from "sonner"

import { ApiError } from "@/lib/api/client"

function messageFromError(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.message || fallback
  }
  if (error instanceof Error) {
    return error.message || fallback
  }
  if (typeof error === "string" && error.trim()) {
    return error.trim()
  }
  return fallback
}

/** Toast errore non bloccante (sostituto di `window.alert` per failure API/validazione). */
export function notifyError(
  error: unknown,
  fallback = "Operazione non riuscita"
): void {
  toast.error(messageFromError(error, fallback))
}

/** Toast successo dopo salvataggio o azione completata. */
export function notifySuccess(message: string): void {
  toast.success(message)
}

/** Toast informativo neutro. */
export function notifyInfo(message: string): void {
  toast.message(message)
}

export { toast }
