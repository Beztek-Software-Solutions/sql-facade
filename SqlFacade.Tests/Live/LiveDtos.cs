// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System;
    using System.Collections.Generic;

    public sealed class LiveCanvasRow
    {
        public string Id { get; set; }
        public string Color { get; set; }
        public int Ordering { get; set; }
    }

    public sealed class LiveCanvasWithStrokes
    {
        public string Id { get; set; }
        public string Color { get; set; }
        public List<LiveStrokeDto> Strokes { get; set; }
    }

    public sealed class LiveStrokeDto
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public int SortOrd { get; set; }
        public List<LiveTagDto> Tags { get; set; }
    }

    public sealed class LiveTagDto
    {
        public string Id { get; set; }
        public string Tag { get; set; }
    }

    public sealed class LiveEntityRow
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
