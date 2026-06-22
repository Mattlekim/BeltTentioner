using System.Windows;
using System;
using System.Windows;
using BeltTensionTest.WPF.ViewModels;

namespace BeltTensionTest.WPF.Views
{
    public partial class MotorSettingsWindow : Window
    {

        public static int TestingAngle
        {
            get
            {
                if (SR_TestAngle == null)
                    return 0;

                return (int)SR_TestAngle.Value;
            }
        }
        private static SliderRow SR_TestAngle;

        public MotorSettingsWindow(object viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            SR_TestAngle = sr_TestAngle;
        }

        private void Cb_Test_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                MainViewModel.OverideMotorAnglesForTesting = true;
            }
            catch (Exception) { }
        }

        private void Cb_Test_Unchecked(object sender, RoutedEventArgs e)
        {   
            try
            {
                MainViewModel.OverideMotorAnglesForTesting = false;
            }
            catch (Exception) { }
        }

        private void cb_MotorSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            cb_Test.IsChecked = false;
        }
    }
}
