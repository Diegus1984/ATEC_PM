using System;
using System.Collections.Generic;
using System.Windows.Media.Effects;
using System.Linq;
using System.Windows.Media;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Risorse;

// Utility condivise per il planner Gantt risorse (calendario, colori, conflitti).
internal static class ResourcePlannerHelpers
{
    // ── Tema barre ──────────────────────────────────────────────

    public static readonly Brush OpBg = B("#3B82F6");
    public static readonly Brush FlexBg = B("#9CA3AF");
    public static readonly Brush FerieBg = B("#EF4444");
    public static readonly Brush FlexFg = B("#1F2937");
    public static readonly Brush Gridline = B("#E4E4E7");
    public static readonly Brush HeaderBg = B("#FAFAFA");
    public static readonly Brush WeekendBg = B("#F4F4F5");
    public static readonly Brush HolidayBg = B("#FEF2F2");
    public static readonly Brush TodayBg = B("#FEF9C3");
    public static readonly Brush Muted = B("#71717A");
    public static readonly Brush Ink = B("#09090B");
    public static readonly Brush RedDay = B("#DC2626");
    public static readonly Brush ConflictBorder = B("#B91C1C");
    public static readonly Brush SelectedBorder = B("#2563EB");
    public static readonly Brush TargetRowBg = B("#DBEAFE");
    public static readonly Brush CreatePreviewBg = B("#93C5FD");
    public static readonly Brush SelfRowBg = B("#EFF6FF");   // riga della risorsa dell'utente loggato (accent leggero)

    // Glow luminoso sfocato al hover (colore neon rispetto al fill della barra)
    private static readonly Dictionary<string, DropShadowEffect> HoverGlowCache = new();

    public static Color HoverGlowColor(ResAssignmentDto a)
    {
        if (a.HasConflict)
            return C("#FF4444");
        return a.Tipo switch
        {
            // Rosso → corallo/rosa acceso
            "FERIE" => C("#FF6B9D"),
            // Grigio → bianco puro
            "FLEX" => C("#FFFFFF"),
            // Blu → ciano elettrico (analogo al lime sul verde)
            _ => C("#22D3EE")
        };
    }

    public static SolidColorBrush HoverGlowBrush(ResAssignmentDto a)
    {
        SolidColorBrush brush = new(HoverGlowColor(a));
        brush.Freeze();
        return brush;
    }

    public static DropShadowEffect HoverGlowEffect(ResAssignmentDto a)
    {
        string key = a.HasConflict ? "CONFLICT" : a.Tipo;
        if (HoverGlowCache.TryGetValue(key, out DropShadowEffect? cached))
            return cached;

        DropShadowEffect effect = new()
        {
            Color = HoverGlowColor(a),
            BlurRadius = 26,
            ShadowDepth = 0,
            Opacity = 1,
            RenderingBias = RenderingBias.Quality
        };
        effect.Freeze();
        HoverGlowCache[key] = effect;
        return effect;
    }

    public static Brush BrushForTipo(string tipo) => tipo switch
    {
        "FERIE" => FerieBg,
        "FLEX" => FlexBg,
        _ => OpBg
    };

    public static (Brush Bg, Brush Fg) ColorsForTipo(string tipo) => tipo switch
    {
        "FERIE" => (FerieBg, Brushes.White),
        "FLEX" => (FlexBg, FlexFg),
        _ => (OpBg, Brushes.White)
    };

    public static string TipoLabel(string t) => t switch
    {
        "FERIE" => "Ferie",
        "FLEX" => "Flessibile",
        _ => "Operativo"
    };

    public static string AssignmentLabel(ResAssignmentDto a) =>
        // Le ferie NON hanno commessa/service/attività: solo descrizione, altrimenti "Ferie".
        // Per le commesse mostra "CODICE — Titolo" (come nel menu a tendina).
        a.Tipo == "FERIE"
            ? FirstNonEmpty(a.Descrizione) ?? TipoLabel(a.Tipo)
            : FirstNonEmpty(ProjectLabel(a), a.ServiceCod, a.OtherActivityDesc, a.Descrizione) ?? TipoLabel(a.Tipo);

