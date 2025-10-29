using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser.AST
{
    public record PathValue(string Path) : IValue
    {
        public T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitPathValue(this);
        }
    }
}
