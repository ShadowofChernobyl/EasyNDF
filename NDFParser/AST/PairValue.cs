using System;

namespace NDFParser.AST
{
    public record class PairValue : IValue
    {
        public IValue Value1 { get; set; }
        public IValue Value2 { get; set; }

        public PairValue(IValue value1, IValue value2)
        {
            Value1 = value1;
            Value2 = value2;
        }

        public T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitPairValue(this);
        }
    }
}
/* -===Pre-GPT Version by Egg===-

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser.AST
{
    public record PairValue(IValue Value1, IValue Value2) : IValue
    {
        public T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitPairValue(this);
        }
    }
}

*/