using System.Windows;
using System.Windows.Media;

namespace YtecStickyNote.Controls;

public sealed class NotePaper : FrameworkElement
{
    public static readonly DependencyProperty PaperColorProperty = DependencyProperty.Register(
        nameof(PaperColor), typeof(Color), typeof(NotePaper),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RuleColorProperty = DependencyProperty.Register(
        nameof(RuleColor), typeof(Color), typeof(NotePaper),
        new FrameworkPropertyMetadata(Colors.LightBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarginColorProperty = DependencyProperty.Register(
        nameof(MarginColor), typeof(Color), typeof(NotePaper),
        new FrameworkPropertyMetadata(Colors.LightCoral, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScrollOffsetProperty = DependencyProperty.Register(
        nameof(ScrollOffset), typeof(double), typeof(NotePaper),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color PaperColor
    {
        get => (Color)GetValue(PaperColorProperty);
        set => SetValue(PaperColorProperty, value);
    }

    public Color RuleColor
    {
        get => (Color)GetValue(RuleColorProperty);
        set => SetValue(RuleColorProperty, value);
    }

    public Color MarginColor
    {
        get => (Color)GetValue(MarginColorProperty);
        set => SetValue(MarginColorProperty, value);
    }

    public double ScrollOffset
    {
        get => (double)GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    public double LineHeight { get; set; } = 30;

    public double TopPadding { get; set; } = 10;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var bounds = new Rect(RenderSize);
        drawingContext.DrawRectangle(new SolidColorBrush(PaperColor), null, bounds);

        // 紙のごく薄い縦繊維。罫線より目立たせない。
        var fiberPen = new Pen(new SolidColorBrush(Color.FromArgb(13, 92, 78, 60)), 0.6);
        for (var x = 18d; x < ActualWidth; x += 44d)
        {
            drawingContext.DrawLine(fiberPen, new Point(x, 0), new Point(x + 5, ActualHeight));
        }

        var offset = ((ScrollOffset % LineHeight) + LineHeight) % LineHeight;
        var firstRule = TopPadding + LineHeight - 1 - offset;
        var rulePen = new Pen(new SolidColorBrush(RuleColor), 1);
        for (var y = firstRule; y < ActualHeight; y += LineHeight)
        {
            drawingContext.DrawLine(rulePen, new Point(0, y), new Point(ActualWidth, y));
        }

        var marginPen = new Pen(new SolidColorBrush(MarginColor), 1.2);
        drawingContext.DrawLine(marginPen, new Point(51, 0), new Point(51, ActualHeight));

        // キャンパスノートらしい綴じ側の小さな目印。
        var holeFill = new SolidColorBrush(Color.FromArgb(36, 65, 58, 48));
        for (var y = 24d; y < ActualHeight; y += 72d)
        {
            drawingContext.DrawEllipse(holeFill, null, new Point(20, y), 3.2, 3.2);
        }
    }
}
