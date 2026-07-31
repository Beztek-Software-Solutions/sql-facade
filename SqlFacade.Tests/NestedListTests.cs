// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using Beztek.Facade.Sql;
    using Microsoft.Data.Sqlite;
    using NUnit.Framework;
    using SqlDbType = Beztek.Facade.Sql.DbType;

    [TestFixture]
    public class NestedListTests
    {
        private ISqlFacade _sqlite;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _sqlite = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(SqlDbType.SQLITE, "Data Source=:memory:"));
            using IDbConnection con = _sqlite.GetSqlFacadeConfig().GetConnection();
            using var cmd = new SqliteCommand(
                "CREATE TABLE parent(id TEXT PRIMARY KEY, name TEXT);"
                + "CREATE TABLE child(id TEXT PRIMARY KEY, parent_id TEXT, label TEXT, sort_ord INT);"
                , (SqliteConnection)con);
            cmd.ExecuteNonQuery();
        }

        [SetUp]
        public void SetUp()
        {
            using IDbConnection con = _sqlite.GetSqlFacadeConfig().GetConnection();
            using var cmd = new SqliteCommand("DELETE FROM child; DELETE FROM parent;", (SqliteConnection)con);
            cmd.ExecuteNonQuery();
        }

        private static NestedList SampleNestedList() =>
            new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "ch"))
                    .WithField(new Field("ch.id", "id"))
                    .WithField(new Field("ch.label", "label"))
                    .WithSort(new Sort("ch.sort_ord")),
                new Expression("ch.parent_id", "p.id"));

        [Test]
        public void ToSql_Postgres_UsesJsonAggAndRowToJson()
        {
            string sql = SampleNestedList().ToSql(SqlDbType.POSTGRES);
            Assert.That(sql, Does.Contain("json_agg"));
            Assert.That(sql, Does.Contain("row_to_json"));
            Assert.That(sql, Does.Contain("FROM \"child\" AS \"ch\"").Or.Contain("FROM child AS ch"));
            Assert.That(sql, Does.Contain("ORDER BY \"ch\".\"sort_ord\""));
            Assert.That(sql, Does.Contain("json_build_array()"));
        }

        [Test]
        public void ToSql_Sqlite_UsesJsonGroupArray()
        {
            string sql = SampleNestedList().ToSql(SqlDbType.SQLITE);
            Assert.That(sql, Does.Contain("json_group_array"));
            Assert.That(sql, Does.Contain("json_object"));
            Assert.That(sql, Does.Contain("ORDER BY \"ch\".\"sort_ord\""));
            Assert.That(sql, Does.Contain("json_array()"));
        }

        [Test]
        public void ToSql_SqlServer_UsesForJsonPath()
        {
            string sql = SampleNestedList().ToSql(SqlDbType.SQLSERVER);
            Assert.That(sql, Does.Contain("FOR JSON PATH, INCLUDE_NULL_VALUES"));
            Assert.That(sql, Does.Contain("AS [id]").Or.Contain("AS id"));
            Assert.That(sql, Does.Contain("ORDER BY"));
            Assert.That(sql, Does.Contain("CHAR(91)+CHAR(93)"));
        }

        [Test]
        public void GetSql_Sqlite_DoesNotMangleEmptyArrayLiteral()
        {
            var select = new SqlSelect(new Table("parent", "p"))
                .WithField(new Field("p.id"))
                .WithNestedList(SampleNestedList());
            string sql = _sqlite.GetSql(select, false);
            Assert.That(sql, Does.Contain("json_array()"));
            Assert.That(sql, Does.Contain("Children"));
            Assert.That(sql, Does.Not.Contain("'\"\"'"));
        }

        [Test]
        public void GetSql_PostgresAndSqlServer_CompileWithoutConnection()
        {
            var select = new SqlSelect(new Table("parent", "p"))
                .WithField(new Field("p.id"))
                .WithNestedList(SampleNestedList());

            var pg = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(SqlDbType.POSTGRES, "Host=localhost;Database=x;Username=x;Password=x"));
            Assert.That(pg.GetSql(select, false), Does.Contain("json_agg"));
            Assert.That(pg.GetSql(select, false), Does.Contain("row_to_json"));

            var mssql = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(SqlDbType.SQLSERVER, "Server=localhost;Database=x;Trusted_Connection=True"));
            Assert.That(mssql.GetSql(select, false), Does.Contain("FOR JSON PATH"));
        }

        [Test]
        public void Sqlite_Runtime_MapsTypedChildListOnParent()
        {
            _sqlite.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("parent").WithField(new Field("id", "p1")).WithField(new Field("name", "Parent One")),
                new SqlInsert("parent").WithField(new Field("id", "p2")).WithField(new Field("name", "Parent Two")),
                new SqlInsert("child").WithField(new Field("id", "c2")).WithField(new Field("parent_id", "p1"))
                    .WithField(new Field("label", "second")).WithField(new Field("sort_ord", 2)),
                new SqlInsert("child").WithField(new Field("id", "c1")).WithField(new Field("parent_id", "p1"))
                    .WithField(new Field("label", "first")).WithField(new Field("sort_ord", 1)),
            });

            var select = new SqlSelect(new Table("parent", "p"))
                .WithField(new Field("p.id", "Id"))
                .WithField(new Field("p.name", "Name"))
                .WithNestedList(SampleNestedList())
                .WithSort(new Sort("p.id"));

            IList<ParentWithChildren> rows = _sqlite.GetResults<ParentWithChildren>(select);
            Assert.That(rows.Count, Is.EqualTo(2));

            ParentWithChildren p1 = rows.Single(r => r.Id == "p1");
            ParentWithChildren p2 = rows.Single(r => r.Id == "p2");

            Assert.That(p1.Children, Is.Not.Null);
            Assert.That(p1.Children.Count, Is.EqualTo(2));
            Assert.That(p1.Children[0].Id, Is.EqualTo("c1"));
            Assert.That(p1.Children[0].Label, Is.EqualTo("first"));
            Assert.That(p1.Children[1].Id, Is.EqualTo("c2"));

            Assert.That(p2.Children, Is.Not.Null);
            Assert.That(p2.Children, Is.Empty);
        }

        [Test]
        public void Sqlite_Runtime_MapsArrayProperty()
        {
            _sqlite.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("parent").WithField(new Field("id", "p1")).WithField(new Field("name", "Parent One")),
                new SqlInsert("child").WithField(new Field("id", "c1")).WithField(new Field("parent_id", "p1"))
                    .WithField(new Field("label", "first")).WithField(new Field("sort_ord", 1)),
            });

            var select = new SqlSelect(new Table("parent", "p"))
                .WithField(new Field("p.id", "Id"))
                .WithField(new Field("p.name", "Name"))
                .WithNestedList(new NestedList<ChildDto>("Children",
                    new SqlSelect(new Table("child", "ch"))
                        .WithField(new Field("ch.id", "id"))
                        .WithField(new Field("ch.label", "label")),
                    new Expression("ch.parent_id", "p.id")));

            ParentWithChildrenArray row = _sqlite.GetSingleResult<ParentWithChildrenArray>(select);
            Assert.That(row.Children, Is.Not.Null);
            Assert.That(row.Children.Length, Is.EqualTo(1));
            Assert.That(row.Children[0].Label, Is.EqualTo("first"));
        }

        [Test]
        public void SqlSelect_RoundTripsNestedListViaJson()
        {
            var select = new SqlSelect(new Table("parent", "p"))
                .WithField(new Field("p.id"))
                .WithNestedList(SampleNestedList());
            var restored = (SqlSelect)_sqlite.DeserializeFromJson(select.ToString());
            Assert.That(restored.NestedLists, Is.Not.Null);
            Assert.That(restored.NestedLists.Count, Is.EqualTo(1));
            Assert.That(restored.NestedLists[0].ResultAlias, Is.EqualTo("Children"));
            Assert.That(restored.NestedLists[0].ElementTypeName, Does.Contain("ChildDto"));
            Assert.That(restored.NestedLists[0].Select, Is.Not.Null);
            Assert.That(restored.NestedLists[0].Select.Fields.Count, Is.EqualTo(2));
            Assert.That(restored.NestedLists[0].Correlate, Is.Not.Null);
            Assert.That(restored.NestedLists[0].Correlate.Expressions, Is.Not.Null);
            Assert.That(restored.NestedLists[0].Correlate.Expressions.Count, Is.EqualTo(1));
            Assert.That(restored.NestedLists[0].Correlate.Expressions[0].Name, Is.EqualTo("ch.parent_id"));
            Assert.That(restored.NestedLists[0].Correlate.Expressions[0].Value?.ToString(), Is.EqualTo("p.id"));
        }

        [Test]
        public void Correlate_FilterWithOrNestedExpressions_EmitsOr()
        {
            var agg = new NestedList<ChildDto>("Children",
                new SqlSelect(new Table("child", "ch"))
                    .WithField(new Field("ch.id", "id")),
                new Filter().WithExpression(new Expression("ch.parent_id", "p.id"))
                    .WithExpression(new Expression("ch.label", "p.name").WithLogicalRelation(LogicalRelation.Or)));
            string sql = agg.ToSql(SqlDbType.SQLITE);
            Assert.That(sql, Does.Contain("ch.parent_id = p.id"));
            Assert.That(sql, Does.Contain("ch.label = p.name"));
            Assert.That(sql.ToLowerInvariant(), Does.Contain(" or "));
        }

        [Test]
        public void Constructor_RequiresCorrelate()
        {
            var child = new SqlSelect(new Table("child", "ch")).WithField(new Field("ch.id", "id"));
            Assert.Throws<ArgumentNullException>(() =>
                new NestedList<ChildDto>("Children", child, (Expression)null));
            Assert.Throws<ArgumentNullException>(() =>
                new NestedList<ChildDto>("Children", child, (Filter)null));
            Assert.Throws<ArgumentException>(() =>
                new NestedList<ChildDto>("Children", child, new Filter()));
        }

        [Test]
        public void Correlate_JoinStyleExpression_EmitsColumnEqualsColumnWhere()
        {
            string sql = SampleNestedList().ToSql(SqlDbType.SQLITE);
            Assert.That(sql, Does.Contain("ch.parent_id = p.id"));
            Assert.That(sql, Does.Contain("WHERE"));
        }

        private sealed class ParentWithChildren
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public List<ChildDto> Children { get; set; }
        }

        private sealed class ParentWithChildrenArray
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public ChildDto[] Children { get; set; }
        }

        private sealed class ChildDto
        {
            public string Id { get; set; }
            public string Label { get; set; }
        }
    }
}
