// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
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
    }
}