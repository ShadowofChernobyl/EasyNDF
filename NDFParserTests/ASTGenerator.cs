using FsCheck;
using FsCheck.Fluent;
using FsCheck.Internals;
using NDFParser.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace NDFParserTests
{
    internal class ASTGenerator
    {
        public static Gen<FileDeclaration> GenerateFile(int d)
        {
            return Gen.ArrayOf(GenerateDeclaration(d - 1)).Select(decls => new FileDeclaration(decls));
        }

        public static Gen<IDeclaration> GenerateDeclaration(int d)
        {
            return Gen.OneOf<IDeclaration>
                ( GenerateAssignmentDeclaration(d - 1).Select(x => x as IDeclaration)
                , GenerateUnnamedDeclaration(d - 1).Select(x => x as IDeclaration)
                );
        }
        
        public static Gen<AssignDeclaration> GenerateAssignmentDeclaration(int d)
        {
            return from exported in Gen.Elements(true, false)
                   from name in GenerateIdString()
                   from value in GenerateValue(d - 1)
                   select new AssignDeclaration(exported, name, value);
        }
        
        public static Gen<UnnamedDeclaration> GenerateUnnamedDeclaration(int d)
        {
            return from value in GenerateValue(d - 1)
                   select new UnnamedDeclaration(value);
        }

        public static Gen<IValue> GenerateValue(int d)
        {
            IEnumerable<Func<Gen<IValue>>> leafNodes =
                [() => GenerateIdValue().Select(x => x as IValue)
                ,() => GenerateAbsReference().Select(x => x as IValue)
                ,() => GenerateRelReference().Select(x => x as IValue)
                ,() => GenerateNumericLiteral().Select(x => x as IValue)
                ,() => GenerateGuidLiteral().Select(x => x as IValue)
                ,() => GenerateStringLiteral().Select(x => x as IValue)
                ,() => GenerateNilLiteral().Select(x => x as IValue)
                ,() => GeneratePathValue().Select(x => x as IValue)
                ];
            if (d <= 1) return from f in Gen.Elements(leafNodes)
                               from x in f()
                               select x;

            IEnumerable<Func<Gen<IValue>>> innerNodes =
                [() => GenerateArrayValue(d-1).Select(x => x as IValue)
                ,() => GenerateAssignmentValue(d-1).Select(x => x as IValue)
                ,() => GenerateCombinedValue(d - 1).Select(x => x as IValue)
                ,() => GeneratePairValue(d-1).Select(x => x as IValue)
                ,() => GenerateObjectValue(d-1).Select(x => x as IValue)
                ,() => GenerateStructValue(d-1).Select(x => x as IValue)
                ];
            return from f in Gen.Elements(Enumerable.Concat(leafNodes, innerNodes))
                               from x in f()
                               select x;
        }

        public static Gen<ObjectValue> GenerateObjectValue(int d)
        {
            return from name in GenerateIdString()
                   from recs in Gen.ArrayOf(Gen.Zip(GenerateIdString(), GenerateValue(d - 1)))
                   select new ObjectValue(name, recs);
        }

        public static Gen<StructValue> GenerateStructValue(int d)
        {
            return from name in GenerateIdString()
                   from recs in Gen.ArrayOf(GenerateValue(d - 1))
                   select new StructValue(name, recs);
        }

        public static Gen<ArrayValue> GenerateArrayValue(int d)
        {
            return Gen.ArrayOf(GenerateValue(d-1)).Select(x => new ArrayValue(x));
        }

        public static Gen<AssignmentValue> GenerateAssignmentValue(int d)
        {
            return
                from name in GenerateIdString()
                from value in GenerateValue(d - 1)
                select new AssignmentValue(name, value);
        }
            
        public static Gen<CombinedValue> GenerateCombinedValue(int d)
        {
            return
                from value1 in GenerateValue(d - 1)
                from value2 in GenerateValue(d - 1)
                select new CombinedValue(value1, value2);
        }

        public static Gen<PairValue> GeneratePairValue(int d)
        {
            return
                from value1 in GenerateValue(d - 1)
                from value2 in GenerateValue(d - 1)
                select new PairValue(value1, value2);
        }
            
        public static Gen<IDValue> GenerateIdValue()
        {
            return from str in GenerateIdString()
                   select new IDValue(str);
        }

        public static Gen<AbsReference> GenerateAbsReference()
        {
            return
                from path in Gen.ArrayOf(GenerateIdString())
                from end in Gen.OneOf(GenerateIdString(), GenerateLowerString())
                select new AbsReference("$/" + string.Concat(path.Select(x => x + "/")) + end);
        }

        public static Gen<RelReference> GenerateRelReference()
        {
            return
                from path in Gen.ArrayOf(Gen.OneOf(GenerateIdString(), GenerateLowerString()))
                from end in Gen.OneOf(GenerateIdString(), GenerateLowerString())
                select new RelReference("~/" + string.Concat(path.Select(x => x + "/")) + end);
        }

        public static Gen<PathValue> GeneratePathValue()
        {
            return
                from path in Gen.ArrayOf(GenerateIdString())
                from mid in GenerateIdString()
                from end in GenerateIdString()
                select new PathValue(string.Concat(path.Select(x=>x + "/")) + mid + "/" + end);
        }

        public static Gen<NumericLiteral> GenerateNumericLiteral()
        {
            var digit = Gen.Elements(Enumerable.Range('0', 10).Select(i => (char)i));
            var digits =
                from d in digit
                from ds in Gen.ArrayOf(digit)
                select new string([d]) + new string(ds);
            return
                from sign in Gen.Elements("", "-")
                from integer in digits
                from dec in Gen.OneOf(Gen.Constant(""), digits.Select(x => "." + x))
                select new NumericLiteral(sign + integer + dec);
        }
        public static Gen<GuidLiteral> GenerateGuidLiteral()
        {
            var hex = Gen.Elements(Enumerable.Range('a', 6).Select(i => (char)i));
            var digits = Gen.Elements(Enumerable.Range('0', 10).Select(i => (char)i));
            var guidDigit = Gen.OneOf(hex, digits);
            return
                from part1 in Gen.ArrayOf(guidDigit, 8)
                from part2 in Gen.ArrayOf(guidDigit, 4)
                from part3 in Gen.ArrayOf(guidDigit, 4)
                from part4 in Gen.ArrayOf(guidDigit, 4)
                from part5 in Gen.ArrayOf(guidDigit, 12)
                select new GuidLiteral($"GUID:{{{new string(part1)}-{new string(part2)}-{new string(part3)}-{new string(part4)}-{new string(part5)}}}");
        }

        public static Gen<StringLiteral> GenerateStringLiteral()
        {
            //TODO: Add more complex string generation that includes escape sequences
            IEnumerable<char> chars = Enumerable.Empty<char>();
            chars = Enumerable.Concat(chars, Enumerable.Range('a', 26).Select(i => (char)i));
            chars = Enumerable.Concat(chars, Enumerable.Range('A', 26).Select(i => (char)i));
            chars = Enumerable.Concat(chars, Enumerable.Range('0', 10).Select(i => (char)i));
            chars = Enumerable.Concat(chars, ['_', ' ', '!', '$', '.']);
            return Gen.ArrayOf(Gen.Elements(chars)).Select(s => new StringLiteral("\"" + new string(s) + "\""));
        }
        public static Gen<NilLiteral> GenerateNilLiteral()
        {
            return Gen.Constant(new NilLiteral());
        }

        public static Gen<string> GenerateIdString()
        {
            var upper = Gen.Elements(Enumerable.Range('A', 26).Select(i => (char)i));
            var lower = Gen.Elements(Enumerable.Range('a', 26).Select(i => (char)i));
            var digits = Gen.Elements(Enumerable.Range('0', 10).Select(i => (char)i));
            var underscore = Gen.Constant('_');

            var restChar = Gen.OneOf(lower, upper, digits, underscore);

            return
                from first in upper
                from size in Gen.Sized(n => Gen.Choose(0, n))
                from restChars in Gen.ArrayOf(restChar, size)
                select first + new string(restChars);
        }

        public static Gen<string> GenerateLowerString()
        {
            var upper = Gen.Elements(Enumerable.Range('A', 26).Select(i => (char)i));
            var lower = Gen.Elements(Enumerable.Range('a', 26).Select(i => (char)i));
            var digits = Gen.Elements(Enumerable.Range('0', 10).Select(i => (char)i));
            var underscore = Gen.Constant('_');

            var restChar = Gen.OneOf(lower, upper, digits, underscore);

            return
                from first in lower
                from size in Gen.Sized(n => Gen.Choose(0, n))
                from restChars in Gen.ArrayOf(restChar, size)
                select first + new string(restChars);
        }

        public static IEnumerable<FileDeclaration> ShrinkDecl(FileDeclaration f)
        {
            return f.Accept(new ASTShrinker()).Select(x => (FileDeclaration)x);
        }

        private class ASTShrinker : IASTVisitor<IEnumerable<IASTNode>>
        {
            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitAbsReference(AbsReference absReference)
            {
                return Enumerable.Empty<IASTNode>();
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitArrayValue(ArrayValue arrayValue)
            {
                IEnumerable<IASTNode> withoutArray = arrayValue.Values;
                IEnumerable<IASTNode> reducedArray = removeSingleElement(arrayValue.Values).Select(x => new ArrayValue(x));
                IEnumerable<IASTNode> simplifiedChildren = arrayValue.Values.SelectMany((baseElem, i) => baseElem.Accept(this).Select(newElem =>
                {
                    IValue[] newArray = arrayValue.Values.ToArray();
                    newArray[i] = (IValue)newElem;
                    return new ArrayValue(newArray);
                }));
                return withoutArray.Concat(reducedArray).Concat(simplifiedChildren);
            }

            private static IEnumerable<T[]> removeSingleElement<T>(T[] values)
            {
                if (values.Length <= 1)
                    return Enumerable.Empty<T[]>();

                return Enumerable.Range(0, values.Length - 1)
                    .Select(i => values.Where((_, idx) => idx != i).ToArray()); 
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitAssignDeclaration(AssignDeclaration assignDeclaration)
            {
                IEnumerable<IASTNode> withoutAssign = [new UnnamedDeclaration(assignDeclaration.Value)];
                IEnumerable<IASTNode> simplifedChild = assignDeclaration.Value.Accept(this).Select(v => assignDeclaration with { Value = (IValue)v });
                return withoutAssign.Concat(simplifedChild);
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitAssignmentValue(AssignmentValue assignmentValue)
            {
                IEnumerable<IASTNode> withoutAssign = [assignmentValue.Value];
                IEnumerable<IASTNode> simplifedChild = assignmentValue.Value.Accept(this).Select(v => assignmentValue with { Value = (IValue)v });
                return withoutAssign.Concat(simplifedChild);
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitFileDeclaration(FileDeclaration fileDeclaration)
            {
                IEnumerable<IASTNode> reducedArrays = removeSingleElement(fileDeclaration.Declarations).Select(x => new FileDeclaration(x));
                IEnumerable<IASTNode> simplifiedChildren = fileDeclaration.Declarations.SelectMany((baseElem, i) => baseElem.Accept(this).Select(newElem =>
                {
                    IDeclaration[] newArray = fileDeclaration.Declarations.ToArray();
                    newArray[i] = (IDeclaration)newElem;
                    return new FileDeclaration(newArray);
                }));
                return reducedArrays.Concat(simplifiedChildren);
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitGuidLiteral(GuidLiteral guidLiteral)
            {
                return Enumerable.Empty<IASTNode>();
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitIDValue(IDValue idValue)
            {
                return Enumerable.Empty<IASTNode>();
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitNilLiteral(NilLiteral nilLiteral)
            {
                return Enumerable.Empty<IASTNode>();
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitNumericLiteral(NumericLiteral numericLiteral)
            {
                return Enumerable.Empty<IASTNode>();
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitObjectValue(ObjectValue objectValue)
            {
                IEnumerable<IASTNode> withoutStruct = objectValue.Properties.Select(x => x.Item2);
                IEnumerable<IASTNode> reducedStruct = removeSingleElement(objectValue.Properties).Select(x => objectValue with { Properties = x});
                IEnumerable<IASTNode> simplifiedChildren = objectValue.Properties.SelectMany((pair, i) => pair.Item2.Accept(this).Select(newElem =>
                {
                    (string, IValue)[] newArray = objectValue.Properties.ToArray();
                    newArray[i] = (pair.Item1, (IValue)newElem);
                    return objectValue with { Properties = newArray };
                }));
                return withoutStruct.Concat(reducedStruct).Concat(simplifiedChildren);
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitCombinedValue(CombinedValue CombinedValue)
            {
                IEnumerable<IASTNode> singleChild = [CombinedValue.ValueL, CombinedValue.ValueR];
                IEnumerable<IASTNode> simplifyLeft = CombinedValue.ValueL.Accept(this).Select(x => CombinedValue with { ValueL = (IValue)x });
                IEnumerable<IASTNode> simplifyRight = CombinedValue.ValueR.Accept(this).Select(x => CombinedValue with { ValueR = (IValue)x });
                return singleChild.Concat(simplifyLeft).Concat(simplifyRight);
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitPairValue(PairValue pairValue)
            {
                IEnumerable<IASTNode> singleChild = [pairValue.Value1, pairValue.Value2];
                IEnumerable<IASTNode> simplifyLeft = pairValue.Value1.Accept(this).Select(x => pairValue with { Value1 = (IValue)x });
                IEnumerable<IASTNode> simplifyRight = pairValue.Value2.Accept(this).Select(x => pairValue with { Value2 = (IValue)x });
                return singleChild.Concat(simplifyLeft).Concat(simplifyRight);
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitPathValue(PathValue pathValue)
            {
                return Enumerable.Empty<IASTNode>();
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitRelReference(RelReference relReference)
            {
                return Enumerable.Empty<IASTNode>();
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitStringLiteral(StringLiteral stringLiteral)
            {
                return Enumerable.Empty<IASTNode>();
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitStructValue(StructValue structValue)
            {
                IEnumerable<IASTNode> withoutStruct = structValue.Values;
                IEnumerable<IASTNode> reducedStruct = removeSingleElement(structValue.Values).Select(x => structValue with { Values = x });
                IEnumerable<IASTNode> simplifiedChildren = structValue.Values.SelectMany((baseElem, i) => baseElem.Accept(this).Select(newElem =>
                {
                    IValue[] newArray = structValue.Values.ToArray();
                    newArray[i] = (IValue)newElem;
                    return structValue with { Values = newArray };
                }));
                return withoutStruct.Concat(reducedStruct).Concat(simplifiedChildren);
            }

            IEnumerable<IASTNode> IASTVisitor<IEnumerable<IASTNode>>.VisitUnnamedDeclaration(UnnamedDeclaration unnamedDeclaration)
            {
                return Enumerable.Empty<IASTNode>();
            }
        }
    }
}
