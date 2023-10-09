// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Concurrent;

    public static class SqlFacadeFactory
    {
        private static readonly ConcurrentDictionary<SqlFacadeConfig, SqlFacade> SqlFacade = new ConcurrentDictionary<SqlFacadeConfig, SqlFacade>();

        /// <summary>
        /// Gets a unique instance of SqlUtil based on the given configuration
        /// </summary>
        /// <param name="sqlUtilConfig"></param>
        /// <returns>an instance of SqlUtil</returns>
        public static ISqlFacade GetSqlFacade(SqlFacadeConfig sqlFacadeConfig)
        {
            return SqlFacade.GetOrAdd(sqlFacadeConfig, (key) => new SqlFacade(sqlFacadeConfig));
        }
    }
}