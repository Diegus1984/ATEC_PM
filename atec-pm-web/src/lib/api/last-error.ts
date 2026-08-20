/**
 * Buffer in memoria dell'ultimo errore API 4xx/5xx incontrato dal client.
 * Usato dal contesto tecnico delle segnalazioni per legare la segnalazione
 * all'errore già registrato nel log del server (tramite TraceIdentifier / messaggio),
 * SENZA salvare payload di richiesta o risposta contenenti dati sensibili.
 */

export interface LastApiError {
  method: string
  url: string
  status: number
  message: string
  timestamp: Date
}

let lastApiError: LastApiError | null = null

const FIVE_MINUTES_MS = 5 * 60 * 1000

export function recordLastError(err: {
  method: string
  url: string
  status: number
  message: string
}): void {
  lastApiError = {
    ...err,
    timestamp: new Date(),
  }
}

export function getLastError(): LastApiError | null {
  if (!lastApiError) return null
  if (Date.now() - lastApiError.timestamp.getTime() > FIVE_MINUTES_MS) {
    lastApiError = null
    return null
  }
  return lastApiError
}

export function formatLastErrorForContext(): string | null {
  const err = getLastError()
  if (!err) return null

  const timeStr = err.timestamp.toLocaleTimeString("it-IT", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  })
  return `[${err.status}] ${err.method.toUpperCase()} ${err.url} (${timeStr})\n${err.message}`
}
