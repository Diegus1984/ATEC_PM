import * as React from "react"

import { getSession, isAuthenticated } from "@/lib/auth/session"
import { loadAuthFeatures } from "@/lib/auth/permissions"

interface AuthBootstrapProps {
  children: React.ReactNode
}

export function AuthBootstrap({ children }: AuthBootstrapProps) {
  const [ready, setReady] = React.useState(!isAuthenticated())

  React.useEffect(() => {
    if (!isAuthenticated()) {
      setReady(true)
      return
    }

    let cancelled = false

    async function bootstrap() {
      try {
        await loadAuthFeatures()
      } catch {
        // L'errore è già registrato nello stato dei permessi («error»): si prosegue apposta
        // fino alla shell, dove l'utente trova l'avviso col «Riprova». Fermarsi qui, o
        // rimandarlo al login, gli toglierebbe l'unica cosa utile da fare.
      } finally {
        if (!cancelled) {
          setReady(true)
        }
      }
    }

    void bootstrap()

    return () => {
      cancelled = true
    }
  }, [])

  if (!ready) {
    return (
      <div className="flex min-h-svh items-center justify-center text-sm text-muted-foreground">
        Caricamento profilo…
      </div>
    )
  }

  void getSession()
  return <>{children}</>
}
