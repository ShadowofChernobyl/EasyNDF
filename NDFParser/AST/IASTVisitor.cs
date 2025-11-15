using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser.AST
{
    public interface IASTVisitor<T>
    {
        T VisitAbsReference(AbsReference absReference);
        T VisitArrayValue(ArrayValue arrayValue);
        T VisitAssignDeclaration(AssignDeclaration assignDeclaration);
        T VisitAssignmentValue(AssignmentValue assignmentValue);
        T VisitFileDeclaration(FileDeclaration fileDeclaration);
        T VisitGuidLiteral(GuidLiteral guidLiteral);
        T VisitIDValue(IDValue idValue);
        T VisitNilLiteral(NilLiteral nilLiteral);
        T VisitNumericLiteral(NumericLiteral numericLiteral);
        T VisitObjectValue(ObjectValue objectValue);
        T VisitCombinedValue(CombinedValue CombinedValue);
        T VisitPairValue(PairValue pairValue);
        T VisitPathValue(PathValue pathValue);
        T VisitRelReference(RelReference relReference);
        T VisitStringLiteral(StringLiteral stringLiteral);
        T VisitStructValue(StructValue structValue);
        T VisitUnnamedDeclaration(UnnamedDeclaration unnamedDeclaration);
    }
}