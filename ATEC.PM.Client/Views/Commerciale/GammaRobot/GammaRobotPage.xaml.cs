using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Commerciale.GammaRobot;

// Consultazione Gamma Robot. Due viste:
//  - "Per Robot": albero Robot → Quadro (configurazione) → distinta componenti.
//  - "Magazzino": elenco componenti → dove entrano (robot/quadri).
// Sola lettura. Dati da /api/gamma-robot/* (vedi GammaRobotController).
public partial class GammaRobotPage : Page
{
    private static readonly SolidColorBrush TabActiveFg = new((Color)ColorConverter.ConvertFromString("#2563EB"));
    private static readonly SolidColorBrush TabInactiveFg = new((Color)ColorConverter.ConvertFromString("#6B7280"));
    private static readonly SolidColorBrush TabActiveBorder = new((Color)ColorConverter.ConvertFromString("#2563EB"));

    private List<GammaRobotDto> _robots = new();
    private List<GammaComponentDto> _components = new();
    private bool _loadingTree;
    private bool _componentsLoaded;

    public GammaRobotPage()
    {
        InitializeComponent();
        SetActiveTab(GammaTabKind.Robot);
        InitComposizioneTab();   // toolbar ADMIN, drag, ecc. (partial GammaRobotPage.Composizione.cs)
        Loaded += async (_, _) => await LoadRobots();
    }

    // ══════════════════════════════════════════════════════
    // TAB
    // ══════════════════════════════════════════════════════

    private enum GammaTabKind { Robot, Magazzino, Composizione }

    private void TabRobot_Click(object sender, RoutedEventArgs e) => SetActiveTab(GammaTabKind.Robot);

