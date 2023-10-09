// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;

    public class SqlRelation
    {
        public static readonly SqlRelation Union = new SqlRelation("Union");
        public static readonly SqlRelation UnionAll = new SqlRelation("UnionAll");
        public static readonly SqlRelation Intersect = new SqlRelation("Intersect");
        public static readonly SqlRelation Except = new SqlRelation("Except");

        public string Value { get; set; }

        public SqlRelation() { }

        private SqlRelation(string value)
        {
            this.Value = value;
        }

        public override bool Equals(Object obj)
        {
            if (!(obj is SqlRelation))
                return false;
            else
                return string.Equals(Value, ((SqlRelation)obj).Value);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}