// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Data;
    using System.Runtime.CompilerServices;
    using System.Transactions;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using MySqlConnector;
    using Npgsql;
    using Oracle.ManagedDataAccess.Client;

    public class SqlFacadeConfig
    {
        private readonly object passwordGate = new object();
        private string cachedPassword;
        private DateTimeOffset refreshAfter = DateTimeOffset.MinValue;

        public DbType DbType { get; set; }

        public string ConnectionString { get; set; }

        /// <summary>
        /// Optional factory that returns a full connection string on every
        /// <see cref="GetConnection"/>. When set, it wins over
        /// <see cref="PasswordProvider"/>. Keep <see cref="ConnectionString"/> as
        /// the stable <see cref="SqlFacadeFactory"/> identity.
        /// </summary>
        public Func<string> ConnectionStringProvider { get; set; }

        /// <summary>
        /// Optional password factory for <b>short-lived</b> credentials (RDS IAM,
        /// Cloud SQL, Azure Entra, …). The facade merges the password into
        /// <see cref="ConnectionString"/> and caches until
        /// <see cref="SqlPassword.ExpiresAt"/> minus <see cref="PasswordRefreshSkew"/>.
        /// Every mint must supply a non-null <see cref="SqlPassword.ExpiresAt"/> —
        /// put static secrets in <see cref="ConnectionString"/> instead of this
        /// provider. Hosts supply minting; this library does not call cloud APIs.
        /// Ignored when <see cref="ConnectionStringProvider"/> is set.
        /// </summary>
        public Func<SqlPassword> PasswordProvider { get; set; }

        /// <summary>
        /// Clock for password-cache expiry. Defaults to <see cref="TimeProvider.System"/>.
        /// </summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        /// <summary>
        /// Refresh cached passwords this far before <see cref="SqlPassword.ExpiresAt"/>.
        /// Default two minutes.
        /// </summary>
        public TimeSpan PasswordRefreshSkew { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Isolation level for <see cref="SqlFacade"/> <see cref="System.Transactions.TransactionScope"/> wrappers.
        /// Defaults to <see cref="System.Transactions.IsolationLevel.ReadCommitted"/> (PostgreSQL default; avoids Serializable 40001 aborts under concurrency).
        /// </summary>
        public System.Transactions.IsolationLevel TransactionIsolationLevel { get; set; } = System.Transactions.IsolationLevel.ReadCommitted;

        public SqlFacadeConfig(DbType dbType, string connectionString)
        {
            this.DbType = dbType;
            this.ConnectionString = connectionString;
        }

        public override bool Equals(Object obj)
        {
            if (!(obj is SqlFacadeConfig other))
                return false;
            return DbType == other.DbType
                && String.Equals(ConnectionString, other.ConnectionString)
                && TransactionIsolationLevel == other.TransactionIsolationLevel
                && ReferenceEquals(ConnectionStringProvider, other.ConnectionStringProvider)
                && ReferenceEquals(PasswordProvider, other.PasswordProvider);
        }

        public override int GetHashCode()
        {
            var hash = DbType.GetHashCode()
                ^ ConnectionString.GetHashCode()
                ^ TransactionIsolationLevel.GetHashCode();
            if (ConnectionStringProvider is not null)
                hash ^= RuntimeHelpers.GetHashCode(ConnectionStringProvider);
            if (PasswordProvider is not null)
                hash ^= RuntimeHelpers.GetHashCode(PasswordProvider);
            return hash;
        }

        public virtual IDbConnection GetConnection()
        {
            if (TryOpenServerConnection(out IDbConnection server))
                return server;
            if (DbType == DbType.SQLITE)
                return OpenSqliteConnection();
            throw new ArgumentException(DbType + " is not supported");
        }

        /// <summary>
        /// Connection string used to open the next connection.
        /// Order: <see cref="ConnectionStringProvider"/>, else
        /// <see cref="ConnectionString"/> with cached <see cref="PasswordProvider"/>
        /// password merged in, else <see cref="ConnectionString"/> alone.
        /// </summary>
        public string ResolveConnectionString()
        {
            if (ConnectionStringProvider is not null)
            {
                var provided = ConnectionStringProvider();
                if (string.IsNullOrWhiteSpace(provided))
                {
                    throw new InvalidOperationException(
                        "SqlFacadeConfig.ConnectionStringProvider returned a null or blank connection string.");
                }

                return provided;
            }

            if (PasswordProvider is not null)
                return ApplyPassword(ConnectionString, ResolvePassword());

            return ConnectionString;
        }

        private string ResolvePassword()
        {
            lock (passwordGate)
            {
                var now = TimeProvider.GetUtcNow();
                if (cachedPassword is not null && now < refreshAfter)
                    return cachedPassword;

                var minted = PasswordProvider();
                cachedPassword = minted.Password;
                // PasswordProvider is for short-lived secrets only; ExpiresAt is required.
                refreshAfter = minted.ExpiresAt - PasswordRefreshSkew;
                return cachedPassword;
            }
        }

        private string ApplyPassword(string connectionString, string password)
        {
            return DbType switch
            {
                DbType.POSTGRES => ApplyNpgsql(connectionString, password),
                DbType.SQLSERVER => ApplySqlServer(connectionString, password),
                DbType.MYSQL or DbType.MARIADB => ApplyMySql(connectionString, password),
                DbType.ORACLE => ApplyOracle(connectionString, password),
                DbType.SQLITE => connectionString,
                _ => throw new ArgumentException(DbType + " is not supported")
            };
        }

        private static string ApplyNpgsql(string connectionString, string password)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Password = password };
            return builder.ConnectionString;
        }

        private static string ApplySqlServer(string connectionString, string password)
        {
            var builder = new SqlConnectionStringBuilder(connectionString) { Password = password };
            return builder.ConnectionString;
        }

        private static string ApplyMySql(string connectionString, string password)
        {
            var builder = new MySqlConnectionStringBuilder(connectionString) { Password = password };
            return builder.ConnectionString;
        }

        private static string ApplyOracle(string connectionString, string password)
        {
            var builder = new OracleConnectionStringBuilder(connectionString) { Password = password };
            return builder.ConnectionString;
        }

        private bool TryOpenServerConnection(out IDbConnection connection)
        {
            connection = null!;
            if (DbType is not (DbType.POSTGRES or DbType.SQLSERVER or DbType.MYSQL or DbType.MARIADB or DbType.ORACLE))
                return false;

            var connectionString = ResolveConnectionString();
            connection = DbType switch
            {
                DbType.POSTGRES => OpenAndEnlist(new NpgsqlConnection(connectionString)),
                DbType.SQLSERVER => OpenAndEnlist(new SqlConnection(connectionString)),
                // MySqlConnector works for both MySQL and MariaDB wire protocols.
                DbType.MYSQL or DbType.MARIADB => OpenAndEnlist(new MySqlConnection(connectionString)),
                DbType.ORACLE => OpenAndEnlist(new OracleConnection(connectionString)),
                _ => null!
            };
            return true;
        }

        private IDbConnection OpenSqliteConnection()
        {
            var connectionString = ResolveConnectionString();
            if (IsInMemorySqliteDB(connectionString))
                return GetOrOpenInMemorySqlite(connectionString);

            // Microsoft.Data.Sqlite does not implement EnlistTransaction (ambient
            // System.Transactions). Open for parity with other engines; app code can still
            // use connection.BeginTransaction() or rely on the facade's TransactionScope
            // for non-distributed local work where the provider participates differently.
            SqliteConnection conn = new SqliteConnection(connectionString);
            conn.Open();
            return conn;
        }

        private InMemorySqliteConnection GetOrOpenInMemorySqlite(string connectionString)
        {
            if (inMemorySqliteConnection == null)
            {
                inMemorySqliteConnection = new InMemorySqliteConnection(connectionString);
                inMemorySqliteConnection.Open();
            }
            // Shared keep-alive connection: do not re-enlist here — sequential
            // TransactionScopes reuse the same handle (Close is a no-op).
            return inMemorySqliteConnection;
        }

        /// <summary>
        /// Opens a server-backed connection and enlists the ambient transaction.
        /// Covered by live engine tests; unit suites cannot reach Open without a listening server.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        private static T OpenAndEnlist<T>(T conn) where T : System.Data.Common.DbConnection
        {
            conn.Open();
            conn.EnlistTransaction(Transaction.Current);
            return conn;
        }

        // Internal

        // Need to keep a reference to the in-memory connection, so that it is not closed
        private InMemorySqliteConnection inMemorySqliteConnection = null;

        private bool IsInMemorySqliteDB(String connectionString)
        {
            return connectionString.ToLower().Contains("data source=:memory:");
        }

        private class InMemorySqliteConnection : SqliteConnection
        {
            public InMemorySqliteConnection(String connectionString) : base(connectionString) { }

            public override void Close()
            {
                // Do not close the connection when in-memory;
            }
        }
    }
}
