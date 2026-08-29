# Regole design — ATEC PM Web

## Regola fondamentale

Il client web **deve essere visivamente identico** al preset scelto su [shadcn/create](https://ui.shadcn.com/create?preset=bIkeymG):

| Parametro | Valore |
|-----------|--------|
| Preset code | `bIkeymG` |
| Style | `radix-vega` |
| Base color | `neutral` |
| Font | Inter Variable |
| Radius | `0.625rem` |
| Icone | Lucide |
| Menu accent | `subtle` |

**Non usare** colori hardcoded, radius custom o componenti raw HTML se esiste il primitivo shadcn.

## Come mantenere allineamento

```powershell
# Re-applicare il preset dopo aggiornamenti shadcn
npx shadcn@latest init --preset bIkeymG -f -y --reinstall

# Aggiungere nuovi componenti (sempre radix-vega dal registry)
npx shadcn@latest add table dialog tabs -y -o
```

## Cosa garantisce il look shadcn/create

- `@import "shadcn/tailwind.css"` — token e utility ufficiali
- `@import "tw-animate-css"` — animazioni `animate-in`, `fade-in`, `slide-in`
- Componenti da `npx shadcn add` con `style: radix-vega` in `components.json`
- Transizioni: `transition-all` su button/input, `shadow-xs` su card/outline
- CSS variables OKLCH in `:root` e `.dark`

## Vietato nel client web

- Palette ATEC WPF (#2563EB sidebar scura) — era solo per lo scaffold iniziale
- `rounded-none` / radius 0 — il preset usa 0.625rem
- Classi Tailwind ad hoc al posto dei token (`bg-gray-100` → `bg-muted`)
- Copiare stili a mano invece di usare il registry shadcn

## Layout pagine → blocchi shadcn

Questo file copre **preset, tema e token**. Per il **layout delle pagine** (come
comporre dashboard, liste, form, KPI, date) la fonte di verità è
[BLOCKS-RULES.md](BLOCKS-RULES.md): ogni pagina deve essere fedele ai blocchi
ufficiali di [ui.shadcn.com/blocks](https://ui.shadcn.com/blocks).

## WPF vs Web

Il design system WPF (`atec-design-system`) resta valido per il client desktop.
Il client web segue **esclusivamente** shadcn/create preset `bIkeymG`.
