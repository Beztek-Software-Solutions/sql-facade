// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;

    public class Filter
    {
        // Flags how this expression is related the the prior filter in the list
        // Either with a logical And (default) Or, and their negations AndNot and OrNot
        public LogicalRelation LogicalRelation { get; set; }

        public List<Expression> Expressions { get; set; }
        public List<Filter> Filters { get; set; }

        public Filter()
        {
            LogicalRelation = LogicalRelation.And;
        }

        public Filter(LogicalRelation logicalRelation)
        {
            LogicalRelation = logicalRelation;
        }

        public Filter WithExpression(Expression expression)
        {
            if (Expressions == null)
            {
                Expressions = new List<Expression>();
            }
            Expressions.Add(expression);
            return this;
        }

        public Filter WithRawExpression(string rawExpression)
        {
            if (Expressions == null)
            {
                Expressions = new List<Expression>();
            }
            Expressions.Add(new Expression(rawExpression, null).WithIsRaw());
            return this;
        }

        public Filter WithFilter(Filter filter)
        {
            if (Filters == null)
            {
                Filters = new List<Filter>();
            }
            Filters.Add(filter);
            return this;
        }
    }
}