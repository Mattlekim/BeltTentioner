using System.Windows;

namespace BeltTensionTest.WPF.Shared
{
    public static class ThemedMessageBox
    {
        // Basic overloads matching common MessageBox usage in the app.
        public static MessageBoxResult Show(string text)
        {
            return Show(text, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string text, string caption)
        {
            return Show(text, caption, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            // Use themed WPF window for consistent appearance
            var w = new ThemedMessageWindow(text ?? string.Empty, caption ?? string.Empty, buttons, icon);
            var res = w.ShowDialog();
            return w.Result;
        }

        public static MessageBoxResult Show(Window owner, string text, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            if (owner == null) return Show(text, caption, buttons, icon);
            var w = new ThemedMessageWindow(text ?? string.Empty, caption ?? string.Empty, buttons, icon);
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.ShowDialog();
            return w.Result;
        }
    }
}
