// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>Parent row with a typed child list from <see cref="NestedList"/> (List&lt;T&gt;).</summary>
    public class CanvasWithStrokes
    {
        public string Id { get; set; }
        public string Color { get; set; }
        /// <summary>Filled by NestedList with ResultAlias "Strokes".</summary>
        public List<StrokeDto> Strokes { get; set; }

        public override string ToString()
        {
            JsonSerializerOptions options = new JsonSerializerOptions {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            return JsonSerializer.Serialize(this, options);
        }
    }

    /// <summary>Same NestedList mapping as <see cref="CanvasWithStrokes"/>, but onto a <c>T[]</c> property.</summary>
    public class CanvasWithStrokeArray
    {
        public string Id { get; set; }
        public string Color { get; set; }
        public StrokeDto[] Strokes { get; set; }
    }

    /// <summary>Parent whose strokes include a grandchild NestedList of tags.</summary>
    public class CanvasWithTaggedStrokes
    {
        public string Id { get; set; }
        public string Color { get; set; }
        public List<StrokeWithTagsDto> Strokes { get; set; }
    }

    /// <summary>Child DTO for NestedList; JSON keys match Field aliases (id, label, sortOrd).</summary>
    public class StrokeDto
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public int SortOrd { get; set; }
    }

    /// <summary>Stroke DTO that also hosts a NestedList of <see cref="StrokeTagDto"/>.</summary>
    public class StrokeWithTagsDto
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public int SortOrd { get; set; }
        /// <summary>Filled by NestedList with ResultAlias "Tags".</summary>
        public List<StrokeTagDto> Tags { get; set; }
    }

    /// <summary>Grandchild NestedList element.</summary>
    public class StrokeTagDto
    {
        public string Id { get; set; }
        public string Tag { get; set; }
    }
}
