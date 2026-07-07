import * as React from "react"
import { Navigate, useLocation, useNavigate } from "react-router-dom"

import { PasswordField } from "@/features/auth/PasswordField"
import { AtecBrandIcon } from "@/components/branding/AtecBrandIcon"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { ApiError, setApiBaseUrl } from "@/lib/api/client"
import { checkPendingNotifications } from "@/lib/api/notifications"
import {
  changePasswordFromLogin,
  MIN_PASSWORD_LENGTH,
  validatePasswordChange,
} from "@/lib/auth/password-change"
import {
  clearSession,
  getSession,
  isLoginComplete,
  login,
  markPasswordChanged,
} from "@/lib/auth/session"
import {
  getStoredServerUrl,
  getStoredUsername,
  resolveLoginApiBase,
  resolveServerUrl,
  saveServerUrl,
  saveUsername,
  serverUrlToPersist,
  shouldShowServerField,
} from "@/lib/auth/server-url"

type LoginMode = "login" | "forced-change" | "voluntary-change"

const PASSWORD_HINT =
  `Almeno ${MIN_PASSWORD_LENGTH} caratteri, diversa da quella attuale ` +
  "e dalla password iniziale (n.cognome)."

export function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const showServerField = shouldShowServerField()
  const passwordRef = React.useRef<HTMLInputElement>(null)

  const [mode, setMode] = React.useState<LoginMode>("login")
  const [serverUrl, setServerUrl] = React.useState(() =>
    showServerField ? resolveServerUrl(getStoredServerUrl()) : ""
  )
  const [username, setUsername] = React.useState(() => getStoredUsername())
  const [password, setPassword] = React.useState("")
  const [currentPassword, setCurrentPassword] = React.useState("")
  const [newPassword, setNewPassword] = React.useState("")
  const [confirmPassword, setConfirmPassword] = React.useState("")
  const [error, setError] = React.useState("")
  const [info, setInfo] = React.useState("")
  const [loading, setLoading] = React.useState(false)

  const redirectTo =
    (location.state as { from?: string } | null)?.from ?? "/"

  // Sessione incompleta (refresh durante cambio obbligatorio): richiedi nuovo login.
  React.useEffect(() => {
    const stored = getSession()
    if (stored?.user.mustChangePassword) {
      clearSession()
    }
  }, [])

  // Focus sulla password solo se lo username era già precompilato all'avvio
  // (utente ricordato dal login precedente), non ad ogni digitazione.
  React.useEffect(() => {
    if (mode === "login" && username.trim()) {
      passwordRef.current?.focus()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  if (isLoginComplete()) {
    return <Navigate to={redirectTo} replace />
  }

  function applyApiBase() {
    setApiBaseUrl(resolveLoginApiBase(serverUrl))
  }

  async function finishLogin() {
    void checkPendingNotifications().catch(() => {})
    navigate(redirectTo, { replace: true })
  }

  async function handleLoginSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError("")
    setInfo("")
    setLoading(true)

    applyApiBase()

    try {
      const session = await login({ username, password })
      saveServerUrl(serverUrlToPersist(resolveLoginApiBase(serverUrl)))
      saveUsername(username)

      if (session.user.mustChangePassword) {
        setMode("forced-change")
        setNewPassword("")
        setConfirmPassword("")
        return
      }

      await finishLogin()
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Errore di connessione al server.")
      }
    } finally {
      setLoading(false)
    }
  }

  async function handleForcedChangeSubmit(
    event: React.FormEvent<HTMLFormElement>
  ) {
    event.preventDefault()
    setError("")
    setLoading(true)

    const validationError = validatePasswordChange(
      password,
      newPassword,
      confirmPassword,
      { forced: true }
    )
    if (validationError) {
      setError(validationError)
      setLoading(false)
      return
    }

    applyApiBase()

    try {
      await changePasswordFromLogin({
        username: username.trim(),
        currentPassword: password,
        newPassword,
        confirmNewPassword: confirmPassword,
      })
      markPasswordChanged()
      await finishLogin()
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Errore di connessione al server.")
      }
    } finally {
      setLoading(false)
    }
  }

  async function handleVoluntaryChangeSubmit(
    event: React.FormEvent<HTMLFormElement>
  ) {
    event.preventDefault()
    setError("")
    setInfo("")
    setLoading(true)

    if (!username.trim()) {
      setError("Inserisci lo username prima di cambiare la password.")
      setLoading(false)
      return
    }

    const current = currentPassword || password
    const validationError = validatePasswordChange(
      current,
      newPassword,
      confirmPassword
    )
    if (validationError) {
      setError(validationError)
      setLoading(false)
      return
    }

    applyApiBase()

    try {
      await changePasswordFromLogin({
        username: username.trim(),
        currentPassword: current,
        newPassword,
        confirmNewPassword: confirmPassword,
      })
      setMode("login")
      setPassword("")
      setCurrentPassword("")
      setNewPassword("")
      setConfirmPassword("")
      setInfo("Password aggiornata. Accedi con la nuova password.")
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Errore di connessione al server.")
      }
    } finally {
      setLoading(false)
    }
  }

  function openVoluntaryChange() {
    setError("")
    setInfo("")
    setCurrentPassword(password)
    setNewPassword("")
    setConfirmPassword("")
    setMode("voluntary-change")
  }

  function backToLogin() {
    setError("")
    setMode("login")
    setCurrentPassword("")
    setNewPassword("")
    setConfirmPassword("")
  }

  if (mode === "forced-change") {
    return (
      <LoginShell
        title="Cambio password obbligatorio"
        description="La password iniziale va cambiata prima di continuare."
      >
        <form className="flex flex-col gap-4" onSubmit={handleForcedChangeSubmit}>
          <div className="grid gap-2">
            <Label htmlFor="new-password">Nuova password</Label>
            <PasswordField
              key="new-password"
              id="new-password"
              autoComplete="new-password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              required
            />
            <p className="text-xs text-muted-foreground">{PASSWORD_HINT}</p>
          </div>

          <div className="grid gap-2">
            <Label htmlFor="confirm-password">Conferma password</Label>
            <PasswordField
              key="confirm-password"
              id="confirm-password"
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(event) => setConfirmPassword(event.target.value)}
              required
            />
          </div>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}

          <Button type="submit" className="w-full" disabled={loading}>
            {loading ? "Salvataggio..." : "Salva e continua"}
          </Button>
        </form>
      </LoginShell>
    )
  }

  if (mode === "voluntary-change") {
    return (
      <LoginShell
        title="Cambia password"
        description="Inserisci la password attuale e quella nuova."
      >
        <form
          className="flex flex-col gap-4"
          onSubmit={handleVoluntaryChangeSubmit}
        >
          <div className="grid gap-2">
            <Label htmlFor="current-password">Password attuale</Label>
            <PasswordField
              key="current-password"
              id="current-password"
              autoComplete="current-password"
              value={currentPassword}
              onChange={(event) => setCurrentPassword(event.target.value)}
              required
            />
          </div>

          <div className="grid gap-2">
            <Label htmlFor="voluntary-new-password">Nuova password</Label>
            <PasswordField
              key="voluntary-new-password"
              id="voluntary-new-password"
              autoComplete="new-password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              required
            />
            <p className="text-xs text-muted-foreground">{PASSWORD_HINT}</p>
          </div>

          <div className="grid gap-2">
            <Label htmlFor="voluntary-confirm-password">Conferma password</Label>
            <PasswordField
              key="voluntary-confirm-password"
              id="voluntary-confirm-password"
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(event) => setConfirmPassword(event.target.value)}
              required
            />
          </div>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}

          <Button type="submit" className="w-full" disabled={loading}>
            {loading ? "Salvataggio..." : "Salva password"}
          </Button>
          <Button
            type="button"
            variant="outline"
            className="w-full"
            disabled={loading}
            onClick={backToLogin}
          >
            Annulla
          </Button>
        </form>
      </LoginShell>
    )
  }

  return (
    <LoginShell
      title="ATEC PM"
      description="Accedi con le credenziali aziendali"
    >
      <form className="flex flex-col gap-4" onSubmit={handleLoginSubmit}>
        {showServerField ? (
          <div className="grid gap-2">
            <Label htmlFor="server">Server API</Label>
            <Input
              id="server"
              type="url"
              autoComplete="off"
              placeholder="http://localhost:5150"
              value={serverUrl}
              onChange={(event) => setServerUrl(event.target.value)}
              required
            />
          </div>
        ) : null}

        <div className="grid gap-2">
          <Label htmlFor="username">Utente</Label>
          <Input
            id="username"
            autoComplete="username"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            required
          />
        </div>

        <div className="grid gap-2">
          <Label htmlFor="password">Password</Label>
          <PasswordField
            key="password"
            id="password"
            ref={passwordRef}
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </div>

        {error ? <p className="text-sm text-destructive">{error}</p> : null}
        {info ? (
          <p className="text-sm text-muted-foreground">{info}</p>
        ) : null}

        <Button type="submit" className="w-full" disabled={loading}>
          {loading ? "Accesso..." : "Accedi"}
        </Button>

        <Button
          type="button"
          variant="outline"
          className="w-full"
          disabled={loading}
          onClick={openVoluntaryChange}
        >
          Cambia password
        </Button>
      </form>
    </LoginShell>
  )
}

function LoginShell({
  title,
  description,
  children,
}: {
  title: string
  description: string
  children: React.ReactNode
}) {
  return (
    <div className="flex min-h-svh flex-col items-center justify-center bg-muted/40 p-6 md:p-10">
      <div className="animate-in fade-in-0 zoom-in-95 w-full max-w-sm duration-500">
        <Card>
          <CardHeader className="text-center">
            <div className="mb-2 flex justify-center">
              <AtecBrandIcon size="lg" />
            </div>
            <CardTitle className="text-xl">{title}</CardTitle>
            <CardDescription>{description}</CardDescription>
          </CardHeader>
          <CardContent>{children}</CardContent>
        </Card>
      </div>
    </div>
  )
}
