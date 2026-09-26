// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Data;
    using System.Transactions;
    using Microsoft.Data.SqlClient;
    using Microsoft.Data.Sqlite;
    using MySqlConnector;
    using Npgsql;
    using Oracle.ManagedDataAccess.Client;

    public class SqlFacadeConfig
    {
        public DbType DbType { get; set; }

        public string ConnectionString { get; set; }

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
                && TransactionIsolationLevel == other.TransactionIsolationLevel;
        }

        public override int GetHashCode()
        {
            return DbType.GetHashCode()
                ^ ConnectionString.GetHashCode()
                ^ TransactionIsolationLevel.GetHashCode();
        }

        public virtual IDbConnection GetConnection()
        {
            if (TryOpenServerConnection(out IDbConnection server))
                return server;
            if (DbType == DbType.SQLITE)
                return OpenSqliteConnection();
            throw new ArgumentException(DbType + " is not supported");
        }

        private bool TryOpenServerConnection(out IDbConnection connection)
        {
            connection = DbType switch
            {
                DbType.POSTGRES => OpenAndEnlist(new NpgsqlConnection(ConnectionString)),
                DbType.SQLSERVER => OpenAndEnlist(new SqlConnection(ConnectionString)),
                // MySqlConnector works for both MySQL and MariaDB wire protocols.
                DbType.MYSQL or DbType.MARIADB => OpenAndEnlist(new MySqlConnection(ConnectionString)),
                DbType.ORACLE => OpenAndEnlist(new OracleConnection(ConnectionString)),
                _ => null
            };
            return connection != null;
        }

        private IDbConnection OpenSqliteConnection()
        {
            if (IsInMemorySqliteDB(ConnectionString))
                return GetOrOpenInMemorySqlite();

            // Microsoft.Data.Sqlite does not implement EnlistTransaction (ambient
            // System.Transactions). Open for parity with other engines; app code can still
            // use connection.BeginTransaction() or rely on the facade's TransactionScope
            // for non-distributed local work where the provider participates differently.
            SqliteConnection conn = new SqliteConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        private InMemorySqliteConnection GetOrOpenInMemorySqlite()
        {
            if (inMemorySqliteConnection == null)
            {
                inMemorySqliteConnection = new InMemorySqliteConnection(ConnectionString);
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
