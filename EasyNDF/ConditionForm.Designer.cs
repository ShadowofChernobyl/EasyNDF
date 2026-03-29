namespace EasyNDF
{
    partial class ConditionForm
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
            ValueTextBox = new TextBox();
            OperandComboBox = new ComboBox();
            OperatorComboBox = new ComboBox();
            OperandLabel = new Label();
            OperatorLabel = new Label();
            ValueLabel = new Label();
            RuleNameLabel = new Label();
            ConditionNameBox = new TextBox();
            ApplyButton = new Button();
            ConditionFormToolTip = new ToolTip(components);
            OverrideCheckBox = new CheckBox();
            OverrideTextBox = new TextBox();
            SuspendLayout();
            // 
            // ValueTextBox
            // 
            ValueTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ValueTextBox.BackColor = Color.FromArgb(15, 15, 15);
            ValueTextBox.BorderStyle = BorderStyle.FixedSingle;
            ValueTextBox.Font = new Font("Microsoft Sans Serif", 12F);
            ValueTextBox.ForeColor = Color.Linen;
            ValueTextBox.Location = new Point(12, 222);
            ValueTextBox.Name = "ValueTextBox";
            ValueTextBox.PlaceholderText = "Enter a value";
            ValueTextBox.Size = new Size(310, 26);
            ValueTextBox.TabIndex = 3;
            // 
            // OperandComboBox
            // 
            OperandComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            OperandComboBox.BackColor = Color.FromArgb(15, 15, 15);
            OperandComboBox.DropDownHeight = 322;
            OperandComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            OperandComboBox.FlatStyle = FlatStyle.Popup;
            OperandComboBox.Font = new Font("Microsoft Sans Serif", 12F);
            OperandComboBox.ForeColor = Color.Linen;
            OperandComboBox.FormattingEnabled = true;
            OperandComboBox.IntegralHeight = false;
            OperandComboBox.Items.AddRange(new object[] { "Entire Descriptor", "Descriptor Title", "Descriptor Type" });
            OperandComboBox.Location = new Point(12, 95);
            OperandComboBox.Name = "OperandComboBox";
            OperandComboBox.Size = new Size(310, 28);
            OperandComboBox.TabIndex = 1;
            // 
            // OperatorComboBox
            // 
            OperatorComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            OperatorComboBox.BackColor = Color.FromArgb(15, 15, 15);
            OperatorComboBox.DropDownHeight = 322;
            OperatorComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            OperatorComboBox.FlatStyle = FlatStyle.Popup;
            OperatorComboBox.Font = new Font("Microsoft Sans Serif", 12F);
            OperatorComboBox.ForeColor = Color.Linen;
            OperatorComboBox.FormattingEnabled = true;
            OperatorComboBox.IntegralHeight = false;
            OperatorComboBox.Items.AddRange(new object[] { "Starts With", "Ends With", "Contains", "Does Not Contain", "Is Equal To", "Is Not Equal To", "Is Less Than", "Is Less Than Or Equal To", "Is Greater Than", "Is Greater Than Or Equal To", "Contains Any Of", "Contains All Of" });
            OperatorComboBox.Location = new Point(12, 160);
            OperatorComboBox.Name = "OperatorComboBox";
            OperatorComboBox.Size = new Size(310, 28);
            OperatorComboBox.TabIndex = 2;
            // 
            // OperandLabel
            // 
            OperandLabel.AutoSize = true;
            OperandLabel.Font = new Font("Verdana", 12F);
            OperandLabel.ForeColor = Color.Linen;
            OperandLabel.Location = new Point(12, 75);
            OperandLabel.Name = "OperandLabel";
            OperandLabel.Size = new Size(84, 18);
            OperandLabel.TabIndex = 9;
            OperandLabel.Text = "Operand:";
            ConditionFormToolTip.SetToolTip(OperandLabel, "Select the item you'd like to compare");
            // 
            // OperatorLabel
            // 
            OperatorLabel.AutoSize = true;
            OperatorLabel.Font = new Font("Verdana", 12F);
            OperatorLabel.ForeColor = Color.Linen;
            OperatorLabel.Location = new Point(12, 140);
            OperatorLabel.Name = "OperatorLabel";
            OperatorLabel.Size = new Size(87, 18);
            OperatorLabel.TabIndex = 10;
            OperatorLabel.Text = "Operator:";
            ConditionFormToolTip.SetToolTip(OperatorLabel, "Choose how you're comparing the selected item");
            // 
            // ValueLabel
            // 
            ValueLabel.AutoSize = true;
            ValueLabel.Font = new Font("Verdana", 12F);
            ValueLabel.ForeColor = Color.Linen;
            ValueLabel.Location = new Point(12, 201);
            ValueLabel.Name = "ValueLabel";
            ValueLabel.Size = new Size(60, 18);
            ValueLabel.TabIndex = 11;
            ValueLabel.Text = "Value:";
            ConditionFormToolTip.SetToolTip(ValueLabel, "Enter the value that wish to compare the Operand with");
            // 
            // RuleNameLabel
            // 
            RuleNameLabel.AutoSize = true;
            RuleNameLabel.Font = new Font("Verdana", 12F);
            RuleNameLabel.ForeColor = Color.Linen;
            RuleNameLabel.Location = new Point(12, 17);
            RuleNameLabel.Name = "RuleNameLabel";
            RuleNameLabel.Size = new Size(62, 18);
            RuleNameLabel.TabIndex = 13;
            RuleNameLabel.Text = "Name:";
            ConditionFormToolTip.SetToolTip(RuleNameLabel, "Enter the desired name for this Condition");
            // 
            // ConditionNameBox
            // 
            ConditionNameBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ConditionNameBox.BackColor = Color.FromArgb(15, 15, 15);
            ConditionNameBox.BorderStyle = BorderStyle.FixedSingle;
            ConditionNameBox.Font = new Font("Microsoft Sans Serif", 12F);
            ConditionNameBox.ForeColor = Color.Linen;
            ConditionNameBox.Location = new Point(12, 38);
            ConditionNameBox.Name = "ConditionNameBox";
            ConditionNameBox.PlaceholderText = "Enter condition name";
            ConditionNameBox.Size = new Size(310, 26);
            ConditionNameBox.TabIndex = 0;
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
            ApplyButton.Location = new Point(242, 282);
            ApplyButton.Name = "ApplyButton";
            ApplyButton.Size = new Size(80, 32);
            ApplyButton.TabIndex = 4;
            ApplyButton.Text = "Apply";
            ConditionFormToolTip.SetToolTip(ApplyButton, "Apply changes");
            ApplyButton.UseVisualStyleBackColor = false;
            ApplyButton.Click += ApplyButton_Click;
            // 
            // OverrideCheckBox
            // 
            OverrideCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            OverrideCheckBox.AutoSize = true;
            OverrideCheckBox.Font = new Font("Verdana", 12F);
            OverrideCheckBox.ForeColor = Color.Linen;
            OverrideCheckBox.Location = new Point(233, 72);
            OverrideCheckBox.Name = "OverrideCheckBox";
            OverrideCheckBox.Size = new Size(89, 22);
            OverrideCheckBox.TabIndex = 15;
            OverrideCheckBox.Text = "Custom";
            OverrideCheckBox.UseVisualStyleBackColor = true;
            OverrideCheckBox.CheckedChanged += OverrideCheckBox_CheckedChanged;
            // 
            // OverrideTextBox
            // 
            OverrideTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            OverrideTextBox.BackColor = Color.FromArgb(15, 15, 15);
            OverrideTextBox.BorderStyle = BorderStyle.FixedSingle;
            OverrideTextBox.Font = new Font("Microsoft Sans Serif", 12F);
            OverrideTextBox.ForeColor = Color.Linen;
            OverrideTextBox.Location = new Point(13, 96);
            OverrideTextBox.Name = "OverrideTextBox";
            OverrideTextBox.PlaceholderText = "Enter custom operand";
            OverrideTextBox.Size = new Size(307, 26);
            OverrideTextBox.TabIndex = 25;
            OverrideTextBox.Visible = false;
            // 
            // ConditionForm
            // 
            AcceptButton = ApplyButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(30, 30, 30);
            ClientSize = new Size(334, 326);
            Controls.Add(OverrideTextBox);
            Controls.Add(OverrideCheckBox);
            Controls.Add(ApplyButton);
            Controls.Add(RuleNameLabel);
            Controls.Add(ConditionNameBox);
            Controls.Add(ValueLabel);
            Controls.Add(OperatorLabel);
            Controls.Add(OperandLabel);
            Controls.Add(OperatorComboBox);
            Controls.Add(OperandComboBox);
            Controls.Add(ValueTextBox);
            MaximizeBox = false;
            MaximumSize = new Size(600, 365);
            MinimumSize = new Size(350, 365);
            Name = "ConditionForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Condition Configuration";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        public TextBox ValueTextBox;
        public Label OperandLabel;
        public Label OperatorLabel;
        public Label ValueLabel;
        public ComboBox OperandComboBox;
        public ComboBox OperatorComboBox;
        public Label RuleNameLabel;
        public TextBox ConditionNameBox;
        private Button ApplyButton;
        private ToolTip ConditionFormToolTip;
        public CheckBox OverrideCheckBox;
        public TextBox OverrideTextBox;
    }
}