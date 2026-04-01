# ATEC PM — Roadmap & Istruzioni Sessione

## Istruzioni per Claude Code — Inizio Sessione

**SEMPRE all'inizio di ogni sessione leggere:**
1. `.claude/skills/atec-design-system/SKILL.md` — Design system (flat design, palette, spacing, brush)
2. `.claude/skills/wpf-xaml-guide/SKILL.md` — Template XAML e pattern WPF
3. Questo file `roadmap.md` — Istruzioni operative

**Regole:**
- Mai generare XAML senza aver letto il design system
- Usare sempre `StaticResource` per colori — mai hardcodare hex nei XAML
- Font: Segoe UI, 12px body, 11px secondary, 14px header
- Spacing: multipli di 4px (4, 8, 12, 16, 20)
- L'utente comunica in italiano
- Tipi espliciti sempre (no `var` a meno che il tipo sia ovvio dal lato destro)
- Commenti italiani per logica business, inglesi per tecnici

## Stack

- **Client**: WPF .NET 8 (C#)
- **Server**: ASP.NET Core Web API .NET 8
- **Shared**: .NET 8 Class Library (DTOs condivisi)
- **Database**: MySQL (XAMPP) con Dapper ORM
- **Auth**: JWT Bearer token + bcrypt dual-hash
- **Grafici**: OxyPlot.Wpf 2.2
- **PDF**: QuestPDF (Community License)
- **Excel**: ClosedXML
- **Rich Text**: TinyMCE 5 self-hosted + WebView2
- **GitHub**: github.com/Diegus1984/ATEC_PM

---

## Roadmap — Database `roadmap_items`

La roadmap completa è nel database MySQL, tabella `atec_pm.roadmap_items`. NON è più in questo file.

### Struttura tabella

```sql
CREATE TABLE roadmap_items (
    id             INT AUTO_INCREMENT PRIMARY KEY,
    module         VARCHAR(100) NOT NULL,    -- Modulo (Preventivazione, BudgetVsActual, CashFlow, ecc.)
    category       ENUM('FEATURE','BUG','REFACTOR','NOTE') NOT NULL DEFAULT 'FEATURE',
    title          VARCHAR(300) NOT NULL,    -- Titolo breve
    description    TEXT,                     -- Dettagli, note tecniche
    status         ENUM('TODO','IN_PROGRESS','DONE') NOT NULL DEFAULT 'TODO',
    priority       TINYINT NOT NULL DEFAULT 3,  -- 1=urgente, 2=alta, 3=media, 4=bassa, 5=nota
    created_at     DATETIME DEFAULT CURRENT_TIMESTAMP,
    completed_at   DATETIME NULL
);
```

### Query utili

**Vedere i TODO aperti (priorità crescente):**
```sql
SELECT module, title, priority, category
FROM roadmap_items WHERE status='TODO'
ORDER BY priority, module;
```

**Vedere cosa è stato completato per modulo:**
```sql
SELECT module, COUNT(*) AS completati
FROM roadmap_items WHERE status='DONE'
GROUP BY module ORDER BY completati DESC;
```

**Riepilogo generale:**
```sql
SELECT status, COUNT(*) AS tot FROM roadmap_items GROUP BY status;
```

**Aggiungere un nuovo item:**
```sql
INSERT INTO roadmap_items (module, category, title, description, status, priority)
VALUES ('NomeModulo', 'FEATURE', 'Titolo breve', 'Dettagli...', 'TODO', 3);
```

**Completare un item:**
```sql
UPDATE roadmap_items SET status='DONE', completed_at=NOW() WHERE id=XXX;
```

**Cercare per parola chiave:**
```sql
SELECT id, module, title, status FROM roadmap_items
WHERE title LIKE '%parola%' OR description LIKE '%parola%';
```

### Accesso MySQL da riga di comando

```
"C:\xampp\mysql\bin\mysql.exe" -u root -pAtec2005 atec_pm
```

---

## Note Tecniche Rapide

- **DockPanel ordering**: `Dock="Right"` prima degli elementi filler in XAML
- **MySQL + Dapper**: usare `System.Data.IDbConnection/IDbTransaction`, non tipi MySqlConnector
- **QuestPDF**: `QuestPDF.Settings.License = LicenseType.Community`
- **DataGrid edit diretto**: TextBox nel CellTemplate con IsReadOnly bindato
- **JWT Claims**: usare `ClaimTypes.NameIdentifier` per GetCurrentEmployeeId()
- **Codex encoding**: ConvertZeroDateTime=True + CharacterSet=latin1
- **Preventivo risorse**: acronimi reparto (PM1, UTE1, PLC2), no nomi reali. Nomi solo in assegnazione fasi
- **Filtro dipendenti fasi**: endpoint `/api/employees/by-phase/{phaseId}` — risale fase → sezione costo → reparti interessati → dipendenti
- **Colori gruppi BvA/Costing**: dal DB `cost_section_groups.bg_color`, no ColorMap hardcoded
