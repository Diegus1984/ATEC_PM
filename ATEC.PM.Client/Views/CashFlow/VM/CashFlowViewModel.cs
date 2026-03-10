using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.CashFlow;

// ═══════════════════════════════════════════════════════════════
// ROOT VM
// ═══════════════════════════════════════════════════════════════
public class CashFlowViewModel : INotifyPropertyChanged
{
    public int ProjectId { get; set; }
    public string ProjectCode { get; set; } = "";
    public DateTime? StartDate { get; set; }

    private decimal _projectRevenue;
    public decimal ProjectRevenue { get => _projectRevenue; set { _projectRevenue = value; Notify(); } }

    private decimal _paymentAmount;
    public decimal PaymentAmount { get => _paymentAmount; set { _paymentAmount = value; Notify(); } }

    private int _monthCount = 13;
    public int MonthCount { get => _monthCount; set { _monthCount = value; Notify(); } }

    private bool _isInitialized;
    public bool IsInitialized { get => _isInitialized; set { _isInitialized = value; Notify(); Notify(nameof(ShowInit)); Notify(nameof(ShowContent)); } }
    public bool ShowInit => !IsInitialized;
    public bool ShowContent => IsInitialized;

    // Righe griglia (tutte le righe del foglio Excel)
    public ObservableCollection<CfGridRow> Rows { get; set; } = new();

    // Categorie (per CRUD)
    public List<CashFlowCategoryDto> CategoryDtos { get; set; } = new();

    // Labels mese per header colonne
    public string[] MonthLabels { get; set; } = Array.Empty<string>();

    // Grafico
    private ISeries[] _series = Array.Empty<ISeries>();
    public ISeries[] Series { get => _series; set { _series = value; Notify(); } }
    private Axis[] _xAxes = Array.Empty<Axis>();
    public Axis[] XAxes { get => _xAxes; set { _xAxes = value; Notify(); } }
    private Axis[] _yAxes = Array.Empty<Axis>();
    public Axis[] YAxes { get => _yAxes; set { _yAxes = value; Notify(); } }

