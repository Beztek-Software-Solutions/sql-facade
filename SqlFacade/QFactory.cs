// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Collections.Concurrent;
    using System.Data;
    using SqlKata.Compilers;
    using SqlKata.Execution;

    public class QFactory : IDisposable
    {
        internal QueryFactory Factory { get; set; }

        internal DbType DbType { get; set; }

        private static ConcurrentDictionary<DbType, Compiler> _compilers = new();

        internal QFactory(SqlFacadeConfig config)
        {
            this.DbType = config.DbType;
            Compiler compiler = GetCompiler(DbType);
            Factory = new QueryFactory(config.GetConnection(), compiler);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Factory.Connection.State == ConnectionState.Open)
                Factory.Connection.Close();
        }

        public static Compiler GetCompiler(DbType dbType)
        {
            return _compilers.GetOrAdd(dbType, _ => {
                return dbType switch {
                    DbType.POSTGRES => new PostgresCompiler(),
                    DbType.SQLSERVER => new SqlServerCompiler(),
                    DbType.SQLITE => new SqliteCompiler(),
                    _ => null
                };
            });
        }
    }
}