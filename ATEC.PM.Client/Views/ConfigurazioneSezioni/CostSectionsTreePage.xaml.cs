using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

// ══════════════════════════════════════════════════════════════
// DRAG ADORNER — visual feedback during drag & drop
// ══════════════════════════════════════════════════════════════

public class DragDropAdorner : Adorner
{
    private readonly Border _visual;
    private Point _position;

    public DragDropAdorner(UIElement adornedElement, string text, string icon, Color bgColor)
        : base(adornedElement)
    {
        IsHitTestVisible = false;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = icon + " ",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });

        _visual = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Opacity = 0.85,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 8, Opacity = 0.3, ShadowDepth = 2
            },
            Child = panel
        };

        AddVisualChild(_visual);
    }

    public void UpdatePosition(Point pos)
    {
        _position = pos;
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(0, 0); // don't affect layout
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Position the badge right under the cursor tip
        _visual.Arrange(new Rect(_position, _visual.DesiredSize));
        return finalSize;
    }

    protected override Visual GetVisualChild(int index) => _visual;
    protected override int VisualChildrenCount => 1;
}

public partial class CostSectionsTreePage : Page
{
    private List<CostSectionGroupDto> _groups = new();
    private List<CostSectionTemplateDto> _templates = new();
    private List<DepartmentDto> _departments = new();
    private List<PhaseTemplateDto> _phases = new();
    private Point _dragStartPoint;
    private TreeViewItem? _lastHighlighted;
    private DragDropAdorner? _dragAdorner;

    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, string> GroupColors = new()
    {
        { "GESTIONE", "#2563EB" }, { "PRESCHIERAMENTO", "#7C3AED" },
        { "INSTALLAZIONE", "#D97706" }, { "OPZIONE", "#DC2626" }
    };

