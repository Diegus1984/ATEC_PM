import * as React from "react"
import { Eye, EyeOff } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"

interface PasswordFieldProps extends Omit<
  React.ComponentProps<typeof Input>,
  "type"
> {
  id: string
  /**
   * Chiamata quando l'occhiolino sta per MOSTRARE il valore: chi ha un segnaposto al posto
   * della password vera (la Configurazione email) qui la va a prendere. Restituire `false`
   * lascia il campo nascosto (per esempio se il server non l'ha data).
   */
  onReveal?: () => Promise<boolean | void> | boolean | void
}

export const PasswordField = React.forwardRef<
  HTMLInputElement,
  PasswordFieldProps
>(function PasswordField({ id, className, onReveal, ...props }, ref) {
  const [visible, setVisible] = React.useState(false)

  async function alterna() {
    if (!visible && onReveal) {
      const esito = await onReveal()
      if (esito === false) return
    }
    setVisible((value) => !value)
  }

  return (
    <div className="relative">
      <Input
        id={id}
        ref={ref}
        type={visible ? "text" : "password"}
        className={className}
        {...props}
      />
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="absolute top-0 right-0 h-full px-3 hover:bg-transparent"
        tabIndex={-1}
        aria-label={visible ? "Nascondi password" : "Mostra password"}
        onClick={() => void alterna()}
      >
        {visible ? (
          <EyeOff className="size-4 text-muted-foreground" />
        ) : (
          <Eye className="size-4 text-muted-foreground" />
        )}
      </Button>
    </div>
  )
})
