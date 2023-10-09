// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    public class GroupBy
    {
        public bool IsRaw { get; set; }
        public string Value { get; set; }

        public GroupBy()
        {
            IsRaw = false;
        }

        public GroupBy(string value, bool isRaw = false)
        {
            this.IsRaw = isRaw;
            this.Value = value;
        }
    }
}