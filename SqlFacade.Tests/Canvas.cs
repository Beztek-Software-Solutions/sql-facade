// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class Canvas
    {
        public string Id { get; set; }
        public string Color { get; set; }
        public int Ordering { get; set; }

        public override string ToString()
        {
            JsonSerializerOptions options = new JsonSerializerOptions {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            return JsonSerializer.Serialize(this, options);
        }

        public override bool Equals(Object obj)
        {
            if (obj == null || !(obj is Canvas))
                return false;
            else
                return string.Equals(Id, ((Canvas)obj).Id) && String.Equals(Color, ((Canvas)obj).Color) && String.Equals(Ordering, ((Canvas)obj).Ordering);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode() ^ Color.GetHashCode() ^ Ordering.GetHashCode();
        }
    }
}