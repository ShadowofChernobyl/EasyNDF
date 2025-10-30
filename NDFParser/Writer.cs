using NDFParser.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser
{
    public class Writer: IASTVisitor<int>
    {
        System.IO.TextWriter writer;
        int indentation = 0;

        public Writer(System.IO.TextWriter writer)
        {
            this.writer = writer;
        }

        private void NewLine()
        {
            writer.Write('\n');
            for (int i = 0; i < indentation; i++)
            {
                writer.Write("    ");
            }
        }

        int IASTVisitor<int>.VisitAbsReference(AbsReference absReference)
        {
            writer.Write(absReference.Reference);
            return 0;
        }


        int IASTVisitor<int>.VisitArrayValue(ArrayValue arrayValue)
        {
            writer.Write('[');
            indentation++;
            foreach (var elem in arrayValue.Values)
            {
                NewLine();
                elem.Accept(this);
                writer.Write(',');
            }
            indentation--;
            NewLine();
            writer.Write(']');
            return 0;
        }

        int IASTVisitor<int>.VisitAssignDeclaration(AssignDeclaration assignDeclaration)
        {
            if (assignDeclaration.Exported) writer.Write("export ");
            writer.Write(assignDeclaration.Name);
            writer.Write(" is ");
            assignDeclaration.Value.Accept(this);
            NewLine();
            return 0;
        }

        int IASTVisitor<int>.VisitAssignmentValue(AssignmentValue assignmentValue)
        {
            //TODO: intelligently decide when parenthesis are required
            writer.Write('(');
            writer.Write(assignmentValue.Name);
            writer.Write(" is ");
            assignmentValue.Value.Accept(this);
            writer.Write(')');
            return 0;
        }

        int IASTVisitor<int>.VisitFileDeclaration(FileDeclaration fileDeclaration)
        {
            foreach (var decl in fileDeclaration.Declarations)
            {
                decl.Accept(this);
            }
            return 0;
        }

        int IASTVisitor<int>.VisitGuidLiteral(GuidLiteral guidLiteral)
        {
            writer.Write(guidLiteral.Value);
            return 0;
        }

        int IASTVisitor<int>.VisitIDValue(IDValue idValue)
        {
            writer.Write(idValue.ID);
            return 0;
        }

        int IASTVisitor<int>.VisitNilLiteral(NilLiteral nilLiteral)
        {
            writer.Write("nil");
            return 0;
        }

        int IASTVisitor<int>.VisitNumericLiteral(NumericLiteral numericLiteral)
        {
            writer.Write(numericLiteral.Value);
            return 0;
        }

        int IASTVisitor<int>.VisitObjectValue(ObjectValue objectValue)
        {
            writer.Write(objectValue.Type);
            NewLine();
            writer.Write("(");
            indentation++;
            foreach(var x in objectValue.Properties)
            {
                NewLine();
                writer.Write(x.Item1);
                for (int i = x.Item1.Length; i < 34; i++)
                {
                    writer.Write(" ");
                }
                writer.Write("= ");
                x.Item2.Accept(this);
            }
            indentation--;
            NewLine();
            writer.Write(")");
            return 0;
        }

        int IASTVisitor<int>.VisitOrValue(OrValue orValue)
        {
            //TODO: intelligently decide when parenthesis are required
            writer.Write('(');
            orValue.ValueL.Accept(this);
            writer.Write(" | ");
            orValue.ValueR.Accept(this);
            writer.Write(')');
            return 0;
        }

        int IASTVisitor<int>.VisitPairValue(PairValue pairValue)
        {
            writer.Write("(");
            pairValue.Value1.Accept(this);
            writer.Write(", ");
            pairValue.Value2.Accept(this);
            writer.Write(")");
            return 0;
        }

        int IASTVisitor<int>.VisitPathValue(PathValue pathValue)
        {
            writer.Write(pathValue.Path);
            return 0;
        }

        int IASTVisitor<int>.VisitRelReference(RelReference relReference)
        {
            writer.Write(relReference.Reference);
            return 0;
        }

        int IASTVisitor<int>.VisitStringLiteral(StringLiteral stringLiteral)
        {
            writer.Write(stringLiteral.Value);
            return 0;
        }

        int IASTVisitor<int>.VisitStructValue(StructValue structValue)
        {
            writer.Write(structValue.Type);
            writer.Write('[');
            indentation++;
            foreach (var elem in structValue.Values)
            {
                NewLine();
                elem.Accept(this);
                writer.Write(',');
            }
            indentation--;
            NewLine();
            writer.Write(']');
            return 0;
        }

        int IASTVisitor<int>.VisitUnnamedDeclaration(UnnamedDeclaration  unnamedDeclaration)
        {
            writer.Write("unnamed ");
            unnamedDeclaration.Value.Accept(this);
            NewLine();
            return 0;
        }

    }
}
