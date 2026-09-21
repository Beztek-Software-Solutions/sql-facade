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
    public class GuidInListTests
    {
        private static readonly Guid IdAlpha = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid IdBeta = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid IdGamma = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        private ISqlFacade _sqlite;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // SQLite has no UUID type — store as TEXT (same pattern as most non-Postgres/SQL Server engines).
            _sqlite = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(SqlDbType.SQLITE, "Data Source=:memory:"));
            using IDbConnection con = _sqlite.GetSqlFacadeConfig().GetConnection();
            using var cmd = new SqliteCommand(
                "CREATE TABLE entity(id TEXT PRIMARY KEY, name TEXT);",
                (SqliteConnection)con);
            cmd.ExecuteNonQuery();
        }

        [SetUp]
        public void SetUp()
        {
            _sqlite.ExecuteSqlWrite(new SqlDelete("entity"));
            _sqlite.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("entity").WithField(new Field("id", IdAlpha.ToString("D"))).WithField(new Field("name", "alpha")),
                new SqlInsert("entity").WithField(new Field("id", IdBeta.ToString("D"))).WithField(new Field("name", "beta")),
                new SqlInsert("entity").WithField(new Field("id", IdGamma.ToString("D"))).WithField(new Field("name", "gamma")),
            });
        }

        [Test]
        public void Sqlite_GuidArray_InList_MatchesTextUuidColumn()
        {
            var select = new SqlSelect("entity")
                .WithField(new Field("id"))
                .WithField(new Field("name"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new[] { IdAlpha, IdGamma }).WithRelation(Relation.In)))
                .WithSort(new Sort("name"));

            IList<EntityRow> rows = _sqlite.GetResults<EntityRow>(select);
            Assert.That(rows.Select(r => r.Name).ToList(), Is.EqualTo(new[] { "alpha", "gamma" }));
        }

        [Test]
        public void Sqlite_GuidList_InList_MatchesTextUuidColumn()
        {
            var select = new SqlSelect("entity")
                .WithField(new Field("id"))
                .WithField(new Field("name"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new List<Guid> { IdBeta }).WithRelation(Relation.In)));

            EntityRow row = _sqlite.GetSingleResult<EntityRow>(select);
            Assert.That(row.Name, Is.EqualTo("beta"));
        }

        [Test]
        public void Sqlite_GuidInList_RoundTripsViaJson()
        {
            var select = new SqlSelect("entity")
                .WithField(new Field("id"))
                .WithField(new Field("name"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new[] { IdAlpha, IdBeta }).WithRelation(Relation.In)))
                .WithSort(new Sort("name"));

            var restored = (SqlSelect)_sqlite.DeserializeFromJson(select.ToString());
            IList<EntityRow> rows = _sqlite.GetResults<EntityRow>(restored);
            Assert.That(rows.Select(r => r.Name).ToList(), Is.EqualTo(new[] { "alpha", "beta" }));
        }

        [Test]
        public void Sqlite_GuidInList_NotIn_ExcludesMatches()
        {
            var select = new SqlSelect("entity")
                .WithField(new Field("id"))
                .WithField(new Field("name"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new[] { IdAlpha })
                        .WithRelation(Relation.In)
                        .WithLogicalRelation(LogicalRelation.AndNot)))
                .WithSort(new Sort("name"));

            IList<EntityRow> rows = _sqlite.GetResults<EntityRow>(select);
            Assert.That(rows.Select(r => r.Name).ToList(), Is.EqualTo(new[] { "beta", "gamma" }));
        }

        [Test]
        public void GetSql_Sqlite_RendersGuidInListAsQuotedStrings()
        {
            var select = new SqlSelect("entity")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new[] { IdAlpha, IdBeta }).WithRelation(Relation.In)));

            string sql = _sqlite.GetSql(select, false);
            Assert.That(sql, Does.Contain(IdAlpha.ToString("D")));
            Assert.That(sql, Does.Contain(IdBeta.ToString("D")));
            Assert.That(sql.ToUpperInvariant(), Does.Contain("IN"));
        }

        [Test]
        public void GetSql_PostgresAndSqlServer_CompileGuidInList()
        {
            var select = new SqlSelect("entity")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new[] { IdAlpha, IdBeta }).WithRelation(Relation.In)));

            ISqlFacade postgres = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(SqlDbType.POSTGRES, "Host=localhost;Database=x;Username=x;Password=x"));
            ISqlFacade sqlServer = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(SqlDbType.SQLSERVER, "Server=localhost;Database=x;Trusted_Connection=True;"));

            string pgSql = postgres.GetSql(select, false);
            string msSql = sqlServer.GetSql(select, false);

            Assert.That(pgSql.ToUpperInvariant(), Does.Contain("IN"));
            Assert.That(msSql.ToUpperInvariant(), Does.Contain("IN"));
            // Native Guid binding still appears as the GUID text in non-parameterized SQL.
            Assert.That(pgSql, Does.Contain(IdAlpha.ToString("D")).Or.Contain(IdAlpha.ToString()));
            Assert.That(msSql, Does.Contain(IdAlpha.ToString("D")).Or.Contain(IdAlpha.ToString()));
        }

        private sealed class EntityRow
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
}
