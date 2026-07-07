import * as React from "react"
import { Check, Copy } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"

/** Mostra le credenziali temporanee dopo un reset, con copia negli appunti. */
export function ResetCredentialsDialog({
  login,
  onClose,
}: {
  login: string | null
  onClose: () => void
}) {
  const [copied, setCopied] = React.useState(false)

  React.useEffect(() => {
    if (login) {
      setCopied(false)
    }
  }, [login])

  async function copy() {
    if (!login) {
      return
    }
    try {
      await navigator.clipboard.writeText(login)
      setCopied(true)
    } catch {
      // Clipboard non disponibile (contesto non sicuro): l'utente copia a mano.
    }
  }

  return (
    <Dialog open={login !== null} onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Credenziali reimpostate</DialogTitle>
          <DialogDescription>
            Username e password temporanei (forma iniziale.cognome). Comunicali
            all'utente in modo sicuro.
          </DialogDescription>
        </DialogHeader>
        <div className="flex items-center gap-2">
          <Input readOnly value={login ?? ""} className="font-mono" />
          <Button
            variant="outline"
            size="icon"
            onClick={copy}
            aria-label="Copia"
          >
            {copied ? <Check /> : <Copy />}
          </Button>
        </div>
        {copied ? (
          <p className="text-xs text-muted-foreground">Copiato negli appunti.</p>
        ) : null}
        <DialogFooter>
          <Button onClick={onClose}>Chiudi</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
