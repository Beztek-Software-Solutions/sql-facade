// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Data;
    using Microsoft.Data.SqlClient;
    using System.Transactions;
    using Microsoft.Data.Sqlite;
    using Npgsql;

    public class SqlFacadeConfig
    {
        public DbType DbType { get; set; }

        public string ConnectionString { get; set; }

        public SqlFacadeConfig(DbType dbType, string connectionString)
        {
            this.DbType = dbType;
            this.ConnectionString = connectionString;
        }

        public override bool Equals(Object obj)
        {
            if (!(obj is SqlFacadeConfig))
                return false;
            else
                return DbType == ((SqlFacadeConfig)obj).DbType && String.Equals(ConnectionString, ((SqlFacadeConfig)obj).ConnectionString);
        }

        public override int GetHashCode()
        {
            return DbType.GetHashCode() ^ ConnectionString.GetHashCode();
        }

        public virtual IDbConnection GetConnection()
        {
            if (DbType == DbType.POSTGRES)
            {
                NpgsqlConnection conn = new NpgsqlConnection(this.ConnectionString);

                // Explicitly enlist the current transaction, to support transaction scoping
                conn.EnlistTransaction(Transaction.Current);

                return conn;
            }
            else if (DbType == DbType.SQLSERVER)
            {
                SqlConnection conn = new SqlConnection(this.ConnectionString);
                conn.Open();

                // Explicitly enlist the current transaction, to support transaction scoping
                conn.EnlistTransaction(Transaction.Current);
                return conn;
            }
            else if (DbType == DbType.SQLITE)
            {
                if (IsInMemorySqliteDB(this.ConnectionString))
                {
                    if (inMemorySqliteConnection == null)
                    {
                        inMemorySqliteConnection = new InMemorySqliteConnection(this.ConnectionString);
                        inMemorySqliteConnection.Open();
                    }
                    return inMemorySqliteConnection;
                }

                SqliteConnection conn = new SqliteConnection(this.ConnectionString);

                return conn;
            }

            throw new ArgumentException(DbType + " is not supported");
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
