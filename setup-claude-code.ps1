# ============================================================
# ATEC PM — Claude Code Setup Script
# Esegui dalla root del repo ATEC PM
# PowerShell: .\setup-claude-code.ps1
# ============================================================

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  ATEC PM — Claude Code Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$repoRoot = Get-Location

# ----------------------------------------------------------
# 1. Struttura directory
# ----------------------------------------------------------
Write-Host "[1/6] Creazione struttura directory..." -ForegroundColor Yellow

$dirs = @(
    ".claude/skills/atec-design-system",
    ".claude/skills/wpf-xaml-guide",
    ".claude/commands",
    "Styles"
)

foreach ($d in $dirs) {
    $fullPath = Join-Path $repoRoot $d
    if (-not (Test-Path $fullPath)) {
        New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
        Write-Host "  + $d" -ForegroundColor Green
    } else {
        Write-Host "  = $d (esiste gia')" -ForegroundColor Gray
    }
}

# ----------------------------------------------------------
# 2. CLAUDE.md — Contesto progetto
# ----------------------------------------------------------
Write-Host "[2/6] Creazione CLAUDE.md..." -ForegroundColor Yellow

$claudeMd = @'
# ATEC PM — Project Context for Claude Code

## Project
ATEC PM is a WPF/.NET 8 project management application built by ATEC - Automation Technology S.r.l. (Turin, Italy).

## Tech Stack
- **UI**: WPF (Windows Presentation Foundation) — NOT WinUI, NOT Avalonia, NOT MAUI
- **Language**: C# / .NET 8
- **Backend**: ASP.NET Core Web API
- **Database**: MySQL with Dapper (no EF Core)
- **Pattern**: Code-behind with partial MVVM (no strict MVVM framework)

## Key Architecture Rules
- `PhaseChanged` event triggers full `LoadPhases()` reload
- `SummaryChanged` event updates totals only
- `_loading` flag prevents cascading `SelectionChanged` events
- Financial data (costs, margins) visible only to PM/ADMIN roles

