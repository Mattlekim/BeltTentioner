using BeltTensionTest.WPF.ViewModels;
using System.Windows;
using System.Windows.Interop;

namespace BeltTensionTest.WPF.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel VM => (MainViewModel)DataContext;
        private TestingWindow? _testingWindow;
        private DebugLogWindow? _debugWindow;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();

            Loaded  += (_, _) => Shared.WpfMessageBridge.Attach(this);
            Closing += (_, _) => VM.Dispose();
        }

        private void OpenTestingWindow_Click(object sender, RoutedEventArgs e)
        {
            if (_testingWindow == null || !_testingWindow.IsLoaded)
            {
                _testingWindow = new TestingWindow(VM);
                _testingWindow.Owner = this;
                _testingWindow.Show();
            }
            else
            {
                _testingWindow.Activate();
            }
        }

        private void OpenDebugLog_Click(object sender, RoutedEventArgs e)
        {
            if (_debugWindow == null || !_debugWindow.IsLoaded)
            {
                _debugWindow = new DebugLogWindow(VM.Device);
                _debugWindow.Owner = this;
                _debugWindow.Show();
            }
            else
            {
                _debugWindow.Activate();
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
