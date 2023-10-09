// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;

    /// <summary>
    /// SQL Utility class to execute SQL abstracted statements (ISql interface) against a relational database
    /// 
    /// Note: It is recommended that you nest all DB calls inside a TransactionScope. The way to use nested transactions is to
    ///       prefix the following line at the place in the call stack above the section you want to become transactional
    ///       "using TransactionScope transactionScope = new TransactionScope();"
    ///       This will call a full rollback of everything inside the scope if there is an exception
    /// </summary>
    public interface ISqlFacade
    {

        /// <summary>
        /// Returns the sqlFacadeConfig object that was used to create an instance of this implementation
        /// </summary>
        /// <returns>The SqlFacadeConfig object that was used to create this implementation</returns>
        public SqlFacadeConfig GetSqlFacadeConfig();

        /// <summary>
        /// Returns a list of objects with type T on execution of the query
        /// </summary>
        /// <typeparam name="T">the Generic of the object type to be returned in the list</typeparam>
        /// <param name="sqlQuery">the query to be executed</param>
        /// <returns>a list of objects with type T on execution of the query</returns>
        public IList<T> GetResults<T>(SqlSelect sqlQuery);

        /// <summary>
        /// Returns the total number of results that satisfy the given query
        /// </summary>
        /// <param name="sqlQuery">the query to be executed</param>
        /// <returns>The total number of results this query would return</returns>
        public int GetTotalNumResults(SqlSelect sqlQuery);

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T">the Generic of the object type to be returned in the PagedResults</typeparam>
        /// <param name="sqlQuery">the query to be executed</param>
        /// <param name="pageNum">the page number requested, starting with 1</param>
        /// <param name="pageSize">The size of the page. This will also obvously be the maximum number of results retrievable for this page.</param>
        /// <param name="retrieveTotalNumResults">Flags whether to also get the total number of results in all the pages</param>
        /// <returns>a PagedResults object, or a PagedResultWithTotal, depending on if the flag retrieveTotalNumResults is not set, or set respectively</returns>
        public PagedResults<T> GetPagedResults<T>(SqlSelect sqlQuery, int pageNum, int pageSize, bool retrieveTotalNumResults = false);

        /// <summary>
        /// Returns a single object of type T obtained by the query.
        /// If the query would return more than one value, an exception is thrown.
        /// The method returns null if there are no results.
        /// </summary>
        /// <typeparam name="T">the Generic of the object type to be returned in the list</typeparam>
        /// <param name="sqlQuery">the query to be executed</param>
        /// <returns>the single object of type T obtained by the query</returns>
        public T GetSingleResult<T>(SqlSelect sqlQuery);

        /// <summary>
        /// Executes the given ISql statement (SqlUpdate, SqlCreate or SqlDelete)
        /// </summary>
        /// <param name="sqlWrite">is the ISql statement (SqlUpdate, SqlCreate or SqlDelete)</param>
        /// <returns>the number of rows affected by this write operation</returns>
        public int ExecuteSqlWrite(ISqlWrite sqlWrite);

        /// <summary>
        /// Executes the given list ISqlWrite statements (SqlUpdate, SqlCreate or SqlDelete) sequentially in the same transaction
        /// </summary>
        /// <param name="sqlWriteList">A list if ISqlWrite objects</param>
        /// <returns>the corresponding numbe of rows changed by each IsqlWrite operation in the input list</returns>
        public IList<int> ExecuteMultiSqlWrite(List<ISqlWrite> sqlWriteList);

        /// <summary>
        /// Returns the SQL string according to the dialect of the DB being used
        /// </summary>
        /// <param name="sql">is the input ISql statement</param>
        /// <param name="isParameterized">flags whether to return parametrised variables in the SQL</param>
        /// <returns> the SQL string according to the dialect of the DB being used</returns>
        public string GetSql(ISql sql, bool isParameterized);

        /// <summary>
        /// Returns and ISql Object that was represented by the provided json string
        /// </summary>
        /// <param name="jsonSql">the json that represents the ISql class</param>
        /// <returns>The corresponding ISql class represented by the provided json string</returns>
        public ISql DeserializeFromJson(string jsonSql);
    }
}
