// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Data;
    using Beztek.Facade.Sql;
    using Microsoft.Data.Sqlite;
    using NUnit.Framework;

    [TestFixture]
    public class SqlFacadeConfigTests
    {
        [Test]
        public void Equals_SameValues_ReturnsTrue()
        {
            var left = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:");
            var right = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:");

            Assert.That(left.Equals(right), Is.True);
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        [Test]
        public void Equals_DifferentDbType_ReturnsFalse()
        {
            var sqlite = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:");
            var postgres = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.POSTGRES, "Data Source=:memory:");

            Assert.That(sqlite.Equals(postgres), Is.False);
        }

        [Test]
        public void GetConnection_UnsupportedDbType_Throws()
        {
            var config = new SqlFacadeConfig((Beztek.Facade.Sql.DbType)999, "invalid");

            Assert.Throws<ArgumentException>(() => config.GetConnection());
        }

        [Test]
        public void GetConnection_FileBasedSqlite_ReturnsOpenConnection()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sql-facade-test-{Guid.NewGuid():N}.db");
            try
            {
                var config = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, $"Data Source={path}");
                using IDbConnection connection = config.GetConnection();
                connection.Open();

                Assert.That(connection, Is.InstanceOf<SqliteConnection>());
                Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
            }
            finally
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }

        [Test]
        public void GetConnection_InMemorySqlite_ReusesSharedConnection()
        {
            var config = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:");
            using IDbConnection first = config.GetConnection();
            using IDbConnection second = config.GetConnection();

            Assert.That(first, Is.SameAs(second));
        }
    }
}
