// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    public class Table
    {
        public string Name { get; set; }
        public object Alias { get; set; }
        public bool IsRaw { get; set; }

        public Table()
        {
            IsRaw = false;
        }

        public Table(string name, object alias = null, bool isRaw = false) : this()
        {
            this.Name = name;
            this.Alias = alias;
            this.IsRaw = isRaw;
        }
    }
}