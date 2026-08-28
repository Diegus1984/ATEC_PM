import * as React from "react"
import { useMutation, useQuery } from "@tanstack/react-query"

import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { fetchAuthLevels } from "@/lib/api/auth-levels"
import { fetchDepartmentsLookup } from "@/lib/api/departments"
import {
  createEmployee,
  fetchEmployee,
  updateEmployee,
} from "@/lib/api/employees"
import {
  fetchUserDetail,
  saveUserCompetences,
  saveUserDepartments,
  setUserCredentials,
  setUserRole,
} from "@/lib/api/users"
import type {
  DepartmentLookupDto,
  EmployeeCompetenceItem,
  EmployeeDepartmentItem,
} from "@/lib/api/types"


/**
 * Ruoli della gerarchia, usati finché l'elenco vero non arriva dal server (o se la chiamata
 * fallisce): la tendina non deve mai restare vuota, o non si può più salvare una scheda.
 */
const ROLE_FALLBACK = [
  { value: "TECH", label: "TECH — Tecnico operativo" },
  { value: "RESP_REPARTO", label: "RESP — Responsabile reparto" },
  { value: "PM", label: "PM — Project Manager" },
  { value: "ADMIN", label: "ADMIN — Accesso totale" },
]

const STATUS_OPTIONS = [
  { value: "ACTIVE", label: "Attivo" },
  { value: "ON_LEAVE", label: "In ferie/permesso" },
  { value: "SICK", label: "Malattia" },
  { value: "TERMINATED", label: "Cessato" },
]

interface DeptRowState {
  member: boolean
  responsible: boolean
  primary: boolean
}

interface CompRowState {
  enabled: boolean
  notes: string
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h3 className="text-sm font-semibold text-foreground">{children}</h3>
  )
}

