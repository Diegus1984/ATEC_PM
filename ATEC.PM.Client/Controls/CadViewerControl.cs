using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ACadSharp;
using ACadSharp.IO;

namespace ATEC.PM.Client.Controls;

/// <summary>
/// Viewer CAD nativo WPF per file DWG e DXF.
/// Usa ACadSharp per il parsing e WPF Canvas per il rendering.
/// </summary>
public class CadViewerControl : UserControl
{
    private readonly Canvas _canvas;
    private readonly ScaleTransform _scaleTransform;
    private readonly TranslateTransform _translateTransform;

    private System.Windows.Point _lastMousePos;
    private bool _isPanning;
    private double _zoom = 1.0;

    private double _minX, _minY, _maxX, _maxY;
    private bool _hasBounds;

    private readonly TextBlock _infoText;
    private string _fileName = "";
    private int _entityCount;

    private static readonly Dictionary<short, System.Windows.Media.Color> AciColors = new()
    {
        {0, Colors.White}, {1, Colors.Red}, {2, Colors.Yellow}, {3, Colors.Lime},
        {4, Colors.Cyan}, {5, Colors.Blue}, {6, Colors.Magenta}, {7, Colors.White},
        {8, Colors.Gray}, {9, Colors.Silver}
    };

    public CadViewerControl()
    {
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1D, 0x26));

        _scaleTransform = new ScaleTransform(1, 1);
        _translateTransform = new TranslateTransform(0, 0);
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(_scaleTransform);
        transformGroup.Children.Add(_translateTransform);

        _canvas = new Canvas
        {
            Background = Brushes.Transparent,
            RenderTransform = transformGroup,
            ClipToBounds = false
        };

        _infoText = new TextBlock
        {
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99)),
            FontSize = 13,
            Margin = new Thickness(12, 10, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var grid = new Grid { ClipToBounds = true };
        grid.Children.Add(_canvas);
        grid.Children.Add(_infoText);

        Content = grid;

        MouseWheel += OnMouseWheel;
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        MouseMove += OnMouseMove;
    }

    /// <summary>
    /// Carica e renderizza un file DWG o DXF.
    /// </summary>
    public void LoadFile(string filePath)
    {
        _fileName = System.IO.Path.GetFileName(filePath);
        _infoText.Text = $"🔄 Caricamento {_fileName}...";
        _canvas.Children.Clear();
        _hasBounds = false;
        _entityCount = 0;
        _minX = double.MaxValue; _minY = double.MaxValue;
        _maxX = double.MinValue; _maxY = double.MinValue;

        try
        {
            string ext = System.IO.Path.GetExtension(filePath).ToLower();
            CadDocument doc;

            if (ext == ".dwg")
                doc = DwgReader.Read(filePath);
            else
                doc = DxfReader.Read(filePath);

            var entities = doc.Entities;

            if (entities == null || !entities.Any())
            {
                _infoText.Text = $"✗ Nessuna entità trovata in {_fileName}";
                _infoText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0x44, 0x38));
                return;
            }

            foreach (var entity in entities)
            {
                RenderEntity(entity);
            }

            if (_hasBounds)
                FitToView();

            _infoText.Text = $"✓ {_fileName} ({_entityCount} entità)";
            _infoText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x12, 0xB7, 0x6A));
        }
        catch (Exception ex)
        {
            _infoText.Text = $"✗ Errore: {ex.Message}";
            _infoText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0x44, 0x38));
        }
    }

    // ══════════════════════════════════════════════════════════
    // RENDERING ENTITÀ
    // ══════════════════════════════════════════════════════════

    private void RenderEntity(ACadSharp.Entities.Entity entity)
    {
        try
        {
            switch (entity)
            {
                case ACadSharp.Entities.Arc arc:
                    RenderArc(arc);
                    break;
                case ACadSharp.Entities.Circle circle:
                    RenderCircle(circle);
                    break;
                case ACadSharp.Entities.Line line:
                    RenderLine(line);
                    break;
                case ACadSharp.Entities.LwPolyline lwPoly:
                    RenderLwPolyline(lwPoly);
                    break;
                case ACadSharp.Entities.Polyline2D poly2d:
                    RenderPolyline2D(poly2d);
                    break;
                case ACadSharp.Entities.Polyline3D poly3d:
                    RenderPolyline3D(poly3d);
                    break;
                case ACadSharp.Entities.Ellipse ellipse:
                    RenderEllipse(ellipse);
                    break;
                case ACadSharp.Entities.Spline spline:
                    RenderSpline(spline);
                    break;
                case ACadSharp.Entities.Point point:
                    RenderPoint(point);
                    break;
                case ACadSharp.Entities.Insert insert:
                    RenderInsert(insert);
                    break;
                case ACadSharp.Entities.MText mtext:
                    RenderMText(mtext);
                    break;
                case ACadSharp.Entities.TextEntity text:
                    RenderText(text);
                    break;
            }
        }
        catch { /* skip entity */ }
    }

    private void RenderLine(ACadSharp.Entities.Line line)
    {
        var wpfLine = new System.Windows.Shapes.Line
        {
            X1 = line.StartPoint.X,
            Y1 = -line.StartPoint.Y,
            X2 = line.EndPoint.X,
            Y2 = -line.EndPoint.Y,
            Stroke = GetBrush(line),
            StrokeThickness = 0.5
        };
        _canvas.Children.Add(wpfLine);
        UpdateBounds(line.StartPoint.X, line.StartPoint.Y);
        UpdateBounds(line.EndPoint.X, line.EndPoint.Y);
        _entityCount++;
    }

    private void RenderCircle(ACadSharp.Entities.Circle circle)
    {
        var wpfEllipse = new System.Windows.Shapes.Ellipse
        {
            Width = circle.Radius * 2,
            Height = circle.Radius * 2,
            Stroke = GetBrush(circle),
            StrokeThickness = 0.5
        };
        Canvas.SetLeft(wpfEllipse, circle.Center.X - circle.Radius);
        Canvas.SetTop(wpfEllipse, -circle.Center.Y - circle.Radius);
        _canvas.Children.Add(wpfEllipse);
        UpdateBounds(circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius);
        UpdateBounds(circle.Center.X + circle.Radius, circle.Center.Y + circle.Radius);
        _entityCount++;
    }

    private void RenderArc(ACadSharp.Entities.Arc arc)
    {
        double startAngle = arc.StartAngle * Math.PI / 180.0;
        double endAngle = arc.EndAngle * Math.PI / 180.0;
        if (endAngle <= startAngle) endAngle += Math.PI * 2;

        int segments = 64;
        var points = new PointCollection();
        for (int i = 0; i <= segments; i++)
        {
            double angle = startAngle + (endAngle - startAngle) * i / segments;
            double x = arc.Center.X + arc.Radius * Math.Cos(angle);
            double y = -(arc.Center.Y + arc.Radius * Math.Sin(angle));
            points.Add(new System.Windows.Point(x, y));
        }

        var polyline = new Polyline
        {
            Points = points,
            Stroke = GetBrush(arc),
            StrokeThickness = 0.5
        };
        _canvas.Children.Add(polyline);
        UpdateBounds(arc.Center.X - arc.Radius, arc.Center.Y - arc.Radius);
        UpdateBounds(arc.Center.X + arc.Radius, arc.Center.Y + arc.Radius);
        _entityCount++;
    }

    private void RenderLwPolyline(ACadSharp.Entities.LwPolyline lwPoly)
    {
        var points = new PointCollection();
        foreach (var vertex in lwPoly.Vertices)
        {
            points.Add(new System.Windows.Point(vertex.Location.X, -vertex.Location.Y));
            UpdateBounds(vertex.Location.X, vertex.Location.Y);
        }

        if (lwPoly.IsClosed && points.Count > 0)
            points.Add(points[0]);

        var polyline = new Polyline
        {
            Points = points,
            Stroke = GetBrush(lwPoly),
            StrokeThickness = 0.5
        };
        _canvas.Children.Add(polyline);
        _entityCount++;
    }

    private void RenderPolyline2D(ACadSharp.Entities.Polyline2D poly)
    {
        var points = new PointCollection();
        foreach (var vertex in poly.Vertices)
        {
            points.Add(new System.Windows.Point(vertex.Location.X, -vertex.Location.Y));
            UpdateBounds(vertex.Location.X, vertex.Location.Y);
        }

        if (poly.IsClosed && points.Count > 0)
            points.Add(points[0]);

        var polyline = new Polyline
        {
            Points = points,
            Stroke = GetBrush(poly),
            StrokeThickness = 0.5
        };
        _canvas.Children.Add(polyline);
        _entityCount++;
    }

    private void RenderPolyline3D(ACadSharp.Entities.Polyline3D poly)
    {
        var points = new PointCollection();
        foreach (var vertex in poly.Vertices)
        {
            points.Add(new System.Windows.Point(vertex.Location.X, -vertex.Location.Y));
            UpdateBounds(vertex.Location.X, vertex.Location.Y);
        }

        if (poly.IsClosed && points.Count > 0)
            points.Add(points[0]);

        var polyline = new Polyline
        {
            Points = points,
            Stroke = GetBrush(poly),
            StrokeThickness = 0.5
        };
        _canvas.Children.Add(polyline);
        _entityCount++;
    }

    private void RenderEllipse(ACadSharp.Entities.Ellipse ellipse)
    {
        double rx = ellipse.MajorAxis;
        double ry = rx * ellipse.RadiusRatio;
        double rotation = ellipse.Rotation * Math.PI / 180.0;

        int segments = 64;
        var points = new PointCollection();
        double sa = ellipse.StartParameter;
        double ea = ellipse.EndParameter;
        if (ea <= sa) ea += Math.PI * 2;

        for (int i = 0; i <= segments; i++)
        {
            double t = sa + (ea - sa) * i / segments;
            double x = rx * Math.Cos(t);
            double y = ry * Math.Sin(t);
            double xr = x * Math.Cos(rotation) - y * Math.Sin(rotation);
            double yr = x * Math.Sin(rotation) + y * Math.Cos(rotation);
            points.Add(new System.Windows.Point(
                ellipse.Center.X + xr,
                -(ellipse.Center.Y + yr)));
            UpdateBounds(ellipse.Center.X + xr, ellipse.Center.Y + yr);
        }

        var polyline = new Polyline
        {
            Points = points,
            Stroke = GetBrush(ellipse),
            StrokeThickness = 0.5
        };
        _canvas.Children.Add(polyline);
        _entityCount++;
    }

    private void RenderSpline(ACadSharp.Entities.Spline spline)
    {
        if (spline.ControlPoints == null || spline.ControlPoints.Count < 2) return;

        var points = new PointCollection();
        var ctrlPts = spline.ControlPoints.ToList();

        for (int i = 0; i < ctrlPts.Count - 1; i++)
        {
            var p0 = ctrlPts[Math.Max(0, i - 1)];
            var p1 = ctrlPts[i];
            var p2 = ctrlPts[Math.Min(ctrlPts.Count - 1, i + 1)];
            var p3 = ctrlPts[Math.Min(ctrlPts.Count - 1, i + 2)];

            for (int j = 0; j <= 10; j++)
            {
                double t = j / 10.0;
                double x = CatmullRom(p0.X, p1.X, p2.X, p3.X, t);
                double y = CatmullRom(p0.Y, p1.Y, p2.Y, p3.Y, t);
                points.Add(new System.Windows.Point(x, -y));
                UpdateBounds(x, y);
            }
        }

        var polyline = new Polyline
        {
            Points = points,
            Stroke = GetBrush(spline),
            StrokeThickness = 0.5
        };
        _canvas.Children.Add(polyline);
        _entityCount++;
    }

    private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }

    private void RenderPoint(ACadSharp.Entities.Point point)
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 2,
            Height = 2,
            Fill = GetBrush(point)
        };
        Canvas.SetLeft(dot, point.Location.X - 1);
        Canvas.SetTop(dot, -point.Location.Y - 1);
        _canvas.Children.Add(dot);
        UpdateBounds(point.Location.X, point.Location.Y);
        _entityCount++;
    }

    private void RenderInsert(ACadSharp.Entities.Insert insert)
    {
        if (insert.Block?.Entities == null) return;
        foreach (var entity in insert.Block.Entities)
        {
            RenderEntity(entity);
        }
    }

    private void RenderMText(ACadSharp.Entities.MText mtext)
    {
        var tb = new TextBlock
        {
            Text = StripMTextFormatting(mtext.Value ?? ""),
            FontSize = Math.Max(mtext.Height * 0.8, 1),
            Foreground = GetBrush(mtext)
        };
        Canvas.SetLeft(tb, mtext.InsertPoint.X);
        Canvas.SetTop(tb, -mtext.InsertPoint.Y);
        _canvas.Children.Add(tb);
        UpdateBounds(mtext.InsertPoint.X, mtext.InsertPoint.Y);
        _entityCount++;
    }

    private void RenderText(ACadSharp.Entities.TextEntity text)
    {
        var tb = new TextBlock
        {
            Text = text.Value ?? "",
            FontSize = Math.Max(text.Height * 0.8, 1),
            Foreground = GetBrush(text)
        };
        Canvas.SetLeft(tb, text.InsertPoint.X);
        Canvas.SetTop(tb, -text.InsertPoint.Y);
        _canvas.Children.Add(tb);
        UpdateBounds(text.InsertPoint.X, text.InsertPoint.Y);
        _entityCount++;
    }

    private static string StripMTextFormatting(string mtext)
    {
        var result = Regex.Replace(mtext, @"\\[a-zA-Z][^;]*;", "");
        result = result.Replace("\\P", "\n").Replace("{", "").Replace("}", "");
        return result.Trim();
    }

    // ══════════════════════════════════════════════════════════
    // COLORI
    // ══════════════════════════════════════════════════════════

    private SolidColorBrush GetBrush(ACadSharp.Entities.Entity entity)
    {
        if (entity.Color.IsTrueColor)
        {
            byte r = entity.Color.R;
            byte g = entity.Color.G;
            byte b = entity.Color.B;
            if (r == 0 && g == 0 && b == 0)
                return new SolidColorBrush(Colors.White);
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        }

        short idx = entity.Color.Index;
        if (idx == 0 || idx == 256)
        {
            if (entity.Layer != null && entity.Layer.Color.Index > 0)
                idx = entity.Layer.Color.Index;
            else
                idx = 7;
        }

        if (AciColors.TryGetValue(idx, out var color))
            return new SolidColorBrush(color);

        return new SolidColorBrush(Colors.White);
    }

    // ══════════════════════════════════════════════════════════
    // BOUNDS & FIT
    // ══════════════════════════════════════════════════════════

    private void UpdateBounds(double x, double y)
    {
        if (x < _minX) _minX = x;
        if (y < _minY) _minY = y;
        if (x > _maxX) _maxX = x;
        if (y > _maxY) _maxY = y;
        _hasBounds = true;
    }

    private void FitToView()
    {
        double w = _maxX - _minX;
        double h = _maxY - _minY;
        if (w <= 0 || h <= 0) return;

        double viewW = ActualWidth > 0 ? ActualWidth : 800;
        double viewH = ActualHeight > 0 ? ActualHeight : 600;

        double sx = viewW / w * 0.9;
        double sy = viewH / h * 0.9;
        _zoom = Math.Min(sx, sy);

        _scaleTransform.ScaleX = _zoom;
        _scaleTransform.ScaleY = _zoom;

        double cx = (_minX + _maxX) / 2.0;
        double cy = -(_minY + _maxY) / 2.0;

        _translateTransform.X = viewW / 2.0 - cx * _zoom;
        _translateTransform.Y = viewH / 2.0 - cy * _zoom;
    }

    // ══════════════════════════════════════════════════════════
    // ZOOM & PAN
    // ══════════════════════════════════════════════════════════

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var pos = e.GetPosition(this);

        double oldZoom = _zoom;
        _zoom *= factor;

        _scaleTransform.ScaleX = _zoom;
        _scaleTransform.ScaleY = _zoom;

        _translateTransform.X = pos.X - (pos.X - _translateTransform.X) * (_zoom / oldZoom);
        _translateTransform.Y = pos.Y - (pos.Y - _translateTransform.Y) * (_zoom / oldZoom);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _lastMousePos = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        ReleaseMouseCapture();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(this);
        _translateTransform.X += pos.X - _lastMousePos.X;
        _translateTransform.Y += pos.Y - _lastMousePos.Y;
        _lastMousePos = pos;
    }
}
