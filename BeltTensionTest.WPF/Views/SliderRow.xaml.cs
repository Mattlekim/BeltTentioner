using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BeltTensionTest.WPF.Views
{
    public partial class SliderRow : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(SliderRow), new PropertyMetadata(""));
        public static readonly DependencyProperty MinProperty =
            DependencyProperty.Register(nameof(Min), typeof(double), typeof(SliderRow), new PropertyMetadata(0.0));
        public static readonly DependencyProperty MaxProperty =
            DependencyProperty.Register(nameof(Max), typeof(double), typeof(SliderRow), new PropertyMetadata(100.0));
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(SliderRow),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty FillBrushProperty =
            DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(SliderRow),
                new PropertyMetadata(Brushes.DodgerBlue));

        public string Label    { get => (string)GetValue(LabelProperty);  set => SetValue(LabelProperty,    value); }
        public double Min      { get => (double)GetValue(MinProperty);    set => SetValue(MinProperty,       value); }
        public double Max      { get => (double)GetValue(MaxProperty);    set => SetValue(MaxProperty,       value); }
        public double Value    { get => (double)GetValue(ValueProperty);  set => SetValue(ValueProperty,     value); }
        public Brush  FillBrush { get => (Brush)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

        public SliderRow() => InitializeComponent();
    }
}
