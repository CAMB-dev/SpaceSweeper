using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SpaceSweeper.Core.Scanning;
using SpaceSweeper.Core.Utilities;
using WpfBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace SpaceSweeper.App.Wpf.Controls;

public sealed class TreemapControl : FrameworkElement
{
    private const int MaxRenderedNodes = 1800;
    private const int MaxChildrenPerNode = 64;
    private const double MinimumChildArea = 18;
    private const double ContainerHeaderHeight = 18;

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

    public event EventHandler<StorageNode?>? NodeActivated;

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

        var budget = MaxRenderedNodes;
        DrawNode(drawingContext, Root, bounds, 0, ref budget);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        SelectNodeAt(e.GetPosition(this), activate: e.ClickCount >= 2);
    }

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonDown(e);
        SelectNodeAt(e.GetPosition(this), activate: false);
    }

    private void SelectNodeAt(WpfPoint point, bool activate)
    {
        for (var index = _hits.Count - 1; index >= 0; index--)
        {
            var hit = _hits[index];
            if (hit.Bounds.Contains(point))
            {
                SetCurrentValue(SelectedNodeProperty, hit.Node);
                NodeSelected?.Invoke(this, hit.Node);
                if (activate && hit.Node.IsContainer)
                {
                    NodeActivated?.Invoke(this, hit.Node);
                }

                return;
            }
        }
    }

    private void DrawNode(DrawingContext drawingContext, StorageNode node, Rect bounds, int depth, ref int budget)
    {
        if (budget <= 0 || bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        budget--;
        _hits.Add((bounds, node));
        var brush = new SolidColorBrush(GetColor(depth, node));
        brush.Freeze();

        var selected = string.Equals(node.Path, SelectedNode?.Path, StringComparison.OrdinalIgnoreCase);
        var pen = selected
            ? new WpfPen(WpfBrushes.Black, 2.2)
            : new WpfPen(new SolidColorBrush(MediaColor.FromRgb(245, 248, 251)), 1);

        drawingContext.DrawRectangle(brush, pen, bounds);

        var childrenBounds = bounds;
        var hasVisibleChildren = node.Children.Count > 0 && bounds.Width * bounds.Height >= MinimumChildArea;
        if (hasVisibleChildren && bounds.Width >= 48 && bounds.Height >= 36)
        {
            var headerBounds = new Rect(bounds.X + 2, bounds.Y + 2, Math.Max(0, bounds.Width - 4), ContainerHeaderHeight);
            DrawLabel(drawingContext, node, headerBounds, GetTextBrush(GetColor(depth, node)), includeSize: false);
            childrenBounds = new Rect(
                bounds.X,
                bounds.Y + ContainerHeaderHeight + 3,
                bounds.Width,
                Math.Max(0, bounds.Height - ContainerHeaderHeight - 3));
        }
        else if (CanDrawInlineLabel(bounds))
        {
            DrawLabel(drawingContext, node, bounds, GetTextBrush(GetColor(depth, node)), includeSize: bounds.Width >= 96 && bounds.Height >= 30);
        }

        if (depth >= 5 || node.Children.Count == 0 || childrenBounds.Width * childrenBounds.Height < MinimumChildArea)
        {
            return;
        }

        var children = node.Children
            .Where(static child => child.Length > 0)
            .Take(MaxChildrenPerNode)
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

        var remaining = childrenBounds;
        var horizontal = childrenBounds.Width >= childrenBounds.Height;

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
            DrawNode(drawingContext, child, childBounds, depth + 1, ref budget);
        }
    }

    private bool CanDrawInlineLabel(Rect bounds)
    {
        return bounds.Width >= 34 && bounds.Height >= 15;
    }

    private void DrawLabel(DrawingContext drawingContext, StorageNode node, Rect bounds, WpfBrush textBrush, bool includeSize)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var fontSize = bounds.Height < 19 ? 9 : 11;
        var label = includeSize
            ? $"{node.Name}  {SizeFormatter.FormatBytes(node.Length)}"
            : node.Name;
        var formatted = new FormattedText(
            label,
            CultureInfo.CurrentUICulture,
            WpfFlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            textBrush,
            dpi.PixelsPerDip)
        {
            MaxTextWidth = Math.Max(0, bounds.Width - 8),
            MaxTextHeight = Math.Max(0, bounds.Height - 6),
            Trimming = TextTrimming.CharacterEllipsis
        };

        drawingContext.PushClip(new RectangleGeometry(bounds));
        drawingContext.DrawText(formatted, new WpfPoint(bounds.X + 4, bounds.Y + 4));
        drawingContext.Pop();
    }

    private static WpfBrush GetTextBrush(MediaColor background)
    {
        var luminance = background.R * 0.299 + background.G * 0.587 + background.B * 0.114;
        return luminance > 155 ? WpfBrushes.Black : WpfBrushes.White;
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