    private string? _lastExpandedGroup;
    private string? _lastExpandedTreeGroup;
    private bool _isRenderingTree;

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private static SolidColorBrush BrushWithAlpha(string hex, byte alpha)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    public CostSectionsTreePage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadData();
        GiveFeedback += OnGiveFeedback;
    }

    private void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_dragAdorner != null)
        {
            var screenPos = GetMouseScreenPosition();
            var localPos = this.PointFromScreen(screenPos);
            // Offset: just below and right of cursor tip
            _dragAdorner.UpdatePosition(new Point(localPos.X + 15, localPos.Y + 15));
        }
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private static Point GetMouseScreenPosition()
    {
        GetCursorPos(out POINT p);
        return new Point(p.X, p.Y);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private void ShowDragAdorner(string text, string icon, Color bgColor)
    {
        HideDragAdorner();
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer == null) return;
        _dragAdorner = new DragDropAdorner(this, text, icon, bgColor);
        layer.Add(_dragAdorner);
    }

    private void HideDragAdorner()
    {
        if (_dragAdorner != null)
        {
            var layer = AdornerLayer.GetAdornerLayer(this);
            layer?.Remove(_dragAdorner);
            _dragAdorner = null;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // DATA LOADING
    // ══════════════════════════════════════════════════════════════

    private async Task LoadData()
    {
        try
        {
            string dJson = await ApiClient.GetAsync("/api/departments");
            var dDoc = JsonDocument.Parse(dJson);
            if (dDoc.RootElement.GetProperty("success").GetBoolean())
                _departments = JsonSerializer.Deserialize<List<DepartmentDto>>(
                    dDoc.RootElement.GetProperty("data").GetRawText(), _jsonOpt) ?? new();

            string gJson = await ApiClient.GetAsync("/api/cost-sections/groups");
            var gDoc = JsonDocument.Parse(gJson);
            if (gDoc.RootElement.GetProperty("success").GetBoolean())
                _groups = JsonSerializer.Deserialize<List<CostSectionGroupDto>>(
                    gDoc.RootElement.GetProperty("data").GetRawText(), _jsonOpt) ?? new();

            string tJson = await ApiClient.GetAsync("/api/cost-sections/templates");
            var tDoc = JsonDocument.Parse(tJson);
            if (tDoc.RootElement.GetProperty("success").GetBoolean())
                _templates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                    tDoc.RootElement.GetProperty("data").GetRawText(), _jsonOpt) ?? new();

            string pJson = await ApiClient.GetAsync("/api/phases/templates");
            var pDoc = JsonDocument.Parse(pJson);
            if (pDoc.RootElement.GetProperty("success").GetBoolean())
                _phases = JsonSerializer.Deserialize<List<PhaseTemplateDto>>(
                    pDoc.RootElement.GetProperty("data").GetRawText(), _jsonOpt) ?? new();

            RenderAll();
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void RenderAll()
    {
        RenderDepartments();
        RenderPhases();
        RenderTree();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        int linked = _phases.Count(p => p.CostSectionTemplateId != null);
        int free = _phases.Count - linked;
        txtCount.Text = $"{_groups.Count} gruppi — {_templates.Count} sezioni — {linked} fasi collegate";
        txtStatus.Text = free > 0
            ? $"{free} fasi ancora da collegare"
            : "Tutte le fasi sono collegate a una sezione costo";
    }

    // ══════════════════════════════════════════════════════════════
    // LEFT PANEL: DEPARTMENTS (draggable badges)
    // ══════════════════════════════════════════════════════════════

    private void RenderDepartments()
    {
        pnlDepartments.Children.Clear();
        foreach (var dept in _departments.Where(d => d.IsActive).OrderBy(d => d.SortOrder))
        {
            var badge = new Border
            {
                Background = Brush("#EEF2FF"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Tag = dept
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = dept.Code,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#4F6EF7"),
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = $" — {dept.Name}",
                FontSize = 11,
                Foreground = Brush("#6B7280"),
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = $" — K:{dept.DefaultMarkup:F2} — €{dept.HourlyCost:F2}/h",
                FontSize = 10,
                Foreground = Brush("#9CA3AF"),
                VerticalAlignment = VerticalAlignment.Center
            });
            badge.Child = sp;
            badge.PreviewMouseLeftButtonDown += DeptBadge_PreviewMouseLeftButtonDown;
            badge.PreviewMouseMove += DeptBadge_PreviewMouseMove;
            pnlDepartments.Children.Add(badge);
        }
    }

    private void DeptBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (sender is Border b && b.Tag is DepartmentDto dept)
            {
                e.Handled = true;
                _ = OpenDepartmentDialog(dept);
            }
            return;
        }
        _dragStartPoint = e.GetPosition(null);
    }

    private async Task OpenDepartmentDialog(DepartmentDto dept)
    {
        var dlg = new DepartmentDialog(dept) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await LoadData();
    }

    private async void BtnAddDepartment_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new DepartmentDialog() { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            await LoadData();
    }

    private void DeptBadge_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        Point pos = e.GetPosition(null);
        Vector diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is Border b && b.Tag is DepartmentDto dept)
            {
                ShowDragAdorner(dept.Code, "🔧", Color.FromRgb(0x4F, 0x6E, 0xF7));
                var data = new DataObject("DepartmentDrop", dept);
                DragDrop.DoDragDrop(b, data, DragDropEffects.Copy);
                HideDragAdorner();
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // LEFT PANEL: PHASE TEMPLATES (unlinked, draggable)
    // ══════════════════════════════════════════════════════════════

    private void RenderPhases(string? filter = null)
    {
        pnlPhases.Children.Clear();
        var unlinked = _phases
            .Where(p => p.CostSectionTemplateId == null)
            .Where(p => string.IsNullOrEmpty(filter) || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || p.Category.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Category).ThenBy(p => p.SortOrder)
            .ToList();

        txtPhaseCount.Text = $"{unlinked.Count} fasi non collegate";

        // Group by department
        var grouped = unlinked
            .GroupBy(p => string.IsNullOrEmpty(p.DepartmentCode) ? "— Senza reparto" : $"{p.DepartmentCode} — {p.DepartmentName}")
            .OrderBy(g => g.Key);

        bool anyExpanded = false;
        foreach (var group in grouped)
        {
            bool shouldExpand = _lastExpandedGroup != null
                ? group.Key == _lastExpandedGroup
                : !anyExpanded; // first group if no memory

            var expander = new Expander
            {
                IsExpanded = shouldExpand,
                Margin = new Thickness(0, 4, 0, 0),
                Tag = group.Key
            };
            expander.Expanded += PhaseExpander_Expanded;
            if (shouldExpand) anyExpanded = true;

            // Custom header
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#4F6EF7"),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = $"  ({group.Count()})",
                FontSize = 10,
                Foreground = Brush("#9CA3AF"),
                VerticalAlignment = VerticalAlignment.Center
            });

            // + button to add new phase template for this department
            var firstPhase = group.First();
            var btnAdd = new Button
            {
                Content = "+", Width = 18, Height = 18, FontSize = 11,
                Background = Brush("#E0E7FF"), Foreground = Brush("#4F6EF7"),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                ToolTip = "Aggiungi fase template",
                Tag = firstPhase.DepartmentId
            };
            btnAdd.Click += BtnAddPhaseTemplate_Click;
            headerPanel.Children.Add(btnAdd);

            expander.Header = headerPanel;

            var content = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            foreach (var phase in group.OrderBy(p => p.SortOrder))
            {
                content.Children.Add(BuildPhaseBadgeLeft(phase));
            }
            expander.Content = content;
            pnlPhases.Children.Add(expander);
        }
    }

    private async Task OpenPhaseTemplateDialog(PhaseTemplateDto phase)
    {
        Window dlg = new()
        {
            Title = $"Modifica Fase — {phase.Name}",
            Width = 400, Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = Brush("#F7F8FA")
        };

        var sp = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

        sp.Children.Add(new TextBlock { Text = "Nome:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("#6B7280") });
        var txtName = new TextBox
        {
            Text = phase.Name, Height = 32, Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13, BorderBrush = Brush("#E4E7EC"), Margin = new Thickness(0, 4, 0, 10)
        };
        sp.Children.Add(txtName);

        sp.Children.Add(new TextBlock { Text = "Categoria:", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("#6B7280") });
        var txtCat = new TextBox
        {
            Text = phase.Category, Height = 32, Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13, BorderBrush = Brush("#E4E7EC"), Margin = new Thickness(0, 4, 0, 14)
        };
        sp.Children.Add(txtCat);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = new Button { Content = "Annulla", Width = 80, Height = 30, Background = Brush("#F3F4F6"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
        var btnOk = new Button { Content = "Salva", Width = 80, Height = 30, Background = Brush("#4F6EF7"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        btnOk.Click += (s, ev) => { dlg.DialogResult = true; dlg.Close(); };
        btnCancel.Click += (s, ev) => { dlg.DialogResult = false; dlg.Close(); };
        btns.Children.Add(btnCancel);
        btns.Children.Add(btnOk);
        sp.Children.Add(btns);

        dlg.Content = sp;
        txtName.Focus();
        txtName.SelectAll();

        if (dlg.ShowDialog() != true) return;

        string newName = txtName.Text.Trim();
        string newCat = txtCat.Text.Trim();

        try
        {
            if (newName != phase.Name && !string.IsNullOrEmpty(newName))
            {
                string json = JsonSerializer.Serialize(new { field = "name", value = newName });
                await ApiClient.PatchAsync($"/api/phases/templates/{phase.Id}/field", json);
                phase.Name = newName;
            }
            if (newCat != phase.Category && !string.IsNullOrEmpty(newCat))
            {
                string json = JsonSerializer.Serialize(new { field = "category", value = newCat });
                await ApiClient.PatchAsync($"/api/phases/templates/{phase.Id}/field", json);
                phase.Category = newCat;
            }
            RenderPhases(txtSearchPhase.Text.Trim());
            RenderTree();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnDeletePhaseTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PhaseTemplateDto phase) return;

        if (MessageBox.Show($"Eliminare la fase \"{phase.Name}\"?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/phases/templates/{phase.Id}");
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadData();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnAddPhaseTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        int? deptId = btn.Tag as int?;

        // Find department info for default category
        var dept = deptId.HasValue ? _departments.FirstOrDefault(d => d.Id == deptId.Value) : null;
        string defaultCategory = dept?.Code ?? "TRASVERSALE";

        string? name = PromptInput("Nuova Fase Template", "Nome fase:", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        int maxSort = _phases.Any() ? _phases.Max(p => p.SortOrder) + 1 : 1;

        try
        {
            string json = JsonSerializer.Serialize(new
            {
                name,
                category = defaultCategory,
                departmentId = deptId,
                costSectionTemplateId = (int?)null,
                sortOrder = maxSort,
                isDefault = false
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/phases/templates", json);
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadData();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void PhaseExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander opened) return;
        _lastExpandedGroup = opened.Tag as string;
        foreach (var child in pnlPhases.Children)
        {
            if (child is Expander exp && exp != opened)
                exp.IsExpanded = false;
        }
    }

    private Border BuildPhaseBadgeLeft(PhaseTemplateDto phase)
    {
        var badge = new Border
        {
            Background = Brush("#FFF8F0"),
            BorderBrush = Brush("#FDE68A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand,
            Tag = phase
        };

        var dp = new DockPanel();

        // Delete button — docked right
        var btnDel = new Button
        {
            Content = "✕", Width = 16, Height = 16, FontSize = 8,
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
            Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Elimina fase template", Tag = phase
        };
        btnDel.Click += BtnDeletePhaseTemplate_Click;
        DockPanel.SetDock(btnDel, Dock.Right);
        dp.Children.Add(btnDel);

        // Category badge — docked right
        var catBadge = new Border
        {
            Background = Brush("#F3F4F6"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = phase.Category,
                FontSize = 9,
                Foreground = Brush("#6B7280")
            }
        };
        DockPanel.SetDock(catBadge, Dock.Right);
        dp.Children.Add(catBadge);

        // Name — fills remaining space, truncated
        dp.Children.Add(new TextBlock
        {
            Text = phase.Name,
            FontSize = 12,
            Foreground = Brush("#92400E"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var sp = dp;

        badge.Child = sp;
        badge.AllowDrop = true;
        badge.PreviewMouseLeftButtonDown += PhaseBadge_PreviewMouseLeftButtonDown;
        badge.PreviewMouseMove += PhaseBadge_PreviewMouseMove;
        badge.DragOver += PhaseBadgeLeft_DragOver;
        badge.Drop += PhaseBadgeLeft_Drop;
        return badge;
    }

    private void TxtSearchPhase_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenderPhases(txtSearchPhase.Text.Trim());
    }

    private void PhaseBadgeLeft_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (sender is not Border b || b.Tag is not PhaseTemplateDto) return;
        if (e.Data.GetDataPresent("DepartmentDrop"))
        {
            e.Effects = DragDropEffects.Copy;
            b.Background = Brush("#E0E7FF");
        }
    }

    private void PhaseBadgeLeft_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border b || b.Tag is not PhaseTemplateDto phase) return;
        b.Background = Brush("#FFF8F0");
        if (e.Data.GetData("DepartmentDrop") is DepartmentDto dept)
            _ = SetPhaseDepartment(phase, dept);
    }

    private void PhaseBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (sender is Border b && b.Tag is PhaseTemplateDto phase)
            {
                e.Handled = true;
                _ = OpenPhaseTemplateDialog(phase);
            }
            return;
        }
        _dragStartPoint = e.GetPosition(null);
    }

    private void PhaseBadge_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        Point pos = e.GetPosition(null);
        Vector diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is Border b && b.Tag is PhaseTemplateDto phase)
            {
                ShowDragAdorner(phase.Name, "📋", Color.FromRgb(0xD9, 0x77, 0x06));
                var data = new DataObject("PhaseDrop", phase);
                DragDrop.DoDragDrop(b, data, DragDropEffects.Copy);
                HideDragAdorner();
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // RIGHT PANEL: TREEVIEW
    // ══════════════════════════════════════════════════════════════

    private void RenderTree()
    {
        _isRenderingTree = true;
        tvSections.Items.Clear();

        foreach (var group in _groups.OrderBy(g => g.SortOrder))
        {
            var sections = _templates
                .Where(t => t.GroupId == group.Id)
                .OrderBy(t => t.SortOrder).ToList();

            var groupNode = BuildGroupNode(group, sections);
            tvSections.Items.Add(groupNode);
        }
        _isRenderingTree = false;
    }

    private TreeViewItem BuildGroupNode(CostSectionGroupDto group, List<CostSectionTemplateDto> sections)
    {
        string color = GroupColors.TryGetValue(group.Name.ToUpper(), out string? c) ? c : "#6B7280";

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = $"  {group.Name}",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"  ({sections.Count} sezioni)",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Edit button
        var btnEdit = new Button
        {
            Content = "✏", Width = 22, Height = 22, FontSize = 10,
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Rinomina gruppo", Tag = group
        };
        btnEdit.Click += BtnEditGroup_Click;
        panel.Children.Add(btnEdit);

        // Add section button
        var btnAddSec = new Button
        {
            Content = "+", Width = 22, Height = 22, FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0),
            FontWeight = FontWeights.Bold,
            ToolTip = "Aggiungi sezione in questo gruppo", Tag = group
        };
        btnAddSec.Click += BtnAddSectionInGroup_Click;
        panel.Children.Add(btnAddSec);

        // Delete button
        var btnDel = new Button
        {
            Content = "✕", Width = 22, Height = 22, FontSize = 10,
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Elimina gruppo", Tag = group
        };
        btnDel.Click += BtnDeleteGroup_Click;
        panel.Children.Add(btnDel);

        var border = new Border
        {
            Background = Brush(color),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 4, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = panel
        };

        bool shouldExpand = _lastExpandedTreeGroup != null
            ? group.Name == _lastExpandedTreeGroup
            : group.SortOrder == _groups.Min(g => g.SortOrder); // first group by default

        if (shouldExpand)
            _lastExpandedTreeGroup = group.Name;

        var item = new TreeViewItem
        {
            Header = border,
            IsExpanded = shouldExpand,
            Tag = group
        };
        item.Expanded += TreeGroupNode_Expanded;

        foreach (var section in sections)
            item.Items.Add(BuildSectionNode(section));

        return item;
    }

    private void TreeGroupNode_Expanded(object sender, RoutedEventArgs e)
    {
        if (_isRenderingTree) return;
        if (sender is not TreeViewItem opened) return;
        if (opened.Tag is CostSectionGroupDto group)
            _lastExpandedTreeGroup = group.Name;

        foreach (var child in tvSections.Items)
        {
            if (child is TreeViewItem tvi && tvi != opened)
                tvi.IsExpanded = false;
        }
    }

    private TreeViewItem BuildSectionNode(CostSectionTemplateDto section)
    {
        string typeColor = section.SectionType == "DA_CLIENTE" ? "#D97706" : "#059669";
        string typeLabel = section.SectionType == "DA_CLIENTE" ? "DA CLIENTE" : "IN SEDE";

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // Section name
        panel.Children.Add(new TextBlock
        {
            Text = section.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1A1D26"),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Type badge
        panel.Children.Add(new Border
        {
            Background = BrushWithAlpha(typeColor, 0x30),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = typeLabel,
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(typeColor)
            }
        });

        // Delete section button
        var btnDel = new Button
        {
            Content = "✕", Width = 20, Height = 20, FontSize = 9,
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)), Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Elimina sezione", Tag = section
        };
        btnDel.Click += BtnDeleteSection_Click;
        panel.Children.Add(btnDel);

        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 1, 0, 1),
            Child = panel
        };

        var item = new TreeViewItem
        {
            Header = border,
            IsExpanded = true,
            Tag = section,
            AllowDrop = true
        };
        item.DragOver += SectionNode_DragOver;
        item.Drop += SectionNode_Drop;

        // Sub-tree: Reparti Interessati (only if section has departments)
        if (section.DepartmentIds.Count > 0)
        {
            var deptGroupNode = BuildDeptGroupNode(section);
            item.Items.Add(deptGroupNode);
        }

        // Sub-tree: Fasi Template (only if section has linked phases)
        var linkedPhases = _phases
            .Where(p => p.CostSectionTemplateId == section.Id)
            .OrderBy(p => p.Category).ThenBy(p => p.SortOrder)
            .ToList();

        if (linkedPhases.Count > 0)
        {
            var phaseGroupNode = BuildPhaseGroupNode(section, linkedPhases);
            item.Items.Add(phaseGroupNode);
        }

        return item;
    }

    private TreeViewItem BuildPhaseGroupNode(CostSectionTemplateDto section, List<PhaseTemplateDto> phases)
    {
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "📋 Fasi Template",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#D97706"),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = $"  ({phases.Count})",
            FontSize = 10,
            Foreground = Brush("#9CA3AF"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var headerBorder = new Border
        {
            Background = Brush("#FFF8F0"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 2, 0, 1),
            Child = headerPanel
        };

        var groupItem = new TreeViewItem
        {
            Header = headerBorder,
            IsExpanded = true,
            AllowDrop = true,
            Tag = section
        };
        // Accept phase drops on the group header too
        groupItem.DragOver += PhaseGroupNode_DragOver;
        groupItem.Drop += PhaseGroupNode_Drop;

        foreach (var phase in phases)
            groupItem.Items.Add(BuildPhaseNode(phase, section));

        return groupItem;
    }

    private TreeViewItem BuildDeptGroupNode(CostSectionTemplateDto section)
    {
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(new TextBlock
        {
            Text = "👥 Reparti Interessati",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#4F6EF7"),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = $"  ({section.DepartmentIds.Count})",
            FontSize = 10,
            Foreground = Brush("#9CA3AF"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var headerBorder = new Border
        {
            Background = Brush("#F0F4FF"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 2, 0, 1),
            Child = headerPanel
        };

        var groupItem = new TreeViewItem
        {
            Header = headerBorder,
            IsExpanded = true,
            AllowDrop = true
        };
        // Accept department drops on the group header too
        groupItem.DragOver += DeptGroupNode_DragOver;
        groupItem.Drop += DeptGroupNode_Drop;
        groupItem.Tag = section; // so we know which section to add to

        foreach (int deptId in section.DepartmentIds)
        {
            var dept = _departments.FirstOrDefault(d => d.Id == deptId);
            if (dept == null) continue;
            groupItem.Items.Add(BuildDeptLeafNode(dept, section));
        }

        return groupItem;
    }

    private TreeViewItem BuildDeptLeafNode(DepartmentDto dept, CostSectionTemplateDto section)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = $"🔧 {dept.Code}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#4F6EF7"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"{dept.Name} — K:{dept.DefaultMarkup:F2} — €{dept.HourlyCost:F2}/h"
        });

        var btnDel = new Button
        {
            Content = "✕", Width = 18, Height = 18, FontSize = 8,
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
            Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"Rimuovi {dept.Code} da {section.Name}",
            Tag = new DeptSectionLink { Department = dept, Section = section }
        };
        btnDel.Click += BtnRemoveDept_Click;
        panel.Children.Add(btnDel);

        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 1, 0, 1),
            Child = panel
        };

        return new TreeViewItem
        {
            Header = border,
            Tag = new DeptSectionLink { Department = dept, Section = section }
        };
    }

    // Helper class for department-section link
    private class DeptSectionLink
    {
        public DepartmentDto Department { get; set; } = null!;
        public CostSectionTemplateDto Section { get; set; } = null!;
    }

    private class PhaseMoveInfo
    {
        public PhaseTemplateDto Phase { get; set; } = null!;
        public CostSectionTemplateDto Section { get; set; } = null!;
        public int Direction { get; set; } // -1 = up, 1 = down
    }

    private TreeViewItem BuildPhaseNode(PhaseTemplateDto phase, CostSectionTemplateDto parentSection)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        panel.Children.Add(new TextBlock
        {
            Text = "📋 ",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = phase.Name,
            FontSize = 12,
            Foreground = Brush("#374151"),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Category badge
        panel.Children.Add(new Border
        {
            Background = Brush("#F3F4F6"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = phase.Category,
                FontSize = 9,
                Foreground = Brush("#6B7280")
            }
        });

        if (!string.IsNullOrEmpty(phase.DepartmentCode))
        {
            panel.Children.Add(new Border
            {
                Background = Brush("#E0E7FF"),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = phase.DepartmentCode,
                    FontSize = 9,
                    Foreground = Brush("#4F6EF7")
                }
            });
        }

        // Unlink button
        var btnUnlink = new Button
        {
            Content = "✕", Width = 18, Height = 18, FontSize = 8,
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)), Foreground = Brush("#EF4444"),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Scollega fase", Tag = phase
        };
        btnUnlink.Click += BtnUnlinkPhase_Click;
        panel.Children.Add(btnUnlink);

        var border = new Border
        {
            Background = Brush("#FAFAFA"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand,
            Child = panel,
            Tag = new PhaseMoveInfo { Phase = phase, Section = parentSection, Direction = 0 }
        };
        border.PreviewMouseLeftButtonDown += PhaseTreeNode_PreviewMouseLeftButtonDown;
        border.PreviewMouseMove += PhaseTreeNode_PreviewMouseMove;

        var phaseItem = new TreeViewItem
        {
            Header = border,
            Tag = phase,
            AllowDrop = true
        };
        phaseItem.DragOver += PhaseNode_DragOver;
        phaseItem.Drop += PhaseNode_Drop;
        return phaseItem;
    }

    // ══════════════════════════════════════════════════════════════
    // DRAG & DROP ON TREEVIEW
    // ══════════════════════════════════════════════════════════════

    private void TvSections_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void TvSections_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    private void SectionNode_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CostSectionTemplateDto) return;

        // Sections accept phase drops and department drops
        if (e.Data.GetDataPresent("PhaseDrop") || e.Data.GetDataPresent("DepartmentDrop"))
        {
            e.Effects = DragDropEffects.Copy;
            ClearHighlight();
            if (tvi.Header is Border b)
            {
                b.Background = Brush("#E0F2FE");
                _lastHighlighted = tvi;
            }
        }
    }

    private void SectionNode_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearHighlight();

        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CostSectionTemplateDto section) return;

        if (e.Data.GetDataPresent("PhaseDrop"))
        {
            if (e.Data.GetData("PhaseDrop") is PhaseTemplateDto phase)
                _ = LinkPhaseToSection(phase, section);
        }
        else if (e.Data.GetDataPresent("DepartmentDrop"))
        {
            if (e.Data.GetData("DepartmentDrop") is DepartmentDto dept)
                _ = AddDepartmentToSection(dept, section);
        }
    }

    private void PhaseGroupNode_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CostSectionTemplateDto) return;

        if (e.Data.GetDataPresent("PhaseDrop"))
        {
            e.Effects = DragDropEffects.Copy;
            ClearHighlight();
            if (tvi.Header is Border b)
            {
                b.Background = Brush("#FDE68A");
                _lastHighlighted = tvi;
            }
        }
    }

    private void PhaseGroupNode_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearHighlight();

        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CostSectionTemplateDto section) return;

        if (e.Data.GetDataPresent("PhaseDrop"))
        {
            if (e.Data.GetData("PhaseDrop") is PhaseTemplateDto phase)
                _ = LinkPhaseToSection(phase, section);
        }
    }

    private void DeptGroupNode_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CostSectionTemplateDto) return;

        if (e.Data.GetDataPresent("DepartmentDrop"))
        {
            e.Effects = DragDropEffects.Copy;
            ClearHighlight();
            if (tvi.Header is Border b)
            {
                b.Background = Brush("#D0D9FF");
                _lastHighlighted = tvi;
            }
        }
    }

    private void DeptGroupNode_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearHighlight();

        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CostSectionTemplateDto section) return;

        if (e.Data.GetDataPresent("DepartmentDrop"))
        {
            if (e.Data.GetData("DepartmentDrop") is DepartmentDto dept)
                _ = AddDepartmentToSection(dept, section);
        }
    }

    private void PhaseNode_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not PhaseTemplateDto) return;

        // Phase nodes accept department drops and reorder drops
        if (e.Data.GetDataPresent("DepartmentDrop") || e.Data.GetDataPresent("PhaseReorder"))
        {
            e.Effects = e.Data.GetDataPresent("PhaseReorder") ? DragDropEffects.Move : DragDropEffects.Copy;
            ClearHighlight();
            if (tvi.Header is Border b)
            {
                b.Background = Brush("#E0E7FF");
                _lastHighlighted = tvi;
            }
        }
    }

    private void PhaseNode_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearHighlight();

        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not PhaseTemplateDto targetPhase) return;

        if (e.Data.GetDataPresent("PhaseReorder"))
        {
            if (e.Data.GetData("PhaseReorder") is PhaseMoveInfo info)
                _ = ReorderPhase(info.Phase, targetPhase, info.Section);
        }
        else if (e.Data.GetDataPresent("DepartmentDrop"))
        {
            if (e.Data.GetData("DepartmentDrop") is DepartmentDto dept)
                _ = SetPhaseDepartment(targetPhase, dept);
        }
    }

    private void PhaseTreeNode_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't start drag if clicking a button
        if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase) return;

        if (e.ClickCount == 2)
        {
            if (sender is Border b && b.Tag is PhaseMoveInfo info)
            {
                e.Handled = true;
                _ = OpenPhaseTemplateDialog(info.Phase);
            }
            return;
        }
        _dragStartPoint = e.GetPosition(null);
    }

    private void PhaseTreeNode_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        Point pos = e.GetPosition(null);
        Vector diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is Border b && b.Tag is PhaseMoveInfo info)
            {
                ShowDragAdorner(info.Phase.Name, "↕", Color.FromRgb(0x37, 0x41, 0x51));
                var data = new DataObject("PhaseReorder", info);
                DragDrop.DoDragDrop(b, data, DragDropEffects.Move);
                HideDragAdorner();
            }
        }
    }

    private void ClearHighlight()
    {
        if (_lastHighlighted?.Header is Border b)
        {
            b.Background = Brushes.White;
            _lastHighlighted = null;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // API: LINK PHASE TO SECTION
    // ══════════════════════════════════════════════════════════════

    private async Task LinkPhaseToSection(PhaseTemplateDto phase, CostSectionTemplateDto section)
    {
        try
        {
            string json = JsonSerializer.Serialize(new { field = "cost_section_template_id", value = section.Id.ToString() });
            string result = await ApiClient.PatchAsync($"/api/phases/templates/{phase.Id}/field", json);
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                phase.CostSectionTemplateId = section.Id;
                phase.CostSectionName = section.Name;
                RenderPhases(txtSearchPhase.Text.Trim());
                RenderTree();
                UpdateCounts();
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async Task ReorderPhase(PhaseTemplateDto movedPhase, PhaseTemplateDto targetPhase, CostSectionTemplateDto section)
    {
        if (movedPhase.Id == targetPhase.Id) return;

        // Get all phases in this section, ordered
        var sectionPhases = _phases
            .Where(p => p.CostSectionTemplateId == section.Id)
            .OrderBy(p => p.SortOrder)
            .ToList();

        // Remove moved phase from list, insert before target
        sectionPhases.Remove(movedPhase);
        int targetIndex = sectionPhases.IndexOf(targetPhase);
        if (targetIndex < 0) targetIndex = 0;
        sectionPhases.Insert(targetIndex, movedPhase);

        // Update sort_order for all phases in section
        try
        {
            for (int i = 0; i < sectionPhases.Count; i++)
            {
                int newSort = i + 1;
                if (sectionPhases[i].SortOrder != newSort)
                {
                    string json = JsonSerializer.Serialize(new { field = "sort_order", value = newSort.ToString() });
                    await ApiClient.PatchAsync($"/api/phases/templates/{sectionPhases[i].Id}/field", json);
                    sectionPhases[i].SortOrder = newSort;
                }
            }
            RenderTree();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async Task UnlinkPhase(PhaseTemplateDto phase)
    {
        try
        {
            string json = JsonSerializer.Serialize(new { field = "cost_section_template_id", value = (string?)null });
            string result = await ApiClient.PatchAsync($"/api/phases/templates/{phase.Id}/field", json);
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                phase.CostSectionTemplateId = null;
                phase.CostSectionName = "";
                RenderPhases(txtSearchPhase.Text.Trim());
                RenderTree();
                UpdateCounts();
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ══════════════════════════════════════════════════════════════
    // API: ADD/REMOVE DEPARTMENT ON SECTION
    // ══════════════════════════════════════════════════════════════

    private async Task AddDepartmentToSection(DepartmentDto dept, CostSectionTemplateDto section)
    {
        if (section.DepartmentIds.Contains(dept.Id))
        {
            txtStatus.Text = $"Reparto {dept.Code} già presente in {section.Name}";
            return;
        }

        var newIds = new List<int>(section.DepartmentIds) { dept.Id };
        try
        {
            string json = JsonSerializer.Serialize(new { departmentIds = newIds },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"/api/cost-sections/templates/{section.Id}/departments", json);

            section.DepartmentIds = newIds;
            RenderTree();
            txtStatus.Text = $"Reparto {dept.Code} aggiunto a {section.Name}";
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async Task RemoveDepartmentFromSection(DepartmentDto dept, CostSectionTemplateDto section)
    {
        var newIds = section.DepartmentIds.Where(id => id != dept.Id).ToList();
        try
        {
            string json = JsonSerializer.Serialize(new { departmentIds = newIds },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"/api/cost-sections/templates/{section.Id}/departments", json);

            section.DepartmentIds = newIds;
            RenderTree();
            txtStatus.Text = $"Reparto {dept.Code} rimosso da {section.Name}";
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnRemoveDept_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DeptSectionLink link)
            await RemoveDepartmentFromSection(link.Department, link.Section);
    }

    // ══════════════════════════════════════════════════════════════
    // API: SET DEPARTMENT ON PHASE TEMPLATE
    // ══════════════════════════════════════════════════════════════

    private async Task SetPhaseDepartment(PhaseTemplateDto phase, DepartmentDto dept)
    {
        if (phase.DepartmentId == dept.Id)
        {
            txtStatus.Text = $"Reparto {dept.Code} già assegnato a {phase.Name}";
            return;
        }

        try
        {
            string json = JsonSerializer.Serialize(new { field = "department_id", value = dept.Id.ToString() });
            string result = await ApiClient.PatchAsync($"/api/phases/templates/{phase.Id}/field", json);
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                phase.DepartmentId = dept.Id;
                phase.DepartmentCode = dept.Code;
                phase.DepartmentName = dept.Name;
                RenderPhases(txtSearchPhase.Text.Trim());
                RenderTree();
                txtStatus.Text = $"Reparto {dept.Code} assegnato a {phase.Name}";
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ══════════════════════════════════════════════════════════════
    // BUTTON HANDLERS
    // ══════════════════════════════════════════════════════════════

    private async void BtnUnlinkPhase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PhaseTemplateDto phase)
            await UnlinkPhase(phase);
    }

    private async void BtnDeleteSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CostSectionTemplateDto section) return;

        if (MessageBox.Show($"Eliminare la sezione \"{section.Name}\"?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/cost-sections/templates/{section.Id}");
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadData();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnEditGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CostSectionGroupDto group) return;

        string? newName = PromptInput("Rinomina Gruppo", "Nome:", group.Name);
        if (newName == null || newName == group.Name) return;

        try
        {
            string json = JsonSerializer.Serialize(new { field = "name", value = newName });
            await ApiClient.PatchAsync($"/api/cost-sections/groups/{group.Id}/field", json);
            await LoadData();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnAddSectionInGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CostSectionGroupDto group) return;

        string? name = PromptInput($"Nuova Sezione in {group.Name}", "Nome sezione:", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        // Ask for type
        var result = MessageBox.Show(
            "Tipo sezione:\n\nSì = DA CLIENTE\nNo = IN SEDE",
            "Tipo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        string sectionType = result == MessageBoxResult.Yes ? "DA_CLIENTE" : "IN_SEDE";

        int maxSort = _templates.Where(t => t.GroupId == group.Id).Any()
            ? _templates.Where(t => t.GroupId == group.Id).Max(t => t.SortOrder) + 1 : 1;

        try
        {
            string json = JsonSerializer.Serialize(new
            {
                name,
                sectionType,
                groupId = group.Id,
                isDefault = false,
                sortOrder = maxSort,
                isActive = true,
                departmentIds = new List<int>()
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string res = await ApiClient.PostAsync("/api/cost-sections/templates", json);
            var doc = JsonDocument.Parse(res);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _lastExpandedTreeGroup = group.Name;
                await LoadData();
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnDeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CostSectionGroupDto group) return;

        if (MessageBox.Show($"Eliminare il gruppo \"{group.Name}\"?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/cost-sections/groups/{group.Id}");
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadData();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnAddGroup_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptInput("Nuovo Gruppo", "Nome gruppo:", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        int maxSort = _groups.Any() ? _groups.Max(g => g.SortOrder) + 1 : 1;
        try
        {
            string json = JsonSerializer.Serialize(new { name, sortOrder = maxSort, isActive = true },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PostAsync("/api/cost-sections/groups", json);
            await LoadData();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnAddSection_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CostSectionTemplateDialog(_groups, _departments) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await LoadData();
    }

    // ══════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════

    private string? PromptInput(string title, string label, string defaultValue)
    {
        Window dlg = new()
        {
            Title = title, Width = 360, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = Brush("#F7F8FA")
        };

        StackPanel sp = new() { Margin = new Thickness(20, 16, 20, 16) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("#6B7280") });
        TextBox txt = new() { Text = defaultValue, Height = 32, Padding = new Thickness(8, 5, 8, 5), FontSize = 13, BorderBrush = Brush("#E4E7EC"), Margin = new Thickness(0, 6, 0, 12) };
        sp.Children.Add(txt);

        StackPanel btns = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button btnOk = new() { Content = "OK", Width = 80, Height = 30, Background = Brush("#4F6EF7"), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        Button btnCancel = new() { Content = "Annulla", Width = 80, Height = 30, Background = Brush("#F3F4F6"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
        btnOk.Click += (s, ev) => { dlg.DialogResult = true; dlg.Close(); };
        btnCancel.Click += (s, ev) => { dlg.DialogResult = false; dlg.Close(); };
        btns.Children.Add(btnCancel);
        btns.Children.Add(btnOk);
        sp.Children.Add(btns);

        dlg.Content = sp;
        txt.Focus();
        txt.SelectAll();

        return dlg.ShowDialog() == true ? txt.Text.Trim() : null;
    }
}
