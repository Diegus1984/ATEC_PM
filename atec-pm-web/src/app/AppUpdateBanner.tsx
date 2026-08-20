import * as React from "react"
import { RefreshCw } from "lucide-react"

import { Button } from "@/components/ui/button"
import { APP_BUILD, fetchServerBuild } from "@/lib/app-version"

/** Ogni quanto si ricontrolla la build sul server (il file pesa poche decine di byte). */
const POLL_MS = 60_000

/**
 * `true` quando sul server c'è una build diversa da quella in esecuzione.
 *
 * Il controllo scatta all'avvio, ogni minuto e ogni volta che la scheda torna in
 * primo piano (chi lascia ATEC PM aperto tutta la notte se ne accorge appena
 * rimette mano al PC). Una volta rilevato l'aggiornamento non si torna indietro:
 * l'avviso resta finché l'utente non ricarica.
 */
function useAppUpdateAvailable(): boolean {
  const [available, setAvailable] = React.useState(false)

  React.useEffect(() => {
    // In sviluppo la pagina la serve Vite (HMR): `version.json` non esiste e il
    // confronto non avrebbe senso.
    if (import.meta.env.DEV) {
      return
    }

    let stopped = false

    async function check() {
      if (stopped || document.hidden) {
        return
      }
      const serverBuild = await fetchServerBuild()
      if (!stopped && serverBuild && serverBuild !== APP_BUILD) {
        setAvailable(true)
      }
    }

    // Un chunk che non si scarica più è la prova provata che il deploy è avvenuto
    // mentre la pagina era aperta (i file hanno l'hash nel nome: i vecchi spariscono).
    // Qui non si aspetta il poll, l'avviso va mostrato subito.
    function handlePreloadError() {
      setAvailable(true)
    }

    const timer = window.setInterval(check, POLL_MS)
    window.addEventListener("focus", check)
    document.addEventListener("visibilitychange", check)
    window.addEventListener("vite:preloadError", handlePreloadError)
    void check()

    return () => {
      stopped = true
      window.clearInterval(timer)
      window.removeEventListener("focus", check)
      document.removeEventListener("visibilitychange", check)
      window.removeEventListener("vite:preloadError", handlePreloadError)
    }
  }, [])

  return available
}

/**
 * Barra fissa in cima all'area di lavoro quando è stata pubblicata una nuova versione.
 *
 * Non si chiude e non ricarica da sola: il momento lo sceglie l'utente, così nessuno
 * si vede sparire da sotto le mani una griglia o un foglio a metà compilazione.
 */
export function AppUpdateBanner() {
  const available = useAppUpdateAvailable()

  if (!available) {
    return null
  }

  // `md:rounded-t-xl` segue gli angoli tondi di SidebarInset (variant="inset"):
  // senza, la barra colorata sborderebbe sopra il riquadro.
  return (
    <div className="flex shrink-0 items-center gap-3 border-b bg-primary px-4 py-2 text-sm text-primary-foreground md:rounded-t-xl">
      <RefreshCw className="size-4 shrink-0" />
      <span className="flex-1">
        È disponibile una nuova versione di ATEC PM. Salva quello che stai scrivendo
        e ricarica la pagina.
      </span>
      <Button
        size="sm"
        variant="secondary"
        className="shrink-0"
        onClick={() => window.location.reload()}
      >
        Aggiorna adesso
      </Button>
    </div>
  )
}
