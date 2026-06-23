using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;

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

        public SliderRow()
        {
            InitializeComponent();
            Loaded += (s, e) => { UpdateTooltips(); UpdateValueBinding(); };

            // Watch for Label property changes so tooltips stay in sync
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(LabelProperty, typeof(SliderRow));
            if (descriptor != null)
                descriptor.AddValueChanged(this, (s, e) => UpdateTooltips());
        }

        private void UpdateTooltips()
        {
            var slider = FindChild<Slider>(this);
            var textbox = FindChild<TextBox>(this);
            if (slider != null)
            {
                slider.ToolTip = string.IsNullOrWhiteSpace(Label) ? null : $"Sets the {Label} value.";
            }
            if (textbox != null)
            {
                textbox.ToolTip = string.IsNullOrWhiteSpace(Label) ? null : $"Current {Label} value";
            }
        }

        private void UpdateValueBinding()
        {
            // Ensure the textbox binds to the Value property and displays one decimal place
            var textbox = FindChild<TextBox>(this);
            if (textbox != null)
            {
                var binding = new Binding(nameof(Value))
                {
                    Source = this,
                    Mode = BindingMode.TwoWay,
                    StringFormat = "F1",
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
                };
                BindingOperations.SetBinding(textbox, TextBox.TextProperty, binding);
            }
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
