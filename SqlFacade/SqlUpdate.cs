// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class SqlUpdate : ISqlWrite
    {
        public string SqlType { get; }
        public string Table { get; set; }
        public IList<CommonTableExpression> CommonTableExpressions { get; set; }
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

        public SqlUpdate WithCommonTableExpression(CommonTableExpression commonTableExpression)
        {
            if (CommonTableExpressions == null)
            {
                CommonTableExpressions = new List<CommonTableExpression>();
            }
            this.CommonTableExpressions.Add(commonTableExpression);
            return this;
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
            JsonSerializerOptions options = new JsonSerializerOptions {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            return JsonSerializer.Serialize(this, options);
        }
    }
}