using NDFParser;
using NDFParser.AST;
using System.Data;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Xml.Linq;
using static EasyNDF.FileManager;
using static EasyNDF.OpenForms;
using static EasyNDF.RuleEngine;

namespace EasyNDF
{
    /* TO DO:
     * - [P3] Implement feature via RuleEngine.Contains() that allows user to remove specific values/propterties in an array/object
     * - [P4] Override feature for ConditionForm/ActionForm ComboBoxes (let user define custom Operands/Targets)
     * - [P4] Allow for copying/pasting of multiple rules/conditions/actions at once
     * - [P5] Make the filepath label look better (maybe ellipsize the middle?)
     * - [P5] Settings Window
     * - [P5] Control Tooltips
     * - [P5] Help Windows for each form
     * - [P5] In the GitHub description, include "What this mod is" and "What this mod is not"
     */
    public partial class MainForm : Form
    {
        public const string EmptyPresetLabel = "<None>"; // Label (constant) for the default empty preset option

        public MainForm()
        {
            InitializeComponent();
            RuleListView.KeyDown += RuleListView_KeyDown; // Enable keyboard shortcuts for RuleListView
            RuleListView.MouseDoubleClick += ConfigureRule_ContextClick; // Double-clicking a rule opens the Configure context menu action
        }

