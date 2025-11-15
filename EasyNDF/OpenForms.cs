using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using static Antlr4.Runtime.Atn.SemanticContext;
using static EasyNDF.OpenForms;
using static EasyNDF.RuleEngine;
using static System.Windows.Forms.Design.AxImporter;

namespace EasyNDF
{

    internal class OpenForms
    {
        public RuleEngine.Rule OpenRuleForm(string name, RuleEngine.Condition[] conditions, RuleEngine.Action[] actions)
        {
            RuleForm ruleForm = new RuleForm();
            RuleEngine.Rule rule = new RuleEngine.Rule(name, conditions, actions);

            // Populate controls with saved rule information.
            ruleForm.RuleNameBox.Text = name;

            foreach (var condition in conditions) 
            {
                var newItem = ruleForm.ConditionListView.Items.Add(condition.Name);
                newItem.Tag = condition;
                    
            };
            
            foreach (var action in actions) 
            {
                var newItem = ruleForm.ActionListView.Items.Add(action.Name);
                newItem.Tag = action;
            };
            

            DialogResult result = ruleForm.ShowDialog();
            // If user clicks OK, save information; Otherwise, do nothing.
            if (result == DialogResult.OK)
            {
                rule.Name = ruleForm.RuleNameBox.Text;
                rule.Conditions = RuleEngine.Condition.ListViewItemsToConditions(ruleForm.ConditionListView.Items);
                rule.Actions = RuleEngine.Action.ListViewItemsToActions(ruleForm.ActionListView.Items);
            }
            else
            {

            }

            ruleForm.Dispose();
            return rule;



        }

        public RuleEngine.Condition OpenConditionForm(RuleEngine.Condition condition)
        {
            ConditionForm conditionForm = new ConditionForm();
            conditionForm.OperandComboBox.Items.AddRange(FileManager.PopulateOperandList().Cast<string>().ToArray());

            if (condition.Name != null)
                conditionForm.ConditionNameBox.Text = condition.Name;

            if (condition.Operand != null)
                conditionForm.OperandComboBox.Text = condition.Operand;

            if (condition.Operator != null)
                conditionForm.OperatorComboBox.Text = condition.Operator;

            if (condition.Value != null)
                conditionForm.ValueTextBox.Text = condition.Value;

            DialogResult result = conditionForm.ShowDialog();

            if (result == DialogResult.OK) 
            {
                condition.Name = conditionForm.ConditionNameBox.Text;
                condition.Operand = conditionForm.OperandComboBox.Text;
                condition.Operator = conditionForm.OperatorComboBox.Text;
                condition.Value = conditionForm.ValueTextBox.Text;
            }
            else
            {

            }

            conditionForm.Dispose();
            return condition;

            
        }

        public RuleEngine.Action OpenActionForm(RuleEngine.Action action)
        {
            ActionForm actionForm = new ActionForm();
            actionForm.TargetComboBox.Items.AddRange(FileManager.PopulateOperandList().Cast<string>().ToArray());

            if (action.Name != null)
                actionForm.ActionNameBox.Text = action.Name;

            if (action.Target != null)
                actionForm.TargetComboBox.Text = action.Target;

            if (action.Operator != null)
                actionForm.OperatorComboBox.Text = action.Operator;

            if (action.Value != null)
                actionForm.ValueTextBox.Text = action.Value;

            DialogResult result = actionForm.ShowDialog();

            if (result == DialogResult.OK)
            {
                action.Name = actionForm.ActionNameBox.Text;
                action.Target = actionForm.TargetComboBox.Text;
                action.Operator = actionForm.OperatorComboBox.Text;
                action.Value = actionForm.ValueTextBox.Text;
            }
            else
            {

            }

            actionForm.Dispose();
            return action;
        }

        public string OpenSettingsForm()
        {
            SettingsForm settingsForm = new SettingsForm();
            DialogResult result = settingsForm.ShowDialog();
            string commentText = settingsForm.AppendTextBox.Text;
            if (result == DialogResult.OK)
            {

            }
            settingsForm.Dispose();
            return commentText;
        }

        public String OpenTextEntryForm(string windowName, string labelText, string placeHolderText)
        {
            TextEntryForm textEntryForm = new TextEntryForm();
            textEntryForm.Text = windowName;
            textEntryForm.TextEntryLabel.Text = labelText;
            textEntryForm.TextBox.PlaceholderText = placeHolderText;
            DialogResult result = textEntryForm.ShowDialog();
            

            string text = textEntryForm.TextBox.Text;

            if (result == DialogResult.OK)
            {
                textEntryForm.Dispose();
                return text;
            }
            else
            {
                textEntryForm.Dispose();
                return "";
            }
        }
    }
}
