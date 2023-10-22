// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using SqlKata.Compilers;
    using SqlKata.Execution;

    public class QFactory : IDisposable
    {
        internal QueryFactory Factory { get; set; }

        internal DbType dbType { get; set; }

        private static Dictionary<DbType, Compiler> compilers = new Dictionary<DbType, Compiler>();

        internal QFactory(SqlFacadeConfig config)
        {
            this.dbType = config.DbType;
            Compiler compiler = GetCompiler(dbType);
            Factory = new QueryFactory(config.GetConnection(), compiler);

            if (Factory.Connection.State == ConnectionState.Closed)
            {
                Factory.Connection.Open();
            }
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
            Compiler compiler = null;
            if (!compilers.TryGetValue(dbType, out compiler))
            {
                if (dbType == DbType.POSTGRES)
                {
                    compiler = new PostgresCompiler();
                    compilers.Add(dbType, compiler);
                }
                else if (dbType == DbType.SQLSERVER)
                {
                    compiler = new SqlServerCompiler();
                    compilers.Add(dbType, compiler);
                }
                else if (dbType == DbType.SQLITE)
                {
                    compiler = new SqliteCompiler();
                    compilers.Add(dbType, compiler);
                }
            }
            return compiler;
        }
    }
}