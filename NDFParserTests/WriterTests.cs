using FsCheck;
using NDFParser;
using NDFParser.AST;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParserTests
{
    public class WriterTests
    {
        [Fact]
        public void TestNilLiteral()
        {
            IASTNode input = new NilLiteral();

            string expected = "nil";

            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }

        [Fact]
        public void TestStringLiteral()
        {
            IASTNode input = new StringLiteral("\"Test\"");

            string expected = "\"Test\"";

            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }

        [Fact]
        public void TestNumericLiteral()
        {
            IASTNode input = new NumericLiteral("100.5");

            string expected = "100.5";

            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }

        [Fact]
        public void TestAbsReference()
        {
            IASTNode input = new AbsReference("$/This/Is/A/path");

            string expected = "$/This/Is/A/path";

            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }

        [Fact]
        public void TestRelReference()
        {
            IASTNode input = new RelReference("~/This/Is/A/path");

            string expected = "~/This/Is/A/path";

            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }

        [Fact]
        public void TestGuidLiteral()
        {
            IASTNode input = new GuidLiteral("GUID:{e4547fd2-b3d6-4d88-ad28-2d9387685f30}");

            string expected = "GUID:{e4547fd2-b3d6-4d88-ad28-2d9387685f30}";

            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }

        [Fact]
        public void TestPathValue()
        {
            IASTNode input = new PathValue("/Path/To/thing");

            string expected = "/Path/To/thing";

            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }
        
        [Fact]
        public void TestArrayValue()
        {
            IASTNode input = new ArrayValue(
                [ new StringLiteral("\"Test\"")
                , new NumericLiteral("5")
                ]);

            string expected = """
                [
                    "Test",
                    5,
                ]
                """.Replace("\r", "");
                
            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }

        [Fact]
        public void TestEmptyArrayValue()
        {
            IASTNode input = new ArrayValue([]);

            string expected = """
                [
                ]
                """.Replace("\r", "");
                
            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }
        
        [Fact]
        public void TestObjectValue()
        {
            IASTNode input = new ObjectValue("TObjectType",
                [ ("Field1", new NumericLiteral("50"))
                , ("Field2", new NilLiteral())
                ]);

            string expected = """
                TObjectType
                (
                    Field1                            = 50
                    Field2                            = nil
                )
                """.Replace("\r", "");
                
            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }
        
        [Fact]
        public void TestNestedObjectValue()
        {
            IASTNode input = new ObjectValue("TObjectType",
                [ ("Field1", new NumericLiteral("50"))
                , ("Field2", new ObjectValue("TNestedObjectType",
                    [ ("Field3", new NilLiteral())
                    , ("Field4", new NilLiteral())
                    ]))
                ]);

            string expected = """
                TObjectType
                (
                    Field1                            = 50
                    Field2                            = TNestedObjectType
                    (
                        Field3                            = nil
                        Field4                            = nil
                    )
                )
                """.Replace("\r", "");
                
            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }
        
        [Fact]
        public void TestAssignmentValue()
        {
            IASTNode input = new AssignmentValue("Name", new NilLiteral());

            string expected = "(Name is nil)";
                
            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }
        
        [Fact]
        public void TestAssignmentDeclaration()
        {
            IASTNode input = new AssignDeclaration(false, "Name", new NilLiteral());

            string expected = "Name is nil\n";
                
            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }
        
        [Fact]
        public void TestExportAssignmentDeclaration()
        {
            IASTNode input = new AssignDeclaration(true, "Name", new NilLiteral());

            string expected = "export Name is nil\n";
                
            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }
        
        [Fact]
        public void TestUnnamedDeclaration()
        {
            IASTNode input = new UnnamedDeclaration(new NilLiteral());

            string expected = "unnamed nil\n";
                
            StringWriter actual = new StringWriter();

            input.Accept(new Writer(actual));

            Assert.Equal(expected, actual.ToString());
        }
    }
}
