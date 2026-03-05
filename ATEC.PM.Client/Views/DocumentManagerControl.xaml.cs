using System.Windows.Media;
using Microsoft.Win32;

namespace ATEC.PM.Client.UserControls;

public partial class DocumentManagerControl : UserControl
{
    private int _projectId;
    private string _serverPath = "";
    private string _currentSubPath = "";
    private List<FileItem> _currentItems = new();
    public event Action? FilesChanged;

    private static SolidColorBrush B(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    public DocumentManagerControl()
    {
        InitializeComponent();
    }

    public async void Load(int projectId, string initialSubPath = "")
    {
        _projectId = projectId;
        await LoadServerPath();
        await Navigate(initialSubPath);
    }

    private async Task LoadServerPath()
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                _serverPath = doc.RootElement.GetProperty("data").GetProperty("serverPath").GetString() ?? "";
        }
        catch { }
    }

    // ═══ NAVIGATION ═══

    private async Task Navigate(string subPath)
    {
        _currentSubPath = subPath;
        UpdateBreadcrumb();
        await LoadFiles();
    }

    private async Task LoadFiles()
    {
        try
        {
            string encoded = Uri.EscapeDataString(_currentSubPath);
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/files?subPath={encoded}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _currentItems = JsonSerializer.Deserialize<List<FileItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            var display = _currentItems.Select(i => new
            {
                TypeIcon = i.IsFolder ? "📁" : GetFileIcon(i.Name),
                i.Name,
                SizeDisplay = i.IsFolder ? "" : FormatSize(i.Size),
                ModifiedDisplay = i.Modified?.ToString("dd/MM/yyyy HH:mm") ?? ""
            }).ToList();

            dgFiles.ItemsSource = display;

            int folders = _currentItems.Count(i => i.IsFolder);
            int files = _currentItems.Count(i => !i.IsFolder);
            txtStatus.Text = $"{folders} cartelle, {files} file";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void UpdateBreadcrumb()
    {
        pnlBreadcrumb.Children.Clear();

        // Root
        Button btnRoot = new()
        {
            Content = "📁 Root",
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = B("#4F6EF7"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnRoot.Click += async (s, e) => await Navigate("");
        pnlBreadcrumb.Children.Add(btnRoot);

        if (!string.IsNullOrEmpty(_currentSubPath))
        {
            string[] parts = _currentSubPath.Replace("\\", "/").Split('/', StringSplitOptions.RemoveEmptyEntries);
            string accumulated = "";
            foreach (string part in parts)
            {
                pnlBreadcrumb.Children.Add(new TextBlock
                {
                    Text = " / ",
                    FontSize = 12,
                    Foreground = B("#9CA3AF"),
                    VerticalAlignment = VerticalAlignment.Center
                });

                accumulated = string.IsNullOrEmpty(accumulated) ? part : $"{accumulated}/{part}";
                string navPath = accumulated;

                Button btn = new()
                {
                    Content = part,
                    Height = 26,
                    Padding = new Thickness(4, 0, 4, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = B("#4F6EF7"),
                    FontSize = 12,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                btn.Click += async (s, e) => await Navigate(navPath);
                pnlBreadcrumb.Children.Add(btn);
            }
        }
    }

    // ═══ DOUBLE CLICK (apri cartella o file) ═══

    private async void DgFiles_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgFiles.SelectedIndex < 0 || dgFiles.SelectedIndex >= _currentItems.Count) return;
        FileItem item = _currentItems[dgFiles.SelectedIndex];

        if (item.IsFolder)
            await Navigate(item.RelativePath);
        else
            OpenFile(item.RelativePath);
    }

    private void OpenFile(string relativePath)
    {
        string fullPath = Path.Combine(_serverPath, relativePath);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"Impossibile aprire: {ex.Message}"); }
    }

    // ═══ UPLOAD ═══

    private async void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dlg = new()
        {
            Title = "Seleziona file da caricare",
            Multiselect = true,
            Filter = "Tutti i file (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        txtStatus.Text = "Caricamento in corso...";
        try
        {
            string encoded = Uri.EscapeDataString(_currentSubPath);
            if (dlg.FileNames.Length == 1)
            {
                await ApiClient.UploadFileAsync($"/api/projects/{_projectId}/upload?subPath={encoded}", dlg.FileName);
            }
            else
            {
                await ApiClient.UploadFilesAsync($"/api/projects/{_projectId}/upload-multiple?subPath={encoded}", dlg.FileNames);
            }
            await LoadFiles();
            FilesChanged?.Invoke();
            txtStatus.Text = $"{dlg.FileNames.Length} file caricati.";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore upload: {ex.Message}"; }
    }

    // ═══ DRAG & DROP ═══

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            brdDropZone.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        brdDropZone.Visibility = Visibility.Collapsed;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null || files.Length == 0) return;

        txtStatus.Text = "Caricamento in corso...";
        try
        {
            string encoded = Uri.EscapeDataString(_currentSubPath);
            // Filtra solo file (non cartelle)
            var filePaths = files.Where(f => System.IO.File.Exists(f)).ToArray();
            if (filePaths.Length == 1)
                await ApiClient.UploadFileAsync($"/api/projects/{_projectId}/upload?subPath={encoded}", filePaths[0]);
            else if (filePaths.Length > 1)
                await ApiClient.UploadFilesAsync($"/api/projects/{_projectId}/upload-multiple?subPath={encoded}", filePaths);

            await LoadFiles();
            FilesChanged?.Invoke();
            txtStatus.Text = $"{filePaths.Length} file caricati.";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    // ═══ NUOVA CARTELLA ═══

    private async void BtnNewFolder_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptInput("Nuova Cartella", "Nome della cartella:");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            string jsonBody = JsonSerializer.Serialize(new { subPath = _currentSubPath, folderName = name },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync($"/api/projects/{_projectId}/create-subfolder", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            { 
                await LoadFiles();
                FilesChanged?.Invoke();
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══ APRI IN EXPLORER ═══

    private void BtnOpenExplorer_Click(object sender, RoutedEventArgs e)
    {
        string fullPath = string.IsNullOrEmpty(_currentSubPath)
            ? _serverPath
            : Path.Combine(_serverPath, _currentSubPath);

        if (Directory.Exists(fullPath))
            System.Diagnostics.Process.Start("explorer.exe", fullPath);
        else
            MessageBox.Show("Cartella non trovata.");
    }

    // ═══ CONTEXT MENU ═══

    private async void CtxDownload_Click(object sender, RoutedEventArgs e)
    {
        if (dgFiles.SelectedIndex < 0 || dgFiles.SelectedIndex >= _currentItems.Count) return;
        FileItem item = _currentItems[dgFiles.SelectedIndex];
        if (item.IsFolder) return;

        SaveFileDialog dlg = new()
        {
            FileName = item.Name,
            Title = "Salva file come..."
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string encoded = Uri.EscapeDataString(item.RelativePath);
            byte[]? bytes = await ApiClient.DownloadAsync($"/api/projects/{_projectId}/download?path={encoded}");
            if (bytes != null)
            {
                System.IO.File.WriteAllBytes(dlg.FileName, bytes);
                txtStatus.Text = $"Scaricato: {item.Name}";
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void CtxRename_Click(object sender, RoutedEventArgs e)
    {
        if (dgFiles.SelectedIndex < 0 || dgFiles.SelectedIndex >= _currentItems.Count) return;
        FileItem item = _currentItems[dgFiles.SelectedIndex];

        string? newName = PromptInput("Rinomina", "Nuovo nome:", item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        try
        {
            string jsonBody = JsonSerializer.Serialize(new { oldPath = item.RelativePath, newName },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync($"/api/projects/{_projectId}/rename", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await LoadFiles();
                FilesChanged?.Invoke();
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void CtxDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgFiles.SelectedIndex < 0 || dgFiles.SelectedIndex >= _currentItems.Count) return;
        FileItem item = _currentItems[dgFiles.SelectedIndex];

        string tipo = item.IsFolder ? "la cartella" : "il file";
        if (MessageBox.Show($"Eliminare {tipo} \"{item.Name}\"?{(item.IsFolder ? "\nTutto il contenuto verrà eliminato." : "")}",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            string jsonBody = JsonSerializer.Serialize(new { itemPath = item.RelativePath },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync($"/api/projects/{_projectId}/delete-item", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            { 
            await LoadFiles();
            FilesChanged?.Invoke();
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void CtxMove_Click(object sender, RoutedEventArgs e)
    {
        if (dgFiles.SelectedIndex < 0 || dgFiles.SelectedIndex >= _currentItems.Count) return;
        FileItem item = _currentItems[dgFiles.SelectedIndex];

        string? destFolder = PromptInput("Sposta", "Percorso cartella destinazione (relativo alla root commessa):", _currentSubPath);
        if (destFolder == null) return;

        try
        {
            string jsonBody = JsonSerializer.Serialize(new { sourcePath = item.RelativePath, destinationFolder = destFolder },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync($"/api/projects/{_projectId}/move-item", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await LoadFiles();
                FilesChanged?.Invoke();
            }
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ═══ HELPERS ═══

    private static string? PromptInput(string title, string label, string defaultValue = "")
    {
        Window prompt = new()
        {
            Title = title,
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };

        StackPanel sp = new() { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) });
        TextBox txt = new() { Text = defaultValue, FontSize = 13, Height = 30, Padding = new Thickness(6, 4, 6, 4) };
        sp.Children.Add(txt);

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Button btnOk = new() { Content = "OK", Width = 80, Height = 30, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4F6EF7")), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold };
        Button btnCancel = new() { Content = "Annulla", Width = 80, Height = 30, Margin = new Thickness(8, 0, 0, 0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6")), BorderThickness = new Thickness(0) };

        string? result = null;
        btnOk.Click += (s, e) => { result = txt.Text; prompt.Close(); };
        btnCancel.Click += (s, e) => prompt.Close();

        buttons.Children.Add(btnOk);
        buttons.Children.Add(btnCancel);
        sp.Children.Add(buttons);
        prompt.Content = sp;

        txt.Focus();
        txt.SelectAll();
        prompt.ShowDialog();
        return result;
    }

    private static string GetFileIcon(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLower();
        return ext switch
        {
            ".pdf" => "📕",
            ".doc" or ".docx" => "📘",
            ".xls" or ".xlsx" => "📗",
            ".dwg" or ".dxf" => "📐",
            ".jpg" or ".jpeg" or ".png" or ".bmp" => "🖼",
            ".zip" or ".rar" or ".7z" => "📦",
            ".txt" => "📝",
            ".csv" => "📊",
            _ => "📄"
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N0} KB";
        return $"{bytes / 1024.0 / 1024.0:N1} MB";
    }
}
