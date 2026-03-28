namespace EasyNDF
{
    partial class TextEntryForm
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
            TextEntryLabel = new Label();
            TextBox = new TextBox();
            ApplyButton = new Button();
            SuspendLayout();
            // 
            // TextEntryLabel
            // 
            TextEntryLabel.AutoSize = true;
            TextEntryLabel.Font = new Font("Verdana", 12F);
            TextEntryLabel.ForeColor = Color.Linen;
            TextEntryLabel.Location = new Point(12, 10);
            TextEntryLabel.Name = "TextEntryLabel";
            TextEntryLabel.Size = new Size(98, 18);
            TextEntryLabel.TabIndex = 3;
            TextEntryLabel.Text = "Enter Text:";
            // 
            // TextBox
            // 
            TextBox.BackColor = Color.FromArgb(15, 15, 15);
            TextBox.BorderStyle = BorderStyle.FixedSingle;
            TextBox.Font = new Font("Microsoft Sans Serif", 12F);
            TextBox.ForeColor = Color.Linen;
            TextBox.Location = new Point(12, 31);
            TextBox.Name = "TextBox";
            TextBox.PlaceholderText = "Enter some text";
            TextBox.Size = new Size(278, 26);
            TextBox.TabIndex = 0;
            // 
            // ApplyButton
            // 
            ApplyButton.BackColor = Color.YellowGreen;
            ApplyButton.BackgroundImage = Properties.Resources.buttonBG4_128x32_Tr1;
            ApplyButton.BackgroundImageLayout = ImageLayout.Stretch;
            ApplyButton.DialogResult = DialogResult.OK;
            ApplyButton.FlatAppearance.BorderSize = 0;
            ApplyButton.FlatStyle = FlatStyle.Flat;
            ApplyButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            ApplyButton.Location = new Point(296, 25);
            ApplyButton.Name = "ApplyButton";
            ApplyButton.Size = new Size(80, 32);
            ApplyButton.TabIndex = 1;
            ApplyButton.Text = "Apply";
            ApplyButton.UseVisualStyleBackColor = false;
            ApplyButton.Click += ApplyButton_Click;
            // 
            // TextEntryForm
            // 
            AcceptButton = ApplyButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(30, 30, 30);
            ClientSize = new Size(383, 69);
            Controls.Add(ApplyButton);
            Controls.Add(TextEntryLabel);
            Controls.Add(TextBox);
            MaximumSize = new Size(399, 108);
            MinimumSize = new Size(399, 108);
            Name = "TextEntryForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "           ";
            Load += TextEntryForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        public Label TextEntryLabel;
        public TextBox TextBox;
        private Button ApplyButton;
    }
}