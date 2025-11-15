using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyNDF
{
    public partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();
        }

        private void CommentCheckbox_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void AppendCheckbox_MouseHover(object sender, EventArgs e)
        {
            SettingsToolTip.SetToolTip(AppendCheckbox, "If you would like to have comments appended to the end of\n" +
                                                       "each modified line in the .ndf files, check this box.");
        }
    }
}
