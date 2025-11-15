using NDFParser.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyNDF
{
    internal class PropertyFinder : IASTVisitor<int>
    {
        public HashSet<string> PropertyNames = new HashSet<string>();

        public int VisitAbsReference(AbsReference absReference)
        {
            return 0;
        }
        public int VisitArrayValue(ArrayValue arrayValue)
        {
            foreach (var item in arrayValue.Values)
            {
                item.Accept(this);
            }
            return 0;
        }
        public int VisitAssignDeclaration(AssignDeclaration assignDeclaration)
        {
            assignDeclaration.Value.Accept(this);
            return 0;
        } 
        public int VisitAssignmentValue(AssignmentValue assignmentValue)
        {
            assignmentValue.Value.Accept(this);
            return 0;
        }
        public int VisitFileDeclaration(FileDeclaration fileDeclaration)
        {
            foreach (var decl in fileDeclaration.Declarations)
            {
                decl.Accept(this);
            }
            return 0;
        }
        public int VisitGuidLiteral(GuidLiteral guidLiteral)
        {
            return 0;
        }
        public int VisitIDValue(IDValue idValue)
        {
            return 0;
        }
        public int VisitNilLiteral(NilLiteral nilLiteral)
        {
            return 0;
        }
        public int VisitNumericLiteral(NumericLiteral numericLiteral)
        {
            return 0;
        }
        public int VisitObjectValue(ObjectValue objectValue)
        {
            foreach (var property in objectValue.Properties)
            {
                PropertyNames.Add(property.Item1);
                property.Item2.Accept(this);     
            }
            return 0;
        }
        public int VisitCombinedValue(CombinedValue CombinedValue)
        {
            CombinedValue.ValueL.Accept(this);
            CombinedValue.ValueR.Accept(this);
            return 0;
        }
        public int VisitPairValue(PairValue pairValue)
        {
            pairValue.Value1.Accept(this);
            pairValue.Value2.Accept(this);
            return 0;
        }
        public int VisitPathValue(PathValue pathValue)
        {
            return 0;
        }
        public int VisitRelReference(RelReference relReference)
        {
            return 0;
        }
        public int VisitStringLiteral(StringLiteral stringLiteral)
        {
            return 0;
        }
        public int VisitStructValue(StructValue structValue)
        {
            foreach (var value in structValue.Values)
            {
                value.Accept(this);
            }
            return 0;
        }
        public int VisitUnnamedDeclaration(UnnamedDeclaration unnamedDeclaration)
        {
            unnamedDeclaration.Value.Accept(this);
            return 0;
        }
    }
}