    // "CODICE — Titolo" se ci sono entrambi, altrimenti quello presente.
    private static string? ProjectLabel(ResAssignmentDto a)
    {
        bool hasCode = !string.IsNullOrWhiteSpace(a.ProjectCode);
        bool hasTitle = !string.IsNullOrWhiteSpace(a.ProjectTitle);
        if (hasCode && hasTitle) return $"{a.ProjectCode} — {a.ProjectTitle}";
        if (hasCode) return a.ProjectCode;
        return hasTitle ? a.ProjectTitle : null;
    }

    public static string BuildTooltip(ResAssignmentDto a, DateTime start, DateTime end)
    {
        int giorni = DisplayDayCount(a.Tipo, start, end);
        List<string> parts = new()
        {
            a.EmployeeName,
            $"{TipoLabel(a.Tipo)} · {start:dd/MM/yyyy} → {end:dd/MM/yyyy} ({giorni} gg)"
        };
        if (a.Tipo != "FERIE")
        {
            if (!string.IsNullOrWhiteSpace(a.ProjectCode) || !string.IsNullOrWhiteSpace(a.ProjectTitle))
                parts.Add($"Commessa: {string.Join(" — ", new[] { a.ProjectCode, a.ProjectTitle }.Where(x => !string.IsNullOrWhiteSpace(x)))}");
            if (!string.IsNullOrWhiteSpace(a.ServiceCod)) parts.Add($"Service: {a.ServiceCod}");
            if (!string.IsNullOrWhiteSpace(a.OtherActivityDesc)) parts.Add($"Attività: {a.OtherActivityDesc}");
        }
        if (!string.IsNullOrWhiteSpace(a.Descrizione)) parts.Add(a.Descrizione!);
        if (a.HasConflict) parts.Add("⚠ In conflitto");
        // Audit "ultima modifica" (collaborazione multi-utente)
        if (a.UpdatedAt.HasValue)
        {
            string chi = string.IsNullOrWhiteSpace(a.UpdatedByName) ? "—" : a.UpdatedByName!;
            parts.Add($"Modificato da {chi} · {a.UpdatedAt.Value:dd/MM HH:mm}");
        }
        return string.Join("\n", parts);
    }

    public static int DayCount(DateTime start, DateTime end) => (end.Date - start.Date).Days + 1;

    // Conteggio "giorni lavorativi": esclude sabato e domenica (usato per il calcolo ferie).
    public static int WorkingDayCount(DateTime start, DateTime end)
    {
        if (end.Date < start.Date) return 0;
        int n = 0;
        for (DateTime d = start.Date; d <= end.Date; d = d.AddDays(1))
            if (!IsWeekend(d)) n++;
        return n;
    }

    // Giorni "da mostrare": per le FERIE esclude i weekend, per gli altri tipi calendario.
    public static int DisplayDayCount(string tipo, DateTime start, DateTime end) =>
        tipo == "FERIE" ? WorkingDayCount(start, end) : DayCount(start, end);

    // ── Calendario ──────────────────────────────────────────────

    private static readonly (int m, int d)[] FixedHolidays =
        { (1, 1), (1, 6), (4, 25), (5, 1), (6, 2), (8, 15), (11, 1), (12, 8), (12, 25), (12, 26) };

    private static readonly string[] MonthNames =
        { "", "Gennaio", "Febbraio", "Marzo", "Aprile", "Maggio", "Giugno", "Luglio", "Agosto", "Settembre", "Ottobre", "Novembre", "Dicembre" };

    private static readonly string[] DowLetters = { "D", "L", "M", "M", "G", "V", "S" };

