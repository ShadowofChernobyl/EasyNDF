using FsCheck;
using FsCheck.Fluent;
using NDFParser;
using NDFParser.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParserTests
{
    public class ParserWriterCompatibility
    {
        [Fact]
        public void TestParserCanReadWriter()
        {
            const int depth = 7;
            Prop.ForAll<FileDeclaration>(Arb.ToArbitrary(ASTGenerator.GenerateFile(depth), ASTGenerator.ShrinkDecl), file =>
            { 
                StringWriter writer = new StringWriter();
                file.Accept(new Writer(writer));
                string str = writer.ToString();
                try
                {
                    FileDeclaration parseResult = Parser.ParseFromString(str);
                    return Prop.Label(parseResult == file, $"===STRING===\n{str}\n===PARSEDRESULT===\n{parseResult.ToString()}");
                } catch (Exception e)
                {
                    return Prop.Label(false, e.ToString());
                }
            }).Check(Config.QuickThrowOnFailure.WithMaxTest(1000));
        }

        [Fact]
        public void ParserMatchesWriterForBinOps()
        {
            var file = new FileDeclaration([new UnnamedDeclaration
                (new OrValue
                    ( new OrValue(new IDValue("A"), new IDValue("B"))
                    , new OrValue(new IDValue("C"), new IDValue("D"))
                    )
                )]);
            StringWriter writer = new StringWriter();
            file.Accept(new Writer(writer));
            string str = writer.ToString();
            FileDeclaration parseResult = Parser.ParseFromString(str);
            Assert.Equal(file, parseResult);
        }
    }
}
