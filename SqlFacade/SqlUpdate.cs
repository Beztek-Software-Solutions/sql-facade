// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;
    using System.Text.Json;

    public class SqlUpdate : ISqlWrite
    {
        public string SqlType { get; }
        public string Table { get; set; }
        public List<Field> Fields { get; set; }
        public List<Expression> Filters { get; set; }

        public SqlUpdate()
        {
            this.SqlType = Constants.Update;
        }

        public SqlUpdate(string table) : this()
        {
            this.Table = table;
        }

        public SqlUpdate WithField(Field field)
        {
            if (Fields == null)
            {
                Fields = new List<Field>();
            }
            this.Fields.Add(field);
            return this;
        }

        public SqlUpdate WithFilter(Expression field)
        {
            if (Filters == null)
            {
                Filters = new List<Expression>();
            }
            this.Filters.Add(field);
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