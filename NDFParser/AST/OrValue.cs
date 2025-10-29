using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser.AST
{
    public record OrValue(IValue ValueL, IValue ValueR) : IValue
    {
        public T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitOrValue(this);
        }
    }
}
