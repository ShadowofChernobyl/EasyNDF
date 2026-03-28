namespace EasyNDF
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            ListViewGroup listViewGroup1 = new ListViewGroup("ListViewGroup", HorizontalAlignment.Left);
            NewRuleButton = new Button();
            HelpMenuButton = new Button();
            ImportButton = new Button();
            ExportButton = new Button();
            PresetComboBox = new ComboBox();
            SettingsButton = new Button();
            PresetLabel = new Label();
            SavePresetButton = new Button();
            RuleListView = new ListView();
            ruleNameHeader = new ColumnHeader();
            label1 = new Label();
            FileNameBox = new TextBox();
            MainFormToolTip = new ToolTip(components);
            MoveRuleUpButton = new Button();
            MoveRuleDownButton = new Button();
            SuspendLayout();
            // 
            // NewRuleButton
            // 
            NewRuleButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            NewRuleButton.BackColor = Color.SteelBlue;
            NewRuleButton.BackgroundImage = Properties.Resources.buttonBG4_128x32_Tr1;
            NewRuleButton.BackgroundImageLayout = ImageLayout.Stretch;
            NewRuleButton.FlatAppearance.BorderSize = 0;
            NewRuleButton.FlatStyle = FlatStyle.Flat;
            NewRuleButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            NewRuleButton.Location = new Point(501, 12);
            NewRuleButton.Name = "NewRuleButton";
            NewRuleButton.Size = new Size(109, 32);
            NewRuleButton.TabIndex = 5;
            NewRuleButton.Text = "New Rule";
            MainFormToolTip.SetToolTip(NewRuleButton, " Create new rule");
            NewRuleButton.UseVisualStyleBackColor = false;
            NewRuleButton.Click += NewRuleButton_Click;
            // 
            // HelpMenuButton
            // 
            HelpMenuButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            HelpMenuButton.BackColor = Color.DimGray;
            HelpMenuButton.BackgroundImage = Properties.Resources.iconHelp_32p_Tr;
            HelpMenuButton.BackgroundImageLayout = ImageLayout.Stretch;
            HelpMenuButton.Cursor = Cursors.Hand;
            HelpMenuButton.FlatAppearance.BorderSize = 0;
            HelpMenuButton.FlatAppearance.MouseDownBackColor = Color.White;
            HelpMenuButton.FlatAppearance.MouseOverBackColor = Color.Silver;
            HelpMenuButton.FlatStyle = FlatStyle.Flat;
            HelpMenuButton.Font = new Font("Verdana", 14F, FontStyle.Bold);
            HelpMenuButton.ForeColor = Color.Linen;
            HelpMenuButton.Location = new Point(12, 642);
            HelpMenuButton.Name = "HelpMenuButton";
            HelpMenuButton.Size = new Size(25, 26);
            HelpMenuButton.TabIndex = 4;
            MainFormToolTip.SetToolTip(HelpMenuButton, "Show help");
            HelpMenuButton.UseVisualStyleBackColor = false;
            // 
            // ImportButton
            // 
            ImportButton.BackColor = Color.YellowGreen;
            ImportButton.BackgroundImage = Properties.Resources.buttonBG4_128x32_Tr1;
            ImportButton.BackgroundImageLayout = ImageLayout.Stretch;
            ImportButton.FlatAppearance.BorderSize = 0;
            ImportButton.FlatStyle = FlatStyle.Flat;
            ImportButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            ImportButton.Location = new Point(12, 12);
            ImportButton.Name = "ImportButton";
            ImportButton.Size = new Size(80, 32);
            ImportButton.TabIndex = 1;
            ImportButton.Text = "Import";
            MainFormToolTip.SetToolTip(ImportButton, "Import .ndf file");
            ImportButton.UseVisualStyleBackColor = false;
            ImportButton.Click += ImportButton_Click;
            // 
            // ExportButton
            // 
            ExportButton.BackColor = Color.SteelBlue;
            ExportButton.BackgroundImage = Properties.Resources.buttonBG4_128x32_Tr1;
            ExportButton.BackgroundImageLayout = ImageLayout.Stretch;
            ExportButton.FlatAppearance.BorderSize = 0;
            ExportButton.FlatStyle = FlatStyle.Flat;
            ExportButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            ExportButton.Location = new Point(98, 12);
            ExportButton.Name = "ExportButton";
            ExportButton.Size = new Size(80, 32);
            ExportButton.TabIndex = 2;
            ExportButton.Text = "Export";
            MainFormToolTip.SetToolTip(ExportButton, "Export current modifications to .ndf");
            ExportButton.UseVisualStyleBackColor = false;
            ExportButton.Click += ExportButton_Click;
            // 
            // PresetComboBox
            // 
            PresetComboBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            PresetComboBox.BackColor = Color.FromArgb(15, 15, 15);
            PresetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            PresetComboBox.FlatStyle = FlatStyle.Popup;
            PresetComboBox.Font = new Font("Microsoft Sans Serif", 12F);
            PresetComboBox.ForeColor = Color.Linen;
            PresetComboBox.FormattingEnabled = true;
            PresetComboBox.Location = new Point(12, 708);
            PresetComboBox.Name = "PresetComboBox";
            PresetComboBox.Size = new Size(560, 28);
            PresetComboBox.TabIndex = 9;
            MainFormToolTip.SetToolTip(PresetComboBox, "Right-Click to delete the current preset (permanently)");
            PresetComboBox.SelectedIndexChanged += PresetComboBox_SelectedIndexChanged;
            PresetComboBox.MouseDown += PresetComboBox_MouseDown;
            // 
            // SettingsButton
            // 
            SettingsButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            SettingsButton.BackColor = Color.DimGray;
            SettingsButton.BackgroundImage = Properties.Resources.iconGear_32p_Tr;
            SettingsButton.BackgroundImageLayout = ImageLayout.Stretch;
            SettingsButton.Cursor = Cursors.Hand;
            SettingsButton.FlatAppearance.BorderSize = 0;
            SettingsButton.FlatAppearance.MouseDownBackColor = Color.White;
            SettingsButton.FlatAppearance.MouseOverBackColor = Color.Silver;
            SettingsButton.FlatStyle = FlatStyle.Flat;
            SettingsButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            SettingsButton.Location = new Point(43, 644);
            SettingsButton.Name = "SettingsButton";
            SettingsButton.Size = new Size(25, 26);
            SettingsButton.TabIndex = 3;
            MainFormToolTip.SetToolTip(SettingsButton, "Settings");
            SettingsButton.UseVisualStyleBackColor = false;
            SettingsButton.Click += SettingsButton_Click;
            // 
            // PresetLabel
            // 
            PresetLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            PresetLabel.AutoSize = true;
            PresetLabel.Font = new Font("Verdana", 12F);
            PresetLabel.ForeColor = Color.Linen;
            PresetLabel.Location = new Point(12, 687);
            PresetLabel.Name = "PresetLabel";
            PresetLabel.Size = new Size(118, 18);
            PresetLabel.TabIndex = 8;
            PresetLabel.Text = "Rule Presets:";
            MainFormToolTip.SetToolTip(PresetLabel, resources.GetString("PresetLabel.ToolTip"));
            // 
            // SavePresetButton
            // 
            SavePresetButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            SavePresetButton.BackColor = Color.SteelBlue;
            SavePresetButton.BackgroundImage = Properties.Resources.iconSave3_32p;
            SavePresetButton.BackgroundImageLayout = ImageLayout.Stretch;
            SavePresetButton.FlatAppearance.BorderSize = 0;
            SavePresetButton.FlatStyle = FlatStyle.Flat;
            SavePresetButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            SavePresetButton.Location = new Point(578, 707);
            SavePresetButton.Name = "SavePresetButton";
            SavePresetButton.Size = new Size(32, 32);
            SavePresetButton.TabIndex = 10;
            MainFormToolTip.SetToolTip(SavePresetButton, "Save preset");
            SavePresetButton.UseVisualStyleBackColor = false;
            SavePresetButton.Click += SavePresetButton_Click;
            // 
            // RuleListView
            // 
            RuleListView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            RuleListView.AutoArrange = false;
            RuleListView.BackColor = Color.FromArgb(15, 15, 15);
            RuleListView.BorderStyle = BorderStyle.FixedSingle;
            RuleListView.Columns.AddRange(new ColumnHeader[] { ruleNameHeader });
            RuleListView.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            RuleListView.ForeColor = Color.Linen;
            RuleListView.FullRowSelect = true;
            listViewGroup1.CollapsedState = ListViewGroupCollapsedState.Expanded;
            listViewGroup1.Header = "ListViewGroup";
            listViewGroup1.Name = "listViewGroup1";
            RuleListView.Groups.AddRange(new ListViewGroup[] { listViewGroup1 });
            RuleListView.HeaderStyle = ColumnHeaderStyle.None;
            RuleListView.Location = new Point(12, 77);
            RuleListView.Name = "RuleListView";
            RuleListView.ShowGroups = false;
            RuleListView.Size = new Size(598, 559);
            RuleListView.TabIndex = 6;
            RuleListView.UseCompatibleStateImageBehavior = false;
            RuleListView.View = View.Details;
            RuleListView.SizeChanged += RuleListView_SizeChanged;
            RuleListView.MouseDown += RuleListView_MouseDown;
            // 
            // ruleNameHeader
            // 
            ruleNameHeader.Text = "Rules";
            ruleNameHeader.Width = 300;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Verdana", 12F);
            label1.ForeColor = Color.Linen;
            label1.Location = new Point(12, 56);
            label1.Name = "label1";
            label1.Size = new Size(60, 18);
            label1.TabIndex = 11;
            label1.Text = "Rules:";
            MainFormToolTip.SetToolTip(label1, "Right-click below or press \"New Rule\" to create new rules");
            // 
            // FileNameBox
            // 
            FileNameBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            FileNameBox.BackColor = Color.FromArgb(30, 30, 30);
            FileNameBox.BorderStyle = BorderStyle.FixedSingle;
            FileNameBox.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            FileNameBox.ForeColor = Color.Linen;
            FileNameBox.Location = new Point(76, 48);
            FileNameBox.Name = "FileNameBox";
            FileNameBox.ReadOnly = true;
            FileNameBox.RightToLeft = RightToLeft.Yes;
            FileNameBox.ShortcutsEnabled = false;
            FileNameBox.Size = new Size(534, 26);
            FileNameBox.TabIndex = 13;
            FileNameBox.TabStop = false;
            // 
            // MainFormToolTip
            // 
            MainFormToolTip.AutoPopDelay = 15000;
            MainFormToolTip.InitialDelay = 500;
            MainFormToolTip.ReshowDelay = 100;
            // 
            // MoveRuleUpButton
            // 
            MoveRuleUpButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            MoveRuleUpButton.BackColor = Color.SteelBlue;
            MoveRuleUpButton.BackgroundImage = Properties.Resources.buttonBG4_32x32_Tr1;
            MoveRuleUpButton.BackgroundImageLayout = ImageLayout.Stretch;
            MoveRuleUpButton.FlatAppearance.BorderSize = 0;
            MoveRuleUpButton.FlatStyle = FlatStyle.Flat;
            MoveRuleUpButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            MoveRuleUpButton.Location = new Point(542, 644);
            MoveRuleUpButton.Name = "MoveRuleUpButton";
            MoveRuleUpButton.Size = new Size(32, 32);
            MoveRuleUpButton.TabIndex = 7;
            MoveRuleUpButton.Text = "˄";
            MainFormToolTip.SetToolTip(MoveRuleUpButton, "Move selected item up");
            MoveRuleUpButton.UseVisualStyleBackColor = false;
            MoveRuleUpButton.Click += MoveRuleUpButton_Click;
            // 
            // MoveRuleDownButton
            // 
            MoveRuleDownButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            MoveRuleDownButton.BackColor = Color.SteelBlue;
            MoveRuleDownButton.BackgroundImage = Properties.Resources.buttonBG4_32x32_Tr1;
            MoveRuleDownButton.BackgroundImageLayout = ImageLayout.Stretch;
            MoveRuleDownButton.FlatAppearance.BorderSize = 0;
            MoveRuleDownButton.FlatStyle = FlatStyle.Flat;
            MoveRuleDownButton.Font = new Font("Verdana", 12F, FontStyle.Bold);
            MoveRuleDownButton.Location = new Point(578, 644);
            MoveRuleDownButton.Name = "MoveRuleDownButton";
            MoveRuleDownButton.Size = new Size(32, 32);
            MoveRuleDownButton.TabIndex = 8;
            MoveRuleDownButton.Text = "˅";
            MainFormToolTip.SetToolTip(MoveRuleDownButton, "Move selected item down");
            MoveRuleDownButton.UseVisualStyleBackColor = false;
            MoveRuleDownButton.Click += MoveRuleDownButton_Click;
            // 
            // MainForm
            // 
            AcceptButton = NewRuleButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(30, 30, 30);
            BackgroundImageLayout = ImageLayout.Stretch;
            ClientSize = new Size(622, 748);
            Controls.Add(MoveRuleDownButton);
            Controls.Add(MoveRuleUpButton);
            Controls.Add(FileNameBox);
            Controls.Add(label1);
            Controls.Add(RuleListView);
            Controls.Add(SavePresetButton);
            Controls.Add(PresetLabel);
            Controls.Add(SettingsButton);
            Controls.Add(PresetComboBox);
            Controls.Add(ExportButton);
            Controls.Add(ImportButton);
            Controls.Add(HelpMenuButton);
            Controls.Add(NewRuleButton);
            HelpButton = true;
            MinimumSize = new Size(453, 350);
            Name = "MainForm";
            RightToLeft = RightToLeft.No;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "EasyNDF (Alpha)";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private Button NewRuleButton;
        private Button HelpMenuButton;
        private Button ImportButton;
        private Button ExportButton;
        private ComboBox PresetComboBox;
        private Button SettingsButton;
        private Label PresetLabel;
        private Button SavePresetButton;
        private ListView RuleListView;
        private ColumnHeader ruleNameHeader;
        private Label label1;
        public TextBox FileNameBox;
        private ToolTip MainFormToolTip;
        private Button MoveRuleUpButton;
        private Button MoveRuleDownButton;
    }
}
