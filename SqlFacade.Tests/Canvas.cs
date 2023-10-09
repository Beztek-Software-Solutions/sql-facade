// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Text.Json;

    public class Canvas
    {
        public string Id { get; set; }
        public string Color { get; set; }

        public override string ToString()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.IgnoreNullValues = true;
            return JsonSerializer.Serialize(this, options);
        }

        public override bool Equals(Object obj)
        {
            if (obj == null || !(obj is Canvas))
                return false;
            else
                return string.Equals(Id, ((Canvas)obj).Id) && String.Equals(Color, ((Canvas)obj).Color);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode() ^ Color.GetHashCode();
        }
    }
}