import * as React from "react"
import { RefreshCw, ShieldOff, Unplug } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/components/ui/empty"
import { loadAuthFeatures, type AuthFeaturesStatus } from "@/lib/auth/permissions"

/**
 * Cosa vede chi non ha NESSUNA voce di menu.
 *
 * Da quando il fallback dei permessi è stato invertito (`canAccessFeature` nega finché non
 * sa), il caso non è più teorico: chi viene creato da Utenti e non passa da Permessi, o chi
 * ha perso la rete al login, si ritrovava sidebar vuota e la pagina iniziale che rimbalzava
 * su «Accesso negato» — senza una riga che dicesse il perché (PIANO-PERMESSI.md §5.2).
 *
 * Le due cause si vedono uguali ma si risolvono in modo opposto, quindi qui si dividono:
 * permessi non arrivati → «Riprova»; permessi arrivati vuoti → chiedi a un amministratore.
 * L'avviso sostituisce il contenuto DENTRO la shell, mai la pagina intera: la testata con
 * «Esci» deve restare raggiungibile, o l'unica via d'uscita sarebbe svuotare il browser.
 */
export function NoAccessNotice({ status }: { status: AuthFeaturesStatus }) {
  return status === "ready" ? <NoGrantsNotice /> : <FeaturesUnavailableNotice />
}

/** I permessi sono arrivati e sono zero: non è un guasto, è una persona da configurare. */
function NoGrantsNotice() {
  return (
    <Empty className="p-10">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <ShieldOff />
        </EmptyMedia>
        <EmptyTitle>Nessun permesso assegnato</EmptyTitle>
        <EmptyDescription>
          Il tuo utente non ha ancora nessuna funzione abilitata, per questo il menu è
          vuoto. Chiedi a un amministratore di assegnarti i permessi dalla pagina
          «Permessi».
        </EmptyDescription>
      </EmptyHeader>
    </Empty>
  )
}

/**
 * I permessi non sono arrivati (rete, server, sessione a metà). Qui NON si manda nessuno
 * dall'amministratore: non c'è niente da configurare, c'è solo da riprovare.
 */
function FeaturesUnavailableNotice() {
  const [retrying, setRetrying] = React.useState(false)

  async function handleRetry() {
    setRetrying(true)
    try {
      await loadAuthFeatures()
      // Andata bene: lo stato passa a «ready», la shell ridisegna il menu e questo
      // avviso sparisce da solo — non serve navigare né ricaricare la pagina.
    } catch {
      // Andata male di nuovo: lo stato resta «error» e il messaggio a video è già
      // quello giusto. Un toast in più direbbe soltanto la stessa cosa due volte.
    } finally {
      setRetrying(false)
    }
  }

  return (
    <Empty className="p-10">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <Unplug />
        </EmptyMedia>
        <EmptyTitle>Permessi non caricati</EmptyTitle>
        <EmptyDescription>
          Non è stato possibile leggere i tuoi permessi dal server, quindi il menu resta
          vuoto per sicurezza. Riprova: se non cambia niente, avvisa un amministratore.
        </EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button
          variant="outline"
          size="sm"
          onClick={() => void handleRetry()}
          disabled={retrying}
        >
          <RefreshCw className={retrying ? "animate-spin" : undefined} />
          Riprova
        </Button>
      </EmptyContent>
    </Empty>
  )
}
