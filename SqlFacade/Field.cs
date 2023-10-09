// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    public class Field
    {
        public string Name { get; set; }
        public object Value { get; set; }
        public bool IsRaw { get; set; }

        public Field()
        {
            IsRaw = false;
        }

        public Field(string name, object value = null, bool isRaw = false) : this()
        {
            this.Name = name;
            this.Value = value;
            this.IsRaw = isRaw;
        }
    }
}