    // Totali
    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; Notify(); } }

    // ═══════════════════════════════════════════════════════════════
    // RICALCOLO COMPLETO
    // ═══════════════════════════════════════════════════════════════
    public void Recalculate()
    {
        CfGridRow? rowPayment = Rows.FirstOrDefault(r => r.RowType == CfRowType.Payment);
        CfGridRow? rowPct = Rows.FirstOrDefault(r => r.RowType == CfRowType.IncomePct);
        CfGridRow? rowEntrate = Rows.FirstOrDefault(r => r.RowType == CfRowType.IncomeTotal);
        CfGridRow? rowAggiust = Rows.FirstOrDefault(r => r.RowType == CfRowType.Adjustment);
        CfGridRow? rowUscite = Rows.FirstOrDefault(r => r.RowType == CfRowType.ExpenseTotal);
        CfGridRow? rowDiff = Rows.FirstOrDefault(r => r.RowType == CfRowType.Cumulative);

        var catRows = Rows.Where(r => r.RowType == CfRowType.CategoryAmount).ToList();
        var catPctRows = Rows.Where(r => r.RowType == CfRowType.CategoryPct).ToList();

        if (rowPct == null || rowPayment == null || rowEntrate == null || rowUscite == null || rowDiff == null) return;

        decimal pctSum = 0;
        decimal cumulative = 0;
        decimal totEntrate = 0;
        decimal totUscite = 0;

        for (int m = 0; m < MonthCount; m++)
        {
            // Pagamento = importo × %
            decimal pct = rowPct.Values[m];

            // Cap % incasso a 100
            if (pctSum + pct > 100m)
            {
                pct = 100m - pctSum;
                rowPct.Values[m] = pct;
            }

            decimal payment = PaymentAmount * pct / 100m;
            rowPayment.Values[m] = payment;
            pctSum += pct;

            // Entrate = pagamento
            decimal entrataMese = payment;

            // Aggiustamento
            decimal aggiust = rowAggiust?.Values[m] ?? 0;
            entrataMese += aggiust;
            rowEntrate.Values[m] = entrataMese;

            // Uscite per categoria
            decimal uscitaMese = 0;
            for (int c = 0; c < catRows.Count; c++)
            {
                if (c < catPctRows.Count)
                {
                    decimal catPct = catPctRows[c].Values[m];

                    // Cap % categoria a 100
                    decimal catPctSum = 0;
                    for (int pm = 0; pm < m; pm++)
                        catPctSum += catPctRows[c].Values[pm];
                    if (catPctSum + catPct > 100m)
                    {
                        catPct = 100m - catPctSum;
                        catPctRows[c].Values[m] = catPct;
                    }

                    decimal catAmt = catRows[c].TotalAmount * catPct / 100m;
                    catRows[c].Values[m] = catAmt;
                    uscitaMese += catAmt;
                }
            }
            rowUscite.Values[m] = uscitaMese;

            // Differenza cumulativa
            cumulative += entrataMese - uscitaMese;
            rowDiff.Values[m] = cumulative;

            totEntrate += entrataMese;
            totUscite += uscitaMese;
        }

        // Aggiorna colonna B (totali)
        rowPayment.TotalAmount = PaymentAmount;
        rowPct.TotalAmount = pctSum;
        rowEntrate.TotalAmount = totEntrate;
        rowUscite.TotalAmount = totUscite;
        rowDiff.TotalAmount = totEntrate - totUscite;

        foreach (var catPctRow in catPctRows)
            catPctRow.TotalAmount = catPctRow.Values.Sum();

        foreach (var r in Rows)
            r.NotifyAllValues();

        StatusText = $"Entrate {totEntrate:N0} €  —  Uscite {totUscite:N0} €  —  Saldo {totEntrate - totUscite:N0} €";

        BuildChart(totEntrate, totUscite);
    }

    private void BuildChart(decimal totEntrate, decimal totUscite)
    {
        CfGridRow? rowEntrate = Rows.FirstOrDefault(r => r.RowType == CfRowType.IncomeTotal);
        CfGridRow? rowUscite = Rows.FirstOrDefault(r => r.RowType == CfRowType.ExpenseTotal);
        CfGridRow? rowDiff = Rows.FirstOrDefault(r => r.RowType == CfRowType.Cumulative);
        if (rowEntrate == null || rowUscite == null || rowDiff == null) return;

        Series = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Name = "Entrate",
                Values = Enumerable.Range(0, MonthCount).Select(i => (double)rowEntrate.Values[i]).ToArray(),
                Fill = new SolidColorPaint(new SKColor(0x05, 0x96, 0x69)), MaxBarWidth = 18
            },
            new ColumnSeries<double>
            {
                Name = "Uscite",
                Values = Enumerable.Range(0, MonthCount).Select(i => (double)rowUscite.Values[i] * -1).ToArray(),
                Fill = new SolidColorPaint(new SKColor(0xDC, 0x26, 0x26)), MaxBarWidth = 18
            },
            new LineSeries<double>
            {
                Name = "Saldo cumulativo",
                Values = Enumerable.Range(0, MonthCount).Select(i => (double)rowDiff.Values[i]).ToArray(),
                Stroke = new SolidColorPaint(new SKColor(0x4F, 0x6E, 0xF7), 3),
                Fill = null, GeometrySize = 6,
                GeometryStroke = new SolidColorPaint(new SKColor(0x4F, 0x6E, 0xF7), 2),
                GeometryFill = new SolidColorPaint(SKColors.White)
            }
        };
        XAxes = new Axis[] { new Axis { Labels = MonthLabels, LabelsRotation = 45, TextSize = 10 } };
        YAxes = new Axis[] { new Axis { Labeler = v => $"{v:N0} €", TextSize = 10 } };
    }

    // ═══════════════════════════════════════════════════════════════
    // FROM DATA
    // ═══════════════════════════════════════════════════════════════
    public static CashFlowViewModel FromData(CashFlowData data)
    {
        var vm = new CashFlowViewModel
        {
            ProjectId = data.ProjectId,
            ProjectCode = data.ProjectCode,
            _projectRevenue = data.ProjectRevenue,
            StartDate = data.StartDate,
            _paymentAmount = data.PaymentAmount,
            _monthCount = data.MonthCount,
            _isInitialized = data.IsInitialized,
            CategoryDtos = data.Categories
        };

        if (!data.IsInitialized) return vm;

        int mc = data.MonthCount;

        // Indicizza dati
        var byType = data.DataItems.GroupBy(d => d.DataType).ToDictionary(g => g.Key, g => g.ToList());
        var scheduleMap = byType.GetValueOrDefault("SCHEDULE", new()).ToDictionary(d => d.MonthNumber, d => d.DateValue);
        var incomePctMap = byType.GetValueOrDefault("INCOME_PCT", new()).ToDictionary(d => d.MonthNumber, d => d.NumValue);
        var adjustMap = byType.GetValueOrDefault("ADJUSTMENT", new()).ToDictionary(d => d.MonthNumber, d => d.NumValue);
        var bankMap = byType.GetValueOrDefault("BANK", new()).ToDictionary(d => d.MonthNumber, d => d.NumValue);
        var catPctList = byType.GetValueOrDefault("CAT_PCT", new());
        var catPctsByCat = catPctList.GroupBy(d => d.RefId).ToDictionary(g => g.Key, g => g.ToDictionary(d => d.MonthNumber, d => d.NumValue));

        // Labels mese
        vm.MonthLabels = new string[mc];
        for (int m = 0; m < mc; m++)
        {
            int mn = m + 1;
            DateTime? dd = scheduleMap.TryGetValue(mn, out var d) ? d : null;
            if (dd == null && data.StartDate.HasValue)
            {
                var dt = data.StartDate.Value.AddMonths(mn);
                dd = new DateTime(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month));
            }
            vm.MonthLabels[m] = dd.HasValue ? dd.Value.ToString("MMM yy") : $"M{mn}";
        }

        // ── Costruisci righe Excel ──

        // Riga DATE (header mese)
        var rowDate = new CfGridRow(mc) { Label = "Data", RowType = CfRowType.Date, IsEditable = true };
        vm.Rows.Add(rowDate);

        // Riga PAGAMENTO (calcolata)
        var rowPayment = new CfGridRow(mc) { Label = "PAGAMENTO", RowType = CfRowType.Payment, TotalAmount = data.PaymentAmount, CellColor = "#FFE699" };
        vm.Rows.Add(rowPayment);

        // Riga % incasso (editabile)
        var rowPct = new CfGridRow(mc) { Label = "%", RowType = CfRowType.IncomePct, IsEditable = true, CellColor = "#92D050" };
        for (int m = 0; m < mc; m++)
            rowPct.Values[m] = incomePctMap.TryGetValue(m + 1, out var ip) ? ip : 0;
        vm.Rows.Add(rowPct);

        // Riga ENTRATE MESE (calcolata)
        var rowEntrate = new CfGridRow(mc) { Label = "ENTRATE MESE", RowType = CfRowType.IncomeTotal, CellColor = "#FFE699" };
        vm.Rows.Add(rowEntrate);

        // Riga Aggiustamento (editabile)
        var rowAggiust = new CfGridRow(mc) { Label = "Aggiustam. Manu", RowType = CfRowType.Adjustment, IsEditable = true };
        for (int m = 0; m < mc; m++)
            rowAggiust.Values[m] = adjustMap.TryGetValue(m + 1, out var a) ? a : 0;
        vm.Rows.Add(rowAggiust);

        // Riga vuota separatore
        vm.Rows.Add(new CfGridRow(mc) { Label = "", RowType = CfRowType.Separator });

        // Per ogni categoria: riga importo (calcolata) + riga % (editabile)
        foreach (var cat in data.Categories)
        {
            var rowCatAmt = new CfGridRow(mc) { Label = cat.Name, RowType = CfRowType.CategoryAmount, TotalAmount = cat.TotalAmount, RefId = cat.Id, CellColor = "#FFE699" };
            vm.Rows.Add(rowCatAmt);

            var rowCatPct = new CfGridRow(mc) { Label = "%", RowType = CfRowType.CategoryPct, RefId = cat.Id, IsEditable = true, CellColor = "#92D050" };
            if (catPctsByCat.TryGetValue(cat.Id, out var pcts))
                for (int m = 0; m < mc; m++)
                    rowCatPct.Values[m] = pcts.TryGetValue(m + 1, out var p) ? p : 0;
            vm.Rows.Add(rowCatPct);
        }

        // Riga vuota separatore
        vm.Rows.Add(new CfGridRow(mc) { Label = "", RowType = CfRowType.Separator });

        // Riga USCITE MESE (calcolata)
        var rowUscite = new CfGridRow(mc) { Label = "USCITE MESE", RowType = CfRowType.ExpenseTotal, CellColor = "#FFE699" };
        vm.Rows.Add(rowUscite);

        // Riga vuota
        vm.Rows.Add(new CfGridRow(mc) { Label = "", RowType = CfRowType.Separator });

        // Riga DIFFERENZA (calcolata, cumulativa)
        var rowDiff = new CfGridRow(mc) { Label = "DIFFERENZA", RowType = CfRowType.Cumulative, CellColor = "#FFE699" };
        vm.Rows.Add(rowDiff);

        // Riga BANCA (editabile)
        var rowBank = new CfGridRow(mc) { Label = "BANCA", RowType = CfRowType.Bank, IsEditable = true };
        for (int m = 0; m < mc; m++)
            rowBank.Values[m] = bankMap.TryGetValue(m + 1, out var b) ? b : 0;
        vm.Rows.Add(rowBank);

        vm.Recalculate();
        return vm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ═══════════════════════════════════════════════════════════════
