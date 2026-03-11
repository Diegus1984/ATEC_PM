using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.CashFlow;

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
// ROOT VM
// ═══════════════════════════════════════════════════════════════
public class CashFlowViewModel : INotifyPropertyChanged
{
    private bool _isInitialized;
    private int _monthCount = 13;
    private decimal _paymentAmount;
    // Grafico
    private PlotModel _plotModel = new();

    private decimal _projectRevenue;
    // Totali
    private string _statusText = "";
    public event PropertyChangedEventHandler? PropertyChanged;

    // Categorie (per CRUD)
    public List<CashFlowCategoryDto> CategoryDtos { get; set; } = new();

    public bool IsInitialized { get => _isInitialized; set { _isInitialized = value; Notify(); Notify(nameof(ShowInit)); Notify(nameof(ShowContent)); } }
    public int MonthCount { get => _monthCount; set { _monthCount = value; Notify(); } }
    // Labels mese per header colonne
    public string[] MonthLabels { get; set; } = Array.Empty<string>();

    public decimal PaymentAmount { get => _paymentAmount; set { _paymentAmount = value; Notify(); } }
    public PlotModel PlotModel { get => _plotModel; set { _plotModel = value; Notify(); } }
    public string ProjectCode { get; set; } = "";
    public int ProjectId { get; set; }
    public decimal ProjectRevenue { get => _projectRevenue; set { _projectRevenue = value; Notify(); } }
    // Righe griglia (tutte le righe del foglio Excel)
    public ObservableCollection<CfGridRow> Rows { get; set; } = new();
    public bool ShowContent => IsInitialized;
    public bool ShowInit => !IsInitialized;
    public DateTime? StartDate { get; set; }
    public string StatusText { get => _statusText; set { _statusText = value; Notify(); } }
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

        var model = new PlotModel { PlotAreaBorderThickness = new OxyThickness(0) };

        // Asse X — mesi
        var xAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = -0.5,
            Maximum = MonthCount - 0.5,
            MajorStep = 1,
            MinorStep = 1,
            FontSize = 10,
            Angle = 45,
            LabelFormatter = v =>
            {
                int idx = (int)Math.Round(v);
                return idx >= 0 && idx < MonthLabels.Length ? MonthLabels[idx] : "";
            }
        };
        model.Axes.Add(xAxis);

        // Asse Y — valori
        double maxVal = Math.Max(
            Enumerable.Range(0, MonthCount).Select(i => Math.Abs((double)rowEntrate.Values[i])).DefaultIfEmpty(0).Max(),
            Enumerable.Range(0, MonthCount).Select(i => Math.Abs((double)rowDiff.Values[i])).DefaultIfEmpty(0).Max());
        double minVal = Enumerable.Range(0, MonthCount)
            .Select(i => Math.Min((double)rowUscite.Values[i] * -1, (double)rowDiff.Values[i]))
            .DefaultIfEmpty(0).Min();
        double step = maxVal switch
        {
            > 500000 => 100000,
            > 200000 => 50000,
            > 100000 => 20000,
            > 50000 => 10000,
            > 20000 => 5000,
            > 10000 => 2000,
            > 5000 => 1000,
            > 1000 => 500,
            _ => 100
        };

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            StringFormat = "N0",
            FontSize = 9,
            MajorStep = step,
            Minimum = minVal - step,
            Maximum = maxVal + step,
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromRgb(0xE4, 0xE7, 0xEC)
        });

        // Barre Entrate (verticali)
        var entrSeries = new RectangleBarSeries
        {
            Title = "Entrate",
            FillColor = OxyColor.FromRgb(0x05, 0x96, 0x69),
            StrokeThickness = 0,
            IsVisible = true
        };
        for (int i = 0; i < MonthCount; i++)
        {
            double val = (double)rowEntrate.Values[i];
            if (val != 0) entrSeries.Items.Add(new RectangleBarItem(i - 0.35, 0, i - 0.05, val));
        }
        model.Series.Add(entrSeries);

        // Barre Uscite (verticali, negative)
        var uscSeries = new RectangleBarSeries
        {
            Title = "Uscite",
            FillColor = OxyColor.FromRgb(0xDC, 0x26, 0x26),
            StrokeThickness = 0,
            IsVisible = true
        };
        for (int i = 0; i < MonthCount; i++)
        {
            double val = (double)rowUscite.Values[i] * -1;
            if (val != 0) uscSeries.Items.Add(new RectangleBarItem(i + 0.05, 0, i + 0.35, val));
        }
        model.Series.Add(uscSeries);

        // Annotazioni valore sopra/sotto le barre
        for (int i = 0; i < MonthCount; i++)
        {
            double valE = (double)rowEntrate.Values[i];
            double valU = (double)rowUscite.Values[i] * -1;
            if (valE != 0)
            {
                model.Annotations.Add(new OxyPlot.Annotations.TextAnnotation
                {
                    Text = $"{valE:N0}",
                    TextPosition = new DataPoint(i - 0.2, valE),
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
                    FontSize = 8,
                    TextColor = OxyColor.FromRgb(0x05, 0x96, 0x69),
                    StrokeThickness = 0
                });
            }
            if (valU != 0)
            {
                model.Annotations.Add(new OxyPlot.Annotations.TextAnnotation
                {
                    Text = $"{valU:N0}",
                    TextPosition = new DataPoint(i + 0.2, valU),
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,
                    FontSize = 8,
                    TextColor = OxyColor.FromRgb(0xDC, 0x26, 0x26),
                    StrokeThickness = 0
                });
            }
        }

        // Linea Saldo cumulativo
        var lineSeries = new OxyPlot.Series.LineSeries
        {
            Title = "Saldo cumulativo",
            Color = OxyColor.FromRgb(0x4F, 0x6E, 0xF7),
            StrokeThickness = 3,
            MarkerType = MarkerType.Circle,
            MarkerSize = 4,
            MarkerFill = OxyColors.White,
            MarkerStroke = OxyColor.FromRgb(0x4F, 0x6E, 0xF7),
            MarkerStrokeThickness = 2,
            TrackerFormatString = "Saldo cumulativo\nValore: {4:N0} €"
        };
        for (int i = 0; i < MonthCount; i++)
            lineSeries.Points.Add(new DataPoint(i, (double)rowDiff.Values[i]));
        model.Series.Add(lineSeries);

        // Legenda
        model.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPosition = OxyPlot.Legends.LegendPosition.BottomCenter,
            LegendOrientation = OxyPlot.Legends.LegendOrientation.Horizontal
        });

        PlotModel = model;
    }
    private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
// ═══════════════════════════════════════════════════════════════
// RIGA GRIGLIA (corrisponde a una riga del foglio Excel)
// ═══════════════════════════════════════════════════════════════
public class CfGridRow : INotifyPropertyChanged
{
    private decimal _totalAmount;
    public CfGridRow(int monthCount)
    {
        Values = new decimal[monthCount];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CellColor { get; set; } = "";
    public bool IsAmountEditable => RowType is CfRowType.CategoryAmount;
    public bool IsEditable { get; set; }
    public bool IsSeparator => RowType == CfRowType.Separator;
    public string Label { get; set; } = "";
    public int RefId { get; set; }
    public CfRowType RowType { get; set; }
    public decimal TotalAmount
    {
        get => _totalAmount;
        set { _totalAmount = value; Notify(); }
    }

    public decimal[] Values { get; set; }
    public void NotifyAllValues()
    {
        Notify(nameof(TotalAmount));
        Notify(nameof(Values));
    }
    private void Notify([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
