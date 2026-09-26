// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    [TestFixture]
    public class NestedListMapperCoverageTests
    {
        private static NestedList ChildrenNested() =>
            new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));

        [Test]
        public void Map_NullRows_ReturnsEmpty()
        {
            Assert.That(NestedListMapper.Map<ParentDto>(null, new[] { ChildrenNested() }), Is.Empty);
        }

        [Test]
        public void Map_ArrayProperty_CoercesListToArray()
        {
            var row = new Dictionary<string, object>
            {
                ["Id"] = "p1",
                ["Children"] = """[{"id":"c1"}]"""
            };

            ArrayParent mapped = NestedListMapper.Map<ArrayParent>(new[] { row }, new[] { ChildrenNested() })[0];
            Assert.That(mapped.Children, Has.Length.EqualTo(1));
            Assert.That(mapped.Children[0].Id, Is.EqualTo("c1"));
        }

        [Test]
        public void Map_UnsupportedCollectionProperty_Throws()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));
            var row = new Dictionary<string, object> { ["Id"] = "p1", ["Children"] = "[]" };

            Assert.Throws<InvalidOperationException>(() =>
                NestedListMapper.Map<HashSetParent>(new[] { row }, new[] { nested }));
        }

        [Test]
        public void Map_MissingColumn_SkipsProperty()
        {
            var row = new Dictionary<string, object> { ["Children"] = "[]" };
            ParentDto mapped = NestedListMapper.Map<ParentDto>(new[] { row }, new[] { ChildrenNested() })[0];
            Assert.That(mapped.Id, Is.Null);
            Assert.That(mapped.Children, Is.Empty);
        }

        [Test]
        public void Map_ConvertValue_Branches()
        {
            var row = new Dictionary<string, object>
            {
                ["Id"] = "p1",
                ["ExternalId"] = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                ["Status"] = 1,
                ["CreatedAt"] = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc),
                ["OffsetAt"] = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero),
                ["EventDate"] = new DateOnly(2026, 7, 31),
                ["FromDateTime"] = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                ["StartTime"] = new TimeOnly(9, 15),
                ["FromSpan"] = TimeSpan.FromHours(1.5),
                ["Flag"] = true,
                ["FlagInt"] = 0,
                ["FlagDouble"] = 1.0,
                ["Score"] = "1.25",
                ["Rate"] = "2.5",
                ["AmountF"] = "3.5",
                ["Count"] = "9",
                ["Big"] = "99",
                ["Children"] = "[]"
            };

            RichParent mapped = NestedListMapper.Map<RichParent>(new[] { row }, new[] { ChildrenNested() })[0];
            Assert.That(mapped.ExternalId, Is.EqualTo(Guid.Parse("11111111-2222-3333-4444-555555555555")));
            Assert.That(mapped.Status, Is.EqualTo(StatusKind.Active));
            Assert.That(mapped.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(mapped.OffsetAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(mapped.EventDate, Is.EqualTo(new DateOnly(2026, 7, 31)));
            Assert.That(mapped.FromDateTime, Is.EqualTo(new DateOnly(2026, 1, 2)));
            Assert.That(mapped.StartTime, Is.EqualTo(new TimeOnly(9, 15)));
            Assert.That(mapped.FromSpan, Is.EqualTo(new TimeOnly(1, 30)));
            Assert.That(mapped.Flag, Is.True);
            Assert.That(mapped.FlagInt, Is.False);
            Assert.That(mapped.FlagDouble, Is.True);
            Assert.That(mapped.Score, Is.EqualTo(1.25m));
            Assert.That(mapped.Rate, Is.EqualTo(2.5));
            Assert.That(mapped.AmountF, Is.EqualTo(3.5f));
            Assert.That(mapped.Count, Is.EqualTo(9));
            Assert.That(mapped.Big, Is.EqualTo(99L));
        }

        [Test]
        public void Map_DateTimeFormats_AndTimezoneDesignators()
        {
            AssertUtc("2026-07-31T21:00:00+02:00", new DateTime(2026, 7, 31, 19, 0, 0, DateTimeKind.Utc));
            // Not in exact-format table — exercises HasExplicitTimezoneDesignator + DateTimeOffset.TryParse.
            AssertUtc("2026-07-31T21:00:00.1234+02:00", new DateTime(2026, 7, 31, 19, 0, 0, 123, DateTimeKind.Utc).AddTicks(4000));
            AssertUtc("2026-07-31T21:00:00.123", new DateTime(2026, 7, 31, 21, 0, 0, 123, DateTimeKind.Utc));
        }

        [Test]
        public void Map_EmptyDateTime_Throws()
        {
            var row = new Dictionary<string, object> { ["CreatedAt"] = "   ", ["Children"] = "[]" };
            Assert.Throws<FormatException>(() =>
                NestedListMapper.Map<DateParent>(new[] { row }, new[] { ChildrenNested() }));
        }

        [Test]
        public void Map_UnrecognizedDateTime_Throws()
        {
            var row = new Dictionary<string, object> { ["CreatedAt"] = "not-a-date", ["Children"] = "[]" };
            Assert.Throws<FormatException>(() =>
                NestedListMapper.Map<DateParent>(new[] { row }, new[] { ChildrenNested() }));
        }

        [Test]
        public void Map_BoolStringBranches()
        {
            Assert.That(MapFlag("true"), Is.True);
            Assert.That(MapFlag("FALSE"), Is.False);
            Assert.That(MapFlag("1"), Is.True);
            Assert.That(MapFlag("0"), Is.False);
        }

        [Test]
        public void Map_EmptyBool_Throws()
        {
            var row = new Dictionary<string, object> { ["Flag"] = "  ", ["Children"] = "[]" };
            Assert.Throws<FormatException>(() =>
                NestedListMapper.Map<FlagParent>(new[] { row }, new[] { ChildrenNested() }));
        }

        [Test]
        public void ParseList_FlexibleTokens_AndRoundTripWrites()
        {
            string json = """
                [{
                  "active": true,
                  "inactive": false,
                  "asNumber": 0,
                  "asDouble": 2.5,
                  "asString": "true",
                  "optionalActive": null,
                  "optionalTruthy": "1",
                  "amount": 9.99,
                  "amountText": "1.5",
                  "optionalAmount": null,
                  "when": "2026-07-31T12:00:00Z",
                  "whenUnix": 1722441600,
                  "optionalWhen": null,
                  "optionalWhenEmpty": "",
                  "optionalWhenUnix": 1722441600,
                  "day": "2026-07-31",
                  "optionalDay": null,
                  "optionalDayEmpty": "  ",
                  "grandchildren": "[]",
                  "more": "[{\"id\":\"g2\"}]"
                }]
                """;
            var list = (List<FlexibleDto>)NestedListMapper.ParseList(typeof(FlexibleDto), json);
            Assert.That(list[0].Active, Is.True);
            Assert.That(list[0].Inactive, Is.False);
            Assert.That(list[0].AsNumber, Is.False);
            Assert.That(list[0].AsDouble, Is.True);
            Assert.That(list[0].AsString, Is.True);
            Assert.That(list[0].OptionalActive, Is.Null);
            Assert.That(list[0].OptionalTruthy, Is.True);
            Assert.That(list[0].Amount, Is.EqualTo(9.99m));
            Assert.That(list[0].AmountText, Is.EqualTo(1.5m));
            Assert.That(list[0].OptionalAmount, Is.Null);
            Assert.That(list[0].When.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(list[0].WhenUnix.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(list[0].OptionalWhen, Is.Null);
            Assert.That(list[0].OptionalWhenEmpty, Is.Null);
            Assert.That(list[0].OptionalWhenUnix, Is.Not.Null);
            Assert.That(list[0].Day, Is.EqualTo(new DateOnly(2026, 7, 31)));
            Assert.That(list[0].OptionalDay, Is.Null);
            Assert.That(list[0].OptionalDayEmpty, Is.Null);
            Assert.That(list[0].Grandchildren, Is.Empty);
            Assert.That(list[0].More, Has.Count.EqualTo(1));

            // Hit converter Write paths (including nullables).
            string roundTrip = JsonSerializer.Serialize(list, NestedListMapper.SharedJsonOptions);
            Assert.That(roundTrip, Does.Contain("active"));
            var again = (List<FlexibleDto>)NestedListMapper.ParseList(typeof(FlexibleDto), roundTrip);
            Assert.That(again[0].OptionalActive, Is.Null);
            Assert.That(again[0].OptionalAmount, Is.Null);
            Assert.That(again[0].OptionalWhen, Is.Null);
            Assert.That(again[0].OptionalDay, Is.Null);
        }

        [Test]
        public void ParseList_StringifiedEmptyGrandchildren_ReturnsEmpty()
        {
            string json = """[{"grandchildren":"  "}]""";
            var list = (List<FlexibleDto>)NestedListMapper.ParseList(typeof(FlexibleDto), json);
            Assert.That(list[0].Grandchildren, Is.Empty);
        }

        [Test]
        public void ParseList_NullElementType_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => NestedListMapper.ParseList(null, "[]"));
        }

        [Test]
        public void ParseList_UnexpectedListToken_Throws()
        {
            Assert.Throws<JsonException>(() =>
                NestedListMapper.ParseList(typeof(FlexibleDto), """[{"grandchildren":{}}]"""));
        }

        [Test]
        public void ParseList_UnexpectedBoolToken_Throws()
        {
            Assert.Throws<JsonException>(() =>
                NestedListMapper.ParseList(typeof(FlexibleDto), """[{"active":[]}]"""));
        }

        [Test]
        public void ParseList_UnexpectedDecimalToken_Throws()
        {
            Assert.Throws<JsonException>(() =>
                NestedListMapper.ParseList(typeof(FlexibleDto), """[{"amount":true}]"""));
        }

        [Test]
        public void ParseList_UnexpectedDateTimeToken_Throws()
        {
            Assert.Throws<JsonException>(() =>
                NestedListMapper.ParseList(typeof(FlexibleDto), """[{"when":true}]"""));
        }

        [Test]
        public void ParseList_UnexpectedDateOnlyToken_Throws()
        {
            Assert.Throws<JsonException>(() =>
                NestedListMapper.ParseList(typeof(FlexibleDto), """[{"day":1}]"""));
        }

        [Test]
        public void ParseList_NullableUnexpectedTokens_Throw()
        {
            Assert.Throws<JsonException>(() =>
                NestedListMapper.ParseList(typeof(FlexibleDto), """[{"optionalWhen":true}]"""));
            Assert.Throws<JsonException>(() =>
                NestedListMapper.ParseList(typeof(FlexibleDto), """[{"optionalDay":1}]"""));
        }

        [Test]
        public void Map_EmptyDateOnlyAndTimeOnly_Throw()
        {
            var nested = ChildrenNested();
            Assert.Throws<FormatException>(() =>
                NestedListMapper.Map<DateOnlyParent>(
                    new[] { new Dictionary<string, object> { ["Day"] = "  ", ["Children"] = "[]" } },
                    new[] { nested }));
            Assert.Throws<FormatException>(() =>
                NestedListMapper.Map<TimeOnlyParent>(
                    new[] { new Dictionary<string, object> { ["Start"] = "", ["Children"] = "[]" } },
                    new[] { nested }));
        }

        [Test]
        public void Map_DateOnlyFromDateTimeString_AndChangeType()
        {
            var nested = ChildrenNested();
            var row = new Dictionary<string, object>
            {
                ["Day"] = "2026-07-31T00:00:00",
                ["ByteVal"] = (byte)7,
                ["Children"] = "[]"
            };
            var mapped = NestedListMapper.Map<DateOnlyParent>(new[] { row }, new[] { nested })[0];
            Assert.That(mapped.Day, Is.EqualTo(new DateOnly(2026, 7, 31)));
            Assert.That(mapped.ByteVal, Is.EqualTo(7));
        }

        [Test]
        public void Map_GuidString_AndDbNullSkipped()
        {
            var nested = ChildrenNested();
            var row = new Dictionary<string, object>
            {
                ["ExternalId"] = "11111111-2222-3333-4444-555555555555",
                ["Name"] = DBNull.Value,
                ["Children"] = "[]"
            };
            var mapped = NestedListMapper.Map<GuidParent>(new[] { row }, new[] { nested })[0];
            Assert.That(mapped.ExternalId, Is.EqualTo(Guid.Parse("11111111-2222-3333-4444-555555555555")));
            Assert.That(mapped.Name, Is.Null);
        }

        [Test]
        public void ParseList_NullListToken_ReturnsNullProperty()
        {
            var list = (List<NullListDto>)NestedListMapper.ParseList(
                typeof(NullListDto), """[{"items":null}]""");
            Assert.That(list[0].Items, Is.Null);
        }

        [Test]
        public void ParseList_NullableDecimalWrite_HasValue()
        {
            var list = (List<FlexibleDto>)NestedListMapper.ParseList(
                typeof(FlexibleDto),
                """[{"active":false,"amount":1,"when":"2026-01-01T00:00:00Z","day":"2026-01-01","optionalAmount":3.5}]""");
            string json = JsonSerializer.Serialize(list, NestedListMapper.SharedJsonOptions);
            Assert.That(json, Does.Contain("3.5").Or.Contain("3.50"));
        }

        private sealed class DateOnlyParent
        {
            public DateOnly Day { get; set; }
            public byte ByteVal { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class TimeOnlyParent
        {
            public TimeOnly Start { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class GuidParent
        {
            public Guid ExternalId { get; set; }
            public string Name { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class NullListDto
        {
            public List<ChildDto> Items { get; set; }
        }

        private static void AssertUtc(string text, DateTime expected)
        {
            var row = new Dictionary<string, object> { ["CreatedAt"] = text, ["Children"] = "[]" };
            DateParent mapped = NestedListMapper.Map<DateParent>(new[] { row }, new[] { ChildrenNested() })[0];
            Assert.That(mapped.CreatedAt, Is.EqualTo(expected));
            Assert.That(mapped.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        private static bool MapFlag(string text)
        {
            var row = new Dictionary<string, object> { ["Flag"] = text, ["Children"] = "[]" };
            return NestedListMapper.Map<FlagParent>(new[] { row }, new[] { ChildrenNested() })[0].Flag;
        }

        private enum StatusKind { Active = 1 }

        private sealed class ParentDto
        {
            public string Id { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class ArrayParent
        {
            public string Id { get; set; }
            public ChildDto[] Children { get; set; }
        }

        private sealed class HashSetParent
        {
            public string Id { get; set; }
            public HashSet<ChildDto> Children { get; set; }
        }

        private sealed class DateParent
        {
            public DateTime CreatedAt { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class FlagParent
        {
            public bool Flag { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class RichParent
        {
            public string Id { get; set; }
            public Guid ExternalId { get; set; }
            public StatusKind Status { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime OffsetAt { get; set; }
            public DateOnly EventDate { get; set; }
            public DateOnly FromDateTime { get; set; }
            public TimeOnly StartTime { get; set; }
            public TimeOnly FromSpan { get; set; }
            public bool Flag { get; set; }
            public bool FlagInt { get; set; }
            public bool FlagDouble { get; set; }
            public decimal Score { get; set; }
            public double Rate { get; set; }
            public float AmountF { get; set; }
            public int Count { get; set; }
            public long Big { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class FlexibleDto
        {
            public bool Active { get; set; }
            public bool Inactive { get; set; }
            public bool AsNumber { get; set; }
            public bool AsDouble { get; set; }
            public bool AsString { get; set; }
            public bool? OptionalActive { get; set; }
            public bool? OptionalTruthy { get; set; }
            public decimal Amount { get; set; }
            public decimal AmountText { get; set; }
            public decimal? OptionalAmount { get; set; }
            public DateTime When { get; set; }
            public DateTime WhenUnix { get; set; }
            public DateTime? OptionalWhen { get; set; }
            public DateTime? OptionalWhenEmpty { get; set; }
            public DateTime? OptionalWhenUnix { get; set; }
            public DateOnly Day { get; set; }
            public DateOnly? OptionalDay { get; set; }
            public DateOnly? OptionalDayEmpty { get; set; }
            public List<ChildDto> Grandchildren { get; set; }
            public List<ChildDto> More { get; set; }
        }

        private sealed class ChildDto
        {
            public string Id { get; set; }
        }
    }
}