        #region Control Functions
        private void SavePresetButton_Click(object sender, EventArgs e)
        {
            OpenForms openForms = new OpenForms();

            if (FileManager.importedFile.ParsedFile == null) // Ensure an NDF file has been imported before saving preset
            { MessageBox.Show("Please import an NDF file before saving a preset.", "No NDF File Imported", MessageBoxButtons.OK, MessageBoxIcon.Warning);  return; }

            string presetName = openForms.OpenTextEntryForm("Save Preset", "Enter Preset Name:", PresetComboBox.Text, "Enter preset name", true).TrimEnd(' ', '.');

            if (string.IsNullOrWhiteSpace(presetName)) // Only proceed if the preset name is not empty
            { return; }

            SavePreset(presetName, PresetComboBox.Items.Contains(presetName)); // Call SavePreset with overwrite = true if the preset already exists

        }
        private void SettingsButton_Click(object sender, EventArgs e)
        {
            OpenForms openForms = new OpenForms();
            openForms.OpenSettingsForm();
        }
        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (FileManager.importedFile.ParsedFile != null) // Ensure an NDF file has been imported before exporting
            {
                RuleEngine.Rule[] rules = new RuleEngine.Rule[RuleListView.Items.Count];
                for (int i = 0; i < RuleListView.Items.Count; i++) // For each item in the RuleListView, create a RuleEngine.Rule object and add it to the rules array
                {
                    var item = RuleListView.Items[i];
                    rules[i] = item.Tag is RuleEngine.Rule r ? r : RuleEngine.Rule.Empty();
                }
                FileManager.ExportFile(rules); // Call ExportFile with the rules array

                if (FileManager.exportedFile.WindowsFile == null) // If no file was selected, do nothing
                { return; }

                MessageBox.Show("NDF file exported successfully.", "Export Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Please import an NDF file before exporting.", "No NDF File Imported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        private void NewRuleButton_Click(object? sender, EventArgs e)
        {
            if (FileManager.importedFile.ParsedFile != null)
            {
                OpenForms openForms = new OpenForms();
                RuleEngine.Rule rule = openForms.OpenRuleForm(RuleEngine.Rule.Empty());

                if (!string.IsNullOrWhiteSpace(rule.Name))
                {
                    ListViewItem newItem = RuleListView.Items.Add(rule.Name);
                    newItem.Tag = rule;

                    /*
                    ListViewItem newItem = RuleListView.Items.Add(rule.Name);
                    ListViewItem.ListViewSubItem newItemConditions = newItem.SubItems.Add("Conditions");
                    newItemConditions.Tag = rule.Conditions ?? new[] { RuleEngine.Condition.Empty() };
                    ListViewItem.ListViewSubItem newItemActions = newItem.SubItems.Add("Actions");
                    newItemActions.Tag = rule.Actions ?? new[] { RuleEngine.Action.Empty() };
                    */
                }
            }
            else
            {
                MessageBox.Show("Please import an NDF file before creating rules.", "No NDF File Imported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RefreshRuleListView();
        }
        private void ImportButton_Click(object sender, EventArgs e)
        {
            FileManager.NDF_File importedFile = FileManager.ImportFile();
            if (importedFile.WindowsFile == null) // If no file was selected, do nothing
            { return; }

            string fileName = importedFile.WindowsFile.FileName;  

            FileNameBox.Text = (fileName);
            FileNameBox.SelectionStart = fileName.Length; // Move cursor to end of text
            MainFormToolTip.SetToolTip(FileNameBox, fileName);
            ImportButton.BackColor = System.Drawing.Color.SteelBlue;
            NewRuleButton.BackColor = System.Drawing.Color.YellowGreen;
            SavePresetButton.BackColor = System.Drawing.Color.YellowGreen;
            RefreshPresetComboBox("ImportingFile", EmptyPresetLabel);

        }
        private void MoveRuleUpButton_Click(object sender, EventArgs e)
        {
            MoveRuleUp_ContextClick(sender, e);
        }
        private void MoveRuleDownButton_Click(object sender, EventArgs e)
        {
            MoveRuleDown_ContextClick(sender, e);
        }
        private void RuleListView_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up: // Move Rule Up or Ensure Selection
                    if (e.Control)
                    { MoveRuleUp_ContextClick(sender, e); e.Handled = true; }
                    else
                    { EnsureRuleSelection(RuleListView.Items); }
                    break;

                case Keys.Down: // Move Rule Down or Ensure Selection
                    if (e.Control)
                    { MoveRuleDown_ContextClick(sender, e); e.Handled = true; }
                    else
                    { EnsureRuleSelection(RuleListView.Items); }
                    break;

                case Keys.C: // Copy Rule
                    if (e.Control)
                    { CopyRule_ContextClick(sender, e); e.Handled = true; }
                    break;

                case Keys.V: // Paste Rule
                    if (e.Control)
                    { PasteRule_ContextClick(sender, e); e.Handled = true; }
                    break;

                case Keys.Delete: // Delete Rule
                    DeleteRule_ContextClick(sender, e); e.Handled = true; break;

                default: break;
            }
        }
        #endregion

        #region Context Menu Functions
        private void RuleListView_MouseDown(object sender, MouseEventArgs e)
        {
            RuleListView.ContextMenuStrip = null;
            ContextMenuStrip cm = new ContextMenuStrip();
            ListViewItem clickedItem = RuleListView.GetItemAt(e.X, e.Y)!;

            if (e.Button == MouseButtons.Right && clickedItem != null)
            {
                cm.Items.Add("Configure", null, new EventHandler(ConfigureRule_ContextClick));
                cm.Items.Add("Duplicate", null, new EventHandler(DuplicateRule_ContextClick));
                cm.Items.Add("Move Up", null, new EventHandler(MoveRuleUp_ContextClick));
                cm.Items.Add("Move Down", null, new EventHandler(MoveRuleDown_ContextClick));
                cm.Items.Add("Copy", null, new EventHandler(CopyRule_ContextClick));
                try
                {
                    switch (((RuleEngine.Rule)clickedItem.Tag!).Enabled) // Toggle Enable/Disable based on current state
                    {
                        case true: cm.Items.Add("Disable", null, new EventHandler(ToggleRule_ContextClick)); break;
                        case false: cm.Items.Add("Enable", null, new EventHandler(ToggleRule_ContextClick)); break;
                    }
                }
                catch (Exception) { } // Ignore errors when no item is selected
                cm.Items.Add("Delete", null, new EventHandler(DeleteRule_ContextClick));
            }
            if (e.Button == MouseButtons.Right
            && RuleListView.GetItemAt(e.X, e.Y) == null)
            {
                cm.Items.Add("Create New Rule", null, new EventHandler(NewRuleButton_Click));
                try
                {
                    if (JsonSerializer.Deserialize<RuleEngine.Rule>(Clipboard.GetText()).Name != ""
                     && FileManager.importedFile.ParsedFile != null)
                    {
                        cm.Items.Add("Paste", null, new EventHandler(PasteRule_ContextClick));
                    }
                }
                catch (Exception) { } // If the clipboard does not contain a valid Rule JSON, do nothing
            }

            RuleListView.ContextMenuStrip = cm;
        }
        private void PresetComboBox_MouseDown(object sender, MouseEventArgs e)
        {
            PresetComboBox.ContextMenuStrip = null;
            ContextMenuStrip cm = new ContextMenuStrip();
            if (e.Button == MouseButtons.Right && PresetComboBox.SelectedIndex != -1)
            {
                cm.Items.Add("Delete Preset", null, new EventHandler(DeletePreset_ContextClick));
            }
            PresetComboBox.ContextMenuStrip = cm;

        }
        private void ConfigureRule_ContextClick(object? sender, EventArgs e)
        {
            ListViewItem selectedItem = RuleListView.SelectedItems[0];
            /*
            var selectedConditions = selectedItem.SubItems[1].Tag as RuleEngine.Condition[] ?? new[] { RuleEngine.Condition.Empty() };
            var selectedActions = selectedItem.SubItems[2].Tag as RuleEngine.Action[] ?? new[] { RuleEngine.Action.Empty() };
            */
            OpenForms openForms = new OpenForms();
            RuleEngine.Rule rule = openForms.OpenRuleForm((RuleEngine.Rule)selectedItem.Tag!);
            // Save the information returned by OpenRuleForm() as long as the rule has a name
            if (!string.IsNullOrWhiteSpace(rule.Name))
            {
                RuleListView.SelectedItems[0].Text = rule.Name;
                RuleListView.SelectedItems[0].Tag = rule;
            }
            RefreshRuleListView();
        }
        private void DuplicateRule_ContextClick(object? sender, EventArgs e)
        {
            ListViewItem selectedItem = RuleListView.SelectedItems[0];
            RuleListView.Items.Insert((RuleListView.Items.Count - 1), (ListViewItem)selectedItem.Clone());
            RefreshRuleListView();

        }
        private void ToggleRule_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                RuleEngine.Rule rule = (RuleEngine.Rule)RuleListView.SelectedItems[0].Tag!;
                rule.Enabled = !rule.Enabled;
                RuleListView.SelectedItems[0].Tag = rule;
                RefreshRuleListView();
            }
            catch (Exception)
            { } // Ignore errors when no item is selected
        }
        private void DeleteRule_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                DialogResult dialogResult = MessageBox.Show("Are you sure you want to delete the Rule '" + RuleListView.SelectedItems[0].Text + "'?", "Confirm Action Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dialogResult == DialogResult.Yes)
                {
                    RuleListView.SelectedItems[0].Remove();
                }
            }
            catch (Exception)
            { } // Ignore errors when no item is selected
        }
        private void CopyRule_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                RuleEngine ruleEngine = new RuleEngine();
                Clipboard.SetText(ruleEngine.ConvertItemToJSON(RuleListView.SelectedItems[0]));
            }
            catch (Exception)
            { } // Ignore errors when no item is selected
        }
        private void PasteRule_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                RuleEngine ruleEngine = new RuleEngine();
                ListViewItem item = ruleEngine.ConvertJSONToItem(Clipboard.GetText());
                if (item.Tag is RuleEngine.Rule)
                { RuleListView.Items.Add(item); }
                else
                {
                    MessageBox.Show($"Copied item data type did not match destination data type.",
                                   "Failed to paste",
                                   MessageBoxButtons.OK,
                                   MessageBoxIcon.Error);
                }
            }
            catch (Exception)
            { } // Ignore errors when clipboard does not contain valid Rule JSON
            RefreshRuleListView();
        }
        private void DeletePreset_ContextClick(object? sender, EventArgs e)
        {
            // Ask the user to confirm deletion of the preset
            DialogResult dialogResult = MessageBox.Show("Are you sure you want to delete the preset '" + PresetComboBox.Text + "'?\nThis action is permanent and cannot be undone.", "Confirm Preset Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dialogResult == DialogResult.Yes)
            {
                FileManager.DeletePresetFile(PresetComboBox.Text);
                RefreshPresetComboBox("DeletePreset", EmptyPresetLabel);
            }
        }
        private void MoveRuleUp_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                ListViewItem selectedItem = RuleListView.SelectedItems[0];
                int selectedIndex = selectedItem.Index;
                if (selectedIndex > 0)
                {
                    RuleListView.Items.RemoveAt(selectedIndex);
                    RuleListView.Items.Insert(selectedIndex - 1, selectedItem);
                }
            }
            catch (Exception)
            {
                // Ignore errors when trying to move the top item up
            }
        }
        private void MoveRuleDown_ContextClick(object? sender, EventArgs e)
        {
            try
            {
                ListViewItem selectedItem = RuleListView.SelectedItems[0];
                int selectedIndex = selectedItem.Index;
                if (selectedIndex < RuleListView.Items.Count - 1)
                {
                    RuleListView.Items.RemoveAt(selectedIndex);
                    RuleListView.Items.Insert(selectedIndex + 1, selectedItem);
                }
            }
            catch (Exception)
            {
                // Ignore errors when trying to move the top item up
            }
        }
        #endregion

        #region Automatic Functions
        private void RuleListView_SizeChanged(object sender, EventArgs e)
        {
            // Ensure the first column doesn't become longer than the width of the listView in order to prevent the automatic appearance of the horizontal scrollbar
            RuleListView.Columns[0].Width = RuleListView.Width - 50;
        }
        private void PresetComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (PresetComboBox.Text == EmptyPresetLabel) // If the selected preset is the default empty option then just empty the RuleListView
            { RuleListView.Items.Clear(); return; }

            if (FileManager.importedFile.ParsedFile != null) // Only load preset if an NDF file has been imported
            {
                RuleListView.Items.Clear();
                foreach (var (rule, i) in FileManager.LoadPresetFile(PresetComboBox.Text).Select((value, index) => (value, index)))
                {
                    ListViewItem newItem = RuleListView.Items.Add(rule.Name);
                    newItem.Tag = rule;
                }
            }
            RefreshRuleListView();
        }
        private void SavePreset(string presetName, Boolean overwrite)
        {
            RuleEngine.Rule[] rules = new RuleEngine.Rule[RuleListView.Items.Count];
            switch (overwrite)
            {
                case true:
                    DialogResult dialogResult = MessageBox.Show("A preset with this name already exists. Do you want to overwrite it?", "Preset Already Exists", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (dialogResult == DialogResult.Yes)
                    { 
                        for (int i = 0; i < RuleListView.Items.Count; i++) // For each item in the RuleListView, create a RuleEngine.Rule object and add it to the rules array
                        {
                            ListViewItem currentItem = RuleListView.Items[i];
                            rules[i] = currentItem.Tag is RuleEngine.Rule r ? r : RuleEngine.Rule.Empty();
                        }

                        FileManager.SavePresetFile(presetName, rules);
                        RefreshPresetComboBox("OverwritePreset", presetName);
                        MessageBox.Show("Preset overwritten successfully.", "Operation Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    { MessageBox.Show("The preset was not overwritten", "Operation Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                    break;
                    
                case false:
                    for (int i = 0; i < RuleListView.Items.Count; i++)
                    {
                        ListViewItem currentItem = RuleListView.Items[i];
                        rules[i] = currentItem.Tag is RuleEngine.Rule r ? r : RuleEngine.Rule.Empty();
                    }

                    FileManager.SavePresetFile(presetName, rules);
                    RefreshPresetComboBox("NewPreset", presetName);
                    break;
            }   
        }
        private void RefreshPresetComboBox(string mode, string newPresetName)
        {
            var presets = FileManager.FetchPresetList().Cast<string>().ToList();
            var selectedPreset = PresetComboBox.Text;
            // Take a snapshot copy of the current items (don't hold a reference to the live collection)
            var itemsSnapshot = RuleListView.Items.Cast<ListViewItem>().ToArray();

            switch (mode)
            {
                case "NewPreset" or "OverwritePreset":
                    PresetComboBox.Items.Clear();
                    PresetComboBox.Items.Add(EmptyPresetLabel); // Add a default empty option
                    PresetComboBox.Items.AddRange(presets.ToArray());
                    PresetComboBox.SelectedItem = newPresetName; // Switch to the newly saved preset
                    break;
                case "DeletePreset":
                    PresetComboBox.Items.Clear();
                    PresetComboBox.Items.Add(EmptyPresetLabel); // Add a default empty option
                    PresetComboBox.Items.AddRange(presets.ToArray());
                    PresetComboBox.SelectedIndex = 0; // Switch to the default empty option
                    RuleListView.Items.AddRange(itemsSnapshot); // Restore the previous rules
                    break;
                case "ImportingFile":
                    PresetComboBox.Items.Clear();
                    PresetComboBox.Items.Add(EmptyPresetLabel); // Add a default empty option
                    PresetComboBox.Items.AddRange(presets.ToArray());
                    PresetComboBox.Text = selectedPreset ?? EmptyPresetLabel; // Maintain the previously selected preset, or switch to default if it no longer exists
                    RuleListView.Items.Clear();
                    RuleListView.Items.AddRange(itemsSnapshot); // Restore the previous rules
                    break;
                default:
                    MessageBox.Show("Invalid mode passed to MainForm.RefreshPresetComboBox().", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;

            }
            /** // Old version of RefreshPresetComboBox() before switch statement refactor, which caused some issues with maintaining selected preset and rule list when importing files and deleting presets. Keeping it here for reference in case of future issues with the new version.
            PresetComboBox.Items.Clear();
            PresetComboBox.Items.Add(EmptyPresetLabel); // Add a default empty option
            PresetComboBox.Items.AddRange(presets.ToArray());

            if (maintainSelectedPreset)
                PresetComboBox.Text = selectedPreset;

            if (PresetComboBox.SelectedIndex == -1) // If the previously selected preset no longer exists
            {
                PresetComboBox.SelectedIndex = 0; // Select the default empty option
                if (maintainRuleList)
                    RuleListView.Items.AddRange(itemsSnapshot);
            }**/
        }
        private void EnsureRuleSelection(ListView.ListViewItemCollection items)
        {
            try
            { items[RuleListView.SelectedIndices[0]].Focused = true; } // Refocus the selected item because pressing Up/Down removes focus
            catch (Exception)
            { } // Ignore errors when no item is selected

        }
        private void RefreshRuleListView() 
        {
            var rules = new RuleEngine.Rule[RuleListView.Items.Count];
            foreach (var (item, i) in RuleListView.Items.Cast<ListViewItem>().Select((value, index) => (value, index)))
            { rules[i] = item.Tag is RuleEngine.Rule r ? r : RuleEngine.Rule.Empty(); } // Create an array of RuleEngine.Rule from the ListViewItems

            RuleListView.Items.Clear(); // Clear the ListView before repopulating it

            foreach (var (rule, i) in rules.Select((value, index) => (value, index)))
            {
                ListViewItem newItem = RuleListView.Items.Add(rule.Name);
                if (rule.Enabled == false)
                { newItem.ForeColor = System.Drawing.Color.Gray; }
                newItem.Tag = rule;
            }
        }
        #endregion
    }
}