export function EmployeeDialog({
  open,
  userId,
  onClose,
  onSaved,
}: {
  open: boolean
  userId: number | "new" | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const isNew = userId === "new"
  const editId = typeof userId === "number" ? userId : null

  const [firstName, setFirstName] = React.useState("")
  const [lastName, setLastName] = React.useState("")
  const [email, setEmail] = React.useState("")
  const [empType, setEmpType] = React.useState("INTERNAL")
  const [status, setStatus] = React.useState("ACTIVE")
  const [userRole, setUserRoleState] = React.useState("TECH")
  const [username, setUsername] = React.useState("")
  const [password, setPassword] = React.useState("")
  const [confirmPassword, setConfirmPassword] = React.useState("")
  const [deptState, setDeptState] = React.useState<Record<number, DeptRowState>>(
    {}
  )
  const [compState, setCompState] = React.useState<Record<number, CompRowState>>(
    {}
  )
  const [ecosEmplCode, setEcosEmplCode] = React.useState("")
  const [mustPunch, setMustPunch] = React.useState(true)
  const [dailyHours, setDailyHours] = React.useState("8")
  const [countsOvertime, setCountsOvertime] = React.useState(true)
  const [error, setError] = React.useState<string | null>(null)

  const departmentsQuery = useQuery({
    queryKey: ["departments", "lookup"],
    queryFn: fetchDepartmentsLookup,
    enabled: open,
  })

  // I ruoli si leggono dal server: oltre ai quattro della gerarchia ci sono i PROFILI
  // (`accessMode = "GRANTS"`, segnalazione #63), che nascono da una migrazione e non possono
  // stare in un elenco scritto a mano — altrimenti si creano profili che nessuno può assegnare.
  const authLevelsQuery = useQuery({
    queryKey: ["auth-levels"],
    queryFn: fetchAuthLevels,
    enabled: open,
  })

  /** Ruoli divisi in due gruppi: la gerarchia di sempre e i profili a lista bianca. */
  const roleGroups = React.useMemo(() => {
    const levels = authLevelsQuery.data ?? []
    if (levels.length === 0) {
      return [{ titolo: "Ruoli", opzioni: ROLE_FALLBACK }]
    }
    const ordinati = [...levels].sort((a, b) => a.sortOrder - b.sortOrder)
    const toOption = (l: (typeof ordinati)[number]) => ({
      value: l.roleName,
      label: l.displayName || l.roleName,
    })
    const gerarchia = ordinati.filter((l) => l.accessMode !== "GRANTS").map(toOption)
    const profili = ordinati.filter((l) => l.accessMode === "GRANTS").map(toOption)
    return [
      { titolo: "Gerarchia", opzioni: gerarchia },
      ...(profili.length > 0 ? [{ titolo: "Profili", opzioni: profili }] : []),
    ]
  }, [authLevelsQuery.data])

  const activeDepartments = React.useMemo(
    () =>
      (departmentsQuery.data ?? [])
        .filter((dept) => dept.isActive)
        .sort((a, b) => a.sortOrder - b.sortOrder),
    [departmentsQuery.data]
  )

  // Carica anagrafica + dettaglio (ruolo/reparti/competenze) in modifica.
  React.useEffect(() => {
    if (!open) {
      return
    }
    setError(null)
    setPassword("")
    setConfirmPassword("")

    if (isNew || editId == null) {
      setFirstName("")
      setLastName("")
      setEmail("")
      setEmpType("INTERNAL")
      setStatus("ACTIVE")
      setUserRoleState("TECH")
      setUsername("")
      setDeptState({})
      setCompState({})
      setEcosEmplCode("")
      setMustPunch(true)
      setDailyHours("8")
      setCountsOvertime(true)
      return
    }

    let cancelled = false
    void Promise.all([fetchEmployee(editId), fetchUserDetail(editId)])
      .then(([emp, detail]) => {
        if (cancelled) {
          return
        }
        setFirstName(emp.firstName)
        setLastName(emp.lastName)
        setEmail(emp.email)
        setEmpType(emp.empType || "INTERNAL")
        setStatus(emp.status || "ACTIVE")
        setUserRoleState(detail.userRole || "TECH")
        setUsername(detail.username || "")
        setEcosEmplCode(emp.ecosEmplCode ?? "")
        setMustPunch(emp.hrMustPunch ?? true)
        setDailyHours(String(emp.hrDailyHours ?? 8))
        setCountsOvertime(emp.hrCountsOvertime ?? true)
        setDeptState(
          Object.fromEntries(
            detail.departments.map((d) => [
              d.departmentId,
              { member: true, responsible: d.isResponsible, primary: d.isPrimary },
            ])
          )
        )
        setCompState(
          Object.fromEntries(
            detail.competences.map((comp) => [
              comp.departmentId,
              { enabled: true, notes: comp.notes },
            ])
          )
        )
      })
      .catch((err: Error) => {
        if (!cancelled) {
          setError(err.message)
        }
      })

    return () => {
      cancelled = true
    }
  }, [open, isNew, editId])

  function setDept(id: number, patch: Partial<DeptRowState>) {
    setDeptState((prev) => {
      const current = prev[id] ?? { member: false, responsible: false, primary: false }
      const next = { ...current, ...patch }
      if (!next.member) {
        next.responsible = false
        next.primary = false
      }
      return { ...prev, [id]: next }
    })
  }

  function setComp(id: number, patch: Partial<CompRowState>) {
    setCompState((prev) => {
      const current = prev[id] ?? { enabled: false, notes: "" }
      return { ...prev, [id]: { ...current, ...patch } }
    })
  }

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!firstName.trim() || !lastName.trim()) {
        throw new Error("Nome e cognome sono obbligatori.")
      }
      if (password) {
        if (password.length < 4) {
          throw new Error("La password deve avere almeno 4 caratteri.")
        }
        if (password !== confirmPassword) {
          throw new Error("Le password non coincidono.")
        }
        if (!username.trim()) {
          throw new Error("Username obbligatorio se imposti una password.")
        }
      }

      // 1. Anagrafica (create o update)
      const payload = {
        id: editId ?? 0,
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: email.trim(),
        empType,
        supplierId: null,
        status,
        ecosEmplCode: ecosEmplCode.trim() || null,
        hrMustPunch: mustPunch,
        hrDailyHours: Number(dailyHours),
        hrCountsOvertime: countsOvertime,
      }
      const id = editId ?? (await createEmployee(payload))
      if (editId != null) {
        await updateEmployee(editId, payload)
      }

      // 2. Ruolo
      await setUserRole(id, userRole)

      // 3. Credenziali (solo se compilate)
      if (username.trim() && password) {
        await setUserCredentials(id, username.trim(), password)
      }

      // 4. Reparti
      const departments: EmployeeDepartmentItem[] = activeDepartments
        .filter((dept) => deptState[dept.id]?.member)
        .map((dept) => ({
          id: 0,
          departmentId: dept.id,
          departmentCode: dept.code,
          departmentName: dept.name,
          isResponsible: deptState[dept.id]?.responsible ?? false,
          isPrimary: deptState[dept.id]?.primary ?? false,
        }))
      await saveUserDepartments(id, departments)

      // 5. Competenze
      const competences: EmployeeCompetenceItem[] = activeDepartments
        .filter((dept) => compState[dept.id]?.enabled)
        .map((dept) => ({
          id: 0,
          departmentId: dept.id,
          departmentCode: dept.code,
          departmentName: dept.name,
          notes: compState[dept.id]?.notes ?? "",
        }))
      await saveUserCompetences(id, competences)
    },
    onSuccess: async () => {
      await onSaved()
    },
    onError: (err: Error) => setError(err.message),
  })

  return (
    <Dialog open={open} onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{isNew ? "Nuovo utente" : "Modifica utente"}</DialogTitle>
          <DialogDescription>
            Anagrafica, accesso, reparti e competenze del dipendente.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-6">
          {/* Sezione 1 — Anagrafica */}
          <section className="space-y-4">
            <SectionTitle>Anagrafica</SectionTitle>
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label>Nome</Label>
                <Input
                  value={firstName}
                  autoFocus
                  onChange={(event) => setFirstName(event.target.value)}
                />
              </div>
              <div className="grid gap-2">
                <Label>Cognome</Label>
                <Input
                  value={lastName}
                  onChange={(event) => setLastName(event.target.value)}
                />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label>Email</Label>
                <Input
                  type="email"
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                />
              </div>
              <div className="grid gap-2">
                <Label>Tipo</Label>
                <Select value={empType} onValueChange={setEmpType}>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="INTERNAL">Interno</SelectItem>
                    <SelectItem value="EXTERNAL">Esterno</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
          </section>

          {/* Sezione — Presenze (port da Timbrature/Users.xaml) */}
          <section className="space-y-4">
            <SectionTitle>Presenze</SectionTitle>
            <p className="text-xs text-muted-foreground">
              Impostazioni per il cartellino HR. Il codice badge Ecos collega le timbrature
              del rilevatore a questa persona (si può anche mappare in blocco da Timbrature).
            </p>
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label htmlFor="ecos-empl-code">Codice badge Ecos</Label>
                <Input
                  id="ecos-empl-code"
                  value={ecosEmplCode}
                  placeholder="Es. 42"
                  onChange={(event) => setEcosEmplCode(event.target.value)}
                />
              </div>
              <div className="grid gap-2">
                <Label htmlFor="hr-ore-giornaliere">Ore giornaliere</Label>
                <Select value={dailyHours} onValueChange={setDailyHours}>
                  <SelectTrigger id="hr-ore-giornaliere" className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="4">4 ore</SelectItem>
                    <SelectItem value="6">6 ore</SelectItem>
                    <SelectItem value="8">8 ore</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="flex flex-col gap-3 sm:flex-row sm:gap-8">
              <label className="flex items-center gap-2 text-sm">
                <Checkbox
                  checked={mustPunch}
                  onCheckedChange={(value) => setMustPunch(!!value)}
                />
                <span>Deve timbrare</span>
              </label>
              <label className="flex items-center gap-2 text-sm">
                <Checkbox
                  checked={countsOvertime}
                  onCheckedChange={(value) => setCountsOvertime(!!value)}
                />
                <span>Conteggia straordinario</span>
              </label>
            </div>
            {!mustPunch ? (
              <p className="text-xs text-muted-foreground">
                Forfait: non deve timbrare. La generazione automatica del cartellino forfait
                arriverà con la riconciliazione assenze (Fase 2).
              </p>
            ) : null}
          </section>

          {/* Sezione 2 — Accesso e ruolo */}
          <section className="space-y-4">
            <SectionTitle>Accesso e ruolo</SectionTitle>
            <div className="grid grid-cols-2 gap-4">
              <div className="grid gap-2">
                <Label>Ruolo sistema</Label>
                <Select value={userRole} onValueChange={setUserRoleState}>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {roleGroups.map((gruppo) => (
                      <SelectGroup key={gruppo.titolo}>
                        <SelectLabel>{gruppo.titolo}</SelectLabel>
                        {gruppo.opzioni.map((role) => (
                          <SelectItem key={role.value} value={role.value}>
                            {role.label}
                          </SelectItem>
                        ))}
                      </SelectGroup>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="grid gap-2">
                <Label>Stato</Label>
                <Select value={status} onValueChange={setStatus}>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {STATUS_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="grid grid-cols-3 gap-4">
              <div className="grid gap-2">
                <Label>Username</Label>
                <Input
                  value={username}
                  autoComplete="off"
                  onChange={(event) => setUsername(event.target.value)}
                />
              </div>
              <div className="grid gap-2">
                <Label>Password</Label>
                <Input
                  type="password"
                  value={password}
                  autoComplete="new-password"
                  onChange={(event) => setPassword(event.target.value)}
                />
              </div>
              <div className="grid gap-2">
                <Label>Conferma</Label>
                <Input
                  type="password"
                  value={confirmPassword}
                  autoComplete="new-password"
                  onChange={(event) => setConfirmPassword(event.target.value)}
                />
              </div>
            </div>
            <p className="text-xs text-muted-foreground">
              {isNew
                ? "Lascia vuoto username/password per non impostare credenziali ora."
                : "Lascia vuota la password per non modificarla."}
            </p>
          </section>

          {/* Sezione 3 — Reparti */}
          <section className="space-y-2">
            <SectionTitle>Reparti di appartenenza</SectionTitle>
            <div className="rounded-md border">
              <div className="grid grid-cols-[1fr_auto_auto] items-center gap-2 border-b bg-muted/50 px-3 py-1.5 text-xs font-medium text-muted-foreground">
                <span>Reparto</span>
                <span className="w-16 text-center">Resp.</span>
                <span className="w-16 text-center">Primario</span>
              </div>
              <div className="max-h-44 overflow-y-auto">
                {activeDepartments.map((dept: DepartmentLookupDto) => {
                  const row = deptState[dept.id]
                  return (
                    <div
                      key={dept.id}
                      className="grid grid-cols-[1fr_auto_auto] items-center gap-2 px-3 py-1.5 text-sm"
                    >
                      <label className="flex items-center gap-2">
                        <Checkbox
                          checked={row?.member ?? false}
                          onCheckedChange={(value) =>
                            setDept(dept.id, { member: !!value })
                          }
                        />
                        <span className="font-semibold">{dept.code}</span>
                        <span className="truncate text-muted-foreground">
                          {dept.name}
                        </span>
                      </label>
                      <div className="flex w-16 justify-center">
                        <Checkbox
                          checked={row?.responsible ?? false}
                          disabled={!row?.member}
                          onCheckedChange={(value) =>
                            setDept(dept.id, { responsible: !!value })
                          }
                        />
                      </div>
                      <div className="flex w-16 justify-center">
                        <Checkbox
                          checked={row?.primary ?? false}
                          disabled={!row?.member}
                          onCheckedChange={(value) =>
                            setDept(dept.id, { primary: !!value })
                          }
                        />
                      </div>
                    </div>
                  )
                })}
              </div>
            </div>
          </section>

          {/* Sezione 4 — Competenze */}
          <section className="space-y-2">
            <SectionTitle>Competenze tecniche</SectionTitle>
            <p className="text-xs text-muted-foreground">
              Reparti su cui è tecnicamente abilitato, anche senza appartenenza formale.
            </p>
            <div className="max-h-44 space-y-1.5 overflow-y-auto rounded-md border p-2">
              {activeDepartments.map((dept: DepartmentLookupDto) => {
                const row = compState[dept.id]
                return (
                  <div key={dept.id} className="flex items-center gap-2">
                    <label className="flex w-44 shrink-0 items-center gap-2 text-sm">
                      <Checkbox
                        checked={row?.enabled ?? false}
                        onCheckedChange={(value) =>
                          setComp(dept.id, { enabled: !!value })
                        }
                      />
                      <span className="font-semibold">{dept.code}</span>
                      <span className="truncate text-muted-foreground">
                        {dept.name}
                      </span>
                    </label>
                    <Input
                      value={row?.notes ?? ""}
                      disabled={!row?.enabled}
                      placeholder="Note (es. Fanuc R30iB, Siemens S7-1500)"
                      className="h-8"
                      onChange={(event) =>
                        setComp(dept.id, { notes: event.target.value })
                      }
                    />
                  </div>
                )
              })}
            </div>
          </section>

          {error ? <p className="text-sm text-destructive">{error}</p> : null}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Annulla
          </Button>
          <Button
            onClick={() => saveMutation.mutate()}
            disabled={saveMutation.isPending}
          >
            {saveMutation.isPending ? "Salvataggio…" : "Salva"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
