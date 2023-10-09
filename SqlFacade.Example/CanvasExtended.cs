// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
    using System.Text.Json;

    public class CanvasExtended : Canvas
    {
        public string ExtraData { get; set; }

        public override string ToString()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.IgnoreNullValues = true;
            return JsonSerializer.Serialize(this, options);
        }
    }
}