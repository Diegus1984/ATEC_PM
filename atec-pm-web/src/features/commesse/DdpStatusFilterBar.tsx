import { cn } from "@/lib/utils"

export interface DdpStatusFilterItem {
  value: string
  label: string
  count: number
  colorBg?: string
  colorFg?: string
}

/** Barra a pillole: conteggi per stato, selezione multipla (filtro OR). */
export function DdpStatusFilterBar({
  items,
  selected,
  onChange,
}: {
  items: DdpStatusFilterItem[]
  selected: Set<string>
  onChange: (next: Set<string>) => void
}) {
  if (items.length === 0) {
    return null
  }

  function toggle(value: string) {
    const next = new Set(selected)
    if (next.has(value)) {
      next.delete(value)
    } else {
      next.add(value)
    }
    onChange(next)
  }

  return (
    <div className="inline-flex max-w-full flex-wrap items-center gap-1 rounded-full bg-muted p-1">
      {items.map((item) => {
        const active = selected.has(item.value)
        return (
          <button
            key={item.value || "__empty__"}
            type="button"
            className={cn(
              "inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-sm font-medium transition-colors",
              active
                ? "border border-border bg-background text-foreground shadow-sm"
                : "text-muted-foreground hover:bg-background/60 hover:text-foreground"
            )}
            onClick={() => toggle(item.value)}
          >
            {item.colorBg ? (
              <span
                className="size-2 shrink-0 rounded-full border"
                style={{
                  backgroundColor: item.colorBg,
                  borderColor: item.colorFg ?? "transparent",
                }}
              />
            ) : null}
            <span className="whitespace-nowrap">{item.label}</span>
            <span
              aria-hidden
              className={cn(
                "inline-flex size-5 shrink-0 items-center justify-center rounded-full text-[11px] font-semibold leading-none tabular-nums",
                active
                  ? "bg-foreground text-background"
                  : "bg-foreground/15 text-foreground"
              )}
            >
              {item.count}
            </span>
          </button>
        )
      })}
    </div>
  )
}
