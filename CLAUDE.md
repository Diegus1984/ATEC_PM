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
