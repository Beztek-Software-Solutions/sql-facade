// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Concurrent;

    public static class SqlFacadeFactory
    {
        private static readonly ConcurrentDictionary<SqlFacadeConfig, SqlFacade> SqlFacade = new ConcurrentDictionary<SqlFacadeConfig, SqlFacade>();

        /// <summary>
        /// Gets a unique <see cref="ISqlFacade"/> instance for the given configuration.
        /// </summary>
        /// <param name="sqlFacadeConfig">SQL facade configuration (DB type, connection string, isolation).</param>
        /// <returns>A cached <see cref="ISqlFacade"/> for this configuration.</returns>
        public static ISqlFacade GetSqlFacade(SqlFacadeConfig sqlFacadeConfig)
        {
            return SqlFacade.GetOrAdd(sqlFacadeConfig, (key) => new SqlFacade(sqlFacadeConfig));
        }
    }
}