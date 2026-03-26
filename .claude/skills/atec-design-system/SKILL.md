---
name: atec-design-system
description: ATEC PM WPF design system. Use this skill whenever generating, editing, or reviewing XAML, styles, UserControls, or any UI component. Enforces flat design, blue corporate palette, compact density, no CornerRadius, no shadows.
---

# ATEC PM — Design System for WPF (.NET 8)

> Industrial project management application by ATEC - Automation Technology S.r.l.
> Framework: WPF / C# / .NET 8 — No WinUI, no Avalonia, no MAUI.

## Design Philosophy
- **Industrial-utilitarian**: Clean, functional, zero decoration
- **Flat design**: No gradients, no drop shadows, no CornerRadius
- **Compact density**: Maximum data visibility, 4px base unit
- **Consistency**: Every control follows the same rules

### Eccezioni consentite
- **ToggleSwitchStyle**: CornerRadius (pallino rotondo) + DropShadowEffect (ombra leggera sul thumb) — necessari per la forma del controllo
- **Card/Border prodotti**: CornerRadius="6" consentito su card prodotto/sezione per distinguerle dallo sfondo
- **Badge stati**: CornerRadius su badge di stato (DRAFT, INVIATA, ecc.) e badge tipo (Prod., MAT, R)

## Color Palette

### Primary (Blue Corporate)
| Token          | Hex       | Usage                              |
|----------------|-----------|-------------------------------------|
| PrimaryDark    | `#1B3A5C` | Navigation bg, headers              |
| Primary        | `#2563EB` | Active states, selected, accent     |
| PrimaryLight   | `#3B82F6` | Hover states                        |
| PrimarySubtle  | `#DBEAFE` | Selected row bg, highlights         |

### Neutrals (Cold Gray)
| Token   | Hex       | Usage                          |
|---------|-----------|--------------------------------|
| Gray900 | `#111827` | Primary text                   |
| Gray700 | `#374151` | Secondary text, labels         |
| Gray500 | `#6B7280` | Placeholder, disabled          |
| Gray300 | `#D1D5DB` | Borders, dividers              |
| Gray200 | `#E5E7EB` | Alternate row bg               |
| Gray100 | `#F3F4F6` | Card bg, input bg              |
| Gray50  | `#F9FAFB` | Page background                |
| White   | `#FFFFFF` | Card surface, input surface    |

### Semantic
| Token     | Hex       | Bg Hex    | Usage                     |
|-----------|-----------|-----------|---------------------------|
| Success   | `#16A34A` | `#DCFCE7` | Completato, OK            |
| Warning   | `#D97706` | `#FEF3C7` | In corso, attenzione      |
| Danger    | `#DC2626` | `#FEE2E2` | Errore, scaduto, critico  |

## Typography
- Font: `Segoe UI` everywhere
- Page title: 18px SemiBold
- Section header: 14px SemiBold
- Body/Input/Cell: 12px Regular
- Secondary/Caption: 11px Regular
- DataGrid header: 11px SemiBold

## Spacing (base: 4px)
- DataGrid row: **28px** | Input: **28px** | Button: **28px/32px**
- Card padding: **12px** | Card gap: **8px**
- Nav item: **32px** | Tab: **30px**

## Component Rules

### Navigation (Dark Sidebar)
- Bg: PrimaryDark | Text: White, opacity 0.7→1.0 on active
- Active: 3px left border Primary | Width: 220px (48px collapsed)

### Forms
- Height 28px, border 1px Gray300, white bg
- Focus: border→Primary | Error: border→Danger
- Labels: 12px Gray700, ABOVE input, 4px gap

### DataGrid
- Row 28px, alternating Gray50/White
- Header: Gray100 bg, 11px SemiBold Gray700
- Selected: PrimarySubtle bg | Hover: Gray100
- Horizontal gridlines only (Gray200)

### KPI Cards
- White bg, 1px Gray300 border, 12px padding
- Label: 11px Gray500 | Value: 20px SemiBold | Trend: 11px colored

### Buttons
- Primary: bg=#2563EB text=White hover=#3B82F6
- Secondary: bg=White text=#374151 border=1px #D1D5DB
- Danger: bg=#DC2626 text=White
- 12px SemiBold, no CornerRadius

### Status Tags
- Rectangular, semantic bg + matching text, 11px, padding 6px/2px

### Tabs
- Active: text Primary, 2px bottom border Primary
- Inactive: text Gray500 | No rounded tabs

## NEVER DO
- CornerRadius on ANY element
- DropShadowEffect or Effect properties
- Gradient brushes
- Row height > 32px in DataGrids
- Hardcoded colors — always StaticResource
- Animations for standard interactions
- Nested ScrollViewers
