using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace belttentiontest.Controls
{
    public class GForceUpDown : NumericUpDown
    {
        protected override void UpdateEditText()
        {
            try
            {
                Text = $"{Value} G";
            }
            catch { }


        }
    }

}
