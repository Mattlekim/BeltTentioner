using SimHub.Plugins.Styles;
using System.Windows.Controls;
using WoteverLocalization;

namespace User.PluginSdkDemo
{
    /// <summary>
    /// Logique d'interaction pour SettingsControlDemo.xaml
    /// </summary>
    public partial class SettingsControlDemo : UserControl
    {
        public DataPluginDemo Plugin { get; }

        public SettingsControlDemo()
        {
            InitializeComponent();
      
        }

        public SettingsControlDemo(DataPluginDemo plugin) : this()
        {
            this.Plugin = plugin;
            UpdateText();
        }

        private async void StyledMessageBox_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var res = await SHMessageBox.Show("Message box", SLoc.GetValue("MyPlugin_LocalizedDialogTitle"), System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);

            await SHMessageBox.Show(res.ToString());
        }

        private void DemoWindow_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var window = new DemoWindow();

            window.Show();
        }

        private async void DemodialogWindow_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialogWindow = new DemoDialogWindow();

            var res = await dialogWindow.ShowDialogWindowAsync(this);

            await SHMessageBox.Show(res.ToString());
        }



        private void UpdateText()
        {
            if (Plugin.Settings.Enabled)
            {
                lbl_Status.Content = "Plugin is Enabled";
                bnt_Enable.Content = "Disable";
                Plugin.EnablePlugin();
            }
            else
            {
                lbl_Status.Content = "Plugin is Disabled";
                bnt_Enable.Content = "Enable";
                Plugin.DisablePlugin();
            }
        }

        private void bnt_Enable_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Plugin.Settings.Enabled = !Plugin.Settings.Enabled;

            UpdateText();
        }
    }
}