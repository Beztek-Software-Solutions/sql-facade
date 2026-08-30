// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>Simple row DTO for canvas selects (property names match Field aliases / column names).</summary>
    public class Canvas
    {
        public string Id { get; set; }
        public string Color { get; set; }

        public override string ToString()
        {
            JsonSerializerOptions options = new JsonSerializerOptions {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            return JsonSerializer.Serialize(this, options);
        }
    }
}