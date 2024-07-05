// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class SqlSelect : ISql
    {
        public string SqlType { get; }
        public Table Table { get; set; }
        public DerivedTable FromDerivedTable { get; set; }
        public IList<CommonTableExpression> CommonTableExpressions { get; set; }
        public IList<Field> Fields { get; set; }
        public IList<Join> Joins { get; set; }
        public Filter Where { get; set; }
        public IList<GroupBy> GroupBys { get; set; }
        public Filter Having { get; set; }
        public List<Sort> Sorts { get; set; }
        public IList<SqlCombine> SqlCombines { get; set; }

        public SqlSelect()
        {
            this.SqlType = Constants.Select;
        }

        public SqlSelect(string from) : this()
        {
            this.Table = new Table(from);
        }

        public SqlSelect(Table table) : this()
        {
            this.Table = table;
        }

        public SqlSelect(DerivedTable fromDerivedTable) : this()
        {
            FromDerivedTable = fromDerivedTable;
        }
        
        public SqlSelect(CommonTableExpression fromCTE) : this()
        {
            FromDerivedTable = fromCTE;
            if (fromCTE.Select != null && fromCTE.Select.CommonTableExpressions != null)
            {
                foreach (var cte in fromCTE.Select.CommonTableExpressions)
                {
                    this.WithCommonTableExpression(cte);
                }
            }
        }

        public SqlSelect WithCommonTableExpression(CommonTableExpression commonTableExpression)
        {
            if (CommonTableExpressions == null)
            {
                CommonTableExpressions = new List<CommonTableExpression>();
            }
            this.CommonTableExpressions.Add(commonTableExpression);
            return this;
        }

        public SqlSelect WithField(Field field)
        {
            if (Fields == null)
            {
                Fields = new List<Field>();
            }
            this.Fields.Add(field);
            return this;
        }

        public SqlSelect WithJoin(Join join)
        {
            if (Joins == null)
            {
                Joins = new List<Join>();
            }
            this.Joins.Add(join);
            return this;
        }

        public SqlSelect WithWhere(Filter where)
        {
            this.Where = where;
            return this;
        }

        public SqlSelect WithGroupBy(GroupBy groupBy)
        {
            if (GroupBys == null)
            {
                GroupBys = new List<GroupBy>();
            }
            this.GroupBys.Add(groupBy);
            return this;
        }

        public SqlSelect WithHaving(Filter having)
        {
            this.Having = having;
            return this;
        }

        public SqlSelect WithSort(Sort sort)
        {
            if (Sorts == null)
            {
                Sorts = new List<Sort>();
            }
            this.Sorts.Add(sort);
            return this;
        }

        public SqlSelect WithCombine(SqlCombine sqlCombine)
        {
            if (SqlCombines == null)
            {
                SqlCombines = new List<SqlCombine>();
            }
            this.SqlCombines.Add(sqlCombine);
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