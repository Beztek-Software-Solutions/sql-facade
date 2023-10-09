// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;

    public class JoinType
    {
        public static readonly JoinType InnerJoin = new JoinType("InnerJoin");
        public static readonly JoinType LeftJoin = new JoinType("LeftJoin");

        public string Value { get; set; }

        public JoinType() { }

        private JoinType(string value)
        {
            this.Value = value;
        }

        public override string ToString()
        {
            return Value;
        }

        public override bool Equals(Object obj)
        {
            if (!(obj is JoinType))
                return false;
            else
                return string.Equals(Value, ((JoinType)obj).Value);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}