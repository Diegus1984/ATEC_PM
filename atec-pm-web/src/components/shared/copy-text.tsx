import * as React from "react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Textarea } from "@/components/ui/textarea"
import { notifySuccess } from "@/lib/toast"

/**
 * Copia negli appunti, con la via d'uscita quando il browser non la concede.
 *
 * **Perché non basta una funzione.** ATEC PM in azienda gira su
 * `http://192.168.2.150:5150`: per il browser non è un «contesto sicuro», quindi
 * `navigator.clipboard` **non esiste**. Il ripiego storico —  textarea nascosta +
 * `document.execCommand("copy")` — è peggio del problema: provato il 18/08/2026 su un IP in
 * HTTP, **restituisce `true` senza copiare niente**. È così che è nato il difetto segnalato
 * («dice copiato, ma incollo e trovo quello di prima»): il codice si fidava di quel `true`.
 *
 * Quindi qui non si finge: se gli appunti non sono scrivibili si apre un dialogo col testo
 * già selezionato e si chiede all'utente **Ctrl+C**, che funziona sempre e ovunque. Un clic
 * in più, ma il testo negli appunti ci finisce davvero.
 */
type CopyFn = (text: string, titolo?: string) => Promise<void>

const CopyContext = React.createContext<CopyFn>(() => Promise.resolve())

/** Uso: `const copia = useCopyText()` → `await copia(testo, "Blocco BUG-042")`. */
// eslint-disable-next-line react-refresh/only-export-components
export function useCopyText(): CopyFn {
  return React.useContext(CopyContext)
}

interface PendingCopy {
  text: string
  titolo: string
}

export function CopyTextProvider({ children }: { children: React.ReactNode }) {
  const [pending, setPending] = React.useState<PendingCopy | null>(null)
  const areaRef = React.useRef<HTMLTextAreaElement>(null)

  const copia = React.useCallback<CopyFn>(async (text, titolo) => {
    // Solo la strada moderna può dire con certezza «copiato»: si usa quando c'è.
    if (window.isSecureContext && navigator.clipboard?.writeText) {
      try {
        await navigator.clipboard.writeText(text)
        notifySuccess("Copiato negli appunti")
        return
      } catch {
        // Permesso negato o pagina non a fuoco: si passa alla copia a mano.
      }
    }
    setPending({ text, titolo: titolo ?? "Copia il testo" })
  }, [])

  // Testo già selezionato all'apertura: all'utente resta solo Ctrl+C.
  React.useEffect(() => {
    if (!pending) return
    const id = window.setTimeout(() => {
      areaRef.current?.focus()
      areaRef.current?.select()
    }, 50)
    return () => window.clearTimeout(id)
  }, [pending])

  return (
    <CopyContext.Provider value={copia}>
      {children}
      <Dialog open={pending !== null} onOpenChange={(open) => !open && setPending(null)}>
        <DialogContent className="flex max-h-[80vh] max-w-2xl flex-col">
          <DialogHeader>
            <DialogTitle>{pending?.titolo}</DialogTitle>
            <DialogDescription>
              Il testo è già selezionato: premi <b>Ctrl+C</b> per copiarlo. (Il browser non
              consente la copia automatica su un indirizzo senza HTTPS.)
            </DialogDescription>
          </DialogHeader>

          <Textarea
            ref={areaRef}
            readOnly
            value={pending?.text ?? ""}
            onFocus={(e) => e.currentTarget.select()}
            className="min-h-64 flex-1 font-mono text-xs"
          />

          <DialogFooter>
            <Button onClick={() => setPending(null)}>Chiudi</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </CopyContext.Provider>
  )
}
