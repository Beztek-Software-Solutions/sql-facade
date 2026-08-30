// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    [TestFixture]
    public class SqlFacadeFactoryTests
    {
        [Test]
        public void GetSqlFacade_SameConfig_ReturnsSameInstance()
        {
            var config = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:");
            ISqlFacade first = SqlFacadeFactory.GetSqlFacade(config);
            ISqlFacade second = SqlFacadeFactory.GetSqlFacade(config);

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void GetSqlFacade_DifferentConnectionString_ReturnsDifferentInstance()
        {
            var sqliteA = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:");
            var sqliteB = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=/tmp/other.db");

            ISqlFacade facadeA = SqlFacadeFactory.GetSqlFacade(sqliteA);
            ISqlFacade facadeB = SqlFacadeFactory.GetSqlFacade(sqliteB);

            Assert.That(facadeB, Is.Not.SameAs(facadeA));
        }

        [Test]
        public void GetSqlFacade_DifferentIsolationLevel_ReturnsDifferentInstance()
        {
            var readCommitted = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:");
            var serializable = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:")
            {
                TransactionIsolationLevel = System.Transactions.IsolationLevel.Serializable
            };

            ISqlFacade first = SqlFacadeFactory.GetSqlFacade(readCommitted);
            ISqlFacade second = SqlFacadeFactory.GetSqlFacade(serializable);

            Assert.That(second, Is.Not.SameAs(first));
        }
    }
}
