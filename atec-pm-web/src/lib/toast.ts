import type { CSSProperties } from "react"
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

/**
 * Tinta di un toast di successo. Serve quando la notifica deve avere **lo stesso colore del
 * comando che l'ha fatta partire**: il pulsante di stato di una fase è verde «in corso» e
 * rosso «completata», e una conferma verde su un'azione rossa si legge come se fosse andata
 * a finire da un'altra parte.
 */
export type TintaSuccesso = "verde" | "rosso"

/**
 * Toast successo dopo salvataggio o azione completata. Verde, se non si chiede altro.
 *
 * <p>Con `tinta: "rosso"` resta un toast di **successo** — spunta, ruolo di cortesia per chi
 * legge con la sintesi vocale — ma prende i colori del pulsante «Fase Completata». Non si usa
 * `toast.error`: quello è un errore, mostra l'ottagono e lo annuncia come tale, e completare
 * una fase non è un guaio. Sonner con `richColors` tinge leggendo `--success-bg/-border/-text`:
 * sovrascriverle inline vale per questo toast e basta.</p>
 */
export function notifySuccess(message: string, tinta: TintaSuccesso = "verde"): void {
  if (tinta === "verde") {
    toast.success(message)
    return
  }

  // Gli stessi valori del pulsante: fondo red-200, bordo red-300, testo nero.
  toast.success(message, {
    style: {
      "--success-bg": "#fecaca",
      "--success-border": "#fca5a5",
      "--success-text": "#18181b",
    } as CSSProperties,
  })
}

/** Toast informativo neutro. */
export function notifyInfo(message: string): void {
  toast.message(message)
}

export { toast }
