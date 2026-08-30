// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Collections.Generic;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    [TestFixture]
    public class SqlJoinAndWriteModelTests
    {
        private static readonly ISqlFacade Sql = SqlFacadeFactory.GetSqlFacade(
            new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:"));

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            using var con = (Microsoft.Data.Sqlite.SqliteConnection)Sql.GetSqlFacadeConfig().GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS canvas(id TEXT PRIMARY KEY, color TEXT);"
                + "CREATE TABLE IF NOT EXISTS tags(id TEXT PRIMARY KEY, canvas_id TEXT, label TEXT);";
            cmd.ExecuteNonQuery();
        }

        [SetUp]
        public void SetUp()
        {
            Sql.ExecuteSqlWrite(new SqlDelete("tags"));
            Sql.ExecuteSqlWrite(new SqlDelete("canvas"));
        }

        [Test]
        public void Select_LeftJoinOnCte_CompilesAndRuns()
        {
            Sql.ExecuteSqlWrite(new SqlInsert("canvas").WithField(new Field("id", "1")).WithField(new Field("color", "red")));
            Sql.ExecuteSqlWrite(new SqlInsert("tags").WithField(new Field("id", "t1")).WithField(new Field("canvas_id", "1")).WithField(new Field("label", "alpha")));

            var palette = new CommonTableExpression("select 'red' as match_color", "palette");
            var select = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("t.label", "Label"))
                .WithCommonTableExpression(palette)
                .WithJoin(new Join(palette, new Expression("palette.match_color", "v.color"), JoinType.LeftJoin))
                .WithJoin(new Join(new Table("tags", "t"), new Expression("t.canvas_id", "v.id"), JoinType.LeftJoin));

            string label = Sql.GetSingleResult<string>(select);
            Assert.That(label, Is.EqualTo("alpha"));
        }

        [Test]
        public void SqlDelete_WithCommonTableExpression_Compiles()
        {
            var delete = new SqlDelete("canvas")
                .WithCommonTableExpression(new CommonTableExpression("select 'x' as id", " doomed"))
                .WithFilter(new Expression("id", "missing"));
            Assert.That(Sql.GetSql(delete, false).ToUpperInvariant(), Does.Contain("WITH"));
        }

        [Test]
        public void SqlUpdate_WithCommonTableExpression_Compiles()
        {
            var update = new SqlUpdate("canvas")
                .WithCommonTableExpression(new CommonTableExpression("select 'x' as id", "seed"))
                .WithField(new Field("color", "blue"))
                .WithFilter(new Expression("id", "missing"));
            Assert.That(Sql.GetSql(update, false).ToUpperInvariant(), Does.Contain("WITH"));
        }
    }

    [TestFixture]
    public class NestedListMapperEdgeTests
    {
        [Test]
        public void ParseList_NullElementType_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => NestedListMapper.ParseList(null, "[]"));
        }

        [Test]
        public void Map_IncompatibleChildrenPropertyType_Throws()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));

            var row = new Dictionary<string, object>
            {
                ["Id"] = "p1",
                ["Children"] = "[]"
            };

            Assert.Throws<InvalidOperationException>(() =>
                NestedListMapper.Map<ParentWithStringChildren>(new[] { row }, new[] { nested }));
        }

        [Test]
        public void Map_SkipsNullScalarColumns()
        {
            var nested = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "c")).WithField(new Field("c.id", "id")),
                new Expression("c.parent_id", "p.id"));

            var row = new Dictionary<string, object>
            {
                ["Id"] = "p1",
                ["Name"] = DBNull.Value,
                ["Children"] = "[]"
            };

            ParentWithOptionalName mapped = NestedListMapper.Map<ParentWithOptionalName>(new[] { row }, new[] { nested })[0];
            Assert.That(mapped.Id, Is.EqualTo("p1"));
            Assert.That(mapped.Name, Is.Null);
        }

        [Test]
        public void ParseList_NullableJsonFields_Deserialize()
        {
            string json = """[{"id":"c1","label":"x","active":null,"amount":null,"when":null}]""";
            var list = (List<NullableChildDto>)NestedListMapper.ParseList(typeof(NullableChildDto), json);
            Assert.That(list[0].Active, Is.Null);
            Assert.That(list[0].Amount, Is.Null);
            Assert.That(list[0].When, Is.Null);
        }

        private sealed class ParentWithStringChildren
        {
            public string Id { get; set; }
            public string Children { get; set; }
        }

        private sealed class ParentWithOptionalName
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class ChildDto
        {
            public string Id { get; set; }
        }

        private sealed class NullableChildDto
        {
            public string Id { get; set; }
            public string Label { get; set; }
            public bool? Active { get; set; }
            public decimal? Amount { get; set; }
            public DateTime? When { get; set; }
        }
    }
}