    private async void TabMagazzino_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTab(GammaTabKind.Magazzino);
        if (!_componentsLoaded) await LoadComponents();
    }

    private void SetActiveTab(GammaTabKind tab)
    {
        viewRobot.Visibility = tab == GammaTabKind.Robot ? Visibility.Visible : Visibility.Collapsed;
        viewMagazzino.Visibility = tab == GammaTabKind.Magazzino ? Visibility.Visible : Visibility.Collapsed;
        viewComposizione.Visibility = tab == GammaTabKind.Composizione ? Visibility.Visible : Visibility.Collapsed;

        StyleTab(tabRobot, tab == GammaTabKind.Robot);
        StyleTab(tabMagazzino, tab == GammaTabKind.Magazzino);
        StyleTab(tabComposizione, tab == GammaTabKind.Composizione);
    }

    private static void StyleTab(Button btn, bool active)
    {
        btn.Foreground = active ? TabActiveFg : TabInactiveFg;
        btn.BorderBrush = active ? TabActiveBorder : Brushes.Transparent;
    }

    // ══════════════════════════════════════════════════════
    // VISTA PER ROBOT
    // ══════════════════════════════════════════════════════

    private async Task LoadRobots()
    {
        _loadingTree = true;
        txtTreeStatus.Text = "Caricamento...";
        treeRobot.Items.Clear();
        try
        {
            _robots = await ApiClient.GetListAsync<GammaRobotDto>("/api/gamma-robot/robots");
            BuildTree(null);
            txtTreeStatus.Text = $"{_robots.Count} robot";
        }
        catch (System.Exception ex)
        {
            txtTreeStatus.Text = "Errore caricamento";
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore nel caricamento robot:\n{ex.Message}", "Gamma Robot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _loadingTree = false; }
    }

    private void BuildTree(string? filter)
    {
        treeRobot.Items.Clear();
        IEnumerable<GammaRobotDto> robots = _robots;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            string f = filter.Trim().ToLowerInvariant();
            robots = robots.Where(r => r.Modello.ToLowerInvariant().Contains(f)
                                       || (r.Serie ?? "").ToLowerInvariant().Contains(f));
        }

        foreach (GammaRobotDto robot in robots)
        {
            TreeViewItem node = new()
            {
                Header = $"{robot.Modello}  ({robot.QuadriCount})",
                Tag = robot
            };
            node.Items.Add(new TreeViewItem { Header = "..." });   // figlio fittizio → lazy load
            node.Expanded += RobotNode_Expanded;
            treeRobot.Items.Add(node);
        }
    }

    private async void RobotNode_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem node || node.Tag is not GammaRobotDto robot) return;
        if (node.Items.Count != 1 || node.Items[0] is not TreeViewItem { Tag: null }) return; // già caricato

        node.Items.Clear();
        List<GammaQuadroDto> quadri = await ApiClient.GetListAsync<GammaQuadroDto>(
            $"/api/gamma-robot/robots/{robot.Id}/quadri");

        foreach (GammaQuadroDto q in quadri)
            node.Items.Add(new TreeViewItem { Header = BuildQuadroLabel(q), Tag = q });
        if (quadri.Count == 0)
            node.Items.Add(new TreeViewItem { Header = "(nessun quadro)", IsEnabled = false });
    }

    private static string BuildQuadroLabel(GammaQuadroDto q)
    {
        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(q.Controllore)) parts.Add(q.Controllore!);
        if (!string.IsNullOrWhiteSpace(q.Generazione) && q.Generazione != q.Controllore)
            parts.Add($"[{q.Generazione}]");
        if (!string.IsNullOrWhiteSpace(q.Payload)) parts.Add($"{q.Payload}kg");
        if (!string.IsNullOrWhiteSpace(q.AreaLavoro)) parts.Add($"{q.AreaLavoro}m");
        string head = parts.Count > 0 ? string.Join("  ", parts) : "Quadro";
        return $"{head}  ({q.ComponentiCount})";
    }

    private async void TreeRobot_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: GammaQuadroDto quadro }) return;
        await LoadDistinta(quadro);
    }

    private async Task LoadDistinta(GammaQuadroDto quadro)
    {
        TreeViewItem? robotNode = FindRobotNodeOf(quadro);
        string modello = (robotNode?.Tag as GammaRobotDto)?.Modello ?? "Robot";
        txtHeaderTitle.Text = modello;
        txtHeaderSub.Text = BuildQuadroSubtitle(quadro);

        List<GammaDistintaItemDto> items = await ApiClient.GetListAsync<GammaDistintaItemDto>(
            $"/api/gamma-robot/quadri/{quadro.Id}/distinta");

        // Raggruppa per (sezione, slot): il principale porta le sue alternative come sotto-livello.
        List<GammaSlotRow> rows = new();
        foreach (IGrouping<(string?, string?), GammaDistintaItemDto> grp in
                 items.GroupBy(i => (i.Sezione, i.Slot)))
        {
            List<GammaDistintaItemDto> g = grp.ToList();
            // Principale = primo non-alternativa e non-opzione; l'opzione è uno slot a sé (componente facoltativo).
            GammaDistintaItemDto principal = g.FirstOrDefault(x => !x.IsAlternate && !x.IsOptional)
                                             ?? g.FirstOrDefault(x => !x.IsAlternate) ?? g[0];
            List<GammaDistintaItemDto> alts = g.Where(x => x != principal).ToList();
            rows.Add(new GammaSlotRow(principal, alts));
        }

        ListCollectionView view = new(rows);   // l'ordinamento per sezione/slot arriva dal server
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GammaSlotRow.Sezione)));
        dgDistinta.ItemsSource = view;

        // Due totali: base (esclude le opzioni) e base+opzioni (modello "schede opzionali").
        decimal totaleBase = rows.Where(r => !r.IsOptional && r.PrezzoVb.HasValue).Sum(r => r.PrezzoVb!.Value);
        decimal totaleOpz = rows.Where(r => r.IsOptional && r.PrezzoVb.HasValue).Sum(r => r.PrezzoVb!.Value);
        int principali = rows.Count(r => !r.IsOptional);
        int opzioni = rows.Count(r => r.IsOptional);
        int alternative = rows.Sum(r => r.Alternatives.Count);
        int senzaPrezzo = rows.Count(r => !r.PrezzoVb.HasValue || r.PrezzoVb.Value <= 0);

        txtFooterCount.Text = $"{principali} componenti" +
                              (alternative > 0 ? $"  ·  {alternative} alternative" : "") +
                              (opzioni > 0 ? $"  ·  {opzioni} opzioni" : "") +
                              (senzaPrezzo > 0 ? $"  ·  {senzaPrezzo} senza prezzo" : "") +
                              "  ·  doppio click = scheda prodotto";
        txtFooterTotal.Text = opzioni > 0
            ? $"Totale VB base: {totaleBase:N2} €    ·    +opzioni: {(totaleBase + totaleOpz):N2} €"
            : $"Totale VB: {totaleBase:N2} €";
    }

    private static string BuildQuadroSubtitle(GammaQuadroDto q)
    {
        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(q.Controllore)) parts.Add($"Controllore {q.Controllore}");
        if (!string.IsNullOrWhiteSpace(q.Generazione)) parts.Add($"Gen. {q.Generazione}");
        if (!string.IsNullOrWhiteSpace(q.Payload)) parts.Add($"Payload {q.Payload} kg");
        if (!string.IsNullOrWhiteSpace(q.AreaLavoro)) parts.Add($"Area {q.AreaLavoro} m");
        if (!string.IsNullOrWhiteSpace(q.OsVersion)) parts.Add($"OS {q.OsVersion}");
        return string.Join("   ·   ", parts);
    }

    private TreeViewItem? FindRobotNodeOf(GammaQuadroDto quadro)
    {
        foreach (object o in treeRobot.Items)
        {
            if (o is not TreeViewItem robotNode || robotNode.Tag is not GammaRobotDto r) continue;
            if (r.Id == quadro.RobotId) return robotNode;
        }
        return null;
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadRobots();

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingTree) return;
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        BuildTree(txtSearch.Text);
    }

    // ══════════════════════════════════════════════════════
    // VISTA MAGAZZINO (per componente)
    // ══════════════════════════════════════════════════════

    private async Task LoadComponents()
    {
        txtCompStatus.Text = "Caricamento...";
        try
        {
            _components = await ApiClient.GetListAsync<GammaComponentDto>("/api/gamma-robot/components");
            _componentsLoaded = true;
            ApplyComponentFilter(txtCompSearch.Text);
        }
        catch (System.Exception ex)
        {
            txtCompStatus.Text = "Errore caricamento";
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore nel caricamento componenti:\n{ex.Message}", "Gamma Robot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyComponentFilter(string? filter)
    {
        IEnumerable<GammaComponentDto> comps = _components;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            string f = filter.Trim().ToLowerInvariant();
            comps = comps.Where(c => c.Code.ToLowerInvariant().Contains(f)
                                     || c.Name.ToLowerInvariant().Contains(f)
                                     || (c.Categoria ?? "").ToLowerInvariant().Contains(f));
        }
        List<GammaComponentDto> list = comps.ToList();
        dgComponents.ItemsSource = list;
        txtCompStatus.Text = $"{list.Count} componenti";
    }

    private void TxtCompSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtCompSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtCompSearch.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        if (_componentsLoaded) ApplyComponentFilter(txtCompSearch.Text);
    }

    private async void DgComponents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgComponents.SelectedItem is not GammaComponentDto comp)
        {
            dgUsage.ItemsSource = null;
            return;
        }
        txtUsageTitle.Text = comp.Code;
        txtUsageSub.Text = comp.Name;
        List<GammaUsageDto> usage = await ApiClient.GetListAsync<GammaUsageDto>(
            $"/api/gamma-robot/products/{comp.ProductId}/usage");
        dgUsage.ItemsSource = usage;
        int totConf = usage.Sum(u => u.Occorrenze);
        txtUsageSub.Text = $"{comp.Name}   ·   {comp.RobotCount} robot · {totConf} configurazioni";
    }
}

// Riga di distinta "principale" con le sue alternative annidate (sotto-livello).
public class GammaSlotRow
{
    public GammaSlotRow(GammaDistintaItemDto principal, List<GammaDistintaItemDto> alternatives)
    {
        Sezione = principal.Sezione;
        Slot = principal.Slot;
        ProductId = principal.ProductId ?? 0;
        ProductCode = principal.ProductCode;
        ProductName = principal.ProductName;
        PrezzoVb = principal.PrezzoVb;
        IsOptional = principal.IsOptional;
        Alternatives = alternatives;
    }

    public string? Sezione { get; }
    public string? Slot { get; }
    public int ProductId { get; }
    public string? ProductCode { get; }
    public string? ProductName { get; }
    public decimal? PrezzoVb { get; }
    public bool IsOptional { get; }
    public List<GammaDistintaItemDto> Alternatives { get; }

    public bool HasAlternatives => Alternatives.Count > 0;
    public string AltLabel => HasAlternatives ? $"▸ {Alternatives.Count} alt." : "";
    public string OptLabel => IsOptional ? "OPT" : "";
}
