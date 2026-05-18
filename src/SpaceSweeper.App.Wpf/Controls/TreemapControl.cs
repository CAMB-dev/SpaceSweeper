using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SpaceSweeper.Core.Scanning;
using SpaceSweeper.Core.Utilities;
using MediaColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace SpaceSweeper.App.Wpf.Controls;

public sealed class TreemapControl : FrameworkElement
{
    public static readonly DependencyProperty RootProperty = DependencyProperty.Register(
        nameof(Root),
        typeof(StorageNode),
        typeof(TreemapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(
        nameof(SelectedNode),
        typeof(StorageNode),
        typeof(TreemapControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly List<(Rect Bounds, StorageNode Node)> _hits = [];

    public event EventHandler<StorageNode?>? NodeSelected;

    public StorageNode? Root
    {
        get => (StorageNode?)GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public StorageNode? SelectedNode
    {
        get => (StorageNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        _hits.Clear();
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(WpfBrushes.White, null, bounds);

        if (Root is null || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        DrawNode(drawingContext, Root, bounds, 0);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        var point = e.GetPosition(this);
        for (var index = _hits.Count - 1; index >= 0; index--)
        {
            var hit = _hits[index];
            if (hit.Bounds.Contains(point))
            {
                SetCurrentValue(SelectedNodeProperty, hit.Node);
                NodeSelected?.Invoke(this, hit.Node);
                return;
            }
        }
    }

    private void DrawNode(DrawingContext drawingContext, StorageNode node, Rect bounds, int depth)
    {
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        _hits.Add((bounds, node));
        var brush = new SolidColorBrush(GetColor(depth, node));
        brush.Freeze();

        var selected = string.Equals(node.Path, SelectedNode?.Path, StringComparison.OrdinalIgnoreCase);
        var pen = selected
            ? new WpfPen(WpfBrushes.Black, 2.2)
            : new WpfPen(new SolidColorBrush(MediaColor.FromRgb(245, 248, 251)), 1);

        drawingContext.DrawRectangle(brush, pen, bounds);

        if (bounds.Width > 72 && bounds.Height > 34)
        {
            DrawLabel(drawingContext, node, bounds);
        }

        if (depth >= 5 || node.Children.Count == 0)
        {
            return;
        }

        var children = node.Children
            .Where(static child => child.Length > 0)
            .OrderByDescending(static child => child.Length)
            .Take(96)
            .ToArray();

        if (children.Length == 0)
        {
            return;
        }

        var total = children.Sum(static child => child.Length);
        if (total <= 0)
        {
            return;
        }

        var remaining = bounds;
        var horizontal = bounds.Width >= bounds.Height;

        foreach (var child in children)
        {
            var ratio = (double)child.Length / total;
            Rect childBounds;

            if (horizontal)
            {
                var width = Math.Max(1, remaining.Width * ratio);
                childBounds = new Rect(remaining.X, remaining.Y, width, remaining.Height);
                remaining = new Rect(remaining.X + width, remaining.Y, Math.Max(0, remaining.Width - width), remaining.Height);
            }
            else
            {
                var height = Math.Max(1, remaining.Height * ratio);
                childBounds = new Rect(remaining.X, remaining.Y, remaining.Width, height);
                remaining = new Rect(remaining.X, remaining.Y + height, remaining.Width, Math.Max(0, remaining.Height - height));
            }

            childBounds.Inflate(-1.5, -1.5);
            DrawNode(drawingContext, child, childBounds, depth + 1);
        }
    }

    private void DrawLabel(DrawingContext drawingContext, StorageNode node, Rect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var formatted = new FormattedText(
            $"{node.Name}  {SizeFormatter.FormatBytes(node.Length)}",
            CultureInfo.CurrentUICulture,
            WpfFlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            WpfBrushes.White,
            dpi.PixelsPerDip)
        {
            MaxTextWidth = Math.Max(0, bounds.Width - 8),
            MaxTextHeight = Math.Max(0, bounds.Height - 6),
            Trimming = TextTrimming.CharacterEllipsis
        };

        drawingContext.DrawText(formatted, new WpfPoint(bounds.X + 4, bounds.Y + 4));
    }

    private static MediaColor GetColor(int depth, StorageNode node)
    {
        var palette = new[]
        {
            MediaColor.FromRgb(41, 98, 146),
            MediaColor.FromRgb(58, 137, 117),
            MediaColor.FromRgb(167, 106, 47),
            MediaColor.FromRgb(133, 87, 145),
            MediaColor.FromRgb(179, 71, 89),
            MediaColor.FromRgb(85, 124, 53)
        };

        var index = Math.Abs((StringComparer.OrdinalIgnoreCase.GetHashCode(node.Name) + depth) % palette.Length);
        var color = palette[index];
        var factor = Math.Min(0.34, depth * 0.045);

        return MediaColor.FromRgb(
            (byte)Math.Min(255, color.R + 255 * factor),
            (byte)Math.Min(255, color.G + 255 * factor),
            (byte)Math.Min(255, color.B + 255 * factor));
    }
}
