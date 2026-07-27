import { useQuery } from "@tanstack/react-query"
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  Cell,
  Legend,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts"

import { Skeleton } from "@/components/ui/skeleton"
import { fetchProjectDashboard } from "@/lib/api/projects"
import type {
  DeptSummary,
  ProjectDashboardData,
  UpcomingDeadline,
} from "@/lib/api/types"
import { getSession } from "@/lib/auth/session"
import { formatDateOrDash } from "@/lib/date-iso"

const DEPT_COLORS: Record<string, string> = {
  PM: "#4F6EF7",
  UTM: "#059669",
  UTE: "#2563EB",
  MEC: "#D97706",
  INS: "#DC2626",
  PLC: "#7C3AED",
  ROB: "#BE185D",
  ACQ: "#0891B2",
  AMM: "#6B7280",
  TRASV: "#6B7280",
  DEFAULT: "#6B7280",
}
const deptColor = (code: string) => DEPT_COLORS[code] ?? DEPT_COLORS.DEFAULT

function statusColor(s: string): string {
  switch (s) {
    case "ACTIVE":
      return "#059669"
    case "COMPLETED":
      return "#2563EB"
    case "ON_HOLD":
      return "#D97706"
    default:
      return "#6B7280"
  }
}
function priorityColor(p: string): string {
  switch (p) {
    case "HIGH":
      return "#EF4444"
    case "MEDIUM":
      return "#F59E0B"
    case "LOW":
      return "#6B7280"
    default:
      return "#9CA3AF"
  }
}
const n0 = (v: number) => v.toLocaleString("it-IT", { maximumFractionDigits: 0 })
const n1 = (v: number) =>
  v.toLocaleString("it-IT", { minimumFractionDigits: 1, maximumFractionDigits: 1 })

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h3 className="mt-6 mb-2 text-sm font-semibold text-foreground">{children}</h3>
  )
}
function Card({ children }: { children: React.ReactNode }) {
  return <div className="rounded-lg border bg-card p-4">{children}</div>
}

function Kpi({
  label,
  value,
  subtitle,
  color,
  extra,
}: {
  label: string
  value: string
  subtitle: string
  color: string
  extra?: React.ReactNode
}) {
  return (
    <div className="rounded-lg border bg-card p-3 flex justify-between items-center" style={{ borderLeft: `3px solid ${color}` }}>
      <div>
        <p className="text-[10px] font-semibold tracking-wide text-muted-foreground">
          {label}
        </p>
        <p className="mt-1 text-xl font-semibold tabular-nums" style={{ color }}>
          {value}
        </p>
        <p className="text-[11px] text-muted-foreground">{subtitle}</p>
      </div>
      {extra ? <div className="text-right text-[11px] tabular-nums font-semibold border-l pl-3 ml-2 flex flex-col gap-0.5 justify-center leading-none">{extra}</div> : null}
    </div>
  )
}

