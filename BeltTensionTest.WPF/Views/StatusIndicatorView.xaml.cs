using System.Windows;
using System.Windows.Controls;

namespace BeltTensionTest.WPF.Views
{
    public partial class StatusIndicatorView : UserControl
    {
        public static readonly DependencyProperty IsOnProperty =
            DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(StatusIndicatorView),
                new PropertyMetadata(false));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatusIndicatorView),
                new PropertyMetadata("Status"));

        public bool   IsOn  { get => (bool)GetValue(IsOnProperty);   set => SetValue(IsOnProperty,  value); }
        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

        public StatusIndicatorView() => InitializeComponent();
    }
}