    public static DateTime MondayOf(DateTime d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)).Date;

    public static bool IsWeekend(DateTime d) =>
        d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;

    public static bool IsHoliday(DateTime d)
    {
        foreach ((int m, int day) in FixedHolidays)
            if (d.Month == m && d.Day == day) return true;
        DateTime em = EasterMonday(d.Year);
        return d.Month == em.Month && d.Day == em.Day;
    }

    public static string MonthName(int m) => MonthNames[m];

    public static string DowLetter(DateTime d) => DowLetters[(int)d.DayOfWeek];

    public static Brush DayBackground(DateTime d)
    {
        if (d.Date == DateTime.Today) return TodayBg;
        if (IsHoliday(d)) return HolidayBg;
        if (IsWeekend(d)) return WeekendBg;
        return Brushes.Transparent;
    }

    // ── Conflitti ───────────────────────────────────────────────

    public static void RecalculateConflicts(IList<ResAssignmentDto> assignments)
    {
        foreach (ResAssignmentDto row in assignments)
            row.HasConflict = false;

        foreach (IGrouping<int, ResAssignmentDto> grp in assignments.GroupBy(r => r.EmployeeId))
        {
            List<ResAssignmentDto> list = grp.ToList();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (Overlap(list[i], list[j]) && Forbidden(list[i].Tipo, list[j].Tipo))
                    {
                        list[i].HasConflict = true;
                        list[j].HasConflict = true;
                    }
        }
    }

    public static bool WouldConflict(
        IList<ResAssignmentDto> assignments,
        int employeeId,
        DateTime start,
        DateTime end,
        string tipo,
        int excludeId)
    {
        foreach (ResAssignmentDto a in assignments)
        {
            if (a.EmployeeId != employeeId || a.Id == excludeId) continue;
            if (!Overlap(a, start, end)) continue;
            if (Forbidden(tipo, a.Tipo)) return true;
        }
        return false;
    }

    public static bool Overlap(ResAssignmentDto a, ResAssignmentDto b) =>
        a.DataInizio.Date <= b.DataFine.Date && b.DataInizio.Date <= a.DataFine.Date;

    public static bool Overlap(ResAssignmentDto a, DateTime start, DateTime end) =>
        a.DataInizio.Date <= end.Date && start.Date <= a.DataFine.Date;

    /// <summary>
    /// Regole di sovrapposizione fra due attività della STESSA risorsa.
    /// Causali: OP (operativo), FLEX (flessibile, può subire stop), FERIE.
    ///
    /// Sovrapposizione AMMESSA (nessun conflitto):
    ///   • OP + FLEX (e viceversa)
    ///   • FLEX + FLEX  (anche N flessibili sullo stesso periodo)
    ///
    /// Sovrapposizione VIETATA (conflitto da segnalare):
    ///   • OP + OP
    ///   • OP + FERIE (e viceversa)
    ///   • FLEX + FERIE (e viceversa)
    ///   • FERIE + FERIE
    ///
    /// In sintesi: le FERIE non tollerano alcuna sovrapposizione; per il resto
    /// l'overlap è lecito solo se almeno una delle due è FLEX (il flessibile la assorbe).
    /// </summary>
    public static bool Forbidden(string t1, string t2)
    {
        const string ferie = "FERIE";
        const string flex = "FLEX";

        // Le ferie sono esclusive: confliggono con qualsiasi altra attività, ferie incluse.
        if (t1 == ferie || t2 == ferie)
            return true;

        // Restano solo OP/FLEX: ammesso solo se almeno una è flessibile.
        return t1 != flex && t2 != flex;
    }

    // ── Misc ────────────────────────────────────────────────────

    public static string? FirstNonEmpty(params string?[] vals) =>
        vals.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static DateTime EasterMonday(int y) => EasterSunday(y).AddDays(1);

    private static DateTime EasterSunday(int y)
    {
        int a = y % 19, b = y / 100, c = y % 100, dd = b / 4, e = b % 4, f = (b + 8) / 25,
            g = (b - f + 1) / 3, h = (19 * a + b - dd - g + 15) % 30, i = c / 4, k = c % 4,
            l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451,
            month = (h + l - 7 * m + 114) / 31, day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateTime(y, month, day);
    }

    private static SolidColorBrush B(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}
