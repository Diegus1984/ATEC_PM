import * as React from "react"
import { ChevronDown, ChevronRight, PanelLeftClose, PanelLeftOpen } from "lucide-react"

import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip"
import { getSession } from "@/lib/auth/session"
import { cn } from "@/lib/utils"

// Sidebar PM condivisa: barra laterale collassabile usata dalle pagine PM (MoM, Check list,
// Milestones) per una «visualizzazione delle commesse» identica ovunque. Estratta da MoMSidebar.
// Le pagine restano le proprietarie dei dati: costruiscono viste rapide e contenitori (adattatori)
// e li passano come props generiche; qui vive solo la UI (collasso, viste, pallini di stato, rail).

/** Pallino di stato impilato accanto a un contenitore (priorità, stato milestone…). */
export type PmSidebarDot = { dotClass: string; label: string }

/** Voce «Vista rapida»: icona **oppure** pallino colorato + etichetta + conteggio. */
export type PmQuickView = {
  key: string
  selected: boolean
  onClick: () => void
  /** Icona a sinistra (usata se `dotClass` non è valorizzato). */
  icon?: React.ReactNode
  /** Pallino colorato al posto dell'icona (es. priorità Check list). */
  dotClass?: string
  label: string
  /**
   * `undefined` ≠ 0: senza numero il badge NON si mostra. Serve alle viste a
   * caricamento pigro (es. «Commesse chiuse» del Bilancio): uno «0» prima del
   * primo caricamento direbbe il falso.
   */
  count?: number
}

/** Contenitore (commessa, gruppo, riunione…): pallini di stato + etichetta «CODICE — Titolo» + conteggio. */
export type PmContainer = {
  key: string
  selected: boolean
  onClick: () => void
  label: string
  /**
   * Conteggio a destra dell'etichetta. **Omettilo** se la pagina non ha quel numero: uno
   * zero scritto accanto a una commessa si legge «qui non c'è niente», ed è una bugia se
   * il dato semplicemente non è stato caricato (è successo in «Ore Commessa», che mostrava
   * `0` accanto a commesse con decine di ore imputate).
   */
  count?: number
  dots: PmSidebarDot[]
  /** Contenuto del pulsante nel rail compresso; default = sigla di 2 lettere ricavata da `label`. */
  railIcon?: React.ReactNode
  /** Foglie opzionali per rappresentare un contenitore ad albero. */
  children?: PmContainerChild[]
  /**
   * Azioni della voce (di norma un `RowActionsMenu`), a destra del conteggio. Vive fuori
   * dal pulsante di selezione, quindi il clic sul menu non cambia vista.
   */
  actions?: React.ReactNode
}

export type PmContainerChild = {
  key: string
  selected: boolean
  onClick: () => void
  label: string
  count: number
  dotClass?: string
}

/** Elenco di contenitori con una sua intestazione (es. «Commesse» e «Attività» separate). */
export type PmSidebarSection = {
  key: string
  label: string
  containers: PmContainer[]
  /** Testo mostrato quando la sezione è vuota. */
  emptyLabel: string
}

export type PmSidebarProps = {
  /** Prefisso per la chiave localStorage del collasso (persistito per-utente): `${storageKey}.sidebar.collapsed.<id>`. */
  storageKey: string
  quickViews: PmQuickView[]
} & (
  | {
      containers: PmContainer[]
      /** Intestazione della sezione contenitori (es. "Commesse / Riunioni"). */
      containersLabel: string
      /** Testo mostrato quando non ci sono contenitori. */
      emptyLabel: string
      sections?: never
    }
  /** Più elenchi separati, ognuno con la sua intestazione. */
  | {
      sections: PmSidebarSection[]
      containers?: never
      containersLabel?: never
      emptyLabel?: never
    }
)

function CountBadge({ children, selected }: { children: React.ReactNode; selected?: boolean }) {
  return (
    <span
      className={cn(
        "ml-auto shrink-0 rounded-md bg-muted px-1.5 py-px font-mono text-[10.5px] font-semibold tabular-nums text-muted-foreground",
        selected && "bg-background"
      )}
    >
      {children}
    </span>
  )
}

function QuickNavItem({
  selected,
  onClick,
  icon,
  dotClass,
  label,
  count,
}: {
  selected: boolean
  onClick: () => void
  icon?: React.ReactNode
  dotClass?: string
  label: string
  count?: number
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-left text-sm font-medium transition-colors",
        selected
          ? "bg-primary/10 font-semibold text-primary"
          : "text-foreground hover:bg-muted/60"
      )}
    >
      {dotClass ? (
        <span className={cn("size-2 shrink-0 rounded-full", dotClass)} />
      ) : (
        <span
          className={cn(
            "shrink-0 [&_svg]:size-4",
            selected ? "text-primary" : "text-muted-foreground"
          )}
        >
          {icon}
        </span>
      )}
      <span className="min-w-0 flex-1 truncate">{label}</span>
      {count == null ? null : <CountBadge selected={selected}>{count}</CountBadge>}
    </button>
  )
}

