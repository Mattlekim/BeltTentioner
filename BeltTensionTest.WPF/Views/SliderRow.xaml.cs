using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;

namespace BeltTensionTest.WPF.Views
{
    public partial class SliderRow : UserControl
    {
        public static readonly DependencyProperty ResourceKeyProperty =
            DependencyProperty.Register(nameof(ResourceKey), typeof(string), typeof(SliderRow), new PropertyMetadata("", OnResourceKeyChanged));

        public string ResourceKey { get => (string)GetValue(ResourceKeyProperty); set => SetValue(ResourceKeyProperty, value); }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(SliderRow), new PropertyMetadata(""));
        public static readonly DependencyProperty MinProperty =
            DependencyProperty.Register(nameof(Min), typeof(double), typeof(SliderRow), new PropertyMetadata(0.0));
        public static readonly DependencyProperty MaxProperty =
            DependencyProperty.Register(nameof(Max), typeof(double), typeof(SliderRow), new PropertyMetadata(100.0));
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(SliderRow),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public static readonly DependencyProperty IsIntegerProperty =
            DependencyProperty.Register(nameof(IsInteger), typeof(bool), typeof(SliderRow),
                new PropertyMetadata(false, OnFormatPropertyChanged));
        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(SliderRow),
                new PropertyMetadata(1, OnFormatPropertyChanged));
        public static readonly DependencyProperty FillBrushProperty =
            DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(SliderRow),
                new PropertyMetadata(Brushes.DodgerBlue));

        public string Label    { get => (string)GetValue(LabelProperty);  set => SetValue(LabelProperty,    value); }
        public double Min      { get => (double)GetValue(MinProperty);    set => SetValue(MinProperty,       value); }
        public double Max      { get => (double)GetValue(MaxProperty);    set => SetValue(MaxProperty,       value); }
        public double Value    { get => (double)GetValue(ValueProperty);  set => SetValue(ValueProperty,     value); }
        public Brush  FillBrush { get => (Brush)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
        public bool IsInteger { get => (bool)GetValue(IsIntegerProperty); set => SetValue(IsIntegerProperty, value); }
        public int DecimalPlaces { get => (int)GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }

        public SliderRow()
        {
            InitializeComponent();
            Loaded += (s, e) => { UpdateFromResource(); UpdateTooltips(); UpdateValueBinding(); AddButtons(); };

            // Watch for Label property changes so tooltips stay in sync
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(LabelProperty, typeof(SliderRow));
            if (descriptor != null)
                descriptor.AddValueChanged(this, (s, e) => UpdateTooltips());

            var resDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ResourceKeyProperty, typeof(SliderRow));
            if (resDescriptor != null)
                resDescriptor.AddValueChanged(this, (s, e) => UpdateFromResource());

        }

        private void AddButtons()
        {
            var grid = FindChild<Grid>(this);
            if (grid == null) return;

            // ensure there is an extra column for the buttons
            if (grid.ColumnDefinitions.Count < 4)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            // avoid adding duplicates
            foreach (var child in grid.Children)
            {
                if (child is StackPanel sp && Grid.GetColumn(sp) == 3)
                    return;
            }

            // create stack panel with two repeat buttons
            var spanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new System.Windows.Thickness(4, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };

            // Use vector shapes for arrows so they render consistently regardless of system fonts/theme
            var up = new System.Windows.Controls.Primitives.RepeatButton
            {
                Width = 20,
                Height = 16,
                Padding = new System.Windows.Thickness(0),
                Margin = new System.Windows.Thickness(0, 0, 0, 1),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new System.Windows.Thickness(0),
                Focusable = false
            };

            var down = new System.Windows.Controls.Primitives.RepeatButton
            {
                Width = 20,
                Height = 16,
                Padding = new System.Windows.Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new System.Windows.Thickness(0),
                Focusable = false
            };

            // Up arrow polygon (triangle)
            var upPolygon = new System.Windows.Shapes.Polygon
            {
                // narrower triangle: 14px wide
                Points = new System.Windows.Media.PointCollection(new[] { new System.Windows.Point(0, 8), new System.Windows.Point(7, 0), new System.Windows.Point(14, 8) }),
                Stretch = System.Windows.Media.Stretch.Uniform,
                Height = 6,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            // Down arrow polygon (triangle)
            var downPolygon = new System.Windows.Shapes.Polygon
            {
                // narrower triangle: 14px wide
                Points = new System.Windows.Media.PointCollection(new[] { new System.Windows.Point(0, 0), new System.Windows.Point(7, 8), new System.Windows.Point(14, 0) }),
                Stretch = System.Windows.Media.Stretch.Uniform,
                Height = 6,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            // Bind polygon Fill to the parent RepeatButton Foreground so it follows disabled/foreground changes
            var upFillBinding = new System.Windows.Data.Binding(nameof(System.Windows.Controls.Control.Foreground)) { Source = up };
            var downFillBinding = new System.Windows.Data.Binding(nameof(System.Windows.Controls.Control.Foreground)) { Source = down };
            upPolygon.SetBinding(System.Windows.Shapes.Shape.FillProperty, upFillBinding);
            downPolygon.SetBinding(System.Windows.Shapes.Shape.FillProperty, downFillBinding);
            upPolygon.Opacity = 0.95;
            downPolygon.Opacity = 0.95;

            up.Content = upPolygon;
            down.Content = downPolygon;

            // Request the app's themed small-button background at runtime. Use 'BgMidBrush' to match dark theme.
            try
            {
                up.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "BgMidBrush");
                down.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "BgMidBrush");
                // also set border to the app border brush for consistency
                up.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderBrush");
                down.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "BorderBrush");
            }
            catch { /* ignore if resource unavailable */ }

            // Try to pick up brushes from application resources; fall back to defaults
            var app = System.Windows.Application.Current;
            // sensible dark defaults if app resources are not available
            var defaultBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x26, 0x26, 0x3A));
            defaultBg.Freeze();
            var defaultFg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xBE));
            defaultFg.Freeze();
            var defaultDisabled = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x46, 0x46, 0x6A));
            defaultDisabled.Freeze();

            System.Windows.Media.Brush bgBrush = defaultBg;
            System.Windows.Media.Brush fgBrush = defaultFg;
            System.Windows.Media.Brush disabledBrush = defaultDisabled;
            try
            {
                if (app != null)
                {
                    var tmp = app.TryFindResource("BgMidBrush") as System.Windows.Media.Brush;
                    if (tmp != null) bgBrush = tmp;
                    var tmp2 = app.TryFindResource("TextBrightBrush") as System.Windows.Media.Brush;
                    if (tmp2 != null) fgBrush = tmp2;
                    var tmp3 = app.TryFindResource("DisabledBrush") as System.Windows.Media.Brush;
                    if (tmp3 != null) disabledBrush = tmp3;
                }
            }
            catch { }

            up.Background = bgBrush;
            down.Background = bgBrush;
            up.Foreground = fgBrush;
            down.Foreground = fgBrush;

            // Ensure arrows update when enabled state changes
            void UpdateEnabledVisual(System.Windows.Controls.Primitives.RepeatButton btn)
            {
                if (btn.IsEnabled)
                {
                    btn.Foreground = fgBrush;
                    btn.Background = bgBrush;
                }
                else
                {
                    btn.Foreground = disabledBrush;
                    // use DisabledBrush for background as well if available
                    try { btn.Background = disabledBrush; } catch { }
                }
            }

            up.IsEnabledChanged += (s, e) => UpdateEnabledVisual(up);
            down.IsEnabledChanged += (s, e) => UpdateEnabledVisual(down);

            up.Click += Up_Click;
            down.Click += Down_Click;

            // Create and assign a local RepeatButton style so visuals are consistent even if app resources
            // are not available or applied late.
            var application2 = System.Windows.Application.Current;
            System.Windows.Media.Brush hoverBrush2 = null;
            System.Windows.Media.Brush pressedBrush2 = null;
            try
            {
                if (application2 != null)
                {
                    hoverBrush2 = application2.TryFindResource("BgLightBrush") as System.Windows.Media.Brush;
                    pressedBrush2 = application2.TryFindResource("AccentBlueBrush") as System.Windows.Media.Brush;
                }
            }
            catch { }
            if (hoverBrush2 == null) hoverBrush2 = bgBrush;
            if (pressedBrush2 == null) pressedBrush2 = hoverBrush2;

            var rbStyle = new System.Windows.Style(typeof(System.Windows.Controls.Primitives.RepeatButton));
            rbStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.BackgroundProperty, bgBrush));
            rbStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.ForegroundProperty, fgBrush));
            rbStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.BorderBrushProperty, System.Windows.Application.Current?.TryFindResource("BorderBrush") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Transparent));

            // Template: border with content presenter
            var tmpl = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Primitives.RepeatButton));
            var bdFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            bdFactory.SetValue(System.Windows.FrameworkElement.NameProperty, "Bd");
            bdFactory.SetBinding(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            bdFactory.SetBinding(System.Windows.Controls.Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            bdFactory.SetBinding(System.Windows.Controls.Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var cpFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            cpFactory.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            cpFactory.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            bdFactory.AppendChild(cpFactory);
            tmpl.VisualTree = bdFactory;

            // Style triggers
            var trigOver = new System.Windows.Trigger { Property = System.Windows.UIElement.IsMouseOverProperty, Value = true };
            trigOver.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.BackgroundProperty, hoverBrush2));
            var trigPressed = new System.Windows.Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            trigPressed.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.BackgroundProperty, pressedBrush2));
            var trigDisabled = new System.Windows.Trigger { Property = System.Windows.UIElement.IsEnabledProperty, Value = false };
            trigDisabled.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.BackgroundProperty, disabledBrush));
            trigDisabled.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x80))));

            rbStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.TemplateProperty, tmpl));
            rbStyle.Triggers.Add(trigOver);
            rbStyle.Triggers.Add(trigPressed);
            rbStyle.Triggers.Add(trigDisabled);

            up.Style = rbStyle;
            down.Style = rbStyle;

            // initialize visuals
            UpdateEnabledVisual(up);
            UpdateEnabledVisual(down);

            spanel.Children.Add(up);
            spanel.Children.Add(down);

            System.Windows.Controls.Grid.SetColumn(spanel, 3);
            grid.Children.Add(spanel);
        }

        private void UpdateTooltips()
        {
            var slider = FindChild<Slider>(this);
            var textbox = FindChild<TextBox>(this);
            if (slider != null)
            {
                // If a resource-specific tooltip exists use it, otherwise fall back to a generated tooltip
                string? resTip = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(ResourceKey))
                        resTip = BeltTensionTest.WPF.Properties.Resources.ResourceManager.GetString(ResourceKey + "ToolTip", BeltTensionTest.WPF.Properties.Resources.Culture);
                }
                catch { }

                // Wrap tooltip text in a TextBlock so long lines wrap to multiple lines
                string? tipText = !string.IsNullOrWhiteSpace(resTip) ? resTip : (string.IsNullOrWhiteSpace(Label) ? null : $"Sets the {Label} value.");
                if (string.IsNullOrWhiteSpace(tipText))
                    slider.ToolTip = null;
                else
                    slider.ToolTip = new TextBlock { Text = tipText, TextWrapping = System.Windows.TextWrapping.Wrap, MaxWidth = 400 };
            }
            if (textbox != null)
            {
                string? resTip = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(ResourceKey))
                        resTip = BeltTensionTest.WPF.Properties.Resources.ResourceManager.GetString(ResourceKey + "CurrentValueToolTip", BeltTensionTest.WPF.Properties.Resources.Culture);
                }
                catch { }

                // Wrap tooltip text in a TextBlock so long lines wrap to multiple lines
                string? curTipText = !string.IsNullOrWhiteSpace(resTip) ? resTip : (string.IsNullOrWhiteSpace(Label) ? null : $"Current {Label} value");
                if (string.IsNullOrWhiteSpace(curTipText))
                    textbox.ToolTip = null;
                else
                    textbox.ToolTip = new TextBlock { Text = curTipText, TextWrapping = System.Windows.TextWrapping.Wrap, MaxWidth = 320 };
            }
        }

        private static void OnResourceKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SliderRow row)
                row.UpdateFromResource();
        }

        private void UpdateFromResource()
        {
            if (string.IsNullOrWhiteSpace(ResourceKey)) return;

            try
            {
                var rm = BeltTensionTest.WPF.Properties.Resources.ResourceManager;
                var culture = BeltTensionTest.WPF.Properties.Resources.Culture;
                var txt = rm.GetString(ResourceKey, culture);
                if (!string.IsNullOrWhiteSpace(txt))
                    Label = txt;

                // if resource contains specific tooltip keys, UpdateTooltips will pick them up
                UpdateTooltips();
            }
            catch { }
        }

        private void UpdateValueBinding()
        {
            // Ensure the textbox binds to the Value property and displays one decimal place
            var textbox = FindChild<TextBox>(this);
            if (textbox != null)
            {
                // determine format from DecimalPlaces and IsInteger
                var places = Math.Max(0, DecimalPlaces);
                var format = IsInteger ? "F0" : $"F{places}";
                var binding = new Binding(nameof(Value))
                {
                    Source = this,
                    Mode = BindingMode.TwoWay,
                    StringFormat = format,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
                };
                BindingOperations.SetBinding(textbox, TextBox.TextProperty, binding);
            }

            // adjust the slider stepping/snap behaviour
            var slider = FindChild<Slider>(this);
            if (slider != null)
            {
                if (IsInteger)
                {
                    slider.TickFrequency = 1.0;
                    slider.IsSnapToTickEnabled = true;
                    slider.SmallChange = 1.0;
                    slider.LargeChange = 1.0;
                }
                else
                {
                    var places = Math.Max(0, DecimalPlaces);
                    var step = Math.Pow(10, -places);
                    // avoid zero
                    if (step <= 0) step = 0.1;
                    slider.TickFrequency = step;
                    slider.IsSnapToTickEnabled = false;
                    slider.SmallChange = step;
                    slider.LargeChange = step * 10;
                }

                // Allow clicking on the slider track to jump to that position
                try
                {
                    // remove existing handler to avoid duplicates
                    slider.PreviewMouseLeftButtonDown -= Slider_PreviewMouseLeftButtonDown;
                    slider.PreviewMouseLeftButtonDown += Slider_PreviewMouseLeftButtonDown;
                }
                catch { }
            }
        }

        private void Slider_PreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (sender is not Slider slider) return;

            // get position relative to the slider control
            var pos = e.GetPosition(slider);
            // if the user clicked the thumb, don't interfere - allow normal drag behavior
            var hit = slider.InputHitTest(pos) as DependencyObject;
            while (hit != null)
            {
                if (hit is System.Windows.Controls.Primitives.Thumb) return;
                hit = VisualTreeHelper.GetParent(hit);
            }

            double ratio = 0;
            if (slider.ActualWidth > 0)
                ratio = pos.X / slider.ActualWidth;
            if (slider.FlowDirection == System.Windows.FlowDirection.RightToLeft)
                ratio = 1.0 - ratio;

            var newValue = slider.Minimum + (slider.Maximum - slider.Minimum) * ratio;

            // respect integer/decimal formatting
            if (IsInteger)
                newValue = Math.Round(newValue);
            else
                newValue = Math.Round(newValue, Math.Max(0, DecimalPlaces));

            // clamp and apply
            if (newValue < slider.Minimum) newValue = slider.Minimum;
            if (newValue > slider.Maximum) newValue = slider.Maximum;

            Value = newValue;

            // prevent default RepeatButton behavior
            e.Handled = true;
        }

        private static void OnFormatPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SliderRow row)
            {
                row.UpdateValueBinding();
            }
        }

        private void Up_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            AdjustValue(true);
        }

        private void Down_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            AdjustValue(false);
        }

        private void AdjustValue(bool increase)
        {
            // determine step from IsInteger/DecimalPlaces
            double step = IsInteger ? 1.0 : Math.Pow(10, -Math.Max(0, DecimalPlaces));
            if (step <= 0) step = 0.1;
            var newValue = Value + (increase ? step : -step);
            // clamp
            if (newValue > Max) newValue = Max;
            if (newValue < Min) newValue = Min;
            // round for display
            if (IsInteger)
                newValue = Math.Round(newValue);
            else
                newValue = Math.Round(newValue, Math.Max(0, DecimalPlaces));
            Value = newValue;
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
