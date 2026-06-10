using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BeltTensionTest.WPF.Controls
{
    /// <summary>
    /// A slim slider that shows both a track and an editable value box.
    /// Replaces ThinTrackBar from WinForms.
    /// </summary>
    public class ThinSlider : Slider
    {
        static ThinSlider()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ThinSlider),
                new FrameworkPropertyMetadata(typeof(ThinSlider)));
        }
    }

    /// <summary>
    /// A status indicator: text + coloured dot (on = green, off = red).
    /// Replaces OnOffStatusControl from WinForms.
    /// </summary>
    public class StatusIndicator : Control
    {
        static StatusIndicator()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(StatusIndicator),
                new FrameworkPropertyMetadata(typeof(StatusIndicator)));
        }

        public static readonly DependencyProperty IsOnProperty =
            DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(StatusIndicator),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelTextProperty =
            DependencyProperty.Register(nameof(LabelText), typeof(string), typeof(StatusIndicator),
                new PropertyMetadata("Status"));

        public bool IsOn
        {
            get => (bool)GetValue(IsOnProperty);
            set => SetValue(IsOnProperty, value);
        }

        public string LabelText
        {
            get => (string)GetValue(LabelTextProperty);
            set => SetValue(LabelTextProperty, value);
        }
    }
}
