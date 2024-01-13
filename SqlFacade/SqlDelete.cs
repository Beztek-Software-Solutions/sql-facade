// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class SqlDelete : ISqlWrite
    {
        public string SqlType { get; }
        public string Table { get; set; }
        public IList<CommonTableExpression> CommonTableExpressions { get; set; }
        public List<Expression> Filters { get; set; }

        public SqlDelete()
        {
            this.SqlType = Constants.Delete;
        }

        public SqlDelete(string table) : this()
        {
            this.Table = table;
        }

        public SqlDelete WithCommonTableExpression(CommonTableExpression commonTableExpression)
        {
            if (CommonTableExpressions == null)
            {
                CommonTableExpressions = new List<CommonTableExpression>();
            }
            this.CommonTableExpressions.Add(commonTableExpression);
            return this;
        }

        public SqlDelete WithFilter(Expression expression)
        {
            if (Filters == null)
            {
                Filters = new List<Expression>();
            }
            this.Filters.Add(expression);
            return this;
        }

        public override string ToString()
        {
            JsonSerializerOptions options = new JsonSerializerOptions {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            return JsonSerializer.Serialize(this, options);
        }
    }
}