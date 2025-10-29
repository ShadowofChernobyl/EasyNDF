using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser.AST
{
    public record FileDeclaration(IDeclaration[] Declarations): IASTNode
    {
        public T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitFileDeclaration(this);
        }

        public override string ToString()
        {
            return $"FileDeclaration {{ Declarations = [{string.Join(", ", Declarations as IEnumerable<IDeclaration>)}] }}";
        }

        public virtual bool Equals(FileDeclaration? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Declarations.SequenceEqual(other.Declarations);
        }

        public override int GetHashCode()
        {
            return Declarations.Aggregate(0, (hash, item) => HashCode.Combine(hash, item));
        }
    }
}
