namespace EasyNDF
{
    partial class SettingsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            AppendCheckbox = new CheckBox();
            AppendTextBox = new TextBox();
            SettingsToolTip = new ToolTip(components);
            SuspendLayout();
            // 
            // AppendCheckbox
            // 
            AppendCheckbox.AutoSize = true;
            AppendCheckbox.Font = new Font("Verdana", 12F);
            AppendCheckbox.ForeColor = Color.Linen;
            AppendCheckbox.Location = new Point(12, 9);
            AppendCheckbox.Name = "AppendCheckbox";
            AppendCheckbox.Size = new Size(236, 22);
            AppendCheckbox.TabIndex = 0;
            AppendCheckbox.Text = "Append in-line comments";
            AppendCheckbox.UseVisualStyleBackColor = true;
            AppendCheckbox.CheckedChanged += CommentCheckbox_CheckedChanged;
            AppendCheckbox.MouseHover += AppendCheckbox_MouseHover;
            // 
            // AppendTextBox
            // 
            AppendTextBox.BackColor = Color.FromArgb(15, 15, 15);
            AppendTextBox.BorderStyle = BorderStyle.FixedSingle;
            AppendTextBox.Font = new Font("Microsoft Sans Serif", 12F);
            AppendTextBox.ForeColor = Color.Linen;
            AppendTextBox.Location = new Point(12, 37);
            AppendTextBox.Name = "AppendTextBox";
            AppendTextBox.PlaceholderText = "Enter your preferred in-line comment";
            AppendTextBox.Size = new Size(354, 26);
            AppendTextBox.TabIndex = 4;
            // 
            // SettingsForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(30, 30, 30);
            ClientSize = new Size(784, 415);
            Controls.Add(AppendTextBox);
            Controls.Add(AppendCheckbox);
            Name = "SettingsForm";
            Text = "Settings";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private CheckBox AppendCheckbox;
        public TextBox AppendTextBox;
        private ToolTip SettingsToolTip;
    }
}