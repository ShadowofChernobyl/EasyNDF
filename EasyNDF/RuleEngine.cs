using NDFParser;
using NDFParser.AST;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static EasyNDF.FileManager;
using static EasyNDF.RuleEngine;

namespace EasyNDF
{
    public class RuleEngine
    {
        #region DataStructures
        public struct Preset
        {
            public Preset(string presetName, Rule[] rules)
            {
                PresetName = presetName;
                Rules = rules;
            }

            public string PresetName { get; set; }
            public Rule[] Rules { get; set; }
        }
        public struct Rule
        {
            public Rule(string name, Condition[] conditions, Action[] actions)
            {
                Name = name;
                Conditions = conditions;
                Actions = actions;
            }

            public string Name { get; set; }
            public Condition[] Conditions { get; set; } = new[] { Condition.Empty() };
            public Action[] Actions { get; set; } = new[] { Action.Empty() };
            public string LogicGate { get; set; } = "Any";
            public string DataTypeToString { get; set; } = "Rule";
            public bool Enabled { get; set; } = true;
            public static Rule Empty() => new Rule("", new[] { Condition.Empty() }, new[] { Action.Empty() });
        }
        public struct Condition
        {
            public Condition(string name, string operand, string @operator, string value)
            {
                Name = name;
                Operand = operand;
                Operator = @operator;
                Value = value;
            }

            public string Name { get; set; } // The name of the condition, which can be used for organizational purposes but doesn't affect execution
            public string Operand { get; set; } // The property or aspect of the descriptor that the condition will evaluate, such as "Descriptor Title", "Descriptor Type", or a specific property name from the descriptor's properties list
            public string Operator { get; set; } // The type of comparison to perform between the descriptor's operand value and the condition's value, such as "Starts With", "Ends With", etc.
            public string Value { get; set; } // The value to compare against the descriptor's operand value using the specified operator
            public Boolean ManualOverride { get; set; } = false; // Boolean to track if the user has entered a custom operand that isn't in the predefined list
            public string CustomOperand { get; set; } = ""; // If ManualOverride is true, this property can store the user-entered operand to be used in condition evaluation instead of the predefined options
            public string DataTypeToString { get; set;  } = "Condition"; // This property is used to identify the type of this struct when it's stored in a ListViewItem's Tag as an IValue, since ListViewItems can store any type of object in their Tag property. By checking this DataTypeToString property, the code can determine whether the Tag contains a Condition, Action, or Comment when converting back from ListViewItems to these structs.
            public static Condition Empty() => new Condition("", "", "", ""); // Static method to create an empty Condition with default values
            public static Condition[] ListViewItemsToConditions(ListView.ListViewItemCollection items)
            {
                var list = new List<Condition>();
                foreach (ListViewItem item in items)
                {
                    if (item?.Tag is Condition taggedCondition)
                    {
                        // If the UI stored the original Condition in Tag, reuse it
                        list.Add(taggedCondition);
                    }
                    else
                    {
                        // Otherwise construct a minimal Condition from the item's text to avoid invalid cast exceptions
                        list.Add(new Condition(item?.Text ?? "", "", "", ""));
                    }
                }
                return list.ToArray();
            }

        }
        public struct Action
        {
            public Action(string name, string target, string @operator, string value)
            {
                Name = name;
                Target = target;
                Operator = @operator;
                Value = value;
            }

            public string Name { get; set; } // The name of the action, which can be used for organizational purposes but doesn't affect execution
            public string Target { get; set; } // The property of the descriptor that the action will modify, such as a specific property name from the descriptor's properties list. [For "Set To" operator, this can also be a new property name to add to the descriptor if it doesn't already exist. (GPT Comment; Need to Fact Check)]
            public string Operator { get; set; } // The type of modification to perform on the descriptor's target property using the specified value, such as "Add", "Subtract", "Multiply", etc.
            public string Value { get; set; } // The value to use in the modification of the descriptor's target property according to the specified operator.
            public Boolean ManualOverride { get; set; } = false; // Boolean to track if the user has entered a custom target that isn't in the predefined list
            public string CustomTarget { get; set; } = ""; // If ManualOverride is true, this property can store the user-entered target to be used in action execution instead of the predefined options
            public Comment Comment { get; set; } = Comment.Empty(); // Optional comment to append to the descriptor property being modified, which can include user-entered text as well as options to include a timestamp and/or the original value before modification
            public string DataTypeToString { get; set; } = "Action"; // This property is used to identify the type of this struct when it's stored in a ListViewItem's Tag as an IValue, since ListViewItems can store any type of object in their Tag property. By checking this DataTypeToString property, the code can determine whether the Tag contains an Action, Condition, or Comment when converting back from ListViewItems to these structs.
            public static Action Empty() => new Action("", "", "", ""); // Static method to create an empty Action with default values

            public static Action[] ListViewItemsToActions(ListView.ListViewItemCollection items)
            {
                var list = new List<Action>();
                foreach (ListViewItem item in items)
                {
                    if (item?.Tag is Action taggedAction)
                    {
                        // If the UI stored the original Action in Tag, reuse it
                        list.Add(taggedAction);
                    }
                    else
                    {
                        // Otherwise construct a minimal Action from the item's text to avoid invalid cast exceptions
                        list.Add(new Action(item?.Text ?? "", "", "", ""));
                    }
                }
                return list.ToArray();
            }
        }
        public struct Comment
        {
            public Comment(string text)
            {
                Text = text;
            }
            public string Text { get; set; }
            public bool Enabled { get; set; } = false;
            public bool IncludeTimestamp { get; set; } = false;
            public bool IncludeOriginalValue { get; set; } = false;
            public string DataTypeToString { get; set; } = "Comment";
            public static Comment Empty() => new Comment("");
        }
        #endregion

