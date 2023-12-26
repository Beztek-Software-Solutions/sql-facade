// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;

    public class Expression
    {
        // Flags how this expression is related the the prior filter in the list
        // Either with a logical And (default) Or, and their negations AndNot and OrNot
        public LogicalRelation LogicalRelation { get; set; }

        // This should never set to "true" directly, but only using WithIsRaw() method, for validation
        public bool IsRaw { get; set; }
        public string Name { get; set; }
        public object Value { get; set; }
        public Relation Relation { get; set; }

        public Expression()
        {
            this.LogicalRelation = LogicalRelation.And;
            IsRaw = false;
            Relation = Relation.EqualTo;
        }

        public Expression(string name, object value) : this()
        {
            this.Name = name;
            this.Value = value;
        }

        public Expression WithSqlExists(SqlSelect sqlSelect)
        {
            this.Value = sqlSelect;
            this.Relation = Relation.Exists;
            // Placeholder name that is never used. We only need the SqlSelect in the Value
            this.Name = Relation.ToString();
            return this;
        }

        public Expression WithSqlIn(string name, SqlSelect sqlSelect)
        {
            this.Value = sqlSelect;
            this.Relation = Relation.In;
            // Placeholder name that is never used. We only need the SqlSelect in the Value
            this.Name = name;
            return this;
        }

        public Expression WithRelation(Relation relation)
        {
            this.Relation = relation;
            return this;
        }

        public Expression WithLogicalRelation(LogicalRelation logicalRelation)
        {
            this.LogicalRelation = logicalRelation;
            return this;
        }

        public Expression WithIsRaw()
        {
            if (Value == null)
            {
                Value = new object[] { };
            }
            else if (!(Value is object[]))
            {
                throw new ArgumentException("Need Value as an object[] binding for the raw expression");
            }
            this.IsRaw = true;
            return this;
        }
    }
}