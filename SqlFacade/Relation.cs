// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;

    public class Relation
    {
        public static readonly Relation EqualTo = new Relation("=");
        public static readonly Relation GreaterThan = new Relation(">");
        public static readonly Relation GreaterThanOrEqualTo = new Relation(">=");
        public static readonly Relation LessThan = new Relation("<");
        public static readonly Relation LessThanOrEqualTo = new Relation("<=");
        public static readonly Relation In = new Relation("In");
        public static readonly Relation NullValue = new Relation("NullValue");
        public static readonly Relation TrueValue = new Relation("TrueValue");
        public static readonly Relation Exists = new Relation("Exists");

        // String comparisons
        public static readonly Relation StartsWith = new Relation("StartsWith");
        public static readonly Relation EndsWith = new Relation("EndsWith");
        public static readonly Relation Contains = new Relation("Contains");

        public string Value { get; set; }

        public Relation() { }

        private Relation(string value)
        {
            this.Value = value;
        }

        public override string ToString()
        {
            return Value;
        }

        public override bool Equals(Object obj)
        {
            if (!(obj is Relation))
                return false;
            else
                return string.Equals(Value, ((Relation)obj).Value);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}