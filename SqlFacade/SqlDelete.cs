// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;
    using System.Text.Json;

    public class SqlDelete : ISqlWrite
    {
        public string SqlType { get; }
        public string Table { get; set; }
        public List<Expression> Filters { get; set; }

        public SqlDelete()
        {
            this.SqlType = Constants.Delete;
        }

        public SqlDelete(string table) : this()
        {
            this.Table = table;
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
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.IgnoreNullValues = true;
            return JsonSerializer.Serialize(this, options);
        }
    }
}