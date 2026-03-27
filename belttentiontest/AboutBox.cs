using System;
using System.Reflection;
using System.Windows.Forms;

namespace belttentiontest
{
    public partial class AboutBox : Form
    {

        public const string Version = "1.1.0";
        public const string FileVersion = Version + ".0"; // ensures four-part version for file/version attributes

        public AboutBox()
        {
            InitializeComponent();
            labelProductName.Text = "Belt Tensioner";
            labelVersion.Text = $"Version: {Version}";
            labelCopyright.Text = "© 2026 Riddlersoft Games";
            labelCompanyName.Text = "Riddlersoft Games";
        }


        private void AboutBox_Load(object sender, EventArgs e)
        {

        }
    }
}
