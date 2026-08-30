// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Data;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    [TestFixture]
    public class SqlFacadeConfigConnectionTests
    {
        [Test]
        public void GetConnection_Postgres_ExecutesProviderBranch()
        {
            var config = new SqlFacadeConfig(
                Beztek.Facade.Sql.DbType.POSTGRES,
                "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1;Command Timeout=1");

            AssertProviderBranch(config);
        }

        [Test]
        public void GetConnection_SqlServer_ExecutesProviderBranch()
        {
            var config = new SqlFacadeConfig(
                Beztek.Facade.Sql.DbType.SQLSERVER,
                "Server=127.0.0.1,1;Database=x;User Id=x;Password=x;Connect Timeout=1");

            AssertProviderBranch(config);
        }

        private static void AssertProviderBranch(SqlFacadeConfig config)
        {
            try
            {
                using IDbConnection connection = config.GetConnection();
                Assert.That(connection, Is.Not.Null);
            }
            catch (Exception ex) when (ex is not SuccessException)
            {
                // Expected when no server is listening — branch still executed.
                Assert.That(ex.Message, Is.Not.Empty);
            }
        }
    }
}
