import * as React from "react"
import { Eye, EyeOff } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"

interface PasswordFieldProps extends Omit<
  React.ComponentProps<typeof Input>,
  "type"
> {
  id: string
}

export const PasswordField = React.forwardRef<
  HTMLInputElement,
  PasswordFieldProps
>(function PasswordField({ id, className, ...props }, ref) {
  const [visible, setVisible] = React.useState(false)

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
        onClick={() => setVisible((value) => !value)}
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
