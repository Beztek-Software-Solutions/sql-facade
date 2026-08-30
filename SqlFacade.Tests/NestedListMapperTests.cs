// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Collections.Generic;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    [TestFixture]
    public class NestedListMapperTests
    {
        [Test]
        public void ParseList_EmptyOrNull_ReturnsEmptyList()
        {
            var list = (List<ChildDto>)NestedListMapper.ParseList(typeof(ChildDto), null);
            Assert.That(list, Is.Not.Null);
            Assert.That(list, Is.Empty);

            list = (List<ChildDto>)NestedListMapper.ParseList(typeof(ChildDto), "[]");
            Assert.That(list, Is.Empty);

            list = (List<ChildDto>)NestedListMapper.ParseList(typeof(ChildDto), "  ");
            Assert.That(list, Is.Empty);
        }

        [Test]
        public void ParseList_ValidJson_DeserializesElements()
        {
            string json = """[{"id":"c1","label":"first","amount":12.5,"active":true}]""";
            var list = (List<TypedChildDto>)NestedListMapper.ParseList(typeof(TypedChildDto), json);

            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].Id, Is.EqualTo("c1"));
            Assert.That(list[0].Label, Is.EqualTo("first"));
            Assert.That(list[0].Amount, Is.EqualTo(12.5m));
            Assert.That(list[0].Active, Is.True);
        }

        [Test]
        public void Map_ScalarTypes_ConvertsParentColumns()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));

            var row = new Dictionary<string, object>
            {
                ["Id"] = "p1",
                ["Name"] = "Parent",
                ["CreatedAt"] = "2026-07-31 21:00:00",
                ["EventDate"] = "2026-07-31",
                ["StartTime"] = "14:30:00",
                ["ExternalId"] = Guid.Parse("11111111-2222-3333-4444-555555555555").ToString(),
                ["Status"] = "Active",
                ["Score"] = "42.5",
                ["Children"] = "[]"
            };

            ParentScalars mapped = NestedListMapper.Map<ParentScalars>(new[] { row }, new[] { nested })[0];

            Assert.That(mapped.Id, Is.EqualTo("p1"));
            Assert.That(mapped.Name, Is.EqualTo("Parent"));
            Assert.That(mapped.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(mapped.EventDate, Is.EqualTo(new DateOnly(2026, 7, 31)));
            Assert.That(mapped.StartTime, Is.EqualTo(new TimeOnly(14, 30, 0)));
            Assert.That(mapped.ExternalId, Is.EqualTo(Guid.Parse("11111111-2222-3333-4444-555555555555")));
            Assert.That(mapped.Status, Is.EqualTo(StatusKind.Active));
            Assert.That(mapped.Score, Is.EqualTo(42.5m));
            Assert.That(mapped.Children, Is.Not.Null.And.Empty);
        }

        [Test]
        public void Map_MissingNestedProperty_Throws()
        {
            var nested = new NestedList<ChildDto>("MissingAlias",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));

            var row = new Dictionary<string, object> { ["Id"] = "p1", ["MissingAlias"] = "[]" };

            Assert.Throws<InvalidOperationException>(() =>
                NestedListMapper.Map<ParentScalars>(new[] { row }, new[] { nested }));
        }

        [Test]
        public void Map_NullableAndNumericScalars_Converts()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));

            var row = new Dictionary<string, object>
            {
                ["Id"] = "p1",
                ["OptionalScore"] = "99",
                ["Rate"] = "3.14",
                ["Count"] = "7",
                ["Flag"] = 1L,
                ["Children"] = "[]"
            };

            NullableScalars mapped = NestedListMapper.Map<NullableScalars>(new[] { row }, new[] { nested })[0];
            Assert.That(mapped.OptionalScore, Is.EqualTo(99));
            Assert.That(mapped.Rate, Is.EqualTo(3.14));
            Assert.That(mapped.Count, Is.EqualTo(7L));
            Assert.That(mapped.Flag, Is.True);
        }

        [Test]
        public void Map_DateTimeWithTimezone_ParsesUtc()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));

            var row = new Dictionary<string, object>
            {
                ["Id"] = "p1",
                ["CreatedAt"] = "2026-07-31T21:00:00Z",
                ["Children"] = "[]"
            };

            DateParent mapped = NestedListMapper.Map<DateParent>(new[] { row }, new[] { nested })[0];
            Assert.That(mapped.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        private sealed class NullableScalars
        {
            public string Id { get; set; }
            public int? OptionalScore { get; set; }
            public double Rate { get; set; }
            public long Count { get; set; }
            public bool Flag { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class DateParent
        {
            public string Id { get; set; }
            public DateTime CreatedAt { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        [Test]
        public void Map_NonDictionaryRow_Throws()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));

            Assert.Throws<InvalidOperationException>(() =>
                NestedListMapper.Map<ParentScalars>(new object[] { "not-a-row" }, new[] { nested }));
        }

        private enum StatusKind
        {
            Active = 1
        }

        private sealed class ParentScalars
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateOnly EventDate { get; set; }
            public TimeOnly StartTime { get; set; }
            public Guid ExternalId { get; set; }
            public StatusKind Status { get; set; }
            public decimal Score { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class TypedChildDto
        {
            public string Id { get; set; }
            public string Label { get; set; }
            public decimal Amount { get; set; }
            public bool Active { get; set; }
        }

        private sealed class ChildDto
        {
            public string Id { get; set; }
        }
    }
}
