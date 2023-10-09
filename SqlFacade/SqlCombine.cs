// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    public class SqlCombine
    {
        public SqlSelect SqlSelect { get; set; }
        public SqlRelation SqlRelation { get; set; }

        public SqlCombine() { }

        public SqlCombine(SqlSelect sqlSelect, SqlRelation sqlRelation)
        {
            SqlSelect = sqlSelect;
            SqlRelation = sqlRelation;
        }
    }
}