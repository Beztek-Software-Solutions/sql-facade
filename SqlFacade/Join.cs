// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;

    public class Join
    {
        public JoinType JoinType { get; set; }
        public Table JoinTable { get; set; }
        public DerivedTable JoinCTE { get; set; }
        public Expression OnExpression { get; set; }
        public List<Expression> JoinExpressions { get; set; }

        public Join() { }

        public Join(Table joinTable, Expression onExpression, JoinType joinType = null)
        {
            this.JoinType = joinType == null ? JoinType.InnerJoin : joinType;
            this.JoinTable = joinTable;
            this.OnExpression = onExpression;
        }

        public Join(DerivedTable joinCTE, Expression onExpression, JoinType joinType = null)
        {
            this.JoinType = joinType == null ? JoinType.InnerJoin : joinType;
            this.JoinCTE = joinCTE;
            this.OnExpression = onExpression;
        }

        public Join WithJoinExpression(Expression expression)
        {
            if (this.JoinExpressions == null)
            {
                this.JoinExpressions = new List<Expression>();
            }
            this.JoinExpressions.Add(expression);
            return this;
        }
    }
}