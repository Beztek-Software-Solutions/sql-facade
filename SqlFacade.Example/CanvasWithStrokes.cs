// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>Parent row with a typed child list from <see cref="NestedList"/>.</summary>
    public class CanvasWithStrokes
    {
        public string Id { get; set; }
        public string Color { get; set; }
        public List<StrokeDto> Strokes { get; set; }

        public override string ToString()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.IgnoreNullValues = true;
            return JsonSerializer.Serialize(this, options);
        }
    }

    public class StrokeDto
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public int SortOrd { get; set; }
    }
}
