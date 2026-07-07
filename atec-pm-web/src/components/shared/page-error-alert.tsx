import { AlertCircle } from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"

export interface PageErrorAlertProps {
  message: string
  title?: string
}

/** Errore persistente a livello pagina/sezione (non toast, non dialog). */
export function PageErrorAlert({
  message,
  title = "Errore",
}: PageErrorAlertProps) {
  return (
    <Alert variant="destructive">
      <AlertCircle />
      <AlertTitle>{title}</AlertTitle>
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  )
}
