using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MonoXR.Client;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Sets each overlay window's distance and rotation in 3D. The values live on the
    /// panels themselves (<see cref="OverlayRenderTarget.DistanceOffset"/> and friends),
    /// take effect at once in VR, and are saved by the overlay window. They only
    /// apply while one overlay per window is on (the 3D depth, while 3D is on too).
    /// </summary>
    public partial class PanelPlacementWindow : Window
    {
        private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(400) };
        // On until the window is built: the sliders raise ValueChanged while XAML loads, before the labels exist.
        private bool _loading = true;
        private bool _unsaved;

        public PanelPlacementWindow()
        {
            InitializeComponent();
            _loading = false;
            var panels = OverlayWindow.ActivePanels?.Where(p => !p.EditModeOnly).ToList() ?? new List<OverlayRenderTarget>();
            cmb_Panel.ItemsSource = panels;
            if (panels.Count > 0) cmb_Panel.SelectedIndex = 0;

            // Saves a moment after the last slider move (not once per tick of the slider),
            // and keeps the note about the mode current.
            _tick.Tick += (_, _) =>
            {
                if (_unsaved) { _unsaved = false; OverlayWindow.PlacementChanged(); }
                UpdateNote();
            };
            _tick.Start();
            Closed += (_, _) => { _tick.Stop(); if (_unsaved) OverlayWindow.PlacementChanged(); };
            UpdateNote();
        }

        private OverlayRenderTarget? Current => cmb_Panel.SelectedItem as OverlayRenderTarget;

        private void UpdateNote()
        {
            NoteText.Text = !OverlayWindow.PerWindowRequested
                ? "Placement only applies with \"One overlay per window\" switched on in the overlay window."
                : !OverlayWindow.PerWindowActive
                    ? "Paused: edit mode, the mouse calibration, the triple layout or too many windows is using the single canvas right now."
                    : "";
            NoteText.Visibility = NoteText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Panel_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => LoadSliders();

        private void LoadSliders()
        {
            var p = Current;
            _loading = true;
            sld_Distance.Value = p?.DistanceOffset ?? 0;
            sld_Yaw.Value = p?.YawDegrees ?? 0;
            sld_Pitch.Value = p?.PitchDegrees ?? 0;
            sld_Roll.Value = p?.RollDegrees ?? 0;
            sld_Depth.Value = p?.StereoDepthOffset ?? 0;
            _loading = false;
            ShowValues();
        }

        private void ShowValues()
        {
            DistanceValue.Text = $"{sld_Distance.Value:+0.00;-0.00;0.00} m";
            YawValue.Text = $"{sld_Yaw.Value:0}\u00B0";
            PitchValue.Text = $"{sld_Pitch.Value:0}\u00B0";
            RollValue.Text = $"{sld_Roll.Value:0}\u00B0";
            DepthValue.Text = $"{sld_Depth.Value:+0.00;-0.00;0.00} m";
        }

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            ShowValues();
            if (Current is not { } p) return;
            p.DistanceOffset = (float)sld_Distance.Value;
            p.YawDegrees = (float)sld_Yaw.Value;
            p.PitchDegrees = (float)sld_Pitch.Value;
            p.RollDegrees = (float)sld_Roll.Value;
            p.StereoDepthOffset = (float)sld_Depth.Value;
            _unsaved = true;
        }

        private void ResetOne_Click(object sender, RoutedEventArgs e)
        {
            if (Current is not { } p) return;
            Reset(p);
            LoadSliders();
            _unsaved = true;
        }

        private void ResetAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in OverlayWindow.ActivePanels ?? Array.Empty<OverlayRenderTarget>()) Reset(p);
            LoadSliders();
            _unsaved = true;
        }

        private static void Reset(OverlayRenderTarget p)
        {
            p.DistanceOffset = 0;
            p.YawDegrees = 0;
            p.PitchDegrees = 0;
            p.RollDegrees = 0;
            p.StereoDepthOffset = 0;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
