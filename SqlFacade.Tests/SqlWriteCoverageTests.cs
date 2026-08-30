// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System.Collections.Generic;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    [TestFixture]
    public class SqlWriteCoverageTests
    {
        private static readonly ISqlFacade Sql = SqlFacadeFactory.GetSqlFacade(
            new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:"));

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            using var con = (Microsoft.Data.Sqlite.SqliteConnection)Sql.GetSqlFacadeConfig().GetConnection();
            using var cmd = con.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS items(id TEXT PRIMARY KEY, category TEXT, qty INT, note TEXT);";
            cmd.ExecuteNonQuery();
        }

        [SetUp]
        public void SetUp() => Sql.ExecuteSqlWrite(new SqlDelete("items"));

        [Test]
        public void SqlDelete_WithMultipleFilters_AffectsMatchingRows()
        {
            Sql.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("items").WithField(new Field("id", "a")).WithField(new Field("category", "x")).WithField(new Field("qty", 1)),
                new SqlInsert("items").WithField(new Field("id", "b")).WithField(new Field("category", "y")).WithField(new Field("qty", 2)),
            });

            var delete = new SqlDelete("items")
                .WithFilter(new Expression("category", "x"))
                .WithFilter(new Expression("qty", 1));
            Assert.That(Sql.ExecuteSqlWrite(delete), Is.EqualTo(1));
            Assert.That(Sql.GetTotalNumResults(new SqlSelect("items")), Is.EqualTo(1));
        }

        [Test]
        public void SqlUpdate_WithMultipleFilters_UpdatesMatchingRows()
        {
            Sql.ExecuteSqlWrite(new SqlInsert("items")
                .WithField(new Field("id", "a"))
                .WithField(new Field("category", "x"))
                .WithField(new Field("qty", 1))
                .WithField(new Field("note", "old")));

            var update = new SqlUpdate("items")
                .WithField(new Field("note", "new"))
                .WithFilter(new Expression("category", "x"))
                .WithFilter(new Expression("qty", 1));
            Assert.That(Sql.ExecuteSqlWrite(update), Is.EqualTo(1));

            string note = Sql.GetSingleResult<string>(new SqlSelect("items")
                .WithField(new Field("note"))
                .WithWhere(new Filter().WithExpression(new Expression("id", "a"))));
            Assert.That(note, Is.EqualTo("new"));
        }

        [Test]
        public void SqlInsert_WithCteAndSelect_InsertsFromCte()
        {
            var cte = new CommonTableExpression("select 'c1' as id, 'books' as category, 5 as qty", "seed");
            var insert = new SqlInsert("items")
                .WithCommonTableExpression(cte)
                .WithQuery(new SqlSelect("seed")
                    .WithField(new Field("id"))
                    .WithField(new Field("category"))
                    .WithField(new Field("qty")));

            Assert.That(Sql.ExecuteSqlWrite(insert), Is.EqualTo(1));
            Assert.That(Sql.GetSingleResult<string>(new SqlSelect("items").WithField(new Field("category"))), Is.EqualTo("books"));
        }
    }
}
