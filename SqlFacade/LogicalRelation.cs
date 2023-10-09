// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;

    public class LogicalRelation
    {
        public static readonly LogicalRelation And = new LogicalRelation("And");
        public static readonly LogicalRelation Or = new LogicalRelation("Or");
        public static readonly LogicalRelation AndNot = new LogicalRelation("AndNot");
        public static readonly LogicalRelation OrNot = new LogicalRelation("OrNot");

        public string Value { get; set; }

        public LogicalRelation() { }

        private LogicalRelation(string value)
        {
            this.Value = value;
        }

        public override bool Equals(Object obj)
        {
            if (!(obj is LogicalRelation))
                return false;
            else
                return string.Equals(Value, ((LogicalRelation)obj).Value);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}