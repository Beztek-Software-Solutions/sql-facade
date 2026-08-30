// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>Canvas plus joined/derived ExtraData (e.g. LeftJoin / derived table demos).</summary>
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
    }
}