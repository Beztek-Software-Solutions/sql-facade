// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    public class CommonTableExpression : DerivedTable
    {
        public string  RawSql { get; set; }

        public CommonTableExpression(SqlSelect select, string alias) : base(select, alias)
        {  }

        public CommonTableExpression(string rawSql, string alias) : base()
        {  
            this.RawSql = rawSql;
            this.Alias = alias;
        }
    }
}