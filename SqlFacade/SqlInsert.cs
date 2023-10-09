// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;
    using System.Text.Json;

    public class SqlInsert : ISqlWrite
    {
        public string SqlType { get; }
        public string Table { get; set; }
        public List<Field> Fields { get; set; }
        public SqlSelect Query { get; set; }

        public SqlInsert()
        {
            this.SqlType = Constants.Insert;
        }

        public SqlInsert(string table) : this()
        {
            this.Table = table;
        }

        public SqlInsert WithField(Field field)
        {
            if (Fields == null)
            {
                Fields = new List<Field>();
            }
            this.Fields.Add(field);
            return this;
        }

        public SqlInsert WithQuery(SqlSelect query)
        {
            this.Query = query;
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