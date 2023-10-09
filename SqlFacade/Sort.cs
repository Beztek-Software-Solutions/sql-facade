// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    public class Sort
    {
        public string Name { get; set; }

        public bool IsAscending { get; set; }

        public Sort()
        {
            this.IsAscending = true;
        }

        public Sort(string name, bool isAscending = true)
        {
            this.Name = name;
            this.IsAscending = isAscending;
        }
    }
}