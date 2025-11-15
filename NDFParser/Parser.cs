using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using NDFParser.AST;
using System.Net.Http.Headers;
using System.Reflection.Metadata.Ecma335;
using System.Xml.Serialization;

namespace NDFParser
{
    public class ThrowingErrorListener : BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {
            throw new Exception($"Syntax error at {line}:{charPositionInLine}: {msg}");
        }
    }

    public class ASTBuilder: ndfBaseVisitor<IASTNode>
    {
        public override IASTNode VisitFile([NotNull] ndfParser.FileContext context)
        {
            return new FileDeclaration(context.declaration().Select(x => (IDeclaration)x.Accept(this)).ToArray());
        }

        public override IASTNode VisitAssignDecl([NotNull] ndfParser.AssignDeclContext context)
        {
            bool export = context.EXPORT() != null;
            return new AssignDeclaration(export, context.ID().GetText(), (IValue)context.value().Accept(this));
        }

        public override IASTNode VisitUnnamedDecl([NotNull] ndfParser.UnnamedDeclContext context)
        {
            return new UnnamedDeclaration((IValue)context.value().Accept(this));
        }

        public override IASTNode VisitNumericLiteral([NotNull] ndfParser.NumericLiteralContext context)
        {
            return new NumericLiteral(context.NUMBER().GetText());
        }

        public override IASTNode VisitStringLiteral([NotNull] ndfParser.StringLiteralContext context)
        {
            return new StringLiteral(context.STRING().GetText());
        }

        public override IASTNode VisitGuidLiteral([NotNull] ndfParser.GuidLiteralContext context)
        {
            return new GuidLiteral(context.GUID_LITERAL().GetText());
        }

        public override IASTNode VisitArrayValue([NotNull] ndfParser.ArrayValueContext context)
        {
            return context.array().Accept(this);
        }

        public override IASTNode VisitObjectValue([NotNull] ndfParser.ObjectValueContext context)
        {
            var parameters = context.ID().Skip(1).Select(x => x.GetText()).Zip(context.value().Select(x => (IValue)x.Accept(this))).ToArray();
            return new ObjectValue(context.ID()[0].GetText(), parameters);
        }

        public override IASTNode VisitStructValue([NotNull] ndfParser.StructValueContext context)
        {
            return new StructValue(context.ID().GetText(), ((ArrayValue)context.array().Accept(this)).Values);
        }

        public override IASTNode VisitPairValue([NotNull] ndfParser.PairValueContext context)
        {
            return new PairValue((IValue)context.value()[0].Accept(this), (IValue)context.value()[1].Accept(this));
        }

        public override IASTNode VisitCombinedValue([NotNull] ndfParser.CombinedValueContext context)
        {
            return new CombinedValue((IValue)context.value()[0].Accept(this), (IValue)context.value()[1].Accept(this));
        }

        public override IASTNode VisitPathValue([NotNull] ndfParser.PathValueContext context)
        {
            return new PathValue(context.PATH().GetText());
        }

        public override IASTNode VisitIdValue([NotNull] ndfParser.IdValueContext context)
        {
            return new IDValue(context.ID().GetText());
        }

        public override IASTNode VisitAssignValue([NotNull] ndfParser.AssignValueContext context)
        {
            return new AssignmentValue(context.ID().GetText(), (IValue)context.value().Accept(this));
        }

        public override IASTNode VisitNilLiteral([NotNull] ndfParser.NilLiteralContext context)
        {
            return new NilLiteral();
        }

        public override IASTNode VisitRefRelativeValue([NotNull] ndfParser.RefRelativeValueContext context)
        {
            return new RelReference(context.RELREFERENCE().GetText());
        }

        public override IASTNode VisitRefAbsoluteValue([NotNull] ndfParser.RefAbsoluteValueContext context)
        {

            return new AbsReference(context.ABSREFERENCE().GetText());
        }

        public override IASTNode VisitArray([NotNull] ndfParser.ArrayContext context)
        {
            return new ArrayValue(context.value().Select(x => (IValue)x.Accept(this)).ToArray());
        }

        public override IASTNode VisitParenthesis([NotNull] ndfParser.ParenthesisContext context)
        {
            return context.value().Accept(this);
        }

        public override IASTNode VisitErrorNode(IErrorNode node)
        {
            throw new Exception($"Error occurred while parsing {node.ToString()}"); 
        }
    }
    public class Parser
    {
        public static FileDeclaration ParseFromFile(string filePath)
        {
            var stream = new AntlrFileStream(filePath);
            return ParseFromStream(stream);
        }

        public static FileDeclaration ParseFromString(string input)
        {
            var stream = new AntlrInputStream(input);
            return ParseFromStream(stream);
        }

        private static FileDeclaration ParseFromStream(AntlrInputStream stream)
        {
            ndfLexer lexer = new ndfLexer(stream);
            CommonTokenStream commonTokenStream = new CommonTokenStream(lexer);

            ndfParser parser = new ndfParser(commonTokenStream);
            parser.AddErrorListener(new ThrowingErrorListener());

            ASTBuilder values = new ASTBuilder();
            var res = (FileDeclaration)values.Visit(parser.file());

            return res;
        }
    }
}