function ContainerNavItem({
  selected,
  onClick,
  label,
  count,
  dots,
  children,
  expanded,
  onToggleExpanded,
  actions,
}: {
  selected: boolean
  onClick: () => void
  label: string
  count?: number
  dots: PmSidebarDot[]
  children?: PmContainerChild[]
  expanded: boolean
  onToggleExpanded: () => void
  actions?: React.ReactNode
}) {
  return (
    <div className="space-y-0.5">
      <div
        className={cn(
          "flex items-center rounded-lg transition-colors",
          selected ? "bg-primary/10" : "hover:bg-muted/60"
        )}
      >
        {/* Tooltip anche a barra ESPANSA: le etichette lunghe sono troncate, così si
            leggono per intero insieme a conteggio e legenda dei pallini. */}
        <Tooltip>
          <TooltipTrigger asChild>
            <button
              type="button"
              onClick={onClick}
              className="flex min-w-0 flex-1 items-center gap-2 px-2.5 py-2 text-left"
            >
              {dots.length > 0 ? (
                <span className="mr-1 flex shrink-0 flex-col gap-0.5" aria-hidden>
                  {dots.map((d) => (
                    <span
                      key={d.label}
                      className={cn("size-1.5 rounded-full", d.dotClass)}
                    />
                  ))}
                </span>
              ) : (
                <span className="mr-1 size-1.5 shrink-0" />
              )}
              <span
                className={cn(
                  "min-w-0 flex-1 truncate text-[12.5px] font-medium",
                  selected && "font-semibold text-primary"
                )}
              >
                {label}
              </span>
              {count == null ? null : (
                <span className="ml-auto shrink-0 font-mono text-[10px] tabular-nums text-muted-foreground">
                  {count}
                </span>
              )}
            </button>
          </TooltipTrigger>
          <TooltipContent side="right">
            {label}
            {count == null ? "" : ` · ${count}`}
            {dots.length > 0 ? ` · ${dots.map((d) => d.label).join(", ")}` : ""}
          </TooltipContent>
        </Tooltip>
        {children && children.length > 0 ? (
          <button
            type="button"
            onClick={onToggleExpanded}
            className="mr-1 flex size-7 shrink-0 items-center justify-center rounded-md text-muted-foreground hover:bg-background/70 hover:text-foreground"
            aria-label={expanded ? `Comprimi ${label}` : `Espandi ${label}`}
            aria-expanded={expanded}
          >
            {expanded ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
          </button>
        ) : null}
        {actions ? (
          <div className="mr-0.5 shrink-0 text-muted-foreground">{actions}</div>
        ) : null}
      </div>

      {children && expanded ? (
        <div className="ml-5 space-y-0.5 border-l pl-2">
          {children.map((child) => (
            <button
              key={child.key}
              type="button"
              onClick={child.onClick}
              className={cn(
                "flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-xs transition-colors",
                child.selected
                  ? "bg-primary/10 font-semibold text-primary"
                  : "text-muted-foreground hover:bg-muted/60 hover:text-foreground"
              )}
            >
              {child.dotClass ? (
                <span className={cn("size-1.5 shrink-0 rounded-full", child.dotClass)} />
              ) : null}
              <span className="min-w-0 flex-1 truncate">{child.label}</span>
              <span className="shrink-0 font-mono text-[10px] tabular-nums text-muted-foreground">
                {child.count}
              </span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  )
}

function collapsedKey(storageKey: string): string {
  const employeeId = getSession()?.user.employeeId ?? "anon"
  return `${storageKey}.sidebar.collapsed.${employeeId}`
}

/** Sigla di 2 lettere per il rail compresso (parte prima del trattino dell'etichetta). */
function railBadge(label: string): string {
  const base = label.split(/[—–-]/)[0].trim()
  const words = base.split(/\s+/).filter(Boolean)
  if (words.length >= 2) return (words[0][0] + words[1][0]).toUpperCase()
  return base.slice(0, 2).toUpperCase()
}

/** Pallini di stato sul bordo sinistro del rail compresso. */
function RailStatusDots({ dots }: { dots: PmSidebarDot[] }) {
  if (dots.length === 0) return null
  return (
    <span className="absolute -left-0.5 top-0.5 flex flex-col gap-px" aria-hidden>
      {dots.map((d) => (
        <span
          key={d.label}
          className={cn("size-1.5 rounded-full ring-1 ring-card", d.dotClass)}
          title={d.label}
        />
      ))}
    </span>
  )
}

function RailButton({
  selected,
  onClick,
  label,
  count,
  dots,
  children,
}: {
  selected: boolean
  onClick: () => void
  label: string
  count?: number
  dots?: PmSidebarDot[]
  children: React.ReactNode
}) {
  const dotsLabel =
    dots && dots.length > 0 ? ` · ${dots.map((d) => d.label).join(", ")}` : ""

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <button
          type="button"
          onClick={onClick}
          className={cn(
            "relative flex size-9 shrink-0 items-center justify-center rounded-lg border transition-colors",
            selected
              ? "border-primary/30 bg-primary/10 text-primary"
              : "border-transparent text-muted-foreground hover:bg-muted/60"
          )}
        >
          {children}
          <RailStatusDots dots={dots ?? []} />
          {count != null && count > 0 ? (
            <span className="absolute -right-1 -top-1 flex h-3.5 min-w-3.5 items-center justify-center rounded-full bg-muted px-1 font-mono text-[8.5px] font-semibold tabular-nums text-muted-foreground ring-1 ring-card">
              {count}
            </span>
          ) : null}
        </button>
      </TooltipTrigger>
      <TooltipContent side="right">
        {label}
        {count == null ? "" : ` · ${count}`}
        {dotsLabel}
      </TooltipContent>
    </Tooltip>
  )
}

export function PmSidebar(props: PmSidebarProps) {
  const { storageKey, quickViews } = props
  // Un solo elenco (`containers`) è il caso normale; `sections` serve alle pagine che
  // separano gli elenchi (Check list: Commesse e Attività).
  const sections: PmSidebarSection[] = props.sections ?? [
    {
      key: "containers",
      label: props.containersLabel ?? "",
      containers: props.containers ?? [],
      emptyLabel: props.emptyLabel ?? "",
    },
  ]

  const [collapsed, setCollapsed] = React.useState<boolean>(
    () => localStorage.getItem(collapsedKey(storageKey)) === "1"
  )
  const toggleCollapsed = React.useCallback(() => {
    setCollapsed((prev) => {
      const next = !prev
      localStorage.setItem(collapsedKey(storageKey), next ? "1" : "0")
      return next
    })
  }, [storageKey])
  // #79: titoli di sezione (Commesse / Altre Attività) collassabili, ricordati per barra.
  const sectionCollapseKey = `${collapsedKey(storageKey)}:sections`
  const [collapsedSectionKeys, setCollapsedSectionKeys] = React.useState<Set<string>>(
    () => {
      try {
        const raw = localStorage.getItem(sectionCollapseKey)
        if (!raw) return new Set()
        const parsed = JSON.parse(raw) as unknown
        return Array.isArray(parsed)
          ? new Set(parsed.filter((k): k is string => typeof k === "string"))
          : new Set()
      } catch {
        return new Set()
      }
    }
  )
  const toggleSectionCollapsed = React.useCallback(
    (key: string) => {
      setCollapsedSectionKeys((prev) => {
        const next = new Set(prev)
        if (next.has(key)) next.delete(key)
        else next.add(key)
        try {
          localStorage.setItem(sectionCollapseKey, JSON.stringify([...next]))
        } catch {
          /* storage non disponibile */
        }
        return next
      })
    },
    [sectionCollapseKey]
  )
  const [expandedContainerKeys, setExpandedContainerKeys] = React.useState<Set<string>>(
    () =>
      new Set(
        sections
          .flatMap((section) => section.containers)
          .filter((container) => container.children?.length)
          .map((container) => container.key)
      )
  )
  const toggleContainerExpanded = React.useCallback((key: string) => {
    setExpandedContainerKeys((previous) => {
      const next = new Set(previous)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }, [])

  return (
    // Un solo TooltipProvider per tutta la barra: i tooltip servono sia sul rail
    // compresso sia sulle voci espanse (etichette troncate).
    <TooltipProvider delayDuration={200}>
    <aside
      className={cn(
        "flex shrink-0 flex-col overflow-hidden border-r bg-card transition-[width] duration-300",
        collapsed ? "w-14" : "w-[286px]"
      )}
    >
      {collapsed ? (
        <>
          <div className="flex min-h-0 flex-1 flex-col items-center gap-1 py-3">
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  type="button"
                  onClick={toggleCollapsed}
                  className="flex size-9 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-muted/60 hover:text-foreground"
                >
                  <PanelLeftOpen className="size-4" />
                </button>
              </TooltipTrigger>
              <TooltipContent side="right">Espandi barra</TooltipContent>
            </Tooltip>

            <div className="my-1 h-px w-6 shrink-0 bg-border" />

            {quickViews.map((q) => (
              <RailButton
                key={q.key}
                selected={q.selected}
                onClick={q.onClick}
                label={q.label}
                count={q.count}
              >
                {q.dotClass ? (
                  <span className={cn("size-2.5 rounded-full", q.dotClass)} />
                ) : (
                  <span className="[&_svg]:size-4">{q.icon}</span>
                )}
              </RailButton>
            ))}

            <div className="flex min-h-0 flex-1 flex-col items-center gap-1 overflow-y-auto">
              {sections.map((section) =>
                section.containers.length === 0 ? null : (
                  <React.Fragment key={section.key}>
                    {/* Una riga di separazione per sezione: nel rail non c'è spazio per le
                        intestazioni, ma i gruppi restano distinguibili. */}
                    <div className="my-1 h-px w-6 shrink-0 bg-border" />
                    {section.containers.map((c) => (
                      <RailButton
                        key={c.key}
                        selected={c.selected}
                        onClick={c.onClick}
                        label={c.label}
                        count={c.count}
                        dots={c.dots}
                      >
                        {c.railIcon ?? (
                          <span className="text-[10px] font-bold uppercase">{railBadge(c.label)}</span>
                        )}
                      </RailButton>
                    ))}
                  </React.Fragment>
                )
              )}
            </div>
          </div>
        </>
      ) : (
        <>
          <div className="px-3.5 pb-1.5 pt-3.5">
            <div className="flex items-center justify-between px-1.5 pb-1.5">
              <span className="text-[10.5px] font-bold uppercase tracking-wider text-muted-foreground">
                Viste rapide
              </span>
              <button
                type="button"
                onClick={toggleCollapsed}
                title="Comprimi barra"
                aria-label="Comprimi barra"
                className="-mr-1 flex size-6 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted/60 hover:text-foreground"
              >
                <PanelLeftClose className="size-3.5" />
              </button>
            </div>
            <div className="space-y-0.5">
              {quickViews.map((q) => (
                <QuickNavItem
                  key={q.key}
                  selected={q.selected}
                  onClick={q.onClick}
                  icon={q.icon}
                  dotClass={q.dotClass}
                  label={q.label}
                  count={q.count}
                />
              ))}
            </div>
          </div>

          {/* Le intestazioni scorrono insieme alle voci: con più sezioni devono restare
              attaccate al proprio elenco. */}
          <div className="min-h-0 flex-1 overflow-y-auto px-2 pb-3 pt-1.5">
            {sections.map((section, index) => {
              const hasLabel = section.label.trim().length > 0
              const sectionCollapsed =
                hasLabel && collapsedSectionKeys.has(section.key)
              return (
                <div
                  key={section.key}
                  className={cn("space-y-0.5", index > 0 && "mt-4")}
                >
                  {hasLabel ? (
                    <button
                      type="button"
                      onClick={() => toggleSectionCollapsed(section.key)}
                      className="flex w-full items-center gap-1 px-2.5 pb-1.5 text-left text-[10.5px] font-bold uppercase tracking-wider text-muted-foreground hover:text-foreground"
                      aria-expanded={!sectionCollapsed}
                    >
                      <ChevronRight
                        className={cn(
                          "size-3.5 shrink-0 transition-transform",
                          !sectionCollapsed && "rotate-90"
                        )}
                      />
                      <span className="min-w-0 flex-1 truncate">{section.label}</span>
                      <span className="tabular-nums font-semibold normal-case tracking-normal text-muted-foreground/80">
                        {section.containers.length}
                      </span>
                    </button>
                  ) : null}
                  {sectionCollapsed ? null : section.containers.length === 0 ? (
                    <p className="px-2.5 py-2 text-xs text-muted-foreground">
                      {section.emptyLabel}
                    </p>
                  ) : (
                    section.containers.map((c) => (
                      <ContainerNavItem
                        key={c.key}
                        selected={c.selected}
                        onClick={c.onClick}
                        label={c.label}
                        count={c.count}
                        dots={c.dots}
                        children={c.children}
                        expanded={expandedContainerKeys.has(c.key)}
                        onToggleExpanded={() => toggleContainerExpanded(c.key)}
                        actions={c.actions}
                      />
                    ))
                  )}
                </div>
              )
            })}
          </div>
        </>
      )}
    </aside>
    </TooltipProvider>
  )
}
