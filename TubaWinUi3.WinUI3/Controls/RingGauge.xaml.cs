using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TubaWinUi3.Services;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace TubaWinUi3.Controls;

/// <summary>
/// 原生环形仪表：Value(0-100) 画一段起点在 12 点的圆弧，圆头端点 + 底色轨道。
/// 用于磁盘健康度 / 分区占用 / 温度等百分比环图展示。
/// </summary>
public sealed partial class RingGauge : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingGauge), new PropertyMetadata(0.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty GaugeSizeProperty = DependencyProperty.Register(
        nameof(GaugeSize), typeof(double), typeof(RingGauge), new PropertyMetadata(100.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(RingGauge), new PropertyMetadata(10.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(RingGauge),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>仪表数值 0-100。</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>画布直径（px）。</summary>
    public double GaugeSize
    {
        get => (double)GetValue(GaugeSizeProperty);
        set => SetValue(GaugeSizeProperty, value);
    }

    /// <summary>环粗（px）。</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>进度环颜色；未设置时使用系统强调色。</summary>
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>底色轨道颜色；未设置时使用淡雅填充色。</summary>
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public RingGauge()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((RingGauge)d).Redraw();

    private void Redraw()
    {
        RootCanvas.Children.Clear();
        var size = Math.Max(GaugeSize, 4);
        var thickness = Math.Max(StrokeThickness, 1);
        RootCanvas.Width = size;
        RootCanvas.Height = size;
        var cx = size / 2;
        var r = (size - thickness) / 2;

        // 未显式指定时使用主题语义色（强调色 / 淡雅填充），避免固定品牌色残留
        var stroke = Stroke ?? new SolidColorBrush(ThemeColors.AccentBlue);
        var track = TrackBrush ?? new SolidColorBrush(ThemeColors.SubtleBg);

        // 底色轨道
        RootCanvas.Children.Add(Circle(cx, r, track, thickness));

        var value = Math.Clamp(Value, 0, 100);
        if (value <= 0.01)
            return;
        if (value >= 99.99)
        {
            RootCanvas.Children.Add(Circle(cx, r, stroke, thickness));
            return;
        }

        // 12 点方向起，顺时针画 value% 的圆弧
        var rad = (value * 3.6 - 90) * Math.PI / 180;
        var end = new Point(cx + r * Math.Cos(rad), cx + r * Math.Sin(rad));
        var figure = new PathFigure
        {
            StartPoint = new Point(cx, cx - r),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(r, r),
            IsLargeArc = value > 50,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        RootCanvas.Children.Add(new Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
    }

    private static Path Circle(double cx, double r, Brush brush, double thickness) => new()
    {
        Data = new EllipseGeometry { Center = new Point(cx, cx), RadiusX = r, RadiusY = r },
        Stroke = brush,
        StrokeThickness = thickness,
    };
}