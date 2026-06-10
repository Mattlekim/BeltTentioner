using BeltTentionerWPF.ViewModels;
using System.Windows;

namespace BeltTentionerWPF.Views
{
    public partial class TestingWindow : Window
    {
        public TestingWindow()
        {
            InitializeComponent();
        }

        private void CurveImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is TestingViewModel vm)
            {
                vm.CurveGraphWidth = (int)e.NewSize.Width;
                vm.CurveGraphHeight = (int)e.NewSize.Height;
            }
        }

        private void MotorImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is TestingViewModel vm)
            {
                vm.MotorGraphWidth = (int)e.NewSize.Width;
                vm.MotorGraphHeight = (int)e.NewSize.Height;
            }
        }
    }
}
