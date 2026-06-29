using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace BeltTensionTest.WPF.Views
{
    public partial class SelectCarSaveWindow : Window
    {
        private double? _ownerOriginalOpacity;
        public string? SelectedCarName { get; private set; }

        public SelectCarSaveWindow(IEnumerable<string> availableNames, string targetCar)
        {
            InitializeComponent();

            // Ensure the window appears centered on top of the main window (set owner)
            var main = Application.Current?.MainWindow;
            if (main != null)
            {
                this.Owner = main;
                this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                this.ShowInTaskbar = false;
            }
            // Dim the owner while this dialog is open and restore when closed
            this.Loaded += SelectCarSaveWindow_Loaded;
            this.Closed += SelectCarSaveWindow_Closed;
            this.Activate();

            // Exclude the target car name (no point picking the same key)
            var list = availableNames
                .Where(n => !string.Equals(n, targetCar, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n)
                .ToList();

            ListBoxSaves.ItemsSource = list;
            if (list.Count > 0) ListBoxSaves.SelectedIndex = 0;
        }

        private void SelectCarSaveWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (this.Owner != null)
            {
                try
                {
                    _ownerOriginalOpacity = this.Owner.Opacity;
                    this.Owner.Opacity = 0.12; // much dimmer
                }
                catch { }
            }
        }

        private void SelectCarSaveWindow_Closed(object? sender, System.EventArgs e)
        {
            if (this.Owner != null && _ownerOriginalOpacity.HasValue)
            {
                try
                {
                    this.Owner.Opacity = _ownerOriginalOpacity.Value;
                }
                catch { }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // restore owner opacity in case dialog closed via Ok
            if (this.Owner != null && _ownerOriginalOpacity.HasValue)
            {
                try { this.Owner.Opacity = _ownerOriginalOpacity.Value; } catch { }
            }
            if (ListBoxSaves.SelectedItem == null)
            {
                MessageBox.Show("Please select a save to load or press Cancel.", "Select Save", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SelectedCarName = ListBoxSaves.SelectedItem.ToString();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { this.DragMove(); } catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
