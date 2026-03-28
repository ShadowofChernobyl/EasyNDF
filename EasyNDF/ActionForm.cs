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
            RefreshCommentVisibility(); // Set initial visibility based on the state of the checkboxes
            RefreshOverrideVisibility(); // Set initial visibility based on the state of the checkboxes
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ActionNameBox.Text))
            { MessageBox.Show("Action must have a name.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            else
            { DialogResult = DialogResult.OK; this.Close(); }
        }

        private void CommentCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            RefreshCommentVisibility();
        }

        private void OverrideCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            RefreshOverrideVisibility();
        }

        private void RefreshCommentVisibility()
        {
            CommentTextBox.Visible = CommentCheckBox.Checked;
            CommentTimestampCheckBox.Visible = CommentCheckBox.Checked;
            CommentOriginalValueCheckBox.Visible = CommentCheckBox.Checked;
            this.MinimumSize = CommentCheckBox.Checked ? new Size(this.MinimumSize.Width, 435) : new Size(this.MinimumSize.Width, 345);
            this.MaximumSize = CommentCheckBox.Checked ? new Size(this.MaximumSize.Width, 435) : new Size(this.MaximumSize.Width, 345);
            this.Size = CommentCheckBox.Checked ? new Size(this.Size.Width, 435) : new Size(this.Size.Width, 345);
        }

        private void RefreshOverrideVisibility()
        {
            OverrideTextBox.Visible = OverrideCheckBox.Checked;
            TargetComboBox.Visible = !OverrideCheckBox.Checked;
        }
    }
}
