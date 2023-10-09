// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    public class DerivedTable
    {
        public SqlSelect Select { get; set; }
        public string Alias { get; set; }

        public DerivedTable() { }

        public DerivedTable(SqlSelect select, string alias)
        {
            this.Select = select;
            this.Alias = alias;
        }
    }
}