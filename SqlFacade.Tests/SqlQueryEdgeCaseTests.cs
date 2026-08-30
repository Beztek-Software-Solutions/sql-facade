// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System.Collections.Generic;
    using System.Data;
    using Beztek.Facade.Sql;
    using Microsoft.Data.Sqlite;
    using NUnit.Framework;

    [TestFixture]
    public class SqlQueryEdgeCaseTests
    {
        private ISqlFacade _sql;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _sql = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:"));
            using IDbConnection con = _sql.GetSqlFacadeConfig().GetConnection();
            using var cmd = new SqliteCommand(
                "CREATE TABLE IF NOT EXISTS canvas(id TEXT PRIMARY KEY, color TEXT, ordering INT);",
                (SqliteConnection)con);
            cmd.ExecuteNonQuery();
        }

        [SetUp]
        public void SetUp()
        {
            _sql.ExecuteSqlWrite(new SqlDelete("canvas"));
        }

        [Test]
        public void GetSingleResult_NoRows_ReturnsDefault()
        {
            var select = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(new Expression("id", "missing")));

            string result = _sql.GetSingleResult<string>(select);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Filter_WithRawExpression_CompilesAndRuns()
        {
            _sql.ExecuteSqlWrite(new SqlInsert("canvas")
                .WithField(new Field("id", "raw-1"))
                .WithField(new Field("color", "blue"))
                .WithField(new Field("ordering", 1)));

            var select = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithRawExpression("color = 'blue'"));

            IList<string> ids = _sql.GetResults<string>(select);
            Assert.That(ids, Is.EqualTo(new[] { "raw-1" }));
        }

        [Test]
        public void Expression_WithIsRaw_RequiresObjectArrayValue()
        {
            var expression = new Expression("1=1", "not-an-array");
            Assert.Throws<System.ArgumentException>(() => expression.WithIsRaw());
        }

        [Test]
        public void SqlInsert_WithCte_Compiles()
        {
            var cte = new CommonTableExpression("select 'seed' as id, 'green' as color", "seed_rows");
            var insert = new SqlInsert("canvas")
                .WithCommonTableExpression(cte)
                .WithQuery(new SqlSelect("seed_rows")
                    .WithField(new Field("id"))
                    .WithField(new Field("color"))
                    .WithField(new Field("1", "ordering", isRaw: true)));

            string sql = _sql.GetSql(insert, false);
            Assert.That(sql.ToUpperInvariant(), Does.Contain("WITH"));
            Assert.That(sql.ToUpperInvariant(), Does.Contain("INSERT"));
        }

        [Test]
        public void Join_CteSource_CompilesAndRuns()
        {
            _sql.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("canvas").WithField(new Field("id", "1")).WithField(new Field("color", "red")).WithField(new Field("ordering", 1)),
            });

            var cte = new CommonTableExpression("select 'red' as match_color", "palette");
            var select = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithCommonTableExpression(cte)
                .WithJoin(new Join(cte, new Expression("palette.match_color", "v.color")));

            Assert.That(_sql.GetSingleResult<string>(select), Is.EqualTo("1"));
        }
    }
}
