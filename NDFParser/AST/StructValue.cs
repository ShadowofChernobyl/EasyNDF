using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser.AST
{
    public record StructValue(string Type, IValue[] Values) : IValue
    {
        public T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitStructValue(this);
        }

        public override string ToString()
        {
            return $"StructValue {{ Values = [{string.Join(", ", Values as IEnumerable<IValue>)}] }}";
        }

        public virtual bool Equals(StructValue? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Values.SequenceEqual(other.Values);
        }

        public override int GetHashCode()
        {
            return Values.Aggregate(0, (hash, item) => HashCode.Combine(hash, item));
        }
    }
}
