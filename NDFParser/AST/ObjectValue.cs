using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser.AST
{
    public record ObjectValue(string Type, (String, IValue)[] Properties) : IValue
    {
        public T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitObjectValue(this);
        }

        public override string ToString()
        {
            return $"ObjectValue {{ Properties = [{string.Join(", ", Properties as IEnumerable<(String, IValue)>)}] }}";
        }

        public virtual bool Equals(ObjectValue? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Properties.SequenceEqual(other.Properties);
        }

        public override int GetHashCode()
        {
            return Properties.Aggregate(0, (hash, item) => HashCode.Combine(hash, item));
        }
    }
}
