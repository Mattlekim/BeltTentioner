using System.Windows;

namespace BeltTensionTest.WPF.Views
{
    public partial class MotorSettingsWindow : Window
    {
        public MotorSettingsWindow(object viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
