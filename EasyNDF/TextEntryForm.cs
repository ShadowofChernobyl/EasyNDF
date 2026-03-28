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
        public bool isForPresetNaming = false;

        public TextEntryForm()
        {
            InitializeComponent();
        }

        private void TextEntryForm_Load(object sender, EventArgs e)
        {
            if (TextBox.Text == MainForm.EmptyPresetLabel) TextBox.Text = "";
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            // Check for reserved preset name
            if (TextBox.Text == MainForm.EmptyPresetLabel && isForPresetNaming == true) { 
                MessageBox.Show($"The name \"{MainForm.EmptyPresetLabel}\" is reserved and cannot be used.", "Invalid Preset Name", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            // Check for invalid filename characters
            if (TextBox.Text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 && isForPresetNaming == true) {
                MessageBox.Show("The preset name may not contain the following characters: " + string.Join(" ", System.IO.Path.GetInvalidFileNameChars()), "Invalid Preset Name", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            // Check for empty entry
            if (string.IsNullOrWhiteSpace(TextBox.Text)) {
                MessageBox.Show("The text field may not be empty.", "Invalid Entry", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
