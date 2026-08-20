import { useQuery } from "@tanstack/react-query"
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  Cell,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts"

import { Skeleton } from "@/components/ui/skeleton"
import { fetchBudgetVsActual } from "@/lib/api/project-bva"
import { fetchProjectDashboard } from "@/lib/api/projects"
import type {
  DeptSummary,
  ProjectDashboardData,
  UpcomingDeadline,
} from "@/lib/api/types"
import { canAccessFeature } from "@/lib/auth/permissions"
import { formatDateOrDash } from "@/lib/date-iso"
import { projectStatusMeta } from "@/features/commesse/project-status"
import { economicKpis } from "@/features/commesse/bva-economics"
// Un solo stile di riquadro (quello del Bilancio): segnalazione #46 chiede che tutte le
// finestre della Dashboard abbiano la stessa estetica delle prime.
import {
  Kpi,
  bvaDeltaClass,
  bvaHoursClass,
  bvaMoneyClass,
  deltaText,
} from "@/features/commesse/bva-shared"
import { hours } from "@/features/commesse/preventivo-dialogs"
import { euro } from "@/lib/format"
import { cn } from "@/lib/utils"

/**
 * Un colore per reparto: tinge la barra e, dalla #108, anche il testo che la
 * accompagna. QLT e SRV mancavano e finivano sul grigio di riserva, cioè lo
 * stesso di AMM e delle fasi senza reparto: tre righe scritte tutte uguali.
 */
const DEPT_COLORS: Record<string, string> = {
  PM: "#4F6EF7",
  UTM: "#059669",
  UTE: "#2563EB",
  MEC: "#D97706",
  INS: "#DC2626",
  PLC: "#7C3AED",
  ROB: "#BE185D",
  ACQ: "#0891B2",
  QLT: "#0F766E",
  SRV: "#B45309",
  AMM: "#6B7280",
  TRASV: "#6B7280",
  DEFAULT: "#6B7280",
}
const deptColor = (code: string) => DEPT_COLORS[code] ?? DEPT_COLORS.DEFAULT

/**
 * Le tre serie del grafico ore (#106), nell'ordine in cui compaiono le barre:
 * preventivate azzurre, assegnate verdi, lavorate rosse. Recharts non legge le
 * variabili CSS del tema, quindi qui gli esadecimali sono obbligati.
 */
const ORE_SERIE = [
  { key: "costingHours", label: "Preventivato", color: "#38BDF8" },
  { key: "assignedHours", label: "Assegnato", color: "#059669" },
  { key: "hoursWorked", label: "Lavorato", color: "#DC2626" },
] as const

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
const n2 = (v: number) =>
  v.toLocaleString("it-IT", { minimumFractionDigits: 0, maximumFractionDigits: 2 })

/** Tooltip Recharts: ore a max 2 decimali (Prev spezzato sui reparti → 145.333… → 145,33). */
const hoursTooltipFormatter = (value: unknown) => {
  const n = typeof value === "number" ? value : Number(value)
  return Number.isFinite(n) ? n2(n) : String(value ?? "")
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h3 className="mt-6 mb-2 text-sm font-semibold text-foreground">{children}</h3>
  )
}

/**
 * I sei riquadri del Conto Economico, ripetuti qui nella Dashboard commessa (#35).
 *
 * Legge lo **stesso endpoint** del Bilancio invece di farsi mandare i numeri dal payload
 * della Dashboard: così i due posti non possono mostrare cifre diverse. Il permesso
 * combacia — l'endpoint e questi riquadri sono chiusi dalla STESSA chiave `data.budget` —
 * quindi chi arriva qui la chiamata la può fare.
 *
 * Query key condivisa con il tab Bilancio (`project-bva`): aprendo prima l'uno e poi
 * l'altro il dato è già in cache e non si ricarica.
 *
 * Subito sotto (#65) i tre riquadri «Bilancio Risorse»: preventivate / assegnate /
 * consuntivate, stesso endpoint e stessa palette ore/€ del Bilancio (#66).
 */
