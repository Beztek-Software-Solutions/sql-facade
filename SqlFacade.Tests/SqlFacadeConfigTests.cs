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
        public void Equals_NonConfigObject_ReturnsFalse()
        {
            var config = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:");
            Assert.That(config.Equals("not-a-config"), Is.False);
            Assert.That(config.Equals(null), Is.False);
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
        public void GetConnection_FileBasedSqlite_OpensUnderAmbientTransactionScope()
        {
            // Microsoft.Data.Sqlite does not support EnlistTransaction; GetConnection must still
            // return an open connection when an ambient TransactionScope is present.
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sql-facade-enlist-{Guid.NewGuid():N}.db");
            try
            {
                var config = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, $"Data Source={path}");
                using var scope = new System.Transactions.TransactionScope(
                    System.Transactions.TransactionScopeOption.Required,
                    System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
                using IDbConnection connection = config.GetConnection();
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

        [Test]
        public void ResolveConnectionString_UsesProvider_WhenSet()
        {
            var config = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:")
            {
                ConnectionStringProvider = () => "Data Source=provided.db"
            };

            Assert.That(config.ResolveConnectionString(), Is.EqualTo("Data Source=provided.db"));
            Assert.That(config.ConnectionString, Is.EqualTo("Data Source=:memory:"));
        }

        [Test]
        public void ResolveConnectionString_Throws_WhenProviderReturnsBlank()
        {
            var config = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:")
            {
                ConnectionStringProvider = () => "  "
            };

            Assert.Throws<InvalidOperationException>(() => config.ResolveConnectionString());
        }

        [Test]
        public void GetConnection_InvokesProvider_OnEachOpen()
        {
            int calls = 0;
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"sql-facade-provider-{Guid.NewGuid():N}.db");
            try
            {
                var config = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=identity-only")
                {
                    ConnectionStringProvider = () =>
                    {
                        calls++;
                        return $"Data Source={path}";
                    }
                };

                using (IDbConnection first = config.GetConnection())
                {
                    Assert.That(first.State, Is.EqualTo(ConnectionState.Open));
                }

                using (IDbConnection second = config.GetConnection())
                {
                    Assert.That(second.State, Is.EqualTo(ConnectionState.Open));
                }

                Assert.That(calls, Is.EqualTo(2));
            }
            finally
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }

        [Test]
        public void Equals_IncludesConnectionStringProviderIdentity()
        {
            Func<string> provider = () => "Data Source=:memory:";
            var withProvider = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:")
            {
                ConnectionStringProvider = provider
            };
            var sameProvider = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:")
            {
                ConnectionStringProvider = provider
            };
            var differentProvider = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:")
            {
                ConnectionStringProvider = () => "Data Source=:memory:"
            };
            var withoutProvider = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:");

            Assert.That(withProvider.Equals(sameProvider), Is.True);
            Assert.That(withProvider.GetHashCode(), Is.EqualTo(sameProvider.GetHashCode()));
            Assert.That(withProvider.Equals(differentProvider), Is.False);
            Assert.That(withProvider.Equals(withoutProvider), Is.False);
        }

        [Test]
        public void ResolveConnectionString_CachesPasswordProvider_UntilNearExpiry()
        {
            int calls = 0;
            var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
            var time = new ManualTimeProvider(start);
            var config = new SqlFacadeConfig(
                Beztek.Facade.Sql.DbType.POSTGRES,
                "Host=db.example.com;Port=5432;Database=grasp;Username=grasp")
            {
                TimeProvider = time,
                PasswordRefreshSkew = TimeSpan.FromMinutes(2),
                PasswordProvider = () =>
                {
                    calls++;
                    return new SqlPassword($"token-{calls}", time.GetUtcNow().AddMinutes(15));
                }
            };

            var first = config.ResolveConnectionString();
            var second = config.ResolveConnectionString();
            Assert.That(first, Does.Contain("token-1"));
            Assert.That(second, Does.Contain("token-1"));
            Assert.That(calls, Is.EqualTo(1));

            time.Advance(TimeSpan.FromMinutes(12));
            Assert.That(config.ResolveConnectionString(), Does.Contain("token-1"));
            Assert.That(calls, Is.EqualTo(1));

            time.Advance(TimeSpan.FromMinutes(2));
            Assert.That(config.ResolveConnectionString(), Does.Contain("token-2"));
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void SqlPassword_RequiresExpiresAt_AndNonBlankPassword()
        {
            var expires = DateTimeOffset.UtcNow.AddMinutes(15);
            var password = new SqlPassword("token", expires);

            Assert.That(password.Password, Is.EqualTo("token"));
            Assert.That(password.ExpiresAt, Is.EqualTo(expires));
            Assert.Throws<ArgumentException>(() => new SqlPassword(" ", expires));
        }

        [Test]
        public void ResolveConnectionString_ConnectionStringProvider_WinsOverPasswordProvider()
        {
            var config = new SqlFacadeConfig(Beztek.Facade.Sql.DbType.POSTGRES, "Host=a;Database=b;Username=c")
            {
                PasswordProvider = () => new SqlPassword("ignored", DateTimeOffset.UtcNow.AddMinutes(15)),
                ConnectionStringProvider = () => "Host=provided;Database=b;Username=c;Password=full"
            };

            Assert.That(config.ResolveConnectionString(), Is.EqualTo("Host=provided;Database=b;Username=c;Password=full"));
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private DateTimeOffset _utc;

            public ManualTimeProvider(DateTimeOffset utc) => _utc = utc;

            public override DateTimeOffset GetUtcNow() => _utc;

            public void Advance(TimeSpan delta) => _utc += delta;
        }
    }
}
