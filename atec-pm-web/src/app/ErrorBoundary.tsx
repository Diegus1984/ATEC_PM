import * as React from "react"
import { AlertTriangle, RefreshCw, Bug } from "lucide-react"

import { Button } from "@/components/ui/button"
import { BugReportDialog } from "@/features/bug-reports/BugReportDialog"
import { canWriteFeature } from "@/lib/auth/permissions"

interface Props {
  children: React.ReactNode
}

interface State {
  hasError: boolean
  error: Error | null
  componentStack: string | null
  dialogOpen: boolean
}

/**
 * Error Boundary React (L4).
 * Cattura i crash non gestiti all'interno dell'area di rendering delle rotte,
 * evitando la pagina bianca e offrendo un recupero rapido con ricarica o
 * invio della segnalazione pre-compilata con lo stack trace.
 */
export class ErrorBoundary extends React.Component<Props, State> {
  public override state: State = {
    hasError: false,
    error: null,
    componentStack: null,
    dialogOpen: false,
  }

  public static getDerivedStateFromError(error: Error): Partial<State> {
    return { hasError: true, error }
  }

  public override componentDidCatch(error: Error, errorInfo: React.ErrorInfo): void {
    this.setState({
      componentStack: errorInfo.componentStack ?? null,
    })
    if (import.meta.env.DEV) {
      console.error("[ErrorBoundary] Crash componente:", error, errorInfo)
    }
  }

  private handleReload = (): void => {
    window.location.reload()
  }

  private handleOpenReport = (): void => {
    this.setState({ dialogOpen: true })
  }

  private handleCloseReport = (): void => {
    this.setState({ dialogOpen: false })
  }

  public override render(): React.ReactNode {
    if (this.state.hasError) {
      const errorMsg = this.state.error?.message || "Errore imprevisto"
      const stackSnippet = (this.state.componentStack || this.state.error?.stack || "").slice(0, 2000)
      const errorContext = `[React Error Boundary]\nErrore: ${errorMsg}\nStack componente:\n${stackSnippet}`
      const canWrite = canWriteFeature("nav.bug_reports")

      return (
        <div className="flex min-h-[400px] flex-col items-center justify-center rounded-lg border border-destructive/20 bg-destructive/5 p-6 text-center">
          <div className="mb-4 flex size-12 items-center justify-center rounded-full bg-destructive/10 text-destructive">
            <AlertTriangle className="size-6" />
          </div>
          <h2 className="text-lg font-semibold text-foreground">
            Si è verificato un errore imprevisto
          </h2>
          <p className="mt-1 max-w-md text-sm text-muted-foreground">
            La visualizzazione di questa sezione si è interrotta. Puoi provare a ricaricare la
            pagina o inviare una segnalazione ai tecnici con i dettagli del problema.
          </p>

          <div className="mt-6 flex flex-wrap items-center justify-center gap-3">
            <Button variant="outline" onClick={this.handleReload}>
              <RefreshCw className="mr-2 size-4" />
              Ricarica pagina
            </Button>
            {canWrite ? (
              <Button variant="default" onClick={this.handleOpenReport}>
                <Bug className="mr-2 size-4" />
                Segnala il problema
              </Button>
            ) : null}
          </div>

          {canWrite && this.state.dialogOpen ? (
            <BugReportDialog
              open={this.state.dialogOpen}
              bug={null}
              initialTitle={`Crash interfaccia: ${errorMsg.slice(0, 100)}`}
              initialContext={errorContext}
              isAdmin={canWriteFeature("action.manage_bug_reports")}
              canWrite={canWrite}
              onClose={this.handleCloseReport}
              onSaved={this.handleCloseReport}
            />
          ) : null}
        </div>
      )
    }

    return this.props.children
  }
}
