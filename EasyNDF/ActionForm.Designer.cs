namespace EasyNDF
{
    partial class ActionForm
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
            RuleNameLabel = new Label();
            ActionNameBox = new TextBox();
            ValueLabel = new Label();
            OperatorLabel = new Label();
            TargetLabel = new Label();
            ApplyButton = new Button();
            OperatorComboBox = new ComboBox();
            TargetComboBox = new ComboBox();
            ValueTextBox = new TextBox();
            SuspendLayout();
            // 
            // RuleNameLabel
            // 
            RuleNameLabel.AutoSize = true;
            RuleNameLabel.Font = new Font("Verdana", 12F);
            RuleNameLabel.ForeColor = Color.Linen;
            RuleNameLabel.Location = new Point(13, 17);
            RuleNameLabel.Name = "RuleNameLabel";
            RuleNameLabel.Size = new Size(62, 18);
            RuleNameLabel.TabIndex = 22;
            RuleNameLabel.Text = "Name:";
            // 
            // ActionNameBox
            // 
            ActionNameBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ActionNameBox.BackColor = Color.FromArgb(15, 15, 15);
            ActionNameBox.BorderStyle = BorderStyle.FixedSingle;
            ActionNameBox.Font = new Font("Microsoft Sans Serif", 12F);
            ActionNameBox.ForeColor = Color.Linen;
            ActionNameBox.Location = new Point(13, 38);
            ActionNameBox.Name = "ActionNameBox";
            ActionNameBox.PlaceholderText = "Enter action name";
            ActionNameBox.Size = new Size(309, 26);
            ActionNameBox.TabIndex = 0;
            // 
            // ValueLabel
            // 
            ValueLabel.AutoSize = true;
            ValueLabel.Font = new Font("Verdana", 12F);
            ValueLabel.ForeColor = Color.Linen;
            ValueLabel.Location = new Point(13, 201);
            ValueLabel.Name = "ValueLabel";
            ValueLabel.Size = new Size(60, 18);
            ValueLabel.TabIndex = 20;
            ValueLabel.Text = "Value:";
            // 
            // OperatorLabel
            // 
            OperatorLabel.AutoSize = true;
            OperatorLabel.Font = new Font("Verdana", 12F);
            OperatorLabel.ForeColor = Color.Linen;
            OperatorLabel.Location = new Point(13, 140);
            OperatorLabel.Name = "OperatorLabel";
            OperatorLabel.Size = new Size(87, 18);
            OperatorLabel.TabIndex = 19;
            OperatorLabel.Text = "Operator:";
            // 
            // TargetLabel
            // 
            TargetLabel.AutoSize = true;
            TargetLabel.Font = new Font("Verdana", 12F);
            TargetLabel.ForeColor = Color.Linen;
            TargetLabel.Location = new Point(13, 75);
            TargetLabel.Name = "TargetLabel";
            TargetLabel.Size = new Size(59, 18);
            TargetLabel.TabIndex = 18;
            TargetLabel.Text = "Target";
            // 
            // ApplyButton
            // 
            ApplyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ApplyButton.BackColor = Color.YellowGreen;
            ApplyButton.FlatStyle = FlatStyle.Flat;
            ApplyButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            ApplyButton.Location = new Point(246, 282);
            ApplyButton.Name = "ApplyButton";
            ApplyButton.Size = new Size(76, 32);
            ApplyButton.TabIndex = 4;
            ApplyButton.Text = "Apply";
            ApplyButton.UseVisualStyleBackColor = false;
            ApplyButton.Click += ApplyButton_Click;
            // 
            // OperatorComboBox
            // 
            OperatorComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            OperatorComboBox.BackColor = Color.FromArgb(15, 15, 15);
            OperatorComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            OperatorComboBox.FlatStyle = FlatStyle.Popup;
            OperatorComboBox.Font = new Font("Microsoft Sans Serif", 12F);
            OperatorComboBox.ForeColor = Color.Linen;
            OperatorComboBox.FormattingEnabled = true;
            OperatorComboBox.Items.AddRange(new object[] { "Add [int, float]", "Subtract [int, float]", "Multiply [int, float]", "Divide [int, float]", "Set To [int, float, string, boolean]", "Insert [array]", "Append [line]", "Prepend [line]", "Replace [line]", "Remove [line]" });
            OperatorComboBox.Location = new Point(13, 160);
            OperatorComboBox.Name = "OperatorComboBox";
            OperatorComboBox.Size = new Size(309, 28);
            OperatorComboBox.TabIndex = 2;
            // 
            // TargetComboBox
            // 
            TargetComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            TargetComboBox.BackColor = Color.FromArgb(15, 15, 15);
            TargetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            TargetComboBox.FlatStyle = FlatStyle.Popup;
            TargetComboBox.Font = new Font("Microsoft Sans Serif", 12F);
            TargetComboBox.ForeColor = Color.Linen;
            TargetComboBox.FormattingEnabled = true;
            TargetComboBox.Items.AddRange(new object[] { "Entire Descriptor", "Descriptor Title", "Descriptor Type" });
            TargetComboBox.Location = new Point(13, 95);
            TargetComboBox.Name = "TargetComboBox";
            TargetComboBox.Size = new Size(309, 28);
            TargetComboBox.TabIndex = 1;
            // 
            // ValueTextBox
            // 
            ValueTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ValueTextBox.BackColor = Color.FromArgb(15, 15, 15);
            ValueTextBox.BorderStyle = BorderStyle.FixedSingle;
            ValueTextBox.Font = new Font("Microsoft Sans Serif", 12F);
            ValueTextBox.ForeColor = Color.Linen;
            ValueTextBox.Location = new Point(13, 222);
            ValueTextBox.Name = "ValueTextBox";
            ValueTextBox.PlaceholderText = "Enter a value";
            ValueTextBox.Size = new Size(309, 26);
            ValueTextBox.TabIndex = 3;
            // 
            // ActionForm
            // 
            AcceptButton = ApplyButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(30, 30, 30);
            ClientSize = new Size(334, 326);
            Controls.Add(RuleNameLabel);
            Controls.Add(ActionNameBox);
            Controls.Add(ValueLabel);
            Controls.Add(OperatorLabel);
            Controls.Add(TargetLabel);
            Controls.Add(ApplyButton);
            Controls.Add(OperatorComboBox);
            Controls.Add(TargetComboBox);
            Controls.Add(ValueTextBox);
            MaximumSize = new Size(600, 365);
            MinimumSize = new Size(350, 365);
            Name = "ActionForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Action Editor";
            Load += ActionForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        public Label RuleNameLabel;
        public TextBox ActionNameBox;
        public Label ValueLabel;
        public Label OperatorLabel;
        public Label TargetLabel;
        public Button ApplyButton;
        public ComboBox OperatorComboBox;
        public ComboBox TargetComboBox;
        public TextBox ValueTextBox;
    }
}