        #region Conditions
        public Boolean StartsWith(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand) {
                case "Descriptor Title":
                    if (descriptor.Name.StartsWith(condition.Value)) { return true; } break;
                    
                case "Descriptor Type":
                    if (descriptor.Type.StartsWith(condition.Value)) { return true; } break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                        switch (property.Item2) // Recursion
                        {
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = objectValue.Properties };
                                if (StartsWith(nestedDescriptor, condition))
                                    return true;
                                break;
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ArrayValue arrayValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (StartsWith(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside StructValues are handled recursively
                            case StructValue structValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (StartsWith(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside PairValues are handled recursively
                            case PairValue pairValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                                if (StartsWith(nestedDescriptor, condition) == true)
                                    return true;

                                break;
                        }

                        if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                        { if (property.Item1 != condition.Operand) continue; }
                        else // If the user is using a custom operand, compare against the user-entered operand instead
                        { if (property.Item1 != condition.CustomOperand) continue; }

                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (propertyValue.ToString()!.StartsWith(condition.Value)) { return true; }
                    }
                break;
            }

            return false; // Default to false if no match found
        }
        public Boolean EndsWith(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Descriptor Title":
                    if (descriptor.Name.EndsWith(condition.Value)) { return true; }
                    break;

                case "Descriptor Type":
                    if (descriptor.Type.EndsWith(condition.Value)) { return true; }
                    break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                        switch (property.Item2) // Recursion
                        {
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = objectValue.Properties };
                                if (EndsWith(nestedDescriptor, condition))
                                    return true;
                                break;
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ArrayValue arrayValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (EndsWith(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside StructValues are handled recursively
                            case StructValue structValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (EndsWith(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside PairValues are handled recursively
                            case PairValue pairValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                                if (EndsWith(nestedDescriptor, condition) == true)
                                    return true;

                                break;
                        }

                        if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                        { if (property.Item1 != condition.Operand) continue; }
                        else // If the user is using a custom operand, compare against the user-entered operand instead
                        { if (property.Item1 != condition.CustomOperand) continue; }

                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (propertyValue.ToString()!.EndsWith(condition.Value)) { return true; }
                    }
                break;
            }

            return false; // Default to false if no match found
        }
        public Boolean Contains(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Entire Descriptor":
                    if (descriptor.ParsedVersion.ToString().Contains(condition.Value)) { return true; } break;

                case "Descriptor Title":
                    if (descriptor.Name.Contains(condition.Value)) { return true; } break;

                case "Descriptor Type":
                    if (descriptor.Type.Contains(condition.Value)) { return true; } break;
                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                        switch (property.Item2) // Recursion
                        {
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = objectValue.Properties };
                                if (Contains(nestedDescriptor, condition))
                                    return true;
                                break;
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ArrayValue arrayValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (Contains(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside StructValues are handled recursively
                            case StructValue structValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (Contains(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside PairValues are handled recursively
                            case PairValue pairValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                                if (Contains(nestedDescriptor, condition) == true)
                                    return true;

                                break;
                        }

                        if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                        { if (property.Item1 != condition.Operand) continue; }
                        else // If the user is using a custom operand, compare against the user-entered operand instead
                        { if (property.Item1 != condition.CustomOperand) continue; }

                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (propertyValue.ToString()!.Contains(condition.Value)) { return true; }
                    }
                break;
            }

            return false; // Default to false if no match found
        }
        public Boolean DoesNotContain(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Entire Descriptor":
                    if (!descriptor.ParsedVersion.Contains(condition.Value)) { return true; }
                    break;

                case "Descriptor Title":
                    if (!descriptor.Name.Contains(condition.Value)) { return true; }
                    break;

                case "Descriptor Type":
                    if (!descriptor.Type.Contains(condition.Value)) { return true; }
                    break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                        switch (property.Item2) // Recursion
                        {
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = objectValue.Properties };
                                if (DoesNotContain(nestedDescriptor, condition))
                                    return true;
                                break;
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ArrayValue arrayValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (DoesNotContain(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside StructValues are handled recursively
                            case StructValue structValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (DoesNotContain(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside PairValues are handled recursively
                            case PairValue pairValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                                if (DoesNotContain(nestedDescriptor, condition) == true)
                                    return true;

                            break;
                        }

                        if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                        { if (property.Item1 != condition.Operand) continue; }
                        else // If the user is using a custom operand, compare against the user-entered operand instead
                        { if (property.Item1 != condition.CustomOperand) continue; }

                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (!propertyValue.ToString()!.Contains(condition.Value)) { return true; }
                    }
                break;
            }

            return false; // Default to false if no match found
        }
        public Boolean IsEqualTo(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Descriptor Title":
                    if (descriptor.Name.Equals(condition.Value)) { return true; }
                    break;

                case "Descriptor Type":
                    if (descriptor.Type.Equals(condition.Value)) { return true; }
                    break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                        switch (property.Item2) // Recursion
                        {
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = objectValue.Properties };
                                if (IsEqualTo(nestedDescriptor, condition))
                                    return true;
                                break;
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ArrayValue arrayValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (IsEqualTo(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside StructValues are handled recursively
                            case StructValue structValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (IsEqualTo(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside PairValues are handled recursively
                            case PairValue pairValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                                if (IsEqualTo(nestedDescriptor, condition) == true)
                                    return true;

                                break;
                        }
                            
                        if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                        { if (property.Item1 != condition.Operand) continue; }
                        else // If the user is using a custom operand, compare against the user-entered operand instead
                        { if (property.Item1 != condition.CustomOperand) continue; }

                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (propertyValue.ToString()!.Equals(condition.Value)) { return true; }
                        
                    }
                break;
            }

            return false; // Default to false if no match found
        }
        public Boolean IsNotEqualTo(FileManager.Descriptor descriptor, Condition condition)
        {
            switch (condition.Operand)
            {
                case "Descriptor Title":
                    if (!descriptor.Name.Equals(condition.Value)) { return true; }
                    break;

                case "Descriptor Type":
                    if (!descriptor.Type.Equals(condition.Value)) { return true; }
                    break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                        switch (property.Item2) // Recursion
                        {
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = objectValue.Properties };
                                if (IsNotEqualTo(nestedDescriptor, condition))
                                    return true;
                                break;
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ArrayValue arrayValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (IsNotEqualTo(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside StructValues are handled recursively
                            case StructValue structValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                if (IsNotEqualTo(nestedDescriptor, condition) == true)
                                    return true;
                                break;
                            // Ensure ObjectValues inside PairValues are handled recursively
                            case PairValue pairValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                                if (IsNotEqualTo(nestedDescriptor, condition) == true)
                                    return true;
                                break;  
                        }

                        if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                        { if (property.Item1 != condition.Operand) continue; }
                        else // If the user is using a custom operand, compare against the user-entered operand instead
                        { if (property.Item1 != condition.CustomOperand) continue; }

                        var propertyName = property.Item1;
                        var propertyValue = property.Item2;

                        if (!propertyValue.ToString()!.Equals(condition.Value)) { return true; }
                    }
                    break;
            }

            return false; // Default to false if no match found
        }
        public Boolean IsLessThan(FileManager.Descriptor descriptor, Condition condition)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                switch (property.Item2) // Recursion
                {
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = objectValue.Properties };
                        if (IsLessThan(nestedDescriptor, condition))
                            return true;
                        break;
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ArrayValue arrayValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (IsLessThan(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside StructValues are handled recursively
                    case StructValue structValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (IsLessThan(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside PairValues are handled recursively
                    case PairValue pairValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                        if (IsLessThan(nestedDescriptor, condition) == true)
                            return true;
                    break;
                }


                if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                { if (property.Item1 != condition.Operand) continue; }
                else // If the user is using a custom operand, compare against the user-entered operand instead
                { if (property.Item1 != condition.CustomOperand) continue; }

                var propertyName = property.Item1;
                var propertyValue = property.Item2;

                if (Convert.ToDouble(propertyValue) < Convert.ToDouble(condition.Value)) { return true; }

            }
            return false; // Default to false if no match found
        }
        public Boolean IsLessThanOrEqualTo(FileManager.Descriptor descriptor, Condition condition)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                switch (property.Item2) // Recursion
                {
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = objectValue.Properties };
                        if (IsLessThanOrEqualTo(nestedDescriptor, condition))
                            return true;
                        break;
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ArrayValue arrayValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (IsLessThanOrEqualTo(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside StructValues are handled recursively
                    case StructValue structValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (IsLessThanOrEqualTo(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside PairValues are handled recursively
                    case PairValue pairValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                        if (IsLessThanOrEqualTo(nestedDescriptor, condition) == true)
                            return true;

                        break;
                        // For primitive values, perform the comparison directly
                }

                if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                { if (property.Item1 != condition.Operand) continue; }
                else // If the user is using a custom operand, compare against the user-entered operand instead
                { if (property.Item1 != condition.CustomOperand) continue; }

                var propertyName = property.Item1;
                var propertyValue = property.Item2;

                if (Convert.ToDouble(propertyValue) <= Convert.ToDouble(condition.Value)) { return true; }
            }

            return false; // Default to false if no match found
        }
        public Boolean IsGreaterThan(FileManager.Descriptor descriptor, Condition condition)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                switch (property.Item2) // Recursion
                {
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = objectValue.Properties };
                        if (IsGreaterThan(nestedDescriptor, condition))
                            return true;
                        break;
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ArrayValue arrayValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (IsGreaterThan(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside StructValues are handled recursively
                    case StructValue structValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (IsGreaterThan(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside PairValues are handled recursively
                    case PairValue pairValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                        if (IsGreaterThan(nestedDescriptor, condition) == true)
                            return true;

                        break;
                        // For primitive values, perform the comparison directly
                }

                if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                { if (property.Item1 != condition.Operand) continue; }
                else // If the user is using a custom operand, compare against the user-entered operand instead
                { if (property.Item1 != condition.CustomOperand) continue; }

                var propertyName = property.Item1;
                var propertyValue = property.Item2;

                if (Convert.ToDouble(propertyValue) > Convert.ToDouble(condition.Value)) { return true; }
            }
            return false; // Default to false if no match found
        }
        public Boolean IsGreaterThanOrEqualTo(FileManager.Descriptor descriptor, Condition condition)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index))) 
            {
                var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                switch (property.Item2) // Recursion
                {
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = objectValue.Properties };
                        if (IsGreaterThanOrEqualTo(nestedDescriptor, condition))
                            return true;
                        break;
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ArrayValue arrayValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (IsGreaterThanOrEqualTo(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside StructValues are handled recursively
                    case StructValue structValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (IsGreaterThanOrEqualTo(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside PairValues are handled recursively
                    case PairValue pairValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                        if (IsGreaterThanOrEqualTo(nestedDescriptor, condition) == true)
                            return true;

                        break;
                        // For primitive values, perform the comparison directly
                }

                if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                { if (property.Item1 != condition.Operand) continue; }
                else // If the user is using a custom operand, compare against the user-entered operand instead
                { if (property.Item1 != condition.CustomOperand) continue; }

                var propertyName = property.Item1;
                var propertyValue = property.Item2;

                if (Convert.ToDouble(propertyValue) >= Convert.ToDouble(condition.Value)) { return true; }
            }

            return false; // Default to false if no match found
        }
        public Boolean ContainsAnyOf(FileManager.Descriptor descriptor, Condition condition) // This operator checks if the target property contains at least one of the comma-separated values specified in condition.Value
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                switch (property.Item2) // Recursion
                {
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = objectValue.Properties };
                        if (ContainsAnyOf(nestedDescriptor, condition))
                            return true;
                        break;
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ArrayValue arrayValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (ContainsAnyOf(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside StructValues are handled recursively
                    case StructValue structValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (ContainsAnyOf(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside PairValues are handled recursively
                    case PairValue pairValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                        if (ContainsAnyOf(nestedDescriptor, condition) == true)
                            return true;

                        break;
                        // For primitive values, perform the comparison directly
                }


                if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                { if (property.Item1 != condition.Operand) continue; }
                else // If the user is using a custom operand, compare against the user-entered operand instead
                { if (property.Item1 != condition.CustomOperand) continue; }

                var propertyName = property.Item1;
                var propertyValue = property.Item2; 

                foreach (var value in propertyValue.ToString()!.Split(',').Select(v => v.Trim()))
                { if (propertyValue.ToString()!.Contains(value)) { return true; }
                }
            }

            return false; // Default to false if no match found
        }
        public Boolean ContainsAllOf(FileManager.Descriptor descriptor, Condition condition) // This operator checks if the target property contains all of the comma-separated values specified in condition.Value, regardless of order or extra values in the property. For example, if condition.Value is "Red, Green" then a property value of "Green, Red, Blue" would satisfy this condition because it contains both "Red" and "Green", even though it also contains "Blue" and the order is different.
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                switch (property.Item2) // Recursion
                {
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = objectValue.Properties };
                        if (ContainsAllOf(nestedDescriptor, condition))
                            return true;
                        break;
                    // Ensure ObjectValues inside ArrayValues are handled recursively
                    case ArrayValue arrayValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (ContainsAllOf(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside StructValues are handled recursively
                    case StructValue structValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        if (ContainsAllOf(nestedDescriptor, condition) == true)
                            return true;
                        break;
                    // Ensure ObjectValues inside PairValues are handled recursively
                    case PairValue pairValue:
                        nestedDescriptor = new FileManager.Descriptor
                        { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                        if (ContainsAllOf(nestedDescriptor, condition) == true)
                            return true;

                        break;
                }

                if (condition.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                { if (property.Item1 != condition.Operand) continue; }
                else // If the user is using a custom operand, compare against the user-entered operand instead
                { if (property.Item1 != condition.CustomOperand) continue; }

                var propertyName = property.Item1;
                var propertyValue = property.Item2;

                var values = condition.Value.Split(',').Select(v => v.Trim());
                if (values.All(v => propertyValue.ToString()!.Contains(v))) { return true; }
            }

            return false; // Default to false if no match found
        } 
        #endregion

        #region Actions
        public Descriptor Add(FileManager.Descriptor descriptor, Action action)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                bool handled = false;
                switch (property.Item2)
                {
                    case ObjectValue objectValue:
                        var nestedObj = new FileManager.Descriptor { Properties = objectValue.Properties };
                        nestedObj = Multiply(nestedObj, action);
                        descriptor.Properties[i] = (property.Item1, new ObjectValue(objectValue.Type, nestedObj.Properties));
                        handled = true;
                        break;

                    case ArrayValue arrayValue:
                        var nestedArr = new FileManager.Descriptor { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedArr = Multiply(nestedArr, action);
                        descriptor.Properties[i] = (property.Item1, new ArrayValue(nestedArr.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case StructValue structValue:
                        var nestedStruct = new FileManager.Descriptor { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedStruct = Multiply(nestedStruct, action);
                        descriptor.Properties[i] = (property.Item1, new StructValue(structValue.Type, nestedStruct.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case PairValue pairValue:
                        if (action.ManualOverride == false)
                        { if (Writer.WriteToString(pairValue.Value1) != action.Target) break; }
                        else
                        { if (Writer.WriteToString(pairValue.Value1) != action.CustomTarget) break; }

                        var originalValue = ((NumericLiteral)pairValue.Value2).Value;
                        descriptor.Properties[i] = (property.Item1, new PairValue(
                            pairValue.Value1,
                            new StringLiteral((Convert.ToDouble(((NumericLiteral)pairValue.Value2).Value) * Convert.ToDouble(action.Value)).ToString() + BuildComment(action, originalValue))
                        ));
                        handled = true;
                        break;
                }

                if (handled) continue;

                // Continue if not the target property
                if (action.ManualOverride == false)
                { if (property.Item1 != action.Target) continue; }
                else
                { if (property.Item1 != action.CustomTarget) continue; }

                // Perform Action on primitive
                var originalPrimitive = ((NumericLiteral)property.Item2).Value;
                descriptor.Properties[i] = (property.Item1, new StringLiteral((Convert.ToDouble(((NumericLiteral)property.Item2).Value) + Convert.ToDouble(action.Value)).ToString() + BuildComment(action, originalPrimitive)));
            }
            return descriptor;
        }
        public Descriptor Subtract(FileManager.Descriptor descriptor, Action action)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                bool handled = false;
                switch (property.Item2)
                {
                    case ObjectValue objectValue:
                        var nestedObj = new FileManager.Descriptor { Properties = objectValue.Properties };
                        nestedObj = Multiply(nestedObj, action);
                        descriptor.Properties[i] = (property.Item1, new ObjectValue(objectValue.Type, nestedObj.Properties));
                        handled = true;
                        break;

                    case ArrayValue arrayValue:
                        var nestedArr = new FileManager.Descriptor { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedArr = Multiply(nestedArr, action);
                        descriptor.Properties[i] = (property.Item1, new ArrayValue(nestedArr.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case StructValue structValue:
                        var nestedStruct = new FileManager.Descriptor { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedStruct = Multiply(nestedStruct, action);
                        descriptor.Properties[i] = (property.Item1, new StructValue(structValue.Type, nestedStruct.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case PairValue pairValue:
                        if (action.ManualOverride == false)
                        { if (Writer.WriteToString(pairValue.Value1) != action.Target) break; }
                        else
                        { if (Writer.WriteToString(pairValue.Value1) != action.CustomTarget) break; }

                        var originalValue = ((NumericLiteral)pairValue.Value2).Value;
                        descriptor.Properties[i] = (property.Item1, new PairValue(
                            pairValue.Value1,
                            new StringLiteral((Convert.ToDouble(((NumericLiteral)pairValue.Value2).Value) * Convert.ToDouble(action.Value)).ToString() + BuildComment(action, originalValue))
                        ));
                        handled = true;
                        break;
                }

                if (handled) continue;

                // Continue if not the target property
                if (action.ManualOverride == false)
                { if (property.Item1 != action.Target) continue; }
                else
                { if (property.Item1 != action.CustomTarget) continue; }

                // Perform Action on primitive
                var originalPrimitive = ((NumericLiteral)property.Item2).Value;
                descriptor.Properties[i] = (property.Item1, new StringLiteral((Convert.ToDouble(((NumericLiteral)property.Item2).Value) - Convert.ToDouble(action.Value)).ToString() + BuildComment(action, originalPrimitive)));
            }
            return descriptor;
        }
        public Descriptor Multiply(FileManager.Descriptor descriptor, Action action)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                bool handled = false;
                switch (property.Item2)
                {
                    case ObjectValue objectValue:
                        var nestedObj = new FileManager.Descriptor { Properties = objectValue.Properties };
                        nestedObj = Multiply(nestedObj, action);
                        descriptor.Properties[i] = (property.Item1, new ObjectValue(objectValue.Type, nestedObj.Properties));
                        handled = true;
                        break;

                    case ArrayValue arrayValue:
                        var nestedArr = new FileManager.Descriptor { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedArr = Multiply(nestedArr, action);
                        descriptor.Properties[i] = (property.Item1, new ArrayValue(nestedArr.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case StructValue structValue:
                        var nestedStruct = new FileManager.Descriptor { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedStruct = Multiply(nestedStruct, action);
                        descriptor.Properties[i] = (property.Item1, new StructValue(structValue.Type, nestedStruct.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case PairValue pairValue:
                        if (action.ManualOverride == false)
                        { if (Writer.WriteToString(pairValue.Value1) != action.Target) break; }
                        else
                        { if (Writer.WriteToString(pairValue.Value1) != action.CustomTarget) break; }

                        var originalValue = ((NumericLiteral)pairValue.Value2).Value;
                        descriptor.Properties[i] = (property.Item1, new PairValue(
                            pairValue.Value1,
                            new StringLiteral((Convert.ToDouble(((NumericLiteral)pairValue.Value2).Value) * Convert.ToDouble(action.Value)).ToString() + BuildComment(action, originalValue))
                        ));
                        handled = true;
                        break;
                }
                
                if (handled) continue;

                // Continue if not the target property
                if (action.ManualOverride == false)
                { if (property.Item1 != action.Target) continue; }
                else
                { if (property.Item1 != action.CustomTarget) continue; }

                // Perform Action on primitive
                var originalPrimitive = ((NumericLiteral)property.Item2).Value;
                descriptor.Properties[i] = (property.Item1, new StringLiteral((Convert.ToDouble(((NumericLiteral)property.Item2).Value) * Convert.ToDouble(action.Value)).ToString() + BuildComment(action, originalPrimitive)));
            }
            return descriptor;
        }
        public Descriptor Divide(FileManager.Descriptor descriptor, Action action)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                bool handled = false;
                switch (property.Item2)
                {
                    case ObjectValue objectValue:
                        var nestedObj = new FileManager.Descriptor { Properties = objectValue.Properties };
                        nestedObj = Multiply(nestedObj, action);
                        descriptor.Properties[i] = (property.Item1, new ObjectValue(objectValue.Type, nestedObj.Properties));
                        handled = true;
                        break;

                    case ArrayValue arrayValue:
                        var nestedArr = new FileManager.Descriptor { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedArr = Multiply(nestedArr, action);
                        descriptor.Properties[i] = (property.Item1, new ArrayValue(nestedArr.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case StructValue structValue:
                        var nestedStruct = new FileManager.Descriptor { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedStruct = Multiply(nestedStruct, action);
                        descriptor.Properties[i] = (property.Item1, new StructValue(structValue.Type, nestedStruct.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case PairValue pairValue:
                        if (action.ManualOverride == false)
                        { if (Writer.WriteToString(pairValue.Value1) != action.Target) break; }
                        else
                        { if (Writer.WriteToString(pairValue.Value1) != action.CustomTarget) break; }

                        var originalValue = ((NumericLiteral)pairValue.Value2).Value;
                        descriptor.Properties[i] = (property.Item1, new PairValue(
                            pairValue.Value1,
                            new StringLiteral((Convert.ToDouble(((NumericLiteral)pairValue.Value2).Value) * Convert.ToDouble(action.Value)).ToString() + BuildComment(action, originalValue))
                        ));
                        handled = true;
                        break;
                }

                if (handled) continue;

                // Continue if not the target property
                if (action.ManualOverride == false)
                { if (property.Item1 != action.Target) continue; }
                else
                { if (property.Item1 != action.CustomTarget) continue; }

                // Perform Action on primitive
                var originalPrimitive = ((NumericLiteral)property.Item2).Value;
                descriptor.Properties[i] = (property.Item1, new StringLiteral((Convert.ToDouble(((NumericLiteral)property.Item2).Value) / Convert.ToDouble(action.Value)).ToString() + BuildComment(action, originalPrimitive)));
            }
            return descriptor;
        }
        public Descriptor SetTo(FileManager.Descriptor descriptor, Action action)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                bool handled = false;
                switch (property.Item2)
                {
                    case ObjectValue objectValue:
                        var nestedObj = new FileManager.Descriptor { Properties = objectValue.Properties };
                        nestedObj = SetTo(nestedObj, action);
                        descriptor.Properties[i] = (property.Item1, new ObjectValue(objectValue.Type, nestedObj.Properties));
                        handled = true;
                        break;

                    case ArrayValue arrayValue:
                        var nestedArr = new FileManager.Descriptor { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedArr = SetTo(nestedArr, action);
                        descriptor.Properties[i] = (property.Item1, new ArrayValue(nestedArr.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case StructValue structValue:
                        var nestedStruct = new FileManager.Descriptor { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedStruct = SetTo(nestedStruct, action);
                        descriptor.Properties[i] = (property.Item1, new StructValue(structValue.Type, nestedStruct.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case PairValue pairValue:
                        // If action targets the readable key (Value1) operate directly on Value2 (like Multiply does)
                        if (action.ManualOverride == false)
                        {
                            if (Writer.WriteToString(pairValue.Value1) == action.Target)
                            {
                                var processedValue = HandleNewlines(action.Value);
                                descriptor.Properties[i] = (property.Item1, new PairValue(pairValue.Value1, new StringLiteral(processedValue + BuildComment(action, Writer.WriteToString(pairValue.Value2)))));
                                handled = true;
                                break;
                            }
                        }
                        else
                        {
                            if (Writer.WriteToString(pairValue.Value1) == action.CustomTarget)
                            {
                                var processedValue = HandleNewlines(action.Value);
                                descriptor.Properties[i] = (property.Item1, new PairValue(pairValue.Value1, new StringLiteral(processedValue + BuildComment(action, Writer.WriteToString(pairValue.Value2)))));
                                handled = true;
                                break;
                            }
                        }

                        // Fallback: allow nested operations targeting "Value1"/"Value2"
                        var nestedPair = new FileManager.Descriptor { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                        nestedPair = SetTo(nestedPair, action);
                        descriptor.Properties[i] = (property.Item1, new PairValue(nestedPair.Properties[0].Item2, nestedPair.Properties[1].Item2));
                        handled = true;
                        break;
                }

                if (handled) continue;

                // Continue if not the target property
                if (action.ManualOverride == false)
                { if (property.Item1 != action.Target) continue; }
                else
                { if (property.Item1 != action.CustomTarget) continue; }

                var processed = HandleNewlines(action.Value);
                descriptor.Properties[i] = (property.Item1, new StringLiteral(processed + BuildComment(action, Writer.WriteToString(property.Item2))));
            }
            return descriptor;
        }
        public Descriptor Insert(FileManager.Descriptor descriptor, Action action)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                bool handled = false;
                switch (property.Item2)
                {
                    case ObjectValue objectValue:
                        var nestedObj = new FileManager.Descriptor { Properties = objectValue.Properties };
                        nestedObj = Insert(nestedObj, action);
                        descriptor.Properties[i] = (property.Item1, new ObjectValue(objectValue.Type, nestedObj.Properties));
                        handled = true;
                        break;

                    case ArrayValue arrayValue:
                        var nestedArr = new FileManager.Descriptor { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedArr = Insert(nestedArr, action);
                        descriptor.Properties[i] = (property.Item1, new ArrayValue(nestedArr.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case StructValue structValue:
                        var nestedStruct = new FileManager.Descriptor { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedStruct = Insert(nestedStruct, action);
                        descriptor.Properties[i] = (property.Item1, new StructValue(structValue.Type, nestedStruct.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case PairValue pairValue:
                        var nestedPair = new FileManager.Descriptor { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                        nestedPair = Insert(nestedPair, action);
                        descriptor.Properties[i] = (property.Item1, new PairValue(nestedPair.Properties[0].Item2, nestedPair.Properties[1].Item2));
                        handled = true;
                        break;
                }

                if (handled) continue;

                // Continue if not the target property
                if (action.ManualOverride == false)
                { if (property.Item1 != action.Target) continue; }
                else
                { if (property.Item1 != action.CustomTarget) continue; }

                string[] splitString = action.Value.Split(new char[] { ':' }, 2);
                if (splitString.Length < 2) return descriptor; // invalid input, no change

                string indexToken = splitString[0].Trim();
                string stringValue = splitString[1];

                // Expand "/n" token into literal newline characters for the inserted string element
                stringValue = HandleNewlines(stringValue);

                if (property.Item2 is not ArrayValue array) return descriptor; // not an array -> no-op
                var currentArray = array.Values.ToList();
                int length = currentArray.Count;
                int index;

                if (indexToken.Equals("LastIndex", StringComparison.OrdinalIgnoreCase))
                { index = length; } // append to end
                else if (indexToken.StartsWith("LastIndex", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = indexToken.Substring("LastIndex".Length).Trim();
                    if (!int.TryParse(suffix, out int offset)) return descriptor;
                    index = length + offset; // relative to "after the last element"
                }
                else if (indexToken.Equals("FirstIndex", StringComparison.OrdinalIgnoreCase))
                { index = 0; }
                else if (indexToken.StartsWith("FirstIndex", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = indexToken.Substring("FirstIndex".Length).Trim();
                    if (!int.TryParse(suffix, out int offset)) return descriptor;
                    index = 0 + offset;
                }
                else
                { if (!int.TryParse(indexToken, out index)) return descriptor; } // numeric index - validate parse

                if (index < 0 || index > currentArray.Count) return descriptor;

                var originalArray = Writer.WriteToString(new ArrayValue(currentArray.ToArray())).ReplaceLineEndings("").Replace(" ", "");

                currentArray.Insert(index, new StringLiteral(stringValue)); // Insert the new value at the specified index

                descriptor.Properties[i] = (property.Item1, new StringLiteral(Writer.WriteToString(new ArrayValue(currentArray.ToArray())).ReplaceLineEndings("").Replace(" ", "") + BuildComment(action, originalArray)));
            }
            return descriptor;
        }
        public Descriptor Replace(FileManager.Descriptor descriptor, Action action)
        {
            foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
            {
                bool handled = false;
                switch (property.Item2)
                {
                    case ObjectValue objectValue:
                        var nestedObj = new FileManager.Descriptor { Properties = objectValue.Properties };
                        nestedObj = Replace(nestedObj, action);
                        descriptor.Properties[i] = (property.Item1, new ObjectValue(objectValue.Type, nestedObj.Properties));
                        handled = true;
                        break;

                    case ArrayValue arrayValue:
                        // If the action is explicitly targeting this array property (e.g. action.Target == "Salves")
                        // then perform the index-based replacement directly on this ArrayValue.
                        if ((action.ManualOverride == false && property.Item1 == action.Target) ||
                            (action.ManualOverride == true && property.Item1 == action.CustomTarget))
                        {
                            string[] splitStringTop = action.Value.Split(new char[] { ':' }, 2);
                            if (splitStringTop.Length < 2) return descriptor; // invalid input, no change

                            string indexTokenTop = splitStringTop[0].Trim();
                            string stringValueTop = splitStringTop[1];

                            // Expand "/n" token into literal newline characters for the replacement string
                            stringValueTop = HandleNewlines(stringValueTop);

                            var currentArrayTop = arrayValue.Values.ToList();
                            int lengthTop = currentArrayTop.Count;
                            int indexTop;

                            if (indexTokenTop.Equals("LastIndex", StringComparison.OrdinalIgnoreCase))
                            { indexTop = lengthTop - 1; }
                            else if (indexTokenTop.StartsWith("LastIndex", StringComparison.OrdinalIgnoreCase))
                            {
                                var suffix = indexTokenTop.Substring("LastIndex".Length).Trim();
                                if (!int.TryParse(suffix, out int offset)) return descriptor;
                                indexTop = (lengthTop - 1) + offset;
                            }
                            else if (indexTokenTop.Equals("FirstIndex", StringComparison.OrdinalIgnoreCase))
                            { indexTop = 0; }
                            else if (indexTokenTop.StartsWith("FirstIndex", StringComparison.OrdinalIgnoreCase))
                            {
                                var suffix = indexTokenTop.Substring("FirstIndex".Length).Trim();
                                if (!int.TryParse(suffix, out int offset)) return descriptor;
                                indexTop = 0 + offset;
                            }
                            else
                            { indexTop = Convert.ToInt32(indexTokenTop); } // numeric index

                            if (indexTop < 0 || indexTop >= currentArrayTop.Count) return descriptor;

                            var originalArrayTop = Writer.WriteToString(new ArrayValue(currentArrayTop.ToArray())).ReplaceLineEndings("").Replace(" ", "");

                            currentArrayTop[indexTop] = new StringLiteral(stringValueTop);

                            descriptor.Properties[i] = (property.Item1, new StringLiteral(Writer.WriteToString(new ArrayValue(currentArrayTop.ToArray())).ReplaceLineEndings("").Replace(" ", "") + BuildComment(action, originalArrayTop)));
                            handled = true;
                            break;
                        }
                        else
                        {
                            // Not targeting this array by name — recurse into elements as before
                            var nestedArr = new FileManager.Descriptor { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                            nestedArr = Replace(nestedArr, action);
                            descriptor.Properties[i] = (property.Item1, new ArrayValue(nestedArr.Properties.Select(p => p.Item2).ToArray()));
                            handled = true;
                            break;
                        }

                    case StructValue structValue:
                        var nestedStruct = new FileManager.Descriptor { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                        nestedStruct = Replace(nestedStruct, action);
                        descriptor.Properties[i] = (property.Item1, new StructValue(structValue.Type, nestedStruct.Properties.Select(p => p.Item2).ToArray()));
                        handled = true;
                        break;

                    case PairValue pairValue:
                        var nestedPair = new FileManager.Descriptor { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                        nestedPair = Replace(nestedPair, action);
                        descriptor.Properties[i] = (property.Item1, new PairValue(nestedPair.Properties[0].Item2, nestedPair.Properties[1].Item2));
                        handled = true;
                        break;
                }

                if (handled) continue;

                // Continue if not the target property
                if (action.ManualOverride == false)
                { if (property.Item1 != action.Target) continue; }
                else
                { if (property.Item1 != action.CustomTarget) continue; }

                string[] splitString = action.Value.Split(new char[] { ':' }, 2);
                if (splitString.Length < 2) return descriptor; // invalid input, no change

                string indexToken = splitString[0].Trim();
                string stringValue = splitString[1];

                // Expand "/n" token into literal newline characters for the replacement string
                stringValue = HandleNewlines(stringValue);

                if (property.Item2 is not ArrayValue array) return descriptor; // If the target property isn't an array, we can't replace an indexed element - no-op.

                var currentArray = ((ArrayValue)property.Item2).Values.ToList();
                int length = currentArray.Count;
                int index;

                if (indexToken.Equals("LastIndex", StringComparison.OrdinalIgnoreCase))
                { index = length - 1; }
                else if (indexToken.StartsWith("LastIndex", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = indexToken.Substring("LastIndex".Length).Trim();
                    if (!int.TryParse(suffix, out int offset)) return descriptor;
                    index = (length - 1) + offset;
                }
                else if (indexToken.Equals("FirstIndex", StringComparison.OrdinalIgnoreCase))
                { index = 0; }
                else if (indexToken.StartsWith("FirstIndex", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = indexToken.Substring("FirstIndex".Length).Trim();
                    if (!int.TryParse(suffix, out int offset)) return descriptor;
                    index = 0 + offset;
                }
                else
                { index = Convert.ToInt32(indexToken); } // numeric index

                if (index < 0 || index >= currentArray.Count) return descriptor;

                var originalArray = Writer.WriteToString(new ArrayValue(currentArray.ToArray())).ReplaceLineEndings("").Replace(" ", "");

                currentArray[index] = new StringLiteral(stringValue);

                descriptor.Properties[i] = (property.Item1, new StringLiteral(Writer.WriteToString(new ArrayValue(currentArray.ToArray())).ReplaceLineEndings("").Replace(" ", "") + BuildComment(action, originalArray)));
            }
            return descriptor;
        }
        public Descriptor Append(FileManager.Descriptor descriptor, Action action)
        {
            switch (action.Target)
            {
                case "Descriptor Title":
                    descriptor.Name = (descriptor.Name + HandleNewlines(action.Value)); break;

                case "Descriptor Type":
                    descriptor.Type = (descriptor.Type + HandleNewlines(action.Value)); break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        bool handled = false;
                        switch (property.Item2)
                        {
                            case ObjectValue objectValue:
                                var nestedObj = new FileManager.Descriptor { Properties = objectValue.Properties };
                                nestedObj = Append(nestedObj, action);
                                descriptor.Properties[i] = (property.Item1, new ObjectValue(objectValue.Type, nestedObj.Properties));
                                handled = true;
                                break;
                            case ArrayValue arrayValue:
                                var nestedArr = new FileManager.Descriptor { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                nestedArr = Append(nestedArr, action);
                                descriptor.Properties[i] = (property.Item1, new ArrayValue(nestedArr.Properties.Select(p => p.Item2).ToArray()));
                                handled = true;
                                break;
                            case StructValue structValue:
                                var nestedStruct = new FileManager.Descriptor { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                nestedStruct = Append(nestedStruct, action);
                                descriptor.Properties[i] = (property.Item1, new StructValue(structValue.Type, nestedStruct.Properties.Select(p => p.Item2).ToArray()));
                                handled = true;
                                break;
                            case PairValue pairValue:
                                var nestedPair = new FileManager.Descriptor { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                                nestedPair = Append(nestedPair, action);
                                descriptor.Properties[i] = (property.Item1, new PairValue(nestedPair.Properties[0].Item2, nestedPair.Properties[1].Item2));
                                handled = true;
                                break;
                        }

                        if (handled) continue;

                        // Continue if not the target property
                        if (action.ManualOverride == false)
                        { if (property.Item1 != action.Target) continue; }
                        else
                        { if (property.Item1 != action.CustomTarget) continue; }

                        descriptor.Properties[i] = (property.Item1, new StringLiteral(Writer.WriteToString(property.Item2) + HandleNewlines(action.Value) + BuildComment(action, Writer.WriteToString(property.Item2))));
                    }
                    break;
            }
            return descriptor;
        }
        public Descriptor Prepend(FileManager.Descriptor descriptor, Action action)
        {
            switch (action.Target)
            {
                case "Descriptor Title":
                    descriptor.Name = (HandleNewlines(action.Value) + descriptor.Name); break;

                case "Descriptor Type":
                    descriptor.Type = (HandleNewlines(action.Value) + descriptor.Type); break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        bool handled = false;
                        switch (property.Item2)
                        {
                            case ObjectValue objectValue:
                                var nestedObj = new FileManager.Descriptor { Properties = objectValue.Properties };
                                nestedObj = Prepend(nestedObj, action);
                                descriptor.Properties[i] = (property.Item1, new ObjectValue(objectValue.Type, nestedObj.Properties));
                                handled = true;
                                break;
                            case ArrayValue arrayValue:
                                var nestedArr = new FileManager.Descriptor { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                nestedArr = Prepend(nestedArr, action);
                                descriptor.Properties[i] = (property.Item1, new ArrayValue(nestedArr.Properties.Select(p => p.Item2).ToArray()));
                                handled = true;
                                break;
                            case StructValue structValue:
                                var nestedStruct = new FileManager.Descriptor { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                nestedStruct = Prepend(nestedStruct, action);
                                descriptor.Properties[i] = (property.Item1, new StructValue(structValue.Type, nestedStruct.Properties.Select(p => p.Item2).ToArray()));
                                handled = true;
                                break;
                            case PairValue pairValue:
                                var nestedPair = new FileManager.Descriptor { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                                nestedPair = Prepend(nestedPair, action);
                                descriptor.Properties[i] = (property.Item1, new PairValue(nestedPair.Properties[0].Item2, nestedPair.Properties[1].Item2));
                                handled = true;
                                break;
                        }

                        if (handled) continue;

                        // Continue if not the target property
                        if (action.ManualOverride == false)
                        { if (property.Item1 != action.Target) continue; }
                        else
                        { if (property.Item1 != action.CustomTarget) continue; }

                        descriptor.Properties[i] = (property.Item1, new StringLiteral(HandleNewlines(action.Value) + Writer.WriteToString(property.Item2) + BuildComment(action, Writer.WriteToString(property.Item2))));
                    }
                    break;
            }
            return descriptor;
        }
        public Descriptor Remove(FileManager.Descriptor descriptor, Action action) 
        {
            switch (action.Target)
            {
                case "Descriptor Title":
                    descriptor.Name = descriptor.Name.Replace(HandleNewlines(action.Value), ""); break;

                case "Descriptor Type":
                    descriptor.Type = descriptor.Type.Replace(HandleNewlines(action.Value), ""); break;

                default:
                    foreach (var (property, i) in descriptor.Properties.Select((value, index) => (value, index)))
                    {
                        var nestedDescriptor = new FileManager.Descriptor(); // Declare nestedDescriptor here to be used in the switch statement below
                        switch (property.Item2) // Recursion
                        {
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ObjectValue objectValue: // Recursively handle nested ObjectValues and their properties
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = objectValue.Properties };
                                Remove(nestedDescriptor, action);
                                break;
                            // Ensure ObjectValues inside ArrayValues are handled recursively
                            case ArrayValue arrayValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = arrayValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                Remove(nestedDescriptor, action);
                                break;
                            // Ensure ObjectValues inside StructValues are handled recursively
                            case StructValue structValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = structValue.Values.Select((v, idx) => ($"Index{idx}", v)).ToArray() };
                                Remove(nestedDescriptor, action);
                                break;
                            // Ensure ObjectValues inside PairValues are handled recursively
                            case PairValue pairValue:
                                nestedDescriptor = new FileManager.Descriptor
                                { Properties = new (string, IValue)[] { ("Value1", pairValue.Value1), ("Value2", pairValue.Value2) } };
                                Remove(nestedDescriptor, action);
                                break;
                        }

                        // Continue if not the target property
                        if (action.ManualOverride == false) // If the user is not using a custom operand, compare against the selected predefined operand
                        { if (property.Item1 != action.Target) continue; }
                        else // If the user is using a custom operand, compare against the user-entered operand instead
                        { if (property.Item1 != action.CustomTarget) continue; }

                        if (property.Item1 != action.Target) continue;
                        //descriptor.Properties[i] = (property.Item1, new StringLiteral(Writer.WriteToString(property.Item2).Replace(HandleNewlines(action.Value), "") + BuildComment(action, Writer.WriteToString(property.Item2))));
                        descriptor.Properties[i] = (property.Item1, new StringLiteral(Writer.WriteToString(property.Item2).Replace(HandleNewlines(action.Value), "") + BuildComment(action, Writer.WriteToString(property.Item2))));

                    }
                    break;
            }

            return descriptor;
        }
        #endregion

        #region EngineMethods
        private static string HandleNewlines(string? input) // Converts the literal string "\n" into actual newline characters, while leaving other escape sequences intact. This allows users to input "\n" in action values to represent newlines without needing to input actual newline characters, which can be difficult to manage in a single-line text field.
        {
            return input is null ? "" : input.Replace("""\n""", Environment.NewLine, StringComparison.Ordinal);
        }
        public string BuildComment(Action action, string originalValue)
        {
            string commentText = "";
            if (action.Comment.Enabled)
            {
                string originalVal = action.Comment.IncludeOriginalValue ? $" Original Value: {originalValue}" : "";
                string timestamp = action.Comment.IncludeTimestamp ? $" [{DateTime.UtcNow} UTC]" : ""; 
                commentText = $" /*{action.Comment.Text}{timestamp}{originalVal}*/";
            }
            return commentText;
        }
        public Boolean CheckConditions(Descriptor descriptor, Condition[] conditions, string logicGate)
        {
            var trueCount = 0; // count of conditions that evaluated to true
            foreach (var condition in conditions)
            {
                switch (condition.Operator)
                {
                    case "Starts With":
                        switch (logicGate)
                        {
                            case "All":
                                if (StartsWith(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (StartsWith(descriptor, condition)) { trueCount++; } else { continue; } break;    
                            default: break;
                        }
                        break;

                    case "Ends With":
                        switch (logicGate)
                        {
                            case "All":
                                if (EndsWith(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (EndsWith(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Contains":
                        switch (logicGate)
                        {
                            case "All":
                                if (Contains(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (Contains(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Does Not Contain":
                        switch (logicGate)
                        {
                            case "All":
                                if (DoesNotContain(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (DoesNotContain(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Is Equal To":
                        switch (logicGate)
                        {
                            case "All":
                                if (IsEqualTo(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (IsEqualTo(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Is Not Equal To":
                        switch (logicGate)
                        {
                            case "All":
                                if (IsNotEqualTo(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (IsNotEqualTo(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Is Less Than":
                        switch (logicGate)
                        {
                            case "All":
                                if (IsLessThan(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (IsLessThan(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Is Less Than Or Equal To":
                        switch (logicGate)
                        {
                            case "All":
                                if (IsLessThanOrEqualTo(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (IsLessThanOrEqualTo(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Is Greater Than":
                        switch (logicGate)
                        {
                            case "All":
                                if (IsGreaterThan(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (IsGreaterThan(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Is Greater Than Or Equal To":
                        switch (logicGate)
                        {
                            case "All":
                                if (IsGreaterThanOrEqualTo(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (IsGreaterThanOrEqualTo(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Contains Any Of":
                        switch (logicGate)
                        {
                            case "All":
                                if (ContainsAnyOf(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (ContainsAnyOf(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    case "Contains All Of":
                        switch (logicGate)
                        {
                            case "All":
                                if (ContainsAllOf(descriptor, condition)) { continue; } else { return false; }
                            case "Any":
                                if (ContainsAllOf(descriptor, condition)) { trueCount++; } else { continue; } break;
                            default: break;
                        }
                        break;

                    default:
                        break;
                }
            } // iterate through each condition in the current rule and verify that each one comes out as true
            
            if (logicGate == "Any" && trueCount > 0)
            { return true; }
            else if (logicGate == "All")
            { return true; }
            else
            { return false; }

        }
        public Descriptor ApplyActions(Descriptor descriptor, Action[] actions)
        {
            foreach (var action in actions)
            {
                switch (action.Operator)
                {
                    case "Add [int, float]":
                        descriptor = Add(descriptor, action); break;

                    case "Subtract [int, float]":
                        descriptor = Subtract(descriptor, action); break;

                    case "Multiply [int, float]":
                        descriptor = Multiply(descriptor, action); break;

                    case "Divide [int, float]":
                        descriptor = Divide(descriptor, action); break;

                    case "Set To [int, float, string, boolean]":
                        descriptor = SetTo(descriptor, action); break;

                    case "Insert [array]":
                        descriptor = Insert(descriptor, action); break;

                    case "Replace [array]":
                        descriptor = Replace(descriptor, action); break;

                    case "Append [string]":
                        descriptor = Append(descriptor, action); break;

                    case "Prepend [string]":
                        descriptor = Prepend(descriptor, action); break;

                    case "Remove [array]": // TO DO - Not yet implemented
                        descriptor = Remove(descriptor, action); break;
                       
                }
            } // iterate through each action in the current rule and execute them

            return descriptor;
        }
        public string ConvertItemToJSON(ListViewItem item)
        {
            if (item.Tag is Rule rule)
            {
                string type = item.Tag!.GetType().ToString();
                rule = (item.Tag is Rule r ? r : new Rule(item.Text, new[] { Condition.Empty() }, new[] { Action.Empty() }));
                rule.DataTypeToString = "Rule";
                return JsonSerializer.Serialize(rule);
            }
            else if (item.Tag is Condition condition)
            {
                string type = item.Tag!.GetType().ToString();
                condition = (item.Tag is Condition cond ? cond : Condition.Empty());
                condition.DataTypeToString = "Condition";
                return JsonSerializer.Serialize(condition);
            }
            else if (item.Tag is Action action)
            {
                string type = item.Tag!.GetType().ToString();
                action = (item.Tag is Action act ? act : Action.Empty());
                action.DataTypeToString = "Action";
                return JsonSerializer.Serialize(action);
            }
            else
            {
                throw new InvalidOperationException("ListViewItem does not contain valid Rule, Condition, or Action data.");
            }
        }
        public ListViewItem ConvertJSONToItem(string json)
        {   
            if (json.Contains("""DataTypeToString":"Rule"""))
            {
                Rule rule = JsonSerializer.Deserialize<Rule>(json);
                ListViewItem ruleItem = new ListViewItem(rule.Name);
                ruleItem.Name = rule.Name;
                ruleItem.Tag = rule;
                return ruleItem;
            }
            else if (json.Contains("""DataTypeToString":"Condition"""))
            {
                Condition condition = JsonSerializer.Deserialize<Condition>(json);
                ListViewItem conditionItem = new ListViewItem(condition.Name);
                conditionItem.Name = condition.Name;
                conditionItem.Tag = condition;
                return conditionItem;

            }
            else if (json.Contains(""""DataTypeToString":"Action""""))
            {
                Action action = JsonSerializer.Deserialize<Action>(json);
                ListViewItem actionItem = new ListViewItem(action.Name);
                actionItem.Name = action.Name;
                actionItem.Tag = action;
                return actionItem;
            }
            else
            {
                return new ListViewItem("");
            }

        }
        #endregion
    }
}
