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
    public partial class TextEntryForm : Form
    {
        public TextEntryForm()
        {
            InitializeComponent();
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            this.Close();
        }

        private void TextEntryForm_Load(object sender, EventArgs e)
        {

        }
    }
}
