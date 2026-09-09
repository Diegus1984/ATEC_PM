import * as React from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"

import { PasswordField } from "@/features/auth/PasswordField"
import { saveEmailSettings } from "@/lib/api/settings"
import type { EmailSettingsDto } from "@/lib/api/types"
import { notifyError, notifySuccess } from "@/lib/toast"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Label } from "@/components/ui/label"

import { motivoPasswordNonValida } from "./password-smtp"

interface CambiaPasswordSmtpDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** La configurazione com'è a video: la password nuova si salva insieme al resto. */
  impostazioni: EmailSettingsDto
  /** Dopo il salvataggio riuscito: la pagina torna a «già salvata». */
  onSalvata: () => void
}

/**
 * «Cambia password» della Configurazione email (Diego, 09/09/2026 sera): la password della
 * casella scritta due volte, con l'occhiolino su entrambe, salvata subito insieme alla
 * configurazione com'è a video. Il campo password inline resta per chi la scrive al volo.
 */
export function CambiaPasswordSmtpDialog({
  open,
  onOpenChange,
  impostazioni,
  onSalvata,
}: CambiaPasswordSmtpDialogProps) {
  const queryClient = useQueryClient()
  const [nuova, setNuova] = React.useState("")
  const [ripeti, setRipeti] = React.useState("")
  React.useEffect(() => {
    if (open) {
      setNuova("")
      setRipeti("")
    }
  }, [open])

  const motivo = motivoPasswordNonValida(nuova, ripeti)
  const salva = useMutation({
    mutationFn: () => saveEmailSettings({ ...impostazioni, password: nuova }),
    onSuccess: async () => {
      notifySuccess("Password SMTP aggiornata. Ora fai «Invia prova».")
      await queryClient.invalidateQueries({ queryKey: ["digest-email-settings"] })
      onSalvata()
      onOpenChange(false)
    },
    onError: (e) => notifyError(e instanceof Error ? e.message : "Salvataggio non riuscito."),
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Cambia la password SMTP</DialogTitle>
          <DialogDescription>
            La password della casella {impostazioni.username || impostazioni.from || "di posta"}:
            scrivila due volte. Si salva subito, insieme alla configurazione com'è a video.
          </DialogDescription>
        </DialogHeader>
        <form
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault()
            if (motivo == null && !salva.isPending) salva.mutate()
          }}
        >
          <div className="grid gap-1.5">
            <Label htmlFor="smtp-password-nuova">Nuova password</Label>
            <PasswordField
              id="smtp-password-nuova"
              value={nuova}
              onChange={(e) => setNuova(e.target.value)}
              autoComplete="new-password"
            />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="smtp-password-ripeti">Ripeti la password</Label>
            <PasswordField
              id="smtp-password-ripeti"
              value={ripeti}
              onChange={(e) => setRipeti(e.target.value)}
              autoComplete="new-password"
            />
          </div>
          {motivo != null && (nuova.length > 0 || ripeti.length > 0) && (
            <p className="text-sm text-destructive">{motivo}</p>
          )}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
              Annulla
            </Button>
            <Button type="submit" disabled={motivo != null || salva.isPending}>
              {salva.isPending ? "Salvataggio…" : "Salva la password"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
