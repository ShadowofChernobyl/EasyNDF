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
            components = new System.ComponentModel.Container();
            RuleNameLabel = new Label();
            ActionNameBox = new TextBox();
            ValueLabel = new Label();
            OperatorLabel = new Label();
            TargetLabel = new Label();
            OperatorComboBox = new ComboBox();
            TargetComboBox = new ComboBox();
            ValueTextBox = new TextBox();
            CommentCheckBox = new CheckBox();
            CommentTextBox = new TextBox();
            CommentTimestampCheckBox = new CheckBox();
            CommentOriginalValueCheckBox = new CheckBox();
            ApplyButton = new Button();
            ActionFormToolTip = new ToolTip(components);
            OverrideCheckBox = new CheckBox();
            OverrideTextBox = new TextBox();
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
            ActionFormToolTip.SetToolTip(RuleNameLabel, "Enter the desired name for this Action");
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
            ActionFormToolTip.SetToolTip(ValueLabel, "Enter the value that wish to modify the target with");
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
            ActionFormToolTip.SetToolTip(OperatorLabel, "Choose how you'd like modify the selected item");
            // 
            // TargetLabel
            // 
            TargetLabel.AutoSize = true;
            TargetLabel.Font = new Font("Verdana", 12F);
            TargetLabel.ForeColor = Color.Linen;
            TargetLabel.Location = new Point(13, 75);
            TargetLabel.Name = "TargetLabel";
            TargetLabel.Size = new Size(66, 18);
            TargetLabel.TabIndex = 18;
            TargetLabel.Text = "Target:";
            ActionFormToolTip.SetToolTip(TargetLabel, "Choose the item you'd like to compare");
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
            OperatorComboBox.Items.AddRange(new object[] { "Add [int, float]", "Subtract [int, float]", "Multiply [int, float]", "Divide [int, float]", "Set To [int, float, string, boolean]", "Append [string]", "Prepend [string]", "Insert [array]", "Replace [array]", "Remove [array]" });
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
            // CommentCheckBox
            // 
            CommentCheckBox.AutoSize = true;
            CommentCheckBox.Font = new Font("Verdana", 12F);
            CommentCheckBox.ForeColor = Color.Linen;
            CommentCheckBox.Location = new Point(13, 261);
            CommentCheckBox.Name = "CommentCheckBox";
            CommentCheckBox.Size = new Size(179, 22);
            CommentCheckBox.TabIndex = 4;
            CommentCheckBox.Text = "Append Comment:";
            ActionFormToolTip.SetToolTip(CommentCheckBox, "Append a comment at the modified line");
            CommentCheckBox.UseVisualStyleBackColor = true;
            CommentCheckBox.CheckedChanged += CommentCheckBox_CheckedChanged;
            // 
            // CommentTextBox
            // 
            CommentTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            CommentTextBox.BackColor = Color.FromArgb(15, 15, 15);
            CommentTextBox.BorderStyle = BorderStyle.FixedSingle;
            CommentTextBox.Font = new Font("Microsoft Sans Serif", 12F);
            CommentTextBox.ForeColor = Color.Linen;
            CommentTextBox.Location = new Point(13, 289);
            CommentTextBox.Name = "CommentTextBox";
            CommentTextBox.PlaceholderText = "Enter comment";
            CommentTextBox.Size = new Size(309, 26);
            CommentTextBox.TabIndex = 5;
            // 
            // CommentTimestampCheckBox
            // 
            CommentTimestampCheckBox.AutoSize = true;
            CommentTimestampCheckBox.Font = new Font("Verdana", 12F);
            CommentTimestampCheckBox.ForeColor = Color.Linen;
            CommentTimestampCheckBox.Location = new Point(210, 321);
            CommentTimestampCheckBox.Name = "CommentTimestampCheckBox";
            CommentTimestampCheckBox.Size = new Size(118, 22);
            CommentTimestampCheckBox.TabIndex = 7;
            CommentTimestampCheckBox.Text = "Timestamp";
            ActionFormToolTip.SetToolTip(CommentTimestampCheckBox, "Display the time that the line was modified (UTC)");
            CommentTimestampCheckBox.UseVisualStyleBackColor = true;
            // 
            // CommentOriginalValueCheckBox
            // 
            CommentOriginalValueCheckBox.AutoSize = true;
            CommentOriginalValueCheckBox.Font = new Font("Verdana", 12F);
            CommentOriginalValueCheckBox.ForeColor = Color.Linen;
            CommentOriginalValueCheckBox.Location = new Point(13, 321);
            CommentOriginalValueCheckBox.Name = "CommentOriginalValueCheckBox";
            CommentOriginalValueCheckBox.Size = new Size(191, 22);
            CommentOriginalValueCheckBox.TabIndex = 6;
            CommentOriginalValueCheckBox.Text = "Show Original Value";
            ActionFormToolTip.SetToolTip(CommentOriginalValueCheckBox, "Display the original value within the comment");
            CommentOriginalValueCheckBox.UseVisualStyleBackColor = true;
            // 
            // ApplyButton
            // 
            ApplyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ApplyButton.BackColor = Color.YellowGreen;
            ApplyButton.BackgroundImage = Properties.Resources.buttonBG4_128x32_Tr1;
            ApplyButton.BackgroundImageLayout = ImageLayout.Stretch;
            ApplyButton.DialogResult = DialogResult.OK;
            ApplyButton.FlatAppearance.BorderSize = 0;
            ApplyButton.FlatStyle = FlatStyle.Flat;
            ApplyButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            ApplyButton.Location = new Point(242, 352);
            ApplyButton.Name = "ApplyButton";
            ApplyButton.Size = new Size(80, 32);
            ApplyButton.TabIndex = 8;
            ApplyButton.Text = "Apply";
            ActionFormToolTip.SetToolTip(ApplyButton, "Apply changes");
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
            OverrideCheckBox.TabIndex = 23;
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
            OverrideTextBox.Location = new Point(14, 96);
            OverrideTextBox.Name = "OverrideTextBox";
            OverrideTextBox.PlaceholderText = "Enter custom target";
            OverrideTextBox.Size = new Size(307, 26);
            OverrideTextBox.TabIndex = 24;
            OverrideTextBox.Visible = false;
            // 
            // ActionForm
            // 
            AcceptButton = ApplyButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(30, 30, 30);
            ClientSize = new Size(334, 396);
            Controls.Add(OverrideTextBox);
            Controls.Add(OverrideCheckBox);
            Controls.Add(ApplyButton);
            Controls.Add(CommentOriginalValueCheckBox);
            Controls.Add(CommentTimestampCheckBox);
            Controls.Add(CommentTextBox);
            Controls.Add(CommentCheckBox);
            Controls.Add(RuleNameLabel);
            Controls.Add(ActionNameBox);
            Controls.Add(ValueLabel);
            Controls.Add(OperatorLabel);
            Controls.Add(TargetLabel);
            Controls.Add(OperatorComboBox);
            Controls.Add(TargetComboBox);
            Controls.Add(ValueTextBox);
            MaximizeBox = false;
            MaximumSize = new Size(1000, 435);
            MinimumSize = new Size(350, 345);
            Name = "ActionForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Action Configuration";
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
        public ComboBox OperatorComboBox;
        public ComboBox TargetComboBox;
        public TextBox ValueTextBox;
        public CheckBox CommentCheckBox;
        public TextBox CommentTextBox;
        public CheckBox CommentTimestampCheckBox;
        public CheckBox CommentOriginalValueCheckBox;
        private Button ApplyButton;
        private ToolTip ActionFormToolTip;
        public CheckBox OverrideCheckBox;
        public TextBox OverrideTextBox;
    }
}