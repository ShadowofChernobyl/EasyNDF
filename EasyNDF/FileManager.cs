using NDFParser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NDFParser.AST;
using System.Windows.Forms.VisualStyles;

namespace EasyNDF
{
    public class FileManager
    {
        public static NDF_File importedFile = new NDF_File();
        public static NDF_File exportedFile = new NDF_File();

        public struct NDF_File
        {
            public NDF_File(OpenFileDialog fileDialog, FileDeclaration parsedFile)
            {
                WindowsFile = fileDialog;
                ParsedFile = parsedFile;
            }
            public FileDialog WindowsFile { get; set; }
            public FileDeclaration ParsedFile { get; set; }
        }

        public struct Descriptor
        {
            public Descriptor(string name, string type, (string, IValue)[] properties, string parsedVersion)
            {
                Name = name;
                Type = type;
                Properties = properties;
                ParsedVersion = parsedVersion;
            }
            public string Name { get; set; }
            public string Type { get; set; }
            public (string, IValue)[] Properties { get; set; }
            public string ParsedVersion { get; set; }
        }

        public static NDF_File ImportFile()
        {
            // Open a file prompt window for the user to select an .ndf file to import
            OpenFileDialog fileDialog = new OpenFileDialog();
            if (fileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                importedFile.WindowsFile = fileDialog;
                importedFile.ParsedFile = Parser.ParseFromFile(fileDialog.FileName);
            }
            return importedFile;
        }

        public static ComboBox.ObjectCollection PopulateOperandList()
        {
            ComboBox.ObjectCollection operandList = new ComboBox().Items;
            PropertyFinder finder = new PropertyFinder();
            importedFile.ParsedFile?.Accept(finder);

            foreach (var field in finder.PropertyNames)
            {
                operandList.Add(field);
            }

            return operandList;
        }

        public static void WriteNDF(FileManager.NDF_File importedFile, FileManager.NDF_File exportedFile, RuleEngine.Rule[] rules)
        {
            RuleEngine ruleEngine = new RuleEngine();
            using (var file = File.CreateText(exportedFile.WindowsFile.FileName))
            {
                foreach (var declaration in importedFile.ParsedFile.Declarations)
                {         
                    if (declaration is not AssignDeclaration assignDeclaration)
                        continue;

                    if (assignDeclaration.Value is not ObjectValue objectValue)
                        continue;

                    Descriptor descriptor = new Descriptor(assignDeclaration.Name, objectValue.Type, objectValue.Properties, declaration.ToString());

                    foreach (var rule in rules)
                    {
                        bool conditionsMet = true;
                        foreach (var condition in rule.Conditions)
                        {
                            switch (condition.Operator)
                            {
                                case "Starts With":
                                    if (ruleEngine.StartsWith(descriptor, condition)) { continue; } 
                                    else { conditionsMet = false; } break;

                                case "Ends With":
                                    if (ruleEngine.EndsWith(descriptor, condition)) { continue; } 
                                    else { conditionsMet = false; } break;
                                    
                                case "Contains":
                                    if (ruleEngine.Contains(descriptor, condition)) { continue; } 
                                    else { conditionsMet = false; } break;

                                case "Does Not Contain":
                                    if (ruleEngine.DoesNotContain(descriptor, condition)) { continue; } 
                                    else { conditionsMet = false; } break;

                                case "Is Equal To":
                                    if (ruleEngine.IsEqualTo(descriptor, condition)) { continue; } 
                                    else { conditionsMet = false; } break;
                                    
                                case "Is Not Equal To":
                                    if (ruleEngine.IsNotEqualTo(descriptor, condition)) { continue; }
                                    else { conditionsMet = false; } break;

                                case "Is Less Than":
                                    if (ruleEngine.IsLessThan(descriptor, condition)) { continue; }
                                    else { conditionsMet = false; } break;

                                case "Is Less Than Or Equal To":
                                    if (ruleEngine.IsLessThanOrEqualTo(descriptor, condition)) { continue; }
                                    else { conditionsMet = false; } break;

                                case "Is Greater Than":
                                    if (ruleEngine.IsGreaterThan(descriptor, condition)) { continue; }
                                    else { conditionsMet = false; } break;

                                case "Is Greater Than Or Equal To":
                                    if (ruleEngine.IsGreaterThanOrEqualTo(descriptor, condition)) { continue; }
                                    else { conditionsMet = false; } break;

                                case "Contains Any Of":
                                    if (ruleEngine.ContainsAnyOf(descriptor, condition)) { continue; }
                                    else { conditionsMet = false; } break;

                                case "Contains All Of":

                                    break;
                                default:

                                    break;
                            }
                        } // iterate through each condition in the current rule and verify that each one comes out as true
                        
                        if (!conditionsMet) continue; // skip to the next rule if any condition is false

                        foreach (var action in rule.Actions)
                        {                         
                            foreach (var (property, i)in descriptor.Properties.Select((value, index) => (value, index))) 
                            {
                                if (property.Item1 != action.Target)
                                    continue;

                                // I honestly just stored these property items like this so it was easier for me to read. Sue me ;-;

                                switch (action.Operator)
                                {
                                    case "Add [int, float]":
                                        objectValue.Properties[i] = ruleEngine.Add(action, property);
                                        break;
                                    case "Subtract [int, float]":
                                        objectValue.Properties[i] = ruleEngine.Subtract(action, property);
                                        break;
                                    case "Multiply [int, float]":
                                        objectValue.Properties[i] = ruleEngine.Multiply(action, property);                            
                                        break;
                                    case "Divide [int, float]":
                                        objectValue.Properties[i] = ruleEngine.Divide(action, property);
                                        break;
                                    case "Set To [int, float, string, boolean]":
                                        objectValue.Properties[i] = ruleEngine.SetTo(action, property);
                                        break;
                                    case "Insert [array]":
                                        objectValue.Properties[i] = ruleEngine.Insert(action, property);
                                        break;
                                    case "Append [line]":
                                        objectValue.Properties[i] = ruleEngine.Append(action, property);
                                        break;
                                    case "Prepend [line]":
                                        objectValue.Properties[i] = ruleEngine.Prepend(action, property);
                                        break;
                                    case "Replace [line]":

                                        break;
                                    case "Remove [line]":

                                        break;
                                }
                            }
                        } // iterate through each action in the current rule and execute them
                    }
                }
                importedFile.ParsedFile.Accept(new Writer(file));
            }
        }

        public static void ExportFile(RuleEngine.Rule[] rules)
        {
            // Open a save file prompt window for the user to select an .ndf file to import
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "NDF files (*.ndf)|*.ndf|All files (*.*)|*.*";
            if (saveFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                exportedFile.WindowsFile = saveFileDialog;
                WriteNDF(importedFile, exportedFile, rules); // Write the new NDF file with the applied rules
                importedFile.ParsedFile = Parser.ParseFromFile(importedFile.WindowsFile.FileName); // Re-parse the imported file to reset any changes made during export
            }
        }
    }
}