function ProjectEconomicKpis({ projectId }: { projectId: number }) {
  const query = useQuery({
    queryKey: ["project-bva", projectId],
    queryFn: () => fetchBudgetVsActual(projectId),
  })

  if (query.isLoading) {
    return (
      <>
        <SectionTitle>Bilancio Commessa</SectionTitle>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2, 3, 4, 5].map((i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
        <SectionTitle>Bilancio Risorse Commessa</SectionTitle>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      </>
    )
  }
  // Errore completo: niente da mostrare. Se manca solo il conto economico, i riquadri
  // risorse (#65) restano comunque utili.
  if (!query.data) return null

  const data = query.data
  const economic = data.economic
  const deltaHours = data.totalActualHours - data.totalBudgetHours

  return (
    <>
      {economic ? (
        <>
          <SectionTitle>Bilancio Commessa</SectionTitle>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {economicKpis(economic).map((kpi) => (
              <Kpi
                key={kpi.label}
                label={kpi.label}
                value={kpi.value}
                hint={kpi.hint}
                accent={kpi.accent}
              />
            ))}
          </div>
        </>
      ) : null}

      <SectionTitle>Bilancio Risorse Commessa</SectionTitle>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Kpi
          label="Ore Preventivate"
          value={hours(data.totalBudgetHours)}
          accent={bvaHoursClass}
          hint={<span className={bvaMoneyClass}>{euro(data.totalBudgetCost)}</span>}
        />
        <Kpi
          label="Ore Assegnate"
          value={hours(data.totalAssignedHours)}
          accent={bvaHoursClass}
          hint={<span className={bvaMoneyClass}>{euro(data.totalAssignedCost)}</span>}
        />
        <Kpi
          label="Ore Consuntivate"
          value={hours(data.totalActualHours)}
          accent={bvaHoursClass}
          hint={
            <span className="flex flex-col items-start gap-0.5">
              <span className={bvaMoneyClass}>{euro(data.totalActualCost)}</span>
              {Math.abs(deltaHours) > 0.05 ? (
                <span className={cn(bvaDeltaClass(deltaHours), "text-base")}>
                  {deltaText(deltaHours)}
                </span>
              ) : null}
            </span>
          }
        />
      </div>
    </>
  )
}
function Card({ children }: { children: React.ReactNode }) {
  return <div className="rounded-lg border bg-card p-4">{children}</div>
}

/**
 * Dashboard Commessa (ex «Dettagli») — segnalazione #46.
 *
 * Prima i dati di bilancio, poi l'avanzamento; niente riquadri che ripetono gli stessi
 * importi del Conto Economico (Ricavo / Margine / Costo Totale / Costo Ore / Costo Mat.
 * erano doppioni di Totale Ordine / Redditività Effettiva / Consuntivo Costi).
 */
export function ProjectDetailsSection({ projectId }: { projectId: number }) {
  // Conto Economico: è un dato che si MOSTRA, quindi `canAccessFeature` — la concessione
  // in sola lettura di `data.budget` deve continuare a farlo vedere.
  const canSeeEconomics = canAccessFeature("data.budget")

  const query = useQuery({
    queryKey: ["project-dashboard", projectId],
    queryFn: () => fetchProjectDashboard(projectId),
    enabled: projectId > 0,
  })

  if (query.isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-28 rounded-xl" />
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-24 rounded-xl" />
          ))}
        </div>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-24 rounded-xl" />
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
  const statusMeta = projectStatusMeta(d.status)
  const StatusIcon = statusMeta.icon
  const phasePct = d.totalPhases > 0 ? Math.round((d.completedPhases / d.totalPhases) * 100) : 0
  const hoursPct = d.budgetHoursTotal > 0 ? Math.round((d.hoursWorked / d.budgetHoursTotal) * 100) : 0
  const hoursOverBudget = hoursPct > 100

  return (
    <div className="space-y-1">
      {/* Testata della commessa (#106): il fondo nero pieno pesava troppo sulla
          pagina — ora è un grigio pastello sfumato. Va a token e non a colori
          fissi, così regge anche il tema scuro; i due badge, che hanno un fondo
          pieno loro, si portano dietro il bianco esplicito (prima lo ereditavano
          dal `text-white` della testata). */}
      <div className="rounded-lg border bg-gradient-to-br from-muted via-muted/50 to-background p-5">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-[22px] font-bold" style={{ color: "#4F6EF7" }}>
            {d.code}
          </span>
          <span
            className="inline-flex items-center gap-1 rounded px-2.5 py-0.5 text-[10px] font-semibold text-white"
            style={{ backgroundColor: statusMeta.color }}
          >
            <StatusIcon className="size-3" />
            {statusMeta.label}
          </span>
          <span
            className="rounded px-2.5 py-0.5 text-[10px] font-semibold text-white"
            style={{ backgroundColor: priorityColor(d.priority) }}
          >
            {d.priority}
          </span>
        </div>
        <p className="mt-1 text-sm text-foreground">{d.title}</p>
        <div className="mt-2 flex flex-wrap gap-2">
          <Chip icon="🏢" text={d.customerName} />
          {d.pmName ? <Chip icon="👤" text={d.pmName} /> : null}
          {d.startDate ? <Chip icon="📅" text={`Inizio: ${formatDateOrDash(d.startDate)}`} /> : null}
          {d.endDatePlanned ? (
            <Chip icon="🏁" text={`Fine prev.: ${formatDateOrDash(d.endDatePlanned)}`} />
          ) : null}
        </div>
      </div>

      {/* 1) Bilancio — prima i numeri economici (#46). Stessi sei riquadri del Bilancio
          (#35), stessa funzione `economicKpis`, stesso endpoint. */}
      {canSeeEconomics ? <ProjectEconomicKpis projectId={projectId} /> : null}

      {/* 2) Avanzamento — solo ciò che non è già nel bilancio (niente Ricavo/Margine/
          Costo Totale/Ore/Mat.: stanno già in Totale Ordine / Redditività / Consuntivo). */}
      {/* #80: niente tooltip su Avanzamento / Ore totali / Tecnici (come #69 sul bilancio). */}
      <SectionTitle>Avanzamento Commessa</SectionTitle>
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {/* #106: l'avanzamento sono le fasi chiuse dal PM (menù della fase in
            «Fasi assegnate» → «Segna come completata»), non le ore lavorate: le
            ore dicono quanto si è speso, non quanto manca. Le fasi spente non
            entrano nel conto. */}
        <Kpi
          label="Avanzamento"
          value={`${phasePct}%`}
          hint={`${d.completedPhases}/${d.totalPhases} fasi completate`}
        />
        <Kpi
          label="Ore totali"
          value={n1(d.hoursWorked)}
          hint={`Budget: ${n0(d.budgetHoursTotal)} h (${hoursPct}%)`}
          accent={hoursOverBudget ? "text-destructive" : undefined}
        />
        <Kpi
          label="Tecnici"
          value={String(d.activeTechnicians.length)}
          hint="attivi sulla commessa"
        />
      </div>

      {d.description ? (
        <>
          <SectionTitle>Descrizione</SectionTitle>
          <Card>
            <p className="text-sm whitespace-pre-wrap text-muted-foreground">{d.description}</p>
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
                  <Tooltip formatter={hoursTooltipFormatter} />
                </PieChart>
              </ResponsiveContainer>
            </Card>
            <Card>
              <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                <p className="text-xs font-semibold">
                  Preventivato vs Assegnato vs Consuntivo
                </p>
                {/* Legenda scritta a mano: quella di recharts si riordina da sola in
                    ordine alfabetico e non segue le barre — chi legge finisce per
                    associare il colore alla voce sbagliata. */}
                <div className="flex flex-wrap items-center gap-3 text-[10px]">
                  {ORE_SERIE.map((serie) => (
                    <span key={serie.label} className="flex items-center gap-1">
                      <span
                        className="size-2 rounded-[2px]"
                        style={{ backgroundColor: serie.color }}
                      />
                      {serie.label}
                    </span>
                  ))}
                </div>
              </div>
              <ResponsiveContainer width="100%" height={220}>
                <BarChart data={d.departmentSummaries}>
                  <XAxis dataKey="departmentCode" fontSize={10} />
                  <YAxis fontSize={9} />
                  <Tooltip formatter={hoursTooltipFormatter} />
                  {ORE_SERIE.map((serie) => (
                    <Bar
                      key={serie.key}
                      dataKey={serie.key}
                      name={serie.label}
                      fill={serie.color}
                    />
                  ))}
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
  // Sulla testata chiara (#106) i chip scuri non stavano più: fondo carta con
  // bordo, come le altre etichette dell'applicazione.
  return (
    <span className="rounded border bg-background/70 px-2 py-0.5 text-[11px] text-muted-foreground">
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
        {/* #111: ogni valore prende la tonalità della PROPRIA barra — preventivate la
            più tenue, assegnate la media, lavorate il colore pieno — così la riga
            scritta e le tre barre sotto si leggono in coppia.
            Le tonalità si ottengono smorzando verso il GRIGIO del tema, non verso il
            bianco: schiarendo con il bianco alla stessa misura delle barre (opacità
            0,3 e 0,6) il testo scendeva a un contrasto di 2,1:1 su fondo carta, e a
            11 px in grassetto non si leggeva più. Smorzando resta la stessa famiglia
            di colore, la scala si vede lo stesso e tutt'e tre restano leggibili.
            Il conteggio fasi resta grigio: è un'informazione di altra natura. */}
        <span className="text-[11px] font-bold">
          <span
            style={{ color: `color-mix(in srgb, ${color} 55%, var(--muted-foreground))` }}
          >
            Prev: {n2(s.costingHours)} h
          </span>
          <span className="font-normal text-muted-foreground"> · </span>
          <span
            style={{ color: `color-mix(in srgb, ${color} 78%, var(--muted-foreground))` }}
          >
            Assegn: {n2(s.assignedHours)} h
          </span>
          <span className="font-normal text-muted-foreground"> · </span>
          <span style={{ color }}>
            Lav: {n2(s.hoursWorked)} h ({pctPrev}%)
          </span>
          <span className="font-normal text-muted-foreground">
            {" "}
            — {s.completedPhases}/{s.totalPhases} fasi
          </span>
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
