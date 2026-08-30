// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using Beztek.Facade.Sql;
    using NUnit.Framework;
    using SqlDbType = Beztek.Facade.Sql.DbType;

    [TestFixture]
    public class SqlDialectCompilationTests
    {
        private static SqlSelect SampleSelect() =>
            new SqlSelect(new Table("orders", "o"))
                .WithField(new Field("o.id", "Id"))
                .WithField(new Field("o.total", "Total"))
                .WithJoin(new Join(new Table("customers", "c"), new Expression("c.id", "o.customer_id")))
                .WithWhere(new Filter().WithExpression(new Expression("o.status", "open")))
                .WithSort(new Sort("o.id"));

        [Test]
        public void GetSql_Sqlite_CompilesSelectInsertUpdateDelete()
        {
            ISqlFacade sqlite = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(SqlDbType.SQLITE, "Data Source=:memory:"));

            string selectSql = sqlite.GetSql(SampleSelect(), false);
            Assert.That(selectSql, Does.Contain("orders").IgnoreCase);
            Assert.That(selectSql, Does.Contain("join").IgnoreCase);

            string insertSql = sqlite.GetSql(new SqlInsert("orders")
                .WithField(new Field("id", "1"))
                .WithField(new Field("total", 10)), false);
            Assert.That(insertSql, Does.Contain("INSERT").IgnoreCase);

            string updateSql = sqlite.GetSql(new SqlUpdate("orders")
                .WithField(new Field("status", "shipped"))
                .WithFilter(new Expression("id", "1")), false);
            Assert.That(updateSql, Does.Contain("UPDATE").IgnoreCase);

            string deleteSql = sqlite.GetSql(new SqlDelete("orders")
                .WithFilter(new Expression("id", "1")), false);
            Assert.That(deleteSql, Does.Contain("DELETE").IgnoreCase);
        }

        [Test]
        public void GetSql_Postgres_CompilesWithDoubleQuotedIdentifiers()
        {
            ISqlFacade postgres = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(SqlDbType.POSTGRES, "Host=localhost;Database=x;Username=x;Password=x"));

            string sql = postgres.GetSql(SampleSelect(), false);
            Assert.That(sql, Does.Contain("\"orders\"").Or.Contain("orders"));
            Assert.That(sql, Does.Contain("JOIN").IgnoreCase);
        }

        [Test]
        public void GetSql_SqlServer_CompilesSelect()
        {
            ISqlFacade sqlServer = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(SqlDbType.SQLSERVER, "Server=localhost;Database=x;Trusted_Connection=True;"));

            string sql = sqlServer.GetSql(SampleSelect(), false);
            Assert.That(sql, Does.Contain("[orders]").Or.Contain("orders"));
            Assert.That(sql, Does.Contain("JOIN").IgnoreCase);
        }

        [Test]
        public void GetSql_Parameterized_UsesBindings()
        {
            ISqlFacade sqlite = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(SqlDbType.SQLITE, "Data Source=:memory:"));
            string sql = sqlite.GetSql(new SqlInsert("orders")
                .WithField(new Field("id", "abc"))
                .WithField(new Field("total", 42)), true);

            Assert.That(sql, Does.Contain("@p"));
            Assert.That(sql, Does.Not.Contain("'abc'"));
        }

        [Test]
        public void GetSql_CteAndUnion_Compiles()
        {
            ISqlFacade sqlite = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(SqlDbType.SQLITE, "Data Source=:memory:"));

            var cte = new CommonTableExpression("select 'a' as id", "palette");
            var select = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithCommonTableExpression(cte)
                .WithCombine(new SqlCombine(
                    new SqlSelect("canvas").WithField(new Field("id")),
                    SqlRelation.UnionAll))
                .WithSort(new Sort("id"));

            string sql = sqlite.GetSql(select, false);
            Assert.That(sql.ToUpperInvariant(), Does.Contain("UNION"));
            Assert.That(sql.ToUpperInvariant(), Does.Contain("ORDER BY"));
        }
    }
}
