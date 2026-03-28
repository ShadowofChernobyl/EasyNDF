using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDFParser.AST
{
    public class StructValue : IValue
    {
        public string Type { get; set; }
        public IValue[] Values { get; set; }

        public StructValue(string type, IValue[] values)
        {
            Type = type;
            Values = values;
        }

        public T Accept<T>(IASTVisitor<T> visitor)
        {
            return visitor.VisitStructValue(this);
        }

        public override string ToString()
        {
            return $"StructValue {{ Values = [{string.Join(", ", Values as IEnumerable<IValue>)}] }}";
        }

        public override bool Equals(object? obj)
        {
            if (obj is not StructValue other) return false;
            if (ReferenceEquals(this, other)) return true;
            return Values.SequenceEqual(other.Values);
        }

        public override int GetHashCode()
        {
            return Values.Aggregate(0, (hash, item) => HashCode.Combine(hash, item));
        }
    }
}
