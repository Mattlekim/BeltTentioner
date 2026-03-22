using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace belttentiontest.Controls
{
    public class PercentageUpDown : NumericUpDown
    {
        protected override void UpdateEditText()
        {
            Text = (Value / 100M).ToString("P0");
        }
    }

}