/** Sezione "Dettagli" — riproduzione fedele del WPF ProjectDashboardControl. */
export function ProjectDetailsSection({ projectId }: { projectId: number }) {
  const role = getSession()?.user.userRole
  const isPm = role === "ADMIN" || role === "PM"

  const query = useQuery({
    queryKey: ["project-dashboard", projectId],
    queryFn: () => fetchProjectDashboard(projectId),
    enabled: projectId > 0,
  })

  if (query.isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-28 rounded-xl" />
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-20 rounded-xl" />
          ))}
        </div>
      </div>
    )
  }
  if (query.isError || !query.data) {
    return (
      <p className="text-sm text-destructive">
        {(query.error as Error)?.message || "Commessa non trovata."}
      </p>
    )
  }

  const d = query.data
  const phasePct = d.totalPhases > 0 ? Math.round((d.completedPhases / d.totalPhases) * 100) : 0
  const hoursPct = d.budgetHoursTotal > 0 ? Math.round((d.hoursWorked / d.budgetHoursTotal) * 100) : 0
  const margin = d.revenue - d.totalCost

  return (
    <div className="space-y-1">
      {/* Header scuro */}
      <div className="rounded-lg p-5 text-white" style={{ backgroundColor: "#1A1D26" }}>
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-[22px] font-bold" style={{ color: "#4F6EF7" }}>
            {d.code}
          </span>
          <span
            className="rounded px-2.5 py-0.5 text-[10px] font-semibold"
            style={{ backgroundColor: statusColor(d.status) }}
          >
            {d.status}
          </span>
          <span
            className="rounded px-2.5 py-0.5 text-[10px] font-semibold"
            style={{ backgroundColor: priorityColor(d.priority) }}
          >
            {d.priority}
          </span>
        </div>
        <p className="mt-1 text-sm" style={{ color: "#E5E7EB" }}>
          {d.title}
        </p>
        <div className="mt-2 flex flex-wrap gap-2">
          <Chip icon="🏢" text={d.customerName} />
          {d.pmName ? <Chip icon="👤" text={d.pmName} /> : null}
          {d.startDate ? <Chip icon="📅" text={`Inizio: ${formatDateOrDash(d.startDate)}`} /> : null}
          {d.endDatePlanned ? (
            <Chip icon="🏁" text={`Fine prev.: ${formatDateOrDash(d.endDatePlanned)}`} />
          ) : null}
        </div>
      </div>

      {/* KPI */}
      <div className="grid gap-3 pt-3 sm:grid-cols-2 lg:grid-cols-4">
        <Kpi label="AVANZAMENTO" value={`${phasePct}%`} subtitle={`${d.completedPhases}/${d.totalPhases} Fasi`} color="#4F6EF7" />
        <Kpi label="ORE TOTALI" value={n1(d.hoursWorked)} subtitle={`Budget: ${n0(d.budgetHoursTotal)} h (${hoursPct}%)`} color={hoursPct > 100 ? "#EF4444" : "#059669"} />
        <Kpi label="TECNICI" value={String(d.activeTechnicians.length)} subtitle="Attivi" color="#7C3AED" />
        <Kpi
          label="COSTO MAT."
          value={`${n0(d.materialCost)} €`}
          subtitle="Da acquisti"
          color="#0891B2"
          extra={
            <>
              <div className="flex justify-between gap-2 text-muted-foreground">
                <span>Comm.:</span>
                <span className="font-bold text-foreground">{n0(d.materialCostCommercial)} €</span>
              </div>
              <div className="flex justify-between gap-2 text-muted-foreground">
                <span>Off.:</span>
                <span className="font-bold text-foreground">{n0(d.materialCostOfficina)} €</span>
              </div>
            </>
          }
        />
        {isPm ? (
          <>
            <Kpi label="COSTO ORE" value={`${n0(d.costWorked)} €`} subtitle="Manodopera" color="#D97706" />
            <Kpi label="COSTO TOTALE" value={`${n0(d.totalCost)} €`} subtitle={`Budget: ${n0(d.budgetTotal)} €`} color={d.totalCost > d.budgetTotal ? "#EF4444" : "#059669"} />
            <Kpi label="RICAVO" value={`${n0(d.revenue)} €`} subtitle="Commessa" color="#2563EB" />
            <Kpi label="MARGINE" value={`${n0(margin)} €`} subtitle={`${d.revenue > 0 ? Math.round((margin / d.revenue) * 100) : 0}%`} color={margin >= 0 ? "#059669" : "#EF4444"} />
          </>
        ) : null}
      </div>

      {d.description ? (
        <>
          <SectionTitle>Descrizione</SectionTitle>
          <Card>
            <p className="text-sm whitespace-pre-wrap text-muted-foreground">{d.description}</p>
          </Card>
        </>
      ) : null}

      {d.serverPath ? (
        <>
          <SectionTitle>Cartella Progetto</SectionTitle>
          <Card>
            <p className="truncate text-xs text-muted-foreground" title={d.serverPath}>
              {d.serverPath}
            </p>
          </Card>
        </>
      ) : null}

      {d.notes ? (
        <>
          <SectionTitle>Note Commessa</SectionTitle>
          <Card>
            <p className="text-sm whitespace-pre-wrap text-muted-foreground">{d.notes}</p>
          </Card>
        </>
      ) : null}

      {/* Grafici */}
      {d.departmentSummaries.length > 0 ? (
        <>
          <SectionTitle>Analisi per Reparto</SectionTitle>
          <div className="grid gap-3 lg:grid-cols-2">
            <Card>
              <p className="mb-2 text-xs font-semibold">Ore per Reparto</p>
              <ResponsiveContainer width="100%" height={220}>
                <PieChart>
                  <Pie
                    data={d.departmentSummaries.filter((s) => s.hoursWorked > 0)}
                    dataKey="hoursWorked"
                    nameKey="departmentCode"
                    innerRadius={45}
                    outerRadius={80}
                    label={(entry) =>
                      ((entry as unknown) as { departmentCode: string }).departmentCode
                    }
                  >
                    {d.departmentSummaries
                      .filter((s) => s.hoursWorked > 0)
                      .map((s) => (
                        <Cell key={s.departmentCode} fill={deptColor(s.departmentCode)} />
                      ))}
                  </Pie>
                  <Tooltip />
                </PieChart>
              </ResponsiveContainer>
            </Card>
            <Card>
              <p className="mb-2 text-xs font-semibold">Preventivato vs Assegnato vs Consuntivo</p>
              <ResponsiveContainer width="100%" height={220}>
                <BarChart data={d.departmentSummaries}>
                  <XAxis dataKey="departmentCode" fontSize={10} />
                  <YAxis fontSize={9} />
                  <Tooltip />
                  <Legend wrapperStyle={{ fontSize: 10 }} />
                  <Bar dataKey="costingHours" name="Preventivato" fill="#4F6EF7" fillOpacity={0.4} />
                  <Bar dataKey="assignedHours" name="Assegnato" fill="#4F6EF7" fillOpacity={0.7} />
                  <Bar dataKey="hoursWorked" name="Lavorato" fill="#059669" />
                </BarChart>
              </ResponsiveContainer>
            </Card>
          </div>
        </>
      ) : null}

      {d.weeklyHours.length > 0 ? (
        <>
          <SectionTitle>Andamento Ore Settimanali</SectionTitle>
          <Card>
            <ResponsiveContainer width="100%" height={200}>
              <AreaChart data={d.weeklyHours}>
                <XAxis dataKey="weekLabel" fontSize={9} />
                <YAxis fontSize={9} />
                <Tooltip />
                <Area type="monotone" dataKey="hours" stroke="#4F6EF7" fill="#4F6EF7" fillOpacity={0.25} />
              </AreaChart>
            </ResponsiveContainer>
          </Card>
        </>
      ) : null}

      <GanttSection data={d} />

      {d.deadlines.length > 0 ? (
        <>
          <SectionTitle>Scadenze Prossime</SectionTitle>
          <Card>
            <div className="space-y-1.5">
              {d.deadlines.map((dl, i) => (
                <DeadlineRow key={i} dl={dl} />
              ))}
            </div>
          </Card>
        </>
      ) : null}

      {d.departmentSummaries.length > 0 ? (
        <>
          <SectionTitle>Ore per Reparto</SectionTitle>
          <Card>
            <div className="space-y-3">
              {d.departmentSummaries.map((s) => (
                <DeptBars key={s.departmentCode} s={s} max={maxHours(d.departmentSummaries)} />
              ))}
            </div>
          </Card>
        </>
      ) : null}

      {d.activeTechnicians.length > 0 ? (
        <>
          <SectionTitle>Personale su Commessa</SectionTitle>
          <Card>
            <table className="w-full text-sm">
              <thead>
                <tr className="text-[10px] font-semibold text-muted-foreground">
                  <th className="text-left">TECNICO</th>
                  <th className="text-left">REPARTO</th>
                  <th className="text-center">FASI</th>
                  <th className="text-right">ORE LAV.</th>
                </tr>
              </thead>
              <tbody>
                {d.activeTechnicians.map((t, i) => (
                  <tr key={i} className="border-t">
                    <td className="py-1">{t.employeeName}</td>
                    <td>
                      <span
                        className="rounded px-1.5 py-0.5 text-[10px] font-semibold text-white"
                        style={{ backgroundColor: deptColor(t.departmentCode) }}
                      >
                        {t.departmentCode}
                      </span>
                    </td>
                    <td className="text-center tabular-nums">{t.phaseCount}</td>
                    <td className="text-right tabular-nums">{n1(t.totalHours)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        </>
      ) : null}

      {d.recentEntries.length > 0 ? (
        <>
          <SectionTitle>Ultime Attività</SectionTitle>
          <Card>
            <table className="w-full text-sm">
              <thead>
                <tr className="text-[10px] font-semibold text-muted-foreground">
                  <th className="text-left">DATA</th>
                  <th className="text-left">RISORSA</th>
                  <th className="text-left">FASE</th>
                  <th className="text-right">ORE</th>
                </tr>
              </thead>
              <tbody>
                {d.recentEntries.map((r, i) => (
                  <tr key={i} className="border-t">
                    <td className="py-1">{formatDateOrDash(r.workDate)}</td>
                    <td>{r.employeeName}</td>
                    <td>{r.phaseName}</td>
                    <td className="text-right tabular-nums">{n1(r.hours)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Card>
        </>
      ) : null}
    </div>
  )
}

function Chip({ icon, text }: { icon: string; text: string }) {
  return (
    <span
      className="rounded px-2 py-0.5 text-[11px]"
      style={{ backgroundColor: "#2A2D36", color: "#D1D5DB" }}
    >
      {icon} {text}
    </span>
  )
}

function maxHours(rows: DeptSummary[]): number {
  return Math.max(
    1,
    ...rows.map((s) => Math.max(s.costingHours, s.assignedHours, s.hoursWorked))
  )
}

function DeptBars({ s, max }: { s: DeptSummary; max: number }) {
  const color = deptColor(s.departmentCode)
  const pctPrev = s.costingHours > 0 ? Math.round((s.hoursWorked / s.costingHours) * 100) : 0
  const bar = (value: number, opacity: number) => (
    <div className="h-2 rounded-sm" style={{ width: `${(value / max) * 100}%`, backgroundColor: color, opacity }} />
  )
  return (
    <div>
      <div className="flex items-center gap-2">
        <span className="w-12 text-xs font-semibold" style={{ color }}>
          {s.departmentCode}
        </span>
        <span className="text-[11px] text-muted-foreground">
          Prev: {n0(s.costingHours)} h · Assegn: {n0(s.assignedHours)} h · Lav: {n1(s.hoursWorked)} h ({pctPrev}%) — {s.completedPhases}/{s.totalPhases} fasi
        </span>
      </div>
      <div className="mt-1 space-y-0.5 pl-12">
        {bar(s.costingHours, 0.3)}
        {bar(s.assignedHours, 0.6)}
        {bar(s.hoursWorked, 1)}
      </div>
    </div>
  )
}

function DeadlineRow({ dl }: { dl: UpcomingDeadline }) {
  const icon = dl.daysRemaining < 0 ? "🔴" : dl.daysRemaining <= 3 ? "🟡" : dl.daysRemaining <= 7 ? "🔵" : "🟢"
  const remaining =
    dl.daysRemaining < 0
      ? `${Math.abs(dl.daysRemaining)}gg RITARDO`
      : dl.daysRemaining === 0
        ? "OGGI"
        : `${dl.daysRemaining}gg`
  const color =
    dl.daysRemaining < 0 ? "#EF4444" : dl.daysRemaining <= 3 ? "#F59E0B" : dl.daysRemaining <= 7 ? "#3B82F6" : "#059669"
  return (
    <div className="flex items-center gap-2 text-sm">
      <span>{icon}</span>
      <span
        className="rounded px-1.5 py-0.5 text-[10px] font-semibold text-white"
        style={{ backgroundColor: deptColor(dl.departmentCode) }}
      >
        {dl.departmentCode}
      </span>
      <span className="flex-1 truncate">{dl.phaseName}</span>
      <span className="text-xs text-muted-foreground">{formatDateOrDash(dl.deadline)}</span>
      <span className="w-28 text-right text-xs font-semibold tabular-nums" style={{ color }}>
        {remaining}
      </span>
    </div>
  )
}

/** Gantt fasi: barre orizzontali posizionate sull'intervallo min–max date. */
function GanttSection({ data }: { data: ProjectDashboardData }) {
  const phases = data.phaseGantt
    .filter((p) => p.startDate || p.endDate)
    .slice()
    .sort((a, b) => a.sortOrder - b.sortOrder)
  if (phases.length === 0) return null

  const times = phases.flatMap((p) => [
    p.startDate ? new Date(p.startDate).getTime() : null,
    p.endDate ? new Date(p.endDate).getTime() : null,
  ])
  const valid = times.filter((t): t is number => t != null && !Number.isNaN(t))
  const min = Math.min(...valid)
  const max = Math.max(...valid)
  const span = Math.max(1, max - min)

  return (
    <>
      <SectionTitle>Timeline Fasi</SectionTitle>
      <Card>
        <div className="space-y-1.5">
          {phases.map((p) => {
            const start = p.startDate ? new Date(p.startDate).getTime() : min
            let end = p.endDate ? new Date(p.endDate).getTime() : start + 7 * 86400000
            if (end <= start) end = start + 86400000
            const left = ((start - min) / span) * 100
            const width = ((end - start) / span) * 100
            return (
              <div key={p.phaseId} className="flex items-center gap-2">
                <span className="w-40 shrink-0 truncate text-[11px]" title={p.phaseName}>
                  {p.phaseName} [{p.departmentCode}]
                </span>
                <div className="relative h-4 flex-1 rounded bg-muted/40">
                  <div
                    className="absolute top-0 h-4 rounded"
                    style={{ left: `${left}%`, width: `${width}%`, backgroundColor: deptColor(p.departmentCode), opacity: 0.7 }}
                    title={`${formatDateOrDash(p.startDate)} → ${formatDateOrDash(p.endDate)}`}
                  />
                </div>
              </div>
            )
          })}
        </div>
      </Card>
    </>
  )
}
