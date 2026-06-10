using BeltTentionerWPF.Services;
using BeltTentionerWPF.ViewModels;
using System.Windows;

namespace BeltTentionerWPF
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
        }

        protected override void OnClosed(EventArgs e)
        {
            _vm.Dispose();
            base.OnClosed(e);
        }
    }
}
