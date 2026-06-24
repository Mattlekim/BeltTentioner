using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace BeltTensionTest.WPF.Views
{
    public partial class SelectCarSaveWindow : Window
    {
        public string? SelectedCarName { get; private set; }

        public SelectCarSaveWindow(IEnumerable<string> availableNames, string targetCar)
        {
            InitializeComponent();

            // Exclude the target car name (no point picking the same key)
            var list = availableNames
                .Where(n => !string.Equals(n, targetCar, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n)
                .ToList();

            ListBoxSaves.ItemsSource = list;
            if (list.Count > 0) ListBoxSaves.SelectedIndex = 0;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
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
