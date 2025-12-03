using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace belttentiontest
{
    public class GForceUpDown : NumericUpDown
    {
        protected override void UpdateEditText()
        {
            this.Text = $"{this.Value} G";
        }
    }

}