## Design System
**ALWAYS read `.claude/skills/atec-design-system/SKILL.md` before generating ANY XAML.**
- Flat design: NO CornerRadius, NO shadows, NO gradients
- Palette: Blue corporate (#2563EB) + Cold gray
- Compact density: 28px row height, 4px base unit
- All colors via StaticResource brushes — never hardcode colors in XAML
- Font: Segoe UI, 12px body, 11px secondary

## Code Style
- Explicit types always (no `var` unless type is obvious from right side)
- Italian comments for business logic, English for technical comments
- ResourceDictionaries in `/Styles/` folder, merged in App.xaml

## Current Modules
ProjectCostingControl, PhaseRowControl, DocumentManagerControl, Chat, Timesheet, Dashboard, CodexViewer

## Commands
- `dotnet build` — Build solution
- `dotnet run --project ATEC_PM` — Run client
'@

$claudeMdPath = Join-Path $repoRoot "CLAUDE.md"
Set-Content -Path $claudeMdPath -Value $claudeMd -Encoding UTF8
Write-Host "  + CLAUDE.md" -ForegroundColor Green

# ----------------------------------------------------------
# 3. Design System Skill
# ----------------------------------------------------------
Write-Host "[3/6] Installazione design system skill..." -ForegroundColor Yellow

$designSystem = @'
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
'@

$designSkillPath = Join-Path $repoRoot ".claude/skills/atec-design-system/SKILL.md"
Set-Content -Path $designSkillPath -Value $designSystem -Encoding UTF8
Write-Host "  + .claude/skills/atec-design-system/SKILL.md" -ForegroundColor Green

# ----------------------------------------------------------
# 4. WPF XAML Quick Guide Skill
# ----------------------------------------------------------
Write-Host "[4/6] Installazione WPF XAML guide skill..." -ForegroundColor Yellow

$wpfGuide = @'
---
name: wpf-xaml-guide
description: WPF XAML patterns and templates for ATEC PM. Use when creating new UserControls, pages, ResourceDictionaries, or refactoring existing XAML. Provides ready-to-use templates that follow the ATEC design system.
---

# WPF XAML Patterns — ATEC PM

## New UserControl Template
```xml
<UserControl x:Class="ATEC_PM.Controls.{ControlName}"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{StaticResource Gray50Brush}">
    <Grid Margin="16">
        <!-- Content here -->
    </Grid>
</UserControl>
```

## New ResourceDictionary Template
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Styles scoped to a specific control -->
</ResourceDictionary>
```
Merge in the control:
```xml
<UserControl.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="/Styles/{ControlName}Styles.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</UserControl.Resources>
```

## Standard Grid Layout (form with sidebar)
```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="220"/>   <!-- Sidebar -->
        <ColumnDefinition Width="*"/>     <!-- Main content -->
    </Grid.ColumnDefinitions>
    <!-- Sidebar in Column 0, Content in Column 1 -->
</Grid>
```

## Form Layout Pattern
```xml
<StackPanel Margin="16" MaxWidth="600">
    <!-- Field group -->
    <TextBlock Text="Nome Progetto" Style="{StaticResource LabelStyle}"/>
    <TextBox Style="{StaticResource AtecTextBox}" Margin="0,4,0,8"/>

    <TextBlock Text="Cliente" Style="{StaticResource LabelStyle}"/>
    <ComboBox Style="{StaticResource AtecComboBox}" Margin="0,4,0,8"/>

    <!-- Actions -->
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
        <Button Content="Annulla" Style="{StaticResource SecondaryButton}" Margin="0,0,8,0"/>
        <Button Content="Salva" Style="{StaticResource PrimaryButton}"/>
    </StackPanel>
</StackPanel>
```

## DataGrid with standard config
```xml
<DataGrid ItemsSource="{Binding Items}"
          Style="{StaticResource AtecDataGrid}"
          AutoGenerateColumns="False"
          IsReadOnly="True"
          SelectionMode="Single"
          CanUserAddRows="False"
          CanUserDeleteRows="False"
          CanUserReorderColumns="False"
          CanUserResizeRows="False">
    <DataGrid.Columns>
        <DataGridTextColumn Header="Codice" Binding="{Binding Code}" Width="100"/>
        <DataGridTextColumn Header="Descrizione" Binding="{Binding Description}" Width="*"/>
        <DataGridTextColumn Header="Stato" Binding="{Binding Status}" Width="100"/>
    </DataGrid.Columns>
</DataGrid>
```

## KPI Dashboard Row
```xml
<UniformGrid Columns="4" Margin="0,0,0,8">
    <!-- Repeat KPI card pattern from design system -->
</UniformGrid>
```

## Conventions
- All UserControls in `/Controls/` folder
- Per-control styles in `/Styles/{ControlName}Styles.xaml`
- Event handlers: `{Action}_{Event}` pattern (e.g., `BtnSave_Click`)
- Loaded event: `{ControlName}_Loaded`
- Always set `SnapsToDevicePixels="True"` on Border elements with 1px borders
'@

$wpfGuidePath = Join-Path $repoRoot ".claude/skills/wpf-xaml-guide/SKILL.md"
Set-Content -Path $wpfGuidePath -Value $wpfGuide -Encoding UTF8
Write-Host "  + .claude/skills/wpf-xaml-guide/SKILL.md" -ForegroundColor Green

# ----------------------------------------------------------
# 5. Plugin installs (da eseguire dentro Claude Code)
# ----------------------------------------------------------
Write-Host "[5/6] Creazione script comandi per Claude Code..." -ForegroundColor Yellow

$pluginCommands = @'
# ============================================================
# Esegui questi comandi DENTRO Claude Code (uno alla volta)
# ============================================================

# --- WPF Dev Pack (57 skill, 11 agenti, 5 comandi WPF) ---
/plugin marketplace add christian289/dotnet-with-claudecode
/plugin install wpf-dev-pack@dotnet-with-claudecode

# --- Interface Design (design system persistente) ---
/plugin marketplace add Dammyjay93/interface-design
/plugin install interface-design@interface-design

# --- dotnet-skills (30 skill .NET: C#, testing, patterns) ---
/plugin marketplace add Aaronontheweb/dotnet-skills
/plugin install dotnet-skills@dotnet-skills

# ============================================================
# MCP Server: C# LSP (IntelliSense + XAML diagnostics)
# Esegui nel terminale PRIMA di aggiungere a Claude Code:
#
#   git clone https://github.com/HYMMA/csharp-lsp-mcp.git C:\Tools\csharp-lsp-mcp
#   cd C:\Tools\csharp-lsp-mcp\src\CSharpLspMcp
#   dotnet publish -c Release -o C:\Tools\csharp-lsp-mcp\publish
#
# Poi in Claude Code:
#   claude mcp add csharp-lsp -- C:\Tools\csharp-lsp-mcp\publish\CSharpLspMcp.exe
# ============================================================
'@

$pluginPath = Join-Path $repoRoot ".claude\PLUGIN_INSTALL_COMMANDS.md"
Set-Content -Path $pluginPath -Value $pluginCommands -Encoding UTF8
Write-Host "  + .claude/PLUGIN_INSTALL_COMMANDS.md" -ForegroundColor Green

# ----------------------------------------------------------
# 6. AtecColors.xaml ResourceDictionary
# ----------------------------------------------------------
Write-Host "[6/6] Creazione AtecColors.xaml..." -ForegroundColor Yellow

$colorsXaml = @'
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- ===== PRIMARY (Blue Corporate) ===== -->
    <SolidColorBrush x:Key="PrimaryDarkBrush"   Color="#1B3A5C"/>
    <SolidColorBrush x:Key="PrimaryBrush"        Color="#2563EB"/>
    <SolidColorBrush x:Key="PrimaryLightBrush"   Color="#3B82F6"/>
    <SolidColorBrush x:Key="PrimarySubtleBrush"  Color="#DBEAFE"/>

    <!-- ===== NEUTRALS (Cold Gray) ===== -->
    <SolidColorBrush x:Key="Gray900Brush" Color="#111827"/>
    <SolidColorBrush x:Key="Gray700Brush" Color="#374151"/>
    <SolidColorBrush x:Key="Gray500Brush" Color="#6B7280"/>
    <SolidColorBrush x:Key="Gray300Brush" Color="#D1D5DB"/>
    <SolidColorBrush x:Key="Gray200Brush" Color="#E5E7EB"/>
    <SolidColorBrush x:Key="Gray100Brush" Color="#F3F4F6"/>
    <SolidColorBrush x:Key="Gray50Brush"  Color="#F9FAFB"/>
    <SolidColorBrush x:Key="WhiteBrush"   Color="#FFFFFF"/>

    <!-- ===== SEMANTIC ===== -->
    <SolidColorBrush x:Key="SuccessBrush"    Color="#16A34A"/>
    <SolidColorBrush x:Key="SuccessBgBrush"  Color="#DCFCE7"/>
    <SolidColorBrush x:Key="WarningBrush"    Color="#D97706"/>
    <SolidColorBrush x:Key="WarningBgBrush"  Color="#FEF3C7"/>
    <SolidColorBrush x:Key="DangerBrush"     Color="#DC2626"/>
    <SolidColorBrush x:Key="DangerBgBrush"   Color="#FEE2E2"/>
    <SolidColorBrush x:Key="InfoBrush"       Color="#2563EB"/>
    <SolidColorBrush x:Key="InfoBgBrush"     Color="#DBEAFE"/>

    <!-- ===== DDP STATUS ===== -->
    <SolidColorBrush x:Key="DDP_GreenBrush"  Color="#16A34A"/>
    <SolidColorBrush x:Key="DDP_YellowBrush" Color="#EAB308"/>
    <SolidColorBrush x:Key="DDP_RedBrush"    Color="#DC2626"/>
    <SolidColorBrush x:Key="DDP_GrayBrush"   Color="#9CA3AF"/>

</ResourceDictionary>
'@

$colorsPath = Join-Path $repoRoot "Styles\AtecColors.xaml"
if (-not (Test-Path $colorsPath)) {
    Set-Content -Path $colorsPath -Value $colorsXaml -Encoding UTF8
    Write-Host "  + Styles/AtecColors.xaml" -ForegroundColor Green
} else {
    Write-Host "  = Styles/AtecColors.xaml (esiste gia', non sovrascritto)" -ForegroundColor Gray
}

# ----------------------------------------------------------
# Done
# ----------------------------------------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Setup completato!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "File creati:" -ForegroundColor White
Write-Host "  CLAUDE.md                                  <- Contesto progetto"
Write-Host "  .claude/skills/atec-design-system/SKILL.md <- Design system"
Write-Host "  .claude/skills/wpf-xaml-guide/SKILL.md     <- Pattern XAML"
Write-Host "  .claude/PLUGIN_INSTALL_COMMANDS.md          <- Comandi plugin"
Write-Host "  Styles/AtecColors.xaml                      <- ResourceDictionary"
Write-Host ""
Write-Host "PROSSIMO STEP:" -ForegroundColor Yellow
Write-Host "  1. Apri Claude Code nella root del repo"
Write-Host "  2. Esegui i comandi in .claude/PLUGIN_INSTALL_COMMANDS.md"
Write-Host "  3. Per il C# LSP MCP, segui le istruzioni nel file"
Write-Host ""
