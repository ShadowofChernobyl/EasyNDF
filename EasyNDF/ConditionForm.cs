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
    public partial class ConditionForm : Form
    {
        public ConditionForm()
        {
            InitializeComponent();
            OperandComboBox.Items.AddRange(FileManager.FetchOperandList().Cast<string>().ToArray());
            RefreshOverrideVisibility();
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ConditionNameBox.Text))
            {
                MessageBox.Show("Condition must have a name.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void OverrideCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            RefreshOverrideVisibility();
        }

        private void RefreshOverrideVisibility()
        {
            OverrideTextBox.Visible = OverrideCheckBox.Checked;
            OperandComboBox.Visible = !OverrideCheckBox.Checked;
        }
    }
}
