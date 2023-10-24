// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Transactions;
    using Dapper;
    using SqlKata;
    using SqlKata.Compilers;
    using SqlKata.Execution;

    public class SqlFacade : ISqlFacade
    {
        private readonly SqlFacadeConfig sqlFacadeConfig;

        public SqlFacade(SqlFacadeConfig sqlFacadeConfig)
        {
            this.sqlFacadeConfig = sqlFacadeConfig;
        }

        public SqlFacadeConfig GetSqlFacadeConfig()
        {
            return sqlFacadeConfig;
        }

        public IList<T> GetResults<T>(SqlSelect sqlQuery)
        {
            return ExecuteInTransaction<List<T>>(new[] { sqlQuery }, GetResults<T>);
        }

        public int GetTotalNumResults(SqlSelect sqlQuery)
        {
            return ExecuteInTransaction<int>(new[] { sqlQuery }, GetTotalNumResults);
        }

        public PagedResults<T> GetPagedResults<T>(SqlSelect sqlQuery, int pageNum, int pageSize, bool retrieveTotalNumResults = false)
        {
            return ExecuteInTransaction<PagedResults<T>>(new object[] { sqlQuery, pageNum, pageSize, retrieveTotalNumResults }, GetPagedResults<T>);
        }

        public T GetSingleResult<T>(SqlSelect sqlQuery)
        {
            IList<T> result = ExecuteInTransaction<List<T>>(new[] { sqlQuery }, GetResults<T>);

            if (result.Count > 1)
                throw new ArgumentException("There are too many results for the given query");

            return result.Count == 0 ? default(T) : result[0];
        }

        public int ExecuteSqlWrite(ISqlWrite sqlWrite)
        {
            return ExecuteInTransaction<int>(new[] { sqlWrite }, ExecuteSqlWrite);
        }

        public IList<int> ExecuteMultiSqlWrite(List<ISqlWrite> sqlWriteList)
        {
            return ExecuteInTransaction<IList<int>>(new[] { sqlWriteList }, ExecuteMultiSqlWrite);
        }

        public string GetSql(ISql sql, bool isParameterized)
        {
            Compiler compiler = QFactory.GetCompiler(sqlFacadeConfig.DbType);
            Query query = new Query();
            BuildQuery(query, sql);
            SqlResult sqlResult = compiler.Compile(query);
            return isParameterized ? sqlResult.Sql : sqlResult.ToString();
        }

        public ISql DeserializeFromJson(string jsonSql)
        {
            string sqlType = null;
            try
            {
                sqlType = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonSql)["SqlType"].ToString();
            }
            catch (Exception e)
            {
                throw new ArgumentException("Unable to deserialize the given json", e);
            }

            if (Object.Equals(sqlType, Constants.Delete))
            {
                return JsonSerializer.Deserialize<SqlDelete>(jsonSql);
            }
            else if (Object.Equals(sqlType, Constants.Insert))
            {
                return JsonSerializer.Deserialize<SqlInsert>(jsonSql);
            }
            else if (Object.Equals(sqlType, Constants.Update))
            {
                return JsonSerializer.Deserialize<SqlUpdate>(jsonSql);
            }
            else if (Object.Equals(sqlType, Constants.Select))
            {
                return JsonSerializer.Deserialize<SqlSelect>(jsonSql);
            }
            throw new ArgumentException("Unable to deserialize the given json");
        }

        // Internal methods

        /// <summary>
        /// Returns the result of the execution passed in as the function func, and using the object array of parameters
        /// </summary>
        /// <typeparam name="V"> is the returned object</typeparam>
        /// <param name="parameters"> the parameters required to execute this method</param>
        /// <param name="func">The function to be executed</param>
        /// <returns>The result of the execution</returns>
        private V ExecuteInTransaction<V>(object[] parameters, Func<QFactory, object[], V> func)
        {
            using TransactionScope transactionScope = new TransactionScope(System.Transactions.Transaction.Current != null ? TransactionScopeOption.RequiresNew : TransactionScopeOption.Required);

            using QFactory qFactory = new QFactory(sqlFacadeConfig);
            try
            {
                V result = func(qFactory, parameters);
                transactionScope.Complete();
                return result;
            }
            catch (Exception e)
            {
                HandleException("Exception executing SQL statement", e);
            }

            // We will never reach here, because HandleException re-throws an excption
            return default(V);
        }

        private static void HandleException(string description, Exception exception)
        {
            ArgumentException castException = exception as ArgumentException;
            if (castException != null)
                throw castException;

            throw new ArgumentException(description, exception);
        }

        private List<T> GetResults<T>(QFactory qFactory, object[] parameters)
        {
            SqlSelect sqlSelect = (SqlSelect)parameters[0];
            Query query = (XQuery)qFactory.Factory.Query();
            BuildQuery(query, sqlSelect);
            return query.Get<T>().AsList();
        }

        private PagedResults<T> GetPagedResults<T>(QFactory qFactory, object[] parameters)
        {
            SqlSelect sqlSelect = (SqlSelect)parameters[0];
            int pageNum = (int)parameters[1];
            int pageSize = (int)parameters[2];
            bool retrieveTotalNumResults = (bool)parameters[3];
            Query query = (XQuery)qFactory.Factory.Query();
            BuildQuery(query, sqlSelect);

            // Pagination
            query.Limit(pageSize);
            query.Offset((pageNum - 1) * pageSize);

            // Execution
            int totalRows = retrieveTotalNumResults ? GetTotalNumResults(qFactory, new[] { sqlSelect }) : -1;
            List<T> results = query.Get<T>().AsList();

            return retrieveTotalNumResults ? new PagedResultsWithTotal<T>(pageNum, pageSize, results, totalRows) : new PagedResults<T>(pageNum, pageSize, results);
        }

        private int ExecuteSqlWrite(QFactory qFactory, object[] parameters)
        {
            ISqlWrite sqlWrite = (ISqlWrite)parameters[0];
            object value = parameters.Length > 1 ? parameters[1] : null;

            // Using Dapper to be able to get the number of rows affected
            Query query = new Query();
            BuildQuery(query, sqlWrite);
            string sql = value == null ? qFactory.Factory.Compiler.Compile(query).ToString() : qFactory.Factory.Compiler.Compile(query).Sql;
            return value == null ? qFactory.Factory.Connection.Execute(sql) : qFactory.Factory.Connection.Execute(sql, value);
        }

        private List<int> ExecuteMultiSqlWrite(QFactory qFactory, object[] parameters)
        {
            List<ISqlWrite> sqlWriteList = (List<ISqlWrite>)parameters[0];
            List<int> result = new List<int>();
            foreach (ISqlWrite sqlWrite in sqlWriteList)
            {
                result.Add(ExecuteSqlWrite(qFactory, new[] { sqlWrite }));
            }
            return result;
        }

        private int GetTotalNumResults(QFactory qFactory, object[] parameters)
        {
            SqlSelect sqlSelect = (SqlSelect)parameters[0];

            // Clone the input SqlSelect so that it can be used by the caller
            SqlSelect sqlSelectForCount = (SqlSelect)DeserializeFromJson(sqlSelect.ToString());
            // Reset these parameters
            sqlSelectForCount.Fields = null;
            sqlSelectForCount.Sorts = null;
            // Add count Field
            sqlSelectForCount.WithField(new Field("count(*)", "Total", true));
            Query query = (XQuery)qFactory.Factory.Query();
            BuildQuery(query, sqlSelectForCount);
            String rawQuery = qFactory.Factory.Compiler.Compile(query).ToString();
            return qFactory.Factory.Connection.Query<int>(rawQuery).AsList<int>()[0];
        }

        private void BuildQuery(Query query, ISql sql)
        {
            string sqlType = sql == null ? null : sql.SqlType;
            if (Object.Equals(sqlType, Constants.Insert))
            {
                BuildInsertQuery(query, (SqlInsert)sql);
            }
            else if (Object.Equals(sqlType, Constants.Delete))
            {
                BuildDeleteQuery(query, (SqlDelete)sql);
            }
            else if (Object.Equals(sqlType, Constants.Update))
            {
                BuildUpdateQuery(query, (SqlUpdate)sql);
            }
            else if (Object.Equals(sqlType, Constants.Select))
            {
                BuildSelectQuery(query, (SqlSelect)sql);
            }
        }

        private Query BuildSelectQuery(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.Table != null)
            {
                query.From(sqlSelect.Table.Alias == null ? sqlSelect.Table.Name : sqlSelect.Table.Name + " as " + sqlSelect.Table.Alias);
            }
            else if (sqlSelect.FromDerivedTable != null)
            {
                // Derived Table
                Query derivedTable = BuildSelectQuery(new Query(), sqlSelect.FromDerivedTable.Select).As(sqlSelect.FromDerivedTable.Alias);
                query.From(derivedTable);
            }

            // Fields and Raw fields
            if (sqlSelect.Fields != null)
            {
                foreach (Field field in sqlSelect.Fields)
                {
                    if (field.IsRaw)
                    {
                        query.SelectRaw(field.Name + " as " + field.Value);
                    }
                    else
                    {
                        query.Select(field.Value == null ? field.Name : field.Name + " as " + field.Value);
                    }
                }
            }

            // Where clauses
            if (sqlSelect.Where != null)
            {
                query.Where(q => BuildFilter(q, sqlSelect.Where));
            }

            // Joins
            if (sqlSelect.Joins != null)
            {
                foreach (Join join in sqlSelect.Joins)
                {
                    SqlKata.Join sqlKataJoin = new SqlKata.Join();
                    sqlKataJoin.On(join.OnExpression.Name, join.OnExpression.Value.ToString(), join.OnExpression.Relation.ToString());
                    if (join.JoinExpressions != null)
                    {
                        bool isFirst = true;
                        foreach (Expression joinExpression in join.JoinExpressions)
                        {
                            this.AddExpression<SqlKata.Join>(sqlKataJoin, isFirst, joinExpression);
                            isFirst = false;
                        }
                    }

                    // Set the join type
                    if (Object.Equals(join.JoinType, JoinType.InnerJoin))
                    {
                        query.Join(join.JoinTable.Alias == null ? join.JoinTable.Name : join.JoinTable.Name + " as " + join.JoinTable.Alias, j => sqlKataJoin);
                    }
                    else if (Object.Equals(join.JoinType, JoinType.LeftJoin))
                    {
                        query.LeftJoin(join.JoinTable.Alias == null ? join.JoinTable.Name : join.JoinTable.Name + " as " + join.JoinTable.Alias, j => sqlKataJoin);
                    }
                }
            }

            // Group By
            if (sqlSelect.GroupBys != null)
            {
                foreach (GroupBy groupBy in sqlSelect.GroupBys)
                {
                    if (groupBy.IsRaw)
                    {
                        query.GroupByRaw(groupBy.Value);
                    }
                    else
                    {
                        query.GroupBy(groupBy.Value);
                    }
                }
            }

            // Having
            if (sqlSelect.Having != null)
            {
                query.Having(q => BuildFilter(q, sqlSelect.Having));
            }

            // Order by clauses
            if (sqlSelect.Sorts != null)
            {
                foreach (Sort sort in sqlSelect.Sorts)
                {
                    if (sort.IsAscending)
                    {
                        query.OrderBy(sort.Name);
                    }
                    else
                    {
                        query.OrderByDesc(sort.Name);
                    }
                }
            }

            // Sql Combines
            if (sqlSelect.SqlCombines != null)
            {
                foreach (SqlCombine sqlCombine in sqlSelect.SqlCombines)
                {
                    if (Object.Equals(sqlCombine.SqlRelation, SqlRelation.Union))
                    {
                        query.Union(q => BuildSelectQuery(q, sqlCombine.SqlSelect));
                    }
                    else if (Object.Equals(sqlCombine.SqlRelation, SqlRelation.UnionAll))
                    {
                        query.UnionAll(q => BuildSelectQuery(q, sqlCombine.SqlSelect));
                    }
                    else if (Object.Equals(sqlCombine.SqlRelation, SqlRelation.Intersect))
                    {
                        query.Intersect(q => BuildSelectQuery(q, sqlCombine.SqlSelect));
                    }
                    else if (Object.Equals(sqlCombine.SqlRelation, SqlRelation.Except))
                    {
                        query.Except(q => BuildSelectQuery(q, sqlCombine.SqlSelect));
                    }
                }
            }
            return query;
        }

        private Query BuildFilter(Query query, Filter filter)
        {
            bool isFirst = true;
            if (filter.Expressions != null)
            {
                foreach (Expression expression in filter.Expressions)
                {
                    this.AddExpression<Query>(query, isFirst, expression);
                    isFirst = false;
                }
            }

            if (filter.Filters != null)
            {
                foreach (Filter nestedFilter in filter.Filters)
                {
                    if (isFirst)
                    {
                        query.Where(q => BuildFilter(q, nestedFilter));
                        isFirst = false;
                    }
                    else
                    {
                        if (Object.Equals(filter.LogicalRelation, LogicalRelation.And))
                        {
                            query.Where(q => BuildFilter(q, nestedFilter));
                        }
                        else if (Object.Equals(filter.LogicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhere(q => BuildFilter(q, nestedFilter));
                        }
                        else if (Object.Equals(filter.LogicalRelation, LogicalRelation.AndNot))
                        {
                            query.WhereNot(q => BuildFilter(q, nestedFilter));
                        }
                        else if (Object.Equals(filter.LogicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhereNot(q => BuildFilter(q, nestedFilter));
                        }
                    }
                }
            }

            return query;
        }

        private void AddExpression<Q>(BaseQuery<Q> query, bool isFirst, Expression expression) where Q : BaseQuery<Q>
        {
            LogicalRelation logicalRelation = expression.LogicalRelation;
            if (isFirst)
            {
                if (Object.Equals(logicalRelation, LogicalRelation.Or))
                {
                    logicalRelation = LogicalRelation.And;
                }
                else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                {
                    logicalRelation = LogicalRelation.AndNot;
                }
            }

            // Types of And combinations between expressions
            if (Object.Equals(logicalRelation, LogicalRelation.And)
                || Object.Equals(logicalRelation, LogicalRelation.AndNot)
                || isFirst)
            {
                if (expression.IsRaw)
                {
                    if (Object.Equals(logicalRelation, LogicalRelation.And))
                    {
                        if (expression.Value is JsonElement)
                        {
                            query.WhereRaw(expression.Name, JsonSerializer.Deserialize<object[]>(expression.Value.ToString()));
                        }
                        else
                        {
                            query.WhereRaw(expression.Name, (object[])expression.Value);
                        }
                    }
                    else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                    {
                        throw new ArgumentException("Logical relation `AndNot' is not support for raw expressions");
                    }
                }
                else
                {
                    if (Object.Equals(expression.Relation, Relation.EqualTo))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.And))
                        {
                            query.Where(expression.Name, Relation.EqualTo.ToString(), expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                        {
                            query.Where(expression.Name, "!=", expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.GreaterThan))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.And))
                        {
                            query.Where(expression.Name, Relation.GreaterThan.ToString(), expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                        {
                            query.Where(expression.Name, "<", expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.GreaterThanOrEqualTo))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.And))
                        {
                            query.Where(expression.Name, Relation.GreaterThanOrEqualTo.ToString(), expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                        {
                            query.Where(expression.Name, "<=", expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.In))
                    {
                        if (expression.Value is JsonElement)
                        {
                            if (Object.Equals(logicalRelation, LogicalRelation.And))
                            {
                                query.WhereIn(expression.Name, JsonSerializer.Deserialize<IEnumerable<string>>(expression.Value.ToString()));
                            }
                            else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                            {
                                query.WhereNotIn(expression.Name, JsonSerializer.Deserialize<IEnumerable<string>>(expression.Value.ToString()));
                            }
                        }
                        else
                        {
                            if (Object.Equals(logicalRelation, LogicalRelation.And))
                            {
                                query.WhereIn(expression.Name, (IEnumerable<string>)expression.Value);
                            }
                            else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                            {
                                query.WhereNotIn(expression.Name, (IEnumerable<string>)expression.Value);
                            }
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.StartsWith))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.And))
                        {
                            query.WhereStarts(expression.Name, expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                        {
                            query.WhereNotStarts(expression.Name, expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.EndsWith))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.And))
                        {
                            query.WhereEnds(expression.Name, expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                        {
                            query.WhereNotEnds(expression.Name, expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.Contains))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.And))
                        {
                            query.WhereContains(expression.Name, expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                        {
                            query.WhereNotContains(expression.Name, expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.NullValue))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.And))
                        {
                            query.WhereNull(expression.Name);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                        {
                            query.WhereNotNull(expression.Name);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.TrueValue))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.And))
                        {
                            query.WhereTrue(expression.Name);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                        {
                            query.WhereFalse(expression.Name);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.Exists))
                    {
                        SqlSelect sqlForExistanceCheck = (SqlSelect)expression.Value;
                        if (Object.Equals(logicalRelation, LogicalRelation.And))
                        {
                            query.WhereExists(BuildSelectQuery(new Query(), sqlForExistanceCheck));
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                        {
                            query.WhereNotExists(BuildSelectQuery(new Query(), sqlForExistanceCheck));
                        }
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown expression relation {expression.Relation}");
                    }
                }
            }
            else if (Object.Equals(logicalRelation, LogicalRelation.Or)
                 || Object.Equals(logicalRelation, LogicalRelation.OrNot))
            {
                if (expression.IsRaw)
                {
                    if (Object.Equals(logicalRelation, LogicalRelation.Or))
                    {
                        query.OrWhereRaw(expression.Name, (object[])expression.Value);
                    }
                    else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                    {
                        throw new ArgumentException("Logical relation `OrNot' is not support for raw expressions");
                    }
                }
                else
                {
                    if (Object.Equals(expression.Relation, Relation.EqualTo))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhere(expression.Name, Relation.EqualTo.ToString(), expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhere(expression.Name, "!=", expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.GreaterThan))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhere(expression.Name, Relation.GreaterThan.ToString(), expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhere(expression.Name, "<", expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.GreaterThanOrEqualTo))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhere(expression.Name, Relation.GreaterThanOrEqualTo.ToString(), expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhere(expression.Name, "<=", expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.StartsWith))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhereStarts(expression.Name, expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhereNotStarts(expression.Name, expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.EndsWith))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhereEnds(expression.Name, expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhereNotEnds(expression.Name, expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.Contains))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhereContains(expression.Name, expression.Value);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhereNotContains(expression.Name, expression.Value);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.NullValue))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhereNull(expression.Name);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhereNotNull(expression.Name);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.TrueValue))
                    {
                        if (Object.Equals(logicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhereTrue(expression.Name);
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhereFalse(expression.Name);
                        }
                    }
                    else if (Object.Equals(expression.Relation, Relation.Exists))
                    {
                        SqlSelect sqlForExistanceCheck = (SqlSelect)expression.Value;
                        if (Object.Equals(logicalRelation, LogicalRelation.Or))
                        {
                            query.OrWhereExists(BuildSelectQuery(new Query(), sqlForExistanceCheck));
                        }
                        else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                        {
                            query.OrWhereNotExists(BuildSelectQuery(new Query(), sqlForExistanceCheck));
                        }
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown expression relation {expression.Relation}");
                    }
                }
            }
        }

        private void BuildUpdateQuery(Query query, SqlUpdate sqlUpdate)
        {
            query.From(sqlUpdate.Table);

            if (sqlUpdate.Filters != null)
            {
                bool isFirst = true;
                foreach (Expression expression in sqlUpdate.Filters)
                {
                    this.AddExpression<Query>(query, isFirst, expression);
                    isFirst = false;
                }
            }

            Dictionary<string, object> fields = new Dictionary<string, object>();
            foreach (Field field in sqlUpdate.Fields)
            {
                fields.Add(field.Name, field.Value);
            }

            query.AsUpdate(fields);
        }

        private void BuildInsertQuery(Query query, SqlInsert sqlInsert)
        {
            query.From(sqlInsert.Table);

            if (sqlInsert.Query != null)
            // In case of insert from query
            {
                Query queryForInsert = BuildSelectQuery(new Query(), sqlInsert.Query);

                List<string> columns = new List<string>();
                foreach (Field field in sqlInsert.Query.Fields)
                {
                    columns.Add(field.Value == null ? field.Name : field.Value.ToString());
                }

                query.AsInsert(columns, queryForInsert);
            }
            else
            // In case of a direct insert statement
            {
                Dictionary<string, object> fields = new Dictionary<string, object>();
                foreach (Field field in sqlInsert.Fields)
                {
                    fields.Add(field.Name, field.Value);
                }

                query.AsInsert(fields);
            }
        }

        private void BuildDeleteQuery(Query query, SqlDelete sqlDelete)
        {
            query.From(sqlDelete.Table);

            if (sqlDelete.Filters != null)
            {
                bool isFirst = true;
                foreach (Expression expression in sqlDelete.Filters)
                {
                    this.AddExpression<Query>(query, isFirst, expression);
                    isFirst = false;
                }
            }

            query.AsDelete();
        }
    }
}
