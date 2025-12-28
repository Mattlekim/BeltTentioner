using System;
using System.Reflection;
using System.Windows.Forms;

namespace belttentiontest
{
    public partial class AboutBox : Form
    {
        public AboutBox()
        {
            InitializeComponent();
            labelProductName.Text = "Belt Tensioner";
            labelVersion.Text = $"Version: {Application.ProductVersion}";
            labelCopyright.Text = "© 2025 Riddlersoft Games";
            labelCompanyName.Text = "Riddlersoft Games";
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {

        }
    }
}
