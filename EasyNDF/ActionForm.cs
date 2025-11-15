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
    public partial class ActionForm : Form
    {
        public ActionForm()
        {
            InitializeComponent();
        }

        private void ActionForm_Load(object sender, EventArgs e)
        {
            
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
