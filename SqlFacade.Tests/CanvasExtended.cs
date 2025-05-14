// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    public class CanvasExtended : Canvas
    {
        public string ExtraData { get; set; }

        public override string ToString()
        {
            JsonSerializerOptions options = new JsonSerializerOptions {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            return JsonSerializer.Serialize(this, options);
        }

        public override bool Equals(Object obj)
        {
            if (obj == null || !(obj is CanvasExtended))
                return false;
            else
                return base.Equals(obj) && String.Equals(ExtraData, ((CanvasExtended)obj).ExtraData);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode() ^ ExtraData.GetHashCode();
        }
    }
}