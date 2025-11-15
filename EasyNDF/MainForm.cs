using NDFParser;
using NDFParser.AST;
using System.Data;
using System.Reflection.Metadata;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Xml.Linq;
using static EasyNDF.OpenForms;

namespace EasyNDF
{
    /** TO DO:
     * - Export Function (Interface between EasyNDF GUI and NDFParser)
     * - Help Window
     * - Settings Window
     * - Control Tooltips
     * - Rule Presets saved to file on computer (JSON?)
     * - Override feature for ConditionForm/ActionForm ComboBoxes (let user define custom Operands/Targets)
     **/
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {

        }

        private void ImportButton_Click(object sender, EventArgs e)
        {
            try
            {
                string fileName = FileManager.ImportFile().WindowsFile.FileName; // saves file name
                FileNameBox.SelectionStart = fileName.Length;
                FileNameBox.Text = (fileName); 
                MainFormToolTip.SetToolTip(FileNameBox, fileName);
                ImportButton.BackColor = System.Drawing.Color.SteelBlue;
                NewRuleButton.BackColor = System.Drawing.Color.YellowGreen;
                AddPresetButton.BackColor = System.Drawing.Color.YellowGreen;
            }
            catch (Exception)
            {
                // If the user cancels the file dialog, do nothing
            }
        }

        private void RuleListView_SizeChanged(object sender, EventArgs e)
        {
            // Ensure the first column doesn't become longer than the width of the listView in order to prevent the automatic appearance of the horizontal scrollbar
            RuleListView.Columns[0].Width = RuleListView.Width - 50;
        }

        private void NewRuleButton_Click(object? sender, EventArgs e)
        {
            if (FileManager.importedFile.ParsedFile != null)
            {
                OpenForms openForms = new OpenForms();
                RuleEngine.Rule rule = openForms.OpenRuleForm("", [], []);

                if (!string.IsNullOrWhiteSpace(rule.Name))
                {
                    ListViewItem newItem = RuleListView.Items.Add(rule.Name);

                    ListViewItem.ListViewSubItem newItemConditions = newItem.SubItems.Add("Conditions");
                    newItemConditions.Tag = rule.Conditions ?? new[] { RuleEngine.Condition.Empty() };

                    ListViewItem.ListViewSubItem newItemActions = newItem.SubItems.Add("Actions");
                    newItemActions.Tag = rule.Actions ?? new[] { RuleEngine.Action.Empty() };
                }
            }
            else
            {
                MessageBox.Show("Please import an NDF file before creating rules.", "No NDF File Imported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        private void ConfigureRule_ContextClick(object? sender, EventArgs e)
        {
            // Initialize selected item and sub-items into callable variables
            ListViewItem selectedItem = RuleListView.SelectedItems[0];
            var selectedConditions = selectedItem.SubItems[1].Tag as RuleEngine.Condition[] ?? new[] { RuleEngine.Condition.Empty() };
            var selectedActions = selectedItem.SubItems[2].Tag as RuleEngine.Action[] ?? new[] { RuleEngine.Action.Empty() };

            OpenForms openForms = new OpenForms();
            RuleEngine.Rule rule = openForms.OpenRuleForm(selectedItem.Text, selectedConditions, selectedActions);
            // Save the information returned by OpenRuleForm() as long as the rule has a name
            if (!string.IsNullOrWhiteSpace(rule.Name))
            {
                RuleListView.SelectedItems[0].Text = rule.Name;
                RuleListView.SelectedItems[0].SubItems[1].Tag = rule.Conditions ?? new[] { RuleEngine.Condition.Empty() };
                RuleListView.SelectedItems[0].SubItems[2].Tag = rule.Actions ?? new[] { RuleEngine.Action.Empty() };
            }
        }

        public void DuplicateRule_ContextClick(object? sender, EventArgs e)
        {
            // Initialize selected item and sub-items into callable variables
            ListViewItem selectedItem = RuleListView.SelectedItems[0];
            var selectedConditions = selectedItem.SubItems[1].Tag as ListViewItem[] ?? Array.Empty<ListViewItem>();
            var selectedActions = selectedItem.SubItems[2].Tag as ListViewItem[] ?? Array.Empty<ListViewItem>();

            // Duplicate the selected item by finding the number of items and inserting a clone at the next available space
            RuleListView.Items.Insert((RuleListView.Items.Count - 1), (ListViewItem)selectedItem.Clone());

        }

        private void DeleteRule_ContextClick(object? sender, EventArgs e)
        {
            RuleListView.SelectedItems[0].Remove();
        }

        private void DeletePreset_ContextClick(object? sender, EventArgs e)
        {
            PresetComboBox.Items.RemoveAt(PresetComboBox.SelectedIndex);
        }

        private void RuleListView_MouseDown(object sender, MouseEventArgs e)
        {
            RuleListView.ContextMenuStrip = null;
            ContextMenuStrip cm = new ContextMenuStrip();
            if (e.Button == MouseButtons.Right && RuleListView.GetItemAt(e.X, e.Y) != null)
            {
                cm.Items.Add("Configure", null, new EventHandler(ConfigureRule_ContextClick));
                cm.Items.Add("Duplicate", null, new EventHandler(DuplicateRule_ContextClick));
                cm.Items.Add("Delete", null, new EventHandler(DeleteRule_ContextClick));
            }
            if (e.Button == MouseButtons.Right
            && RuleListView.GetItemAt(e.X, e.Y) == null)
            {
                cm.Items.Add("Create New Rule", null, new EventHandler(NewRuleButton_Click));
            }
            RuleListView.ContextMenuStrip = cm;
        }

        private void AddPresetButton_Click(object sender, EventArgs e)
        {
            OpenForms openForms = new OpenForms();
            string presetName = openForms.OpenTextEntryForm("Add Preset", "Enter Preset Name:", "Enter preset name");
            if (!string.IsNullOrWhiteSpace(presetName))
            {
                PresetComboBox.Items.Add(presetName);
            }
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
                    var conditions = item.SubItems[1].Tag as RuleEngine.Condition[] ?? new[] { RuleEngine.Condition.Empty() };
                    var actions = item.SubItems[2].Tag as RuleEngine.Action[] ?? new[] { RuleEngine.Action.Empty() };
                    rules[i] = new RuleEngine.Rule(item.Text, conditions, actions);
                }
                FileManager.ExportFile(rules); // Call ExportFile with the rules array
            }
            else
            {
                MessageBox.Show("Please import an NDF file before exporting.", "No NDF File Imported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
    }
}
