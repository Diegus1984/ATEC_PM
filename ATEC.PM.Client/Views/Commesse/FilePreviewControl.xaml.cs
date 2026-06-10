using System.IO;
using System.Windows.Forms.Integration;
using ATEC.PM.Client.Controls;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class FilePreviewControl : UserControl
{
    public FilePreviewControl()
    {
        InitializeComponent();
    }

    public async Task ShowAsync(int projectId, string relativePath, double containerHeight)
    {
        string fileName = Path.GetFileName(relativePath);
        string ext = Path.GetExtension(fileName).ToLower();
        double viewHeight = Math.Max(500, containerHeight - 60);

        try
        {
            if (ext == ".pdf")
                await ShowPdf(projectId, relativePath, fileName, viewHeight);
            else if (ext is ".doc" or ".docx")
                await ShowWord(projectId, relativePath, fileName, ext);
            else if (ext is ".xlsx" or ".xls" or ".csv")
                await ShowExcel(projectId, relativePath);
            else if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
                await ShowImage(projectId, relativePath, fileName);
            else if (ext is ".dwg" or ".dxf")
                await ShowCad(projectId, relativePath, fileName, viewHeight);
            else if (ext is ".sldprt" or ".sldasm" or ".slddrw" or ".easm" or ".eprt" or ".edrw" or ".iges" or ".igs")
                await ShowEDrawings(projectId, relativePath, fileName, viewHeight);
            else if (ext is ".step" or ".stp" or ".stl" or ".obj")
                await Show3D(projectId, relativePath, fileName, viewHeight);
            else
                ShowGenericInfo(fileName, relativePath, ext);
        }
        catch (Exception ex)
        {
            PreviewContent.Content = new TextBlock { Text = $"Errore: {ex.Message}", Foreground = System.Windows.Media.Brushes.Red };
        }
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private static string GetFileIcon(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLower();
        return ext switch
        {
            ".pdf" => "\U0001F4D5",
            ".doc" or ".docx" => "\U0001F4D8",
            ".xls" or ".xlsx" => "\U0001F4D7",
            ".dwg" or ".dxf" => "\U0001F4D0",
            ".jpg" or ".jpeg" or ".png" or ".bmp" => "\U0001F5BC",
            ".zip" or ".rar" or ".7z" => "\U0001F4E6",
            ".txt" => "\U0001F4DD",
            ".csv" => "\U0001F4CA",
            _ => "\U0001F4C4"
        };
    }

    private static string EnsureTempDir(string subFolder = "")
    {
        string dir = string.IsNullOrEmpty(subFolder)
            ? Path.Combine(Path.GetTempPath(), "ATEC_PM")
            : Path.Combine(Path.GetTempPath(), "ATEC_PM", subFolder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanTempFiles(string dir)
    {
        try
        {
            foreach (string old in Directory.GetFiles(dir))
            {
                try { File.Delete(old); }
                catch { }
            }
        }
        catch { }
    }

    private async Task ShowPdf(int projectId, string relativePath, string fileName, double viewHeight)
    {
        string encoded = Uri.EscapeDataString(relativePath);
        byte[]? bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

        if (bytes == null || bytes.Length == 0)
        {
            PreviewContent.Content = new TextBlock { Text = "Impossibile scaricare il file.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
            return;
        }

        string tempDir = EnsureTempDir();
        string tempFile = Path.Combine(tempDir, fileName);
        File.WriteAllBytes(tempFile, bytes);

        DockPanel panel = new();
        Border infoBar = new()
        {
            Background = Brush("#F7F8FA"),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8)
        };
        infoBar.Child = new TextBlock
        {
            Text = $"\U0001F4D5  {fileName} (Anteprima)",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(infoBar, Dock.Top);
        panel.Children.Add(infoBar);

        var webView = new Microsoft.Web.WebView2.Wpf.WebView2 { Height = viewHeight };
        panel.Children.Add(webView);
        PreviewContent.Content = panel;

        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Navigate(new Uri(tempFile).AbsoluteUri);
    }

    private async Task ShowWord(int projectId, string relativePath, string fileName, string ext)
    {
        if (ext == ".doc")
        {
            StackPanel infoPanel = new();
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"\U0001F4D8  {fileName}",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 16)
            });
            infoPanel.Children.Add(new TextBlock
            {
                Text = "Il formato .doc (Word 97-2003) non supporta anteprima integrata.\nUsa il pulsante 'Scarica' per aprirlo con Word.",
                FontSize = 14,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            });
            PreviewContent.Content = infoPanel;
            return;
        }

        string encoded = Uri.EscapeDataString(relativePath);
        string? html = await ApiClient.GetRawAsync($"/api/projects/{projectId}/preview?path={encoded}");

        if (string.IsNullOrEmpty(html))
        {
            PreviewContent.Content = new TextBlock { Text = "HTML vuoto dal server", Foreground = System.Windows.Media.Brushes.Red };
            return;
        }

        string tempDir = EnsureTempDir();
        string tempHtml = Path.Combine(tempDir, $"preview_doc_{projectId}.html");
        File.WriteAllText(tempHtml, html, System.Text.Encoding.UTF8);

        var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
        PreviewContent.Content = webView;
        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
    }

    private async Task ShowExcel(int projectId, string relativePath)
    {
        string encoded = Uri.EscapeDataString(relativePath);
        string? html = await ApiClient.GetRawAsync($"/api/projects/{projectId}/preview?path={encoded}");

        if (string.IsNullOrEmpty(html))
        {
            PreviewContent.Content = new TextBlock { Text = "HTML vuoto dal server", Foreground = System.Windows.Media.Brushes.Red };
            return;
        }

        string tempDir = EnsureTempDir();
        string tempHtml = Path.Combine(tempDir, $"preview_{projectId}.html");
        File.WriteAllText(tempHtml, html, System.Text.Encoding.UTF8);

        var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
        PreviewContent.Content = webView;
        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
    }

    private async Task ShowImage(int projectId, string relativePath, string fileName)
    {
        string encoded = Uri.EscapeDataString(relativePath);
        byte[]? bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

        if (bytes == null || bytes.Length == 0)
        {
            PreviewContent.Content = new TextBlock { Text = "Impossibile scaricare l'immagine.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
            return;
        }

        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.EndInit();

        StackPanel imgPanel = new();
        imgPanel.Children.Add(new TextBlock { Text = $"\U0001F5BC  {fileName}", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        imgPanel.Children.Add(new System.Windows.Controls.Image { Source = bmp, MaxHeight = 500, Stretch = System.Windows.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
        PreviewContent.Content = imgPanel;
    }

    private async Task ShowCad(int projectId, string relativePath, string fileName, double viewHeight)
    {
        string encoded = Uri.EscapeDataString(relativePath);
        byte[]? bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

        if (bytes == null || bytes.Length == 0)
        {
            PreviewContent.Content = new TextBlock { Text = "Impossibile scaricare il file.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
            return;
        }

        string tempDir = EnsureTempDir("cad");
        CleanTempFiles(tempDir);
        string tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{fileName}");
        File.WriteAllBytes(tempFile, bytes);

        DockPanel panel = new();
        Border infoBar = new()
        {
            Background = Brush("#F7F8FA"),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8)
        };
        infoBar.Child = new TextBlock
        {
            Text = $"\U0001F4D0  {fileName} (Vista CAD \u2014 scroll per zoom, trascina per muovere)",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(infoBar, Dock.Top);
        panel.Children.Add(infoBar);

        CadViewerControl cadViewer = new() { Height = viewHeight };
        panel.Children.Add(cadViewer);
        PreviewContent.Content = panel;

        cadViewer.Loaded += (_, _) => cadViewer.LoadFile(tempFile);
    }

    private async Task ShowEDrawings(int projectId, string relativePath, string fileName, double viewHeight)
    {
        string encoded = Uri.EscapeDataString(relativePath);
        byte[]? bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

        if (bytes == null || bytes.Length == 0)
        {
            PreviewContent.Content = new TextBlock { Text = "Impossibile scaricare il file.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
            return;
        }

        string tempDir = EnsureTempDir("cad");
        CleanTempFiles(tempDir);
        string tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{fileName}");
        File.WriteAllBytes(tempFile, bytes);

        bool eDrawingsInstalled = false;
        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"EModelView.EModelNonVersionSpecificViewControl\CLSID");
            eDrawingsInstalled = key != null;
        }
        catch { }

        if (!eDrawingsInstalled)
        {
            StackPanel infoPanel = new() { Margin = new Thickness(20) };
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"\U0001F527  {fileName}",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 16)
            });
            infoPanel.Children.Add(new TextBlock
            {
                Text = "eDrawings Viewer non \u00E8 installato su questo PC.\n\n" +
                       "Per visualizzare file SolidWorks, DXF, DWG e IGES \u00E8 necessario installare\n" +
                       "eDrawings Viewer (gratuito) da: https://www.edrawingsviewer.com/download-edrawings\n\n" +
                       "Usa il pulsante 'Scarica' per aprire il file con un'applicazione esterna.",
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            });
            PreviewContent.Content = infoPanel;
            return;
        }

        DockPanel panel = new();
        Border infoBar = new()
        {
            Background = Brush("#F7F8FA"),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8)
        };
        infoBar.Child = new TextBlock
        {
            Text = $"\U0001F527  {fileName} (eDrawings \u2014 trascina per ruotare, scroll per zoom)",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(infoBar, Dock.Top);
        panel.Children.Add(infoBar);

        try
        {
            WindowsFormsHost host = new() { Height = viewHeight };
            EDrawingHost eDrawCtrl = new();
            string fileToOpen = tempFile;
            eDrawCtrl.ControlLoaded += (ctrl) =>
            {
                try
                {
                    dynamic eView = ctrl;
                    eView.OpenDoc(fileToOpen, false, false, false, "");
                }
                catch { }
            };
            host.Child = eDrawCtrl;
            panel.Children.Add(host);
            PreviewContent.Content = panel;
        }
        catch (Exception ex)
        {
            PreviewContent.Content = new TextBlock
            {
                Text = $"Errore caricamento eDrawings: {ex.Message}\n\nUsa 'Scarica' per aprire il file esternamente.",
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            };
        }
    }

    private async Task Show3D(int projectId, string relativePath, string fileName, double viewHeight)
    {
        string ext = Path.GetExtension(fileName).ToLower();
        string encoded = Uri.EscapeDataString(relativePath);
        byte[]? bytes = await ApiClient.DownloadAsync($"/api/projects/{projectId}/download?path={encoded}");

        if (bytes == null || bytes.Length == 0)
        {
            PreviewContent.Content = new TextBlock { Text = "Impossibile scaricare il file.", FontSize = 14, Foreground = System.Windows.Media.Brushes.Gray };
            return;
        }

        string tempDir = EnsureTempDir("3d");
        string tempModel = Path.Combine(tempDir, fileName);
        File.WriteAllBytes(tempModel, bytes);

        string viewerBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATEC_PM", "3DViewer");

        if (!Directory.Exists(viewerBase) || !File.Exists(Path.Combine(viewerBase, "three.module.js")))
        {
            PreviewContent.Content = new TextBlock
            {
                Text = "\u26A0 Librerie 3D non trovate.\n\nEseguire lo script Download_3DViewer.ps1 per scaricare le dipendenze.\nCartella attesa: " + viewerBase,
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            };
            return;
        }

        string isStep = (ext is ".step" or ".stp") ? "true" : "false";
        string modelFileName = fileName.Replace("'", "\\'");
        string modelsDir = Path.Combine(tempDir, "models");
        Directory.CreateDirectory(modelsDir);
        File.Copy(tempModel, Path.Combine(modelsDir, fileName), true);

        string viewerHtml = Build3DViewerHtml(isStep, fileName, modelFileName);
        string tempHtmlPath = Path.Combine(tempDir, $"viewer3d_{projectId}.html");
        File.WriteAllText(tempHtmlPath, viewerHtml, System.Text.Encoding.UTF8);

        DockPanel panel = new();
        Border infoBar = new()
        {
            Background = Brush("#F7F8FA"),
            BorderBrush = Brush("#E4E7EC"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8)
        };
        infoBar.Child = new TextBlock
        {
            Text = $"\U0001F527  {fileName} (Vista 3D \u2014 trascina per ruotare, scroll per zoom)",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(infoBar, Dock.Top);
        panel.Children.Add(infoBar);

        var webView = new Microsoft.Web.WebView2.Wpf.WebView2 { Height = viewHeight };
        panel.Children.Add(webView);
        PreviewContent.Content = panel;

        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "atec.libs", viewerBase,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "atec.models", Path.Combine(tempDir, "models"),
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Navigate(new Uri(tempHtmlPath).AbsoluteUri);
    }

    private void ShowGenericInfo(string fileName, string relativePath, string ext)
    {
        StackPanel infoPanel = new();
        infoPanel.Children.Add(new TextBlock { Text = $"{GetFileIcon(fileName)}  {fileName}", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });

        Grid infoGrid = new();
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddInfoRow(infoGrid, 0, "Percorso", relativePath);
        AddInfoRow(infoGrid, 1, "Tipo", ext.TrimStart('.').ToUpper());
        AddInfoRow(infoGrid, 2, "Azione", "File non visualizzabile in anteprima. Usa 'Scarica'.");

        infoPanel.Children.Add(infoGrid);
        PreviewContent.Content = infoPanel;
    }

    private static void AddInfoRow(Grid grid, int row, string label, string value)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock lbl = new() { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 4, 0, 4) };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        TextBlock val = new() { Text = value, FontSize = 13, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(val, row);
        Grid.SetColumn(val, 2);
        grid.Children.Add(val);
    }

    private static string Build3DViewerHtml(string isStep, string fileName, string modelFileName)
    {
        return $@"<!DOCTYPE html>
<html><head>
<meta charset='utf-8'/>
<style>
  body {{ margin:0; overflow:hidden; background:#1a1d26; }}
  canvas {{ display:block; }}
  #info {{ position:absolute; top:10px; left:12px; color:#999; font:13px sans-serif; z-index:10; }}
  #info.ok {{ color:#12B76A; }}
  #info.err {{ color:#F04438; }}
</style>
</head><body>
<div id='info'>🔄 Caricamento modello...</div>
<script type='importmap'>
{{
  ""imports"": {{
    ""three"": ""https://atec.libs/three.module.js"",
    ""three/addons/"": ""https://atec.libs/""
  }}
}}
</script>
<script type='module'>
import * as THREE from 'three';
import {{ OrbitControls }} from 'three/addons/OrbitControls.js';
import {{ STLLoader }} from 'three/addons/STLLoader.js';
import {{ OBJLoader }} from 'three/addons/OBJLoader.js';

const info = document.getElementById('info');
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x1a1d26);

const camera = new THREE.PerspectiveCamera(45, window.innerWidth/window.innerHeight, 0.1, 100000);
const renderer = new THREE.WebGLRenderer({{ antialias:true }});
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(window.devicePixelRatio);
renderer.toneMapping = THREE.NoToneMapping;
document.body.appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.08;

scene.add(new THREE.AmbientLight(0xffffff, 1.0));
const dir1 = new THREE.DirectionalLight(0xffffff, 1.5);
dir1.position.set(5, 10, 7); scene.add(dir1);
const dir2 = new THREE.DirectionalLight(0xffffff, 0.8);
dir2.position.set(-5, -3, -5); scene.add(dir2);
const dir3 = new THREE.DirectionalLight(0xffffff, 0.6);
dir3.position.set(0, -8, 3); scene.add(dir3);
const dir4 = new THREE.DirectionalLight(0xffffff, 0.4);
dir4.position.set(-3, 5, -8); scene.add(dir4);

const grid = new THREE.GridHelper(1000, 20, 0x2a2d36, 0x2a2d36);
scene.add(grid);
const material = new THREE.MeshStandardMaterial({{color: 0xB0B8C8, metalness: 0.3, roughness: 0.5}});

function fitCamera(object) {{
    const box = new THREE.Box3().setFromObject(object);
    const center = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3()).length();
    const dist = size * 1.5;
    camera.position.copy(center.clone().add(new THREE.Vector3(dist*0.6, dist*0.4, dist*0.6)));
    camera.near = size / 1000;
    camera.far = size * 100;
    camera.updateProjectionMatrix();
    controls.target.copy(center);
    controls.update();
    grid.scale.setScalar(size / 500);
    grid.position.y = box.min.y;
}}

const isStepFlag = {isStep};
const modelUrl = 'https://atec.models/{fileName}';

try {{
    if (isStepFlag) {{
        const resp = await fetch('https://atec.libs/occt-import-js.js');
        const scriptText = await resp.text();
        const blob = new Blob([scriptText], {{ type: 'application/javascript' }});
        const scriptUrl = URL.createObjectURL(blob);
        await new Promise((resolve, reject) => {{
            const s = document.createElement('script');
            s.src = scriptUrl; s.onload = resolve; s.onerror = reject;
            document.head.appendChild(s);
        }});
        const occt = await occtimportjs({{
            locateFile: () => 'https://atec.libs/occt-import-js.wasm'
        }});
        const response = await fetch(modelUrl);
        const buffer = new Uint8Array(await response.arrayBuffer());
        const result = occt.ReadStepFile(buffer, null);
        if (!result.meshes || result.meshes.length === 0) {{
            info.textContent = '\u2717 Nessuna geometria trovata nel file STEP';
            info.className = 'err';
        }} else {{
            const group = new THREE.Group();
            for (const meshData of result.meshes) {{
                const geom = new THREE.BufferGeometry();
                geom.setAttribute('position', new THREE.Float32BufferAttribute(meshData.attributes.position.array, 3));
                if (meshData.attributes.normal)
                    geom.setAttribute('normal', new THREE.Float32BufferAttribute(meshData.attributes.normal.array, 3));
                if (meshData.index)
                    geom.setIndex(new THREE.BufferAttribute(new Uint32Array(meshData.index.array), 1));
                geom.computeVertexNormals();
                const mat = material.clone();
                if (meshData.color)
                    mat.color.setRGB(meshData.color[0]/255, meshData.color[1]/255, meshData.color[2]/255);
                group.add(new THREE.Mesh(geom, mat));
            }}
            scene.add(group);
            fitCamera(group);
            info.textContent = '\u2713 {modelFileName} (' + result.meshes.length + ' mesh)';
            info.className = 'ok';
        }}
    }} else if (modelUrl.endsWith('.stl')) {{
        const loader = new STLLoader();
        const response = await fetch(modelUrl);
        const buffer = await response.arrayBuffer();
        const geometry = loader.parse(buffer);
        geometry.computeVertexNormals();
        const mesh = new THREE.Mesh(geometry, material);
        scene.add(mesh);
        fitCamera(mesh);
        info.textContent = '\u2713 {modelFileName}';
        info.className = 'ok';
    }} else {{
        const loader = new OBJLoader();
        const response = await fetch(modelUrl);
        const text = await response.text();
        const obj = loader.parse(text);
        obj.traverse(c => {{ if (c.isMesh) c.material = material; }});
        scene.add(obj);
        fitCamera(obj);
        info.textContent = '\u2713 {modelFileName}';
        info.className = 'ok';
    }}
}} catch(err) {{
    info.textContent = '\u2717 Errore: ' + err.message;
    info.className = 'err';
    console.error(err);
}}

function animate() {{
    requestAnimationFrame(animate);
    controls.update();
    renderer.render(scene, camera);
}}
animate();

window.addEventListener('resize', () => {{
    camera.aspect = window.innerWidth/window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
}});
</script></body></html>";
    }
}
