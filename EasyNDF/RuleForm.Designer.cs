namespace EasyNDF
{
    partial class RuleForm
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
            ListViewGroup listViewGroup1 = new ListViewGroup("ListViewGroup", HorizontalAlignment.Left);
            ListViewGroup listViewGroup2 = new ListViewGroup("ListViewGroup", HorizontalAlignment.Left);
            RuleNameBox = new TextBox();
            RuleNameLabel = new Label();
            label1 = new Label();
            label2 = new Label();
            ConditionListView = new ListView();
            ruleNameHeader = new ColumnHeader();
            ActionListView = new ListView();
            columnHeader1 = new ColumnHeader();
            RuleFormToolTip = new ToolTip(components);
            FilterComboBox = new ComboBox();
            MoveConditionUpButton = new Button();
            MoveConditionDownButton = new Button();
            AddConditionButton = new Button();
            AddActionButton = new Button();
            MoveActionDownButton = new Button();
            MoveActionUpButton = new Button();
            ApplyButton = new Button();
            EnableCheckBox = new CheckBox();
            SuspendLayout();
            // 
            // RuleNameBox
            // 
            RuleNameBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            RuleNameBox.BackColor = Color.FromArgb(15, 15, 15);
            RuleNameBox.BorderStyle = BorderStyle.FixedSingle;
            RuleNameBox.Font = new Font("Microsoft Sans Serif", 12F);
            RuleNameBox.ForeColor = Color.Linen;
            RuleNameBox.Location = new Point(11, 38);
            RuleNameBox.Name = "RuleNameBox";
            RuleNameBox.PlaceholderText = "Enter rule name";
            RuleNameBox.Size = new Size(360, 26);
            RuleNameBox.TabIndex = 0;
            // 
            // RuleNameLabel
            // 
            RuleNameLabel.AutoSize = true;
            RuleNameLabel.Font = new Font("Verdana", 12F);
            RuleNameLabel.ForeColor = Color.Linen;
            RuleNameLabel.Location = new Point(11, 16);
            RuleNameLabel.Name = "RuleNameLabel";
            RuleNameLabel.Size = new Size(62, 18);
            RuleNameLabel.TabIndex = 1;
            RuleNameLabel.Text = "Name:";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Verdana", 12F);
            label1.ForeColor = Color.Linen;
            label1.Location = new Point(12, 82);
            label1.Name = "label1";
            label1.Size = new Size(102, 18);
            label1.TabIndex = 12;
            label1.Text = "Conditions:";
            // 
            // label2
            // 
            label2.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            label2.AutoSize = true;
            label2.Font = new Font("Verdana", 12F);
            label2.ForeColor = Color.Linen;
            label2.Location = new Point(12, 378);
            label2.Name = "label2";
            label2.Size = new Size(75, 18);
            label2.TabIndex = 13;
            label2.Text = "Actions:";
            // 
            // ConditionListView
            // 
            ConditionListView.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            ConditionListView.AutoArrange = false;
            ConditionListView.BackColor = Color.FromArgb(15, 15, 15);
            ConditionListView.BorderStyle = BorderStyle.FixedSingle;
            ConditionListView.Columns.AddRange(new ColumnHeader[] { ruleNameHeader });
            ConditionListView.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            ConditionListView.ForeColor = Color.Linen;
            ConditionListView.FullRowSelect = true;
            listViewGroup1.CollapsedState = ListViewGroupCollapsedState.Expanded;
            listViewGroup1.Header = "ListViewGroup";
            listViewGroup1.Name = "listViewGroup1";
            ConditionListView.Groups.AddRange(new ListViewGroup[] { listViewGroup1 });
            ConditionListView.HeaderStyle = ColumnHeaderStyle.None;
            ConditionListView.Location = new Point(11, 106);
            ConditionListView.Name = "ConditionListView";
            ConditionListView.ShowGroups = false;
            ConditionListView.Size = new Size(360, 255);
            ConditionListView.TabIndex = 2;
            ConditionListView.UseCompatibleStateImageBehavior = false;
            ConditionListView.View = View.Details;
            ConditionListView.MouseDown += ConditionListView_MouseDown;
            // 
            // ruleNameHeader
            // 
            ruleNameHeader.Text = "Rules";
            ruleNameHeader.Width = 300;
            // 
            // ActionListView
            // 
            ActionListView.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ActionListView.AutoArrange = false;
            ActionListView.BackColor = Color.FromArgb(15, 15, 15);
            ActionListView.BorderStyle = BorderStyle.FixedSingle;
            ActionListView.Columns.AddRange(new ColumnHeader[] { columnHeader1 });
            ActionListView.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            ActionListView.ForeColor = Color.Linen;
            ActionListView.FullRowSelect = true;
            listViewGroup2.CollapsedState = ListViewGroupCollapsedState.Expanded;
            listViewGroup2.Header = "ListViewGroup";
            listViewGroup2.Name = "listViewGroup1";
            ActionListView.Groups.AddRange(new ListViewGroup[] { listViewGroup2 });
            ActionListView.HeaderStyle = ColumnHeaderStyle.None;
            ActionListView.Location = new Point(11, 402);
            ActionListView.Name = "ActionListView";
            ActionListView.ShowGroups = false;
            ActionListView.Size = new Size(360, 255);
            ActionListView.TabIndex = 4;
            ActionListView.UseCompatibleStateImageBehavior = false;
            ActionListView.View = View.Details;
            ActionListView.MouseDown += ActionListView_MouseDown;
            // 
            // columnHeader1
            // 
            columnHeader1.Text = "Rules";
            columnHeader1.Width = 300;
            // 
            // FilterComboBox
            // 
            FilterComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            FilterComboBox.BackColor = Color.FromArgb(15, 15, 15);
            FilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            FilterComboBox.FlatStyle = FlatStyle.Popup;
            FilterComboBox.Font = new Font("Microsoft Sans Serif", 12F);
            FilterComboBox.ForeColor = Color.Linen;
            FilterComboBox.FormattingEnabled = true;
            FilterComboBox.Items.AddRange(new object[] { "Any", "All" });
            FilterComboBox.Location = new Point(148, 71);
            FilterComboBox.Name = "FilterComboBox";
            FilterComboBox.Size = new Size(115, 28);
            FilterComboBox.TabIndex = 19;
            RuleFormToolTip.SetToolTip(FilterComboBox, "Filter: \r\nAny = Any condition must be met\r\nAll = All conditions must be met");
            // 
            // MoveConditionUpButton
            // 
            MoveConditionUpButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            MoveConditionUpButton.BackColor = Color.SteelBlue;
            MoveConditionUpButton.BackgroundImage = Properties.Resources.buttonBG4_32x32_Tr1;
            MoveConditionUpButton.BackgroundImageLayout = ImageLayout.Stretch;
            MoveConditionUpButton.FlatAppearance.BorderSize = 0;
            MoveConditionUpButton.FlatStyle = FlatStyle.Flat;
            MoveConditionUpButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            MoveConditionUpButton.Location = new Point(269, 69);
            MoveConditionUpButton.Name = "MoveConditionUpButton";
            MoveConditionUpButton.Size = new Size(32, 32);
            MoveConditionUpButton.TabIndex = 20;
            MoveConditionUpButton.Text = "˄";
            RuleFormToolTip.SetToolTip(MoveConditionUpButton, "Move selected condition up");
            MoveConditionUpButton.UseVisualStyleBackColor = false;
            MoveConditionUpButton.Click += MoveConditionUpButton_Click;
            // 
            // MoveConditionDownButton
            // 
            MoveConditionDownButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            MoveConditionDownButton.BackColor = Color.SteelBlue;
            MoveConditionDownButton.BackgroundImage = Properties.Resources.buttonBG4_32x32_Tr1;
            MoveConditionDownButton.BackgroundImageLayout = ImageLayout.Stretch;
            MoveConditionDownButton.FlatAppearance.BorderSize = 0;
            MoveConditionDownButton.FlatStyle = FlatStyle.Flat;
            MoveConditionDownButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            MoveConditionDownButton.Location = new Point(305, 69);
            MoveConditionDownButton.Name = "MoveConditionDownButton";
            MoveConditionDownButton.Size = new Size(32, 32);
            MoveConditionDownButton.TabIndex = 21;
            MoveConditionDownButton.Text = "˅";
            RuleFormToolTip.SetToolTip(MoveConditionDownButton, "Move selected condition up");
            MoveConditionDownButton.UseVisualStyleBackColor = false;
            MoveConditionDownButton.Click += MoveConditionDownButton_Click;
            // 
            // AddConditionButton
            // 
            AddConditionButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AddConditionButton.BackColor = Color.YellowGreen;
            AddConditionButton.BackgroundImage = Properties.Resources.buttonBG4_32x32_Tr1;
            AddConditionButton.BackgroundImageLayout = ImageLayout.Stretch;
            AddConditionButton.FlatAppearance.BorderSize = 0;
            AddConditionButton.FlatStyle = FlatStyle.Flat;
            AddConditionButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            AddConditionButton.Location = new Point(341, 69);
            AddConditionButton.Name = "AddConditionButton";
            AddConditionButton.Size = new Size(32, 32);
            AddConditionButton.TabIndex = 22;
            AddConditionButton.Text = "+";
            RuleFormToolTip.SetToolTip(AddConditionButton, "Add Condition");
            AddConditionButton.UseVisualStyleBackColor = false;
            AddConditionButton.Click += AddConditionButton_Click;
            // 
            // AddActionButton
            // 
            AddActionButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AddActionButton.BackColor = Color.YellowGreen;
            AddActionButton.BackgroundImage = Properties.Resources.buttonBG4_32x32_Tr1;
            AddActionButton.BackgroundImageLayout = ImageLayout.Stretch;
            AddActionButton.FlatAppearance.BorderSize = 0;
            AddActionButton.FlatStyle = FlatStyle.Flat;
            AddActionButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            AddActionButton.Location = new Point(339, 366);
            AddActionButton.Name = "AddActionButton";
            AddActionButton.Size = new Size(32, 32);
            AddActionButton.TabIndex = 25;
            AddActionButton.Text = "+";
            RuleFormToolTip.SetToolTip(AddActionButton, "Add action");
            AddActionButton.UseVisualStyleBackColor = false;
            AddActionButton.Click += AddActionButton_Click;
            // 
            // MoveActionDownButton
            // 
            MoveActionDownButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            MoveActionDownButton.BackColor = Color.SteelBlue;
            MoveActionDownButton.BackgroundImage = Properties.Resources.buttonBG4_32x32_Tr1;
            MoveActionDownButton.BackgroundImageLayout = ImageLayout.Stretch;
            MoveActionDownButton.FlatAppearance.BorderSize = 0;
            MoveActionDownButton.FlatStyle = FlatStyle.Flat;
            MoveActionDownButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            MoveActionDownButton.Location = new Point(303, 366);
            MoveActionDownButton.Name = "MoveActionDownButton";
            MoveActionDownButton.Size = new Size(32, 32);
            MoveActionDownButton.TabIndex = 24;
            MoveActionDownButton.Text = "˅";
            RuleFormToolTip.SetToolTip(MoveActionDownButton, "Move selected action up");
            MoveActionDownButton.UseVisualStyleBackColor = false;
            MoveActionDownButton.Click += MoveActionDownButton_Click;
            // 
            // MoveActionUpButton
            // 
            MoveActionUpButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            MoveActionUpButton.BackColor = Color.SteelBlue;
            MoveActionUpButton.BackgroundImage = Properties.Resources.buttonBG4_32x32_Tr1;
            MoveActionUpButton.BackgroundImageLayout = ImageLayout.Stretch;
            MoveActionUpButton.FlatAppearance.BorderSize = 0;
            MoveActionUpButton.FlatStyle = FlatStyle.Flat;
            MoveActionUpButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            MoveActionUpButton.Location = new Point(266, 366);
            MoveActionUpButton.Name = "MoveActionUpButton";
            MoveActionUpButton.Size = new Size(32, 32);
            MoveActionUpButton.TabIndex = 23;
            MoveActionUpButton.Text = "˄";
            RuleFormToolTip.SetToolTip(MoveActionUpButton, "Move selected action up");
            MoveActionUpButton.UseVisualStyleBackColor = false;
            MoveActionUpButton.Click += MoveActionUpButton_Click;
            // 
            // ApplyButton
            // 
            ApplyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ApplyButton.BackColor = Color.YellowGreen;
            ApplyButton.BackgroundImage = Properties.Resources.buttonBG4_128x32_Tr1;
            ApplyButton.BackgroundImageLayout = ImageLayout.Stretch;
            ApplyButton.FlatAppearance.BorderSize = 0;
            ApplyButton.FlatStyle = FlatStyle.Flat;
            ApplyButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            ApplyButton.Location = new Point(291, 667);
            ApplyButton.Name = "ApplyButton";
            ApplyButton.Size = new Size(80, 32);
            ApplyButton.TabIndex = 26;
            ApplyButton.Text = "Apply";
            RuleFormToolTip.SetToolTip(ApplyButton, "Apply changes");
            ApplyButton.UseVisualStyleBackColor = false;
            ApplyButton.Click += ApplyButton_Click;
            // 
            // EnableCheckBox
            // 
            EnableCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            EnableCheckBox.AutoSize = true;
            EnableCheckBox.Font = new Font("Verdana", 12F);
            EnableCheckBox.ForeColor = Color.Linen;
            EnableCheckBox.Location = new Point(280, 15);
            EnableCheckBox.Name = "EnableCheckBox";
            EnableCheckBox.Size = new Size(92, 22);
            EnableCheckBox.TabIndex = 14;
            EnableCheckBox.Text = "Enabled";
            EnableCheckBox.UseVisualStyleBackColor = true;
            // 
            // RuleForm
            // 
            AcceptButton = ApplyButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(30, 30, 30);
            ClientSize = new Size(384, 711);
            Controls.Add(ApplyButton);
            Controls.Add(AddActionButton);
            Controls.Add(MoveActionDownButton);
            Controls.Add(MoveActionUpButton);
            Controls.Add(AddConditionButton);
            Controls.Add(MoveConditionDownButton);
            Controls.Add(MoveConditionUpButton);
            Controls.Add(FilterComboBox);
            Controls.Add(EnableCheckBox);
            Controls.Add(ActionListView);
            Controls.Add(ConditionListView);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(RuleNameLabel);
            Controls.Add(RuleNameBox);
            MaximizeBox = false;
            MaximumSize = new Size(800, 750);
            MinimumSize = new Size(400, 750);
            Name = "RuleForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Rule Configuration";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        public TextBox RuleNameBox;
        public Label RuleNameLabel;
        public Label label1;
        public Label label2;
        private ColumnHeader ruleNameHeader;
        private ColumnHeader columnHeader1;
        public ListView ConditionListView;
        public ListView ActionListView;
        private ToolTip RuleFormToolTip;
        public CheckBox EnableCheckBox;
        public ComboBox FilterComboBox;
        private Button MoveConditionUpButton;
        private Button MoveConditionDownButton;
        private Button AddConditionButton;
        private Button AddActionButton;
        private Button MoveActionDownButton;
        private Button MoveActionUpButton;
        private Button ApplyButton;
    }
}