// TIPO RIGA
// ═══════════════════════════════════════════════════════════════
public enum CfRowType
{
    Date,           // riga date scadenza
    Payment,        // PAGAMENTO (calcolato)
    IncomePct,      // % incasso (editabile)
    IncomeTotal,    // ENTRATE MESE (calcolato)
    Adjustment,     // Aggiustamento manuale (editabile)
    CategoryAmount, // Importo categoria (calcolato)
    CategoryPct,    // % categoria (editabile)
    ExpenseTotal,   // USCITE MESE (calcolato)
    Cumulative,     // DIFFERENZA cumulativa (calcolato)
    Bank,           // BANCA (editabile)
    Separator       // riga vuota
}

// ═══════════════════════════════════════════════════════════════
// RIGA GRIGLIA (corrisponde a una riga del foglio Excel)
// ═══════════════════════════════════════════════════════════════
public class CfGridRow : INotifyPropertyChanged
{
    public string Label { get; set; } = "";
    public CfRowType RowType { get; set; }
    public int RefId { get; set; }
    public bool IsEditable { get; set; }
    public string CellColor { get; set; } = "";
    public bool IsSeparator => RowType == CfRowType.Separator;

    private decimal _totalAmount;
    public decimal TotalAmount
    {
        get => _totalAmount;
        set { _totalAmount = value; Notify(); }
    }

    public decimal[] Values { get; set; }

    public CfGridRow(int monthCount)
    {
        Values = new decimal[monthCount];
    }

    public void NotifyAllValues()
    {
        Notify(nameof(TotalAmount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
