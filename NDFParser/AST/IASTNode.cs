using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser.AST
{
    public interface IASTNode
    {
        public T Accept<T>(IASTVisitor<T> visitor);
    }
}
