// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
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
            var transactionOptions = new TransactionOptions {
                IsolationLevel = sqlFacadeConfig.TransactionIsolationLevel,
            };
            // Always Required: join an ambient outer scope when present so app-level
            // TransactionScope can roll back multiple facade calls together. (RequiresNew
            // would commit each call independently and surprise callers.)
            using TransactionScope transactionScope = new TransactionScope(
                TransactionScopeOption.Required,
                transactionOptions,
                TransactionScopeAsyncFlowOption.Enabled);

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
            if (sqlSelect.NestedLists != null && sqlSelect.NestedLists.Count > 0)
                return NestedListMapper.Map<T>(query.Get(), sqlSelect.NestedLists);
            return query.Get<T>().AsList();
        }

        /// <summary>
        /// Compiles the child <see cref="SqlSelect"/> with the same builder as top-level queries,
        /// then wraps it in the dialect-specific JSON array aggregate.
        /// </summary>
        private string BuildNestedListSubquery(NestedList nestedList)
        {
            if (nestedList == null)
                throw new ArgumentNullException(nameof(nestedList));

            SqlSelect effective = nestedList.SelectForCompile(sqlFacadeConfig.DbType);
            Query innerQuery = new Query();
            BuildSelectQuery(innerQuery, effective);
            string innerSql = QFactory.GetCompiler(sqlFacadeConfig.DbType).Compile(innerQuery).ToString();
            return NestedList.Wrap(sqlFacadeConfig.DbType, innerSql, effective);
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
            List<T> results = sqlSelect.NestedLists != null && sqlSelect.NestedLists.Count > 0
                ? NestedListMapper.Map<T>(query.Get(), sqlSelect.NestedLists)
                : query.Get<T>().AsList();

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

            // Count must not carry ORDER BY: SQL Server rejects ORDER BY inside the CTE/derived
            // table used for counting unless TOP/OFFSET is also present. NestedLists are irrelevant
            // to the row count and expensive to compile.
            SqlSelect sqlSelectForCountSource = new SqlSelect
            {
                Table = sqlSelect.Table,
                FromDerivedTable = sqlSelect.FromDerivedTable,
                CommonTableExpressions = sqlSelect.CommonTableExpressions,
                Fields = sqlSelect.Fields,
                Joins = sqlSelect.Joins,
                Where = sqlSelect.Where,
                GroupBys = sqlSelect.GroupBys,
                Having = sqlSelect.Having,
                SqlCombines = sqlSelect.SqlCombines
                // Sorts / NestedLists intentionally omitted
            };

            SqlSelect sqlSelectForCount = new SqlSelect(new CommonTableExpression(sqlSelectForCountSource, "cte"))
                .WithField(new Field("count(*)", "Total", true));

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
            if (TryRewriteCombineWithOuterSort(query, sqlSelect, out Query rewritten))
                return rewritten;

            ApplySelectFrom(query, sqlSelect);
            ApplySelectCtes(query, sqlSelect);
            ApplySelectFields(query, sqlSelect);
            ApplySelectNestedLists(query, sqlSelect);
            ApplySelectWhere(query, sqlSelect);
            ApplySelectJoins(query, sqlSelect);
            ApplySelectGroupBys(query, sqlSelect);
            ApplySelectHaving(query, sqlSelect);
            ApplySelectCombines(query, sqlSelect);
            ApplySelectSorts(query, sqlSelect);
            return query;
        }

        private bool TryRewriteCombineWithOuterSort(Query query, SqlSelect sqlSelect, out Query rewritten)
        {
            // SqlKata attaches OrderBy to the first UNION/INTERSECT/EXCEPT branch. When the select
            // has both combines and sorts, wrap as a derived table so ORDER BY applies to the result set.
            rewritten = null;
            if (sqlSelect.SqlCombines == null || sqlSelect.SqlCombines.Count == 0
                || sqlSelect.Sorts == null || sqlSelect.Sorts.Count == 0)
                return false;

            List<Sort> sorts = sqlSelect.Sorts;
            sqlSelect.Sorts = null;
            try
            {
                SqlSelect outer = new SqlSelect(new DerivedTable(sqlSelect, "_combine"));
                foreach (Sort sort in sorts)
                    outer = outer.WithSort(sort);
                rewritten = BuildSelectQuery(query, outer);
                return true;
            }
            finally
            {
                sqlSelect.Sorts = sorts;
            }
        }

        private void ApplySelectFrom(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.Table != null)
            {
                query.From(sqlSelect.Table.Alias == null
                    ? sqlSelect.Table.Name
                    : sqlSelect.Table.Name + " as " + sqlSelect.Table.Alias);
                return;
            }

            if (sqlSelect.FromDerivedTable != null)
            {
                Query derivedTable = BuildSelectQuery(new Query(), sqlSelect.FromDerivedTable.Select)
                    .As(sqlSelect.FromDerivedTable.Alias);
                query.From(derivedTable);
            }
        }

        private void ApplySelectCtes(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.CommonTableExpressions == null)
                return;

            foreach (CommonTableExpression commonTableExpression in sqlSelect.CommonTableExpressions)
            {
                if (commonTableExpression.RawSql != null)
                {
                    query.WithRaw(commonTableExpression.Alias, commonTableExpression.RawSql);
                    continue;
                }

                Query cte = new Query();
                BuildSelectQuery(cte, commonTableExpression.Select);
                query.With(commonTableExpression.Alias, cte);
            }
        }

        private static void ApplySelectFields(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.Fields == null)
                return;

            foreach (Field field in sqlSelect.Fields)
            {
                if (field.IsRaw)
                    query.SelectRaw(field.Name + " as " + field.Value);
                else
                    query.Select(field.Value == null ? field.Name : field.Name + " as " + field.Value);
            }
        }

        private void ApplySelectNestedLists(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.NestedLists == null)
                return;

            foreach (NestedList nestedList in sqlSelect.NestedLists)
            {
                string subquery = BuildNestedListSubquery(nestedList);
                query.SelectRaw(subquery + " as " + nestedList.ResultAlias);
            }
        }

        private void ApplySelectWhere(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.Where != null)
                query.Where(q => BuildFilter(q, sqlSelect.Where));
        }

        private void ApplySelectJoins(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.Joins == null)
                return;

            foreach (Join join in sqlSelect.Joins)
                ApplySelectJoin(query, join);
        }

        private void ApplySelectJoin(Query query, Join join)
        {
            SqlKata.Join sqlKataJoin = new SqlKata.Join();
            sqlKataJoin.On(
                join.OnExpression.Name,
                UnwrapJsonValue(join.OnExpression.Value)?.ToString(),
                join.OnExpression.Relation.ToString());

            if (join.JoinExpressions != null)
            {
                bool isFirst = true;
                foreach (Expression joinExpression in join.JoinExpressions)
                {
                    AddExpression(sqlKataJoin, isFirst, joinExpression);
                    isFirst = false;
                }
            }

            string tableRef = join.JoinTable != null
                ? (join.JoinTable.Alias == null
                    ? join.JoinTable.Name
                    : join.JoinTable.Name + " as " + join.JoinTable.Alias)
                : join.JoinCTE.Alias;

            if (Object.Equals(join.JoinType, JoinType.LeftJoin))
                query.LeftJoin(tableRef, j => sqlKataJoin);
            else
                query.Join(tableRef, j => sqlKataJoin);
        }

        private static void ApplySelectGroupBys(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.GroupBys == null)
                return;

            foreach (GroupBy groupBy in sqlSelect.GroupBys)
            {
                if (groupBy.IsRaw)
                    query.GroupByRaw(groupBy.Value);
                else
                    query.GroupBy(groupBy.Value);
            }
        }

        private void ApplySelectHaving(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.Having != null)
                query.Having(q => BuildFilter(q, sqlSelect.Having));
        }

        private void ApplySelectCombines(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.SqlCombines == null)
                return;

            foreach (SqlCombine sqlCombine in sqlSelect.SqlCombines)
            {
                if (Object.Equals(sqlCombine.SqlRelation, SqlRelation.Union))
                    query.Union(q => BuildSelectQuery(q, sqlCombine.SqlSelect));
                else if (Object.Equals(sqlCombine.SqlRelation, SqlRelation.UnionAll))
                    query.UnionAll(q => BuildSelectQuery(q, sqlCombine.SqlSelect));
                else if (Object.Equals(sqlCombine.SqlRelation, SqlRelation.Intersect))
                    query.Intersect(q => BuildSelectQuery(q, sqlCombine.SqlSelect));
                else if (Object.Equals(sqlCombine.SqlRelation, SqlRelation.Except))
                    query.Except(q => BuildSelectQuery(q, sqlCombine.SqlSelect));
            }
        }

        private static void ApplySelectSorts(Query query, SqlSelect sqlSelect)
        {
            if (sqlSelect.Sorts == null)
                return;

            foreach (Sort sort in sqlSelect.Sorts)
            {
                if (sort.IsAscending)
                    query.OrderBy(sort.Name);
                else
                    query.OrderByDesc(sort.Name);
            }
        }

        private Query BuildFilter(Query query, Filter filter)
        {
            bool isFirst = true;
            if (filter.Expressions != null)
            {
                foreach (Expression expression in filter.Expressions)
                {
                    AddExpression(query, isFirst, expression);
                    isFirst = false;
                }
            }

            if (filter.Filters != null)
            {
                foreach (Filter nestedFilter in filter.Filters)
                {
                    ApplyNestedFilter(query, filter, nestedFilter, ref isFirst);
                }
            }

            return query;
        }

        private void ApplyNestedFilter(Query query, Filter parent, Filter nestedFilter, ref bool isFirst)
        {
            if (isFirst)
            {
                query.Where(q => BuildFilter(q, nestedFilter));
                isFirst = false;
                return;
            }

            if (Object.Equals(parent.LogicalRelation, LogicalRelation.And))
                query.Where(q => BuildFilter(q, nestedFilter));
            else if (Object.Equals(parent.LogicalRelation, LogicalRelation.Or))
                query.OrWhere(q => BuildFilter(q, nestedFilter));
            else if (Object.Equals(parent.LogicalRelation, LogicalRelation.AndNot))
                query.WhereNot(q => BuildFilter(q, nestedFilter));
            else if (Object.Equals(parent.LogicalRelation, LogicalRelation.OrNot))
                query.OrWhereNot(q => BuildFilter(q, nestedFilter));
        }

        private static object UnwrapJsonValue(object value)
        {
            if (value == null || value is not JsonElement jsonElement)
                return value;
            return UnwrapJsonElement(jsonElement);
        }

        private static object UnwrapJsonElement(JsonElement jsonElement) =>
            jsonElement.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => UnwrapJsonNumber(jsonElement),
                _ => jsonElement
            };

        private static object UnwrapJsonNumber(JsonElement jsonElement)
        {
            if (jsonElement.TryGetInt32(out int intValue))
                return intValue;
            if (jsonElement.TryGetInt64(out long longValue))
                return longValue;
            return jsonElement.GetDouble();
        }

        private static object ResolveExpressionValue(Expression expression)
        {
            if (expression.Value is not JsonElement jsonElement)
            {
                return expression.Value;
            }

            if (Object.Equals(expression.Relation, Relation.In))
            {
                return expression.Value;
            }

            if (Object.Equals(expression.Relation, Relation.Exists) && jsonElement.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<SqlSelect>(jsonElement.GetRawText());
            }

            if (expression.IsRaw && jsonElement.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<object[]>(jsonElement.GetRawText());
            }

            return UnwrapJsonValue(jsonElement);
        }

        private enum InClauseMode
        {
            And,
            AndNot,
            Or,
            OrNot
        }

        private static InClauseMode GetInClauseMode(LogicalRelation logicalRelation)
        {
            if (Object.Equals(logicalRelation, LogicalRelation.And))
            {
                return InClauseMode.And;
            }
            if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
            {
                return InClauseMode.AndNot;
            }
            if (Object.Equals(logicalRelation, LogicalRelation.Or))
            {
                return InClauseMode.Or;
            }
            if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
            {
                return InClauseMode.OrNot;
            }

            throw new ArgumentException($"Unsupported logical relation {logicalRelation} for In expression");
        }

        private static Type GetEnumerableElementType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            foreach (Type iface in type.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return iface.GetGenericArguments()[0];
                }
            }

            return null;
        }

        private static bool IsStringJsonArray(JsonElement jsonElement)
        {
            foreach (JsonElement item in jsonElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when every array element is a GUID string (JSON round-trip of <see cref="Guid"/> lists).
        /// </summary>
        private static bool IsGuidJsonArray(JsonElement jsonElement)
        {
            foreach (JsonElement item in jsonElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(item.GetString(), out _))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyInSubquery<Q>(BaseQuery<Q> query, string column, SqlSelect sqlSelect, InClauseMode mode) where Q : BaseQuery<Q>
        {
            Query subQuery = BuildSelectQuery(new Query(), sqlSelect);
            switch (mode)
            {
                case InClauseMode.And:
                    query.WhereIn(column, subQuery);
                    break;
                case InClauseMode.AndNot:
                    query.WhereNotIn(column, subQuery);
                    break;
                case InClauseMode.Or:
                    query.OrWhereIn(column, subQuery);
                    break;
                case InClauseMode.OrNot:
                    query.OrWhereNotIn(column, subQuery);
                    break;
            }
        }

        private void ApplyInValues<Q, T>(BaseQuery<Q> query, string column, IEnumerable<T> values, InClauseMode mode) where Q : BaseQuery<Q>
        {
            switch (mode)
            {
                case InClauseMode.And:
                    query.WhereIn(column, values);
                    break;
                case InClauseMode.AndNot:
                    query.WhereNotIn(column, values);
                    break;
                case InClauseMode.Or:
                    query.OrWhereIn(column, values);
                    break;
                case InClauseMode.OrNot:
                    query.OrWhereNotIn(column, values);
                    break;
            }
        }

        /// <summary>
        /// Binds GUID IN-list values per dialect: native <see cref="Guid"/> for Postgres
        /// (<c>uuid</c>) and SQL Server (<c>uniqueidentifier</c>); invariant <c>D</c>-format
        /// strings for SQLite, MySQL, MariaDB, Oracle, and any other engine that stores UUIDs as text
        /// (<c>CHAR(36)</c>, <c>VARCHAR</c>, etc.).
        /// </summary>
        private void ApplyGuidInValues<Q>(BaseQuery<Q> query, string column, IEnumerable<Guid> values, InClauseMode mode) where Q : BaseQuery<Q>
        {
            if (sqlFacadeConfig.DbType == DbType.POSTGRES || sqlFacadeConfig.DbType == DbType.SQLSERVER)
            {
                ApplyInValues(query, column, values, mode);
            }
            else
            {
                // SQLite / MySQL / MariaDB / Oracle: no portable native UUID bind — use D-format text.
                ApplyInValues(query, column, values.Select(g => g.ToString("D")), mode);
            }
        }

        private void ApplyInExpression<Q>(BaseQuery<Q> query, Expression expression, InClauseMode mode) where Q : BaseQuery<Q>
        {
            object value = expression.Value;
            string column = expression.Name;

            if (value is SqlSelect sqlSelect)
            {
                ApplyInSubquery(query, column, sqlSelect, mode);
                return;
            }

            if (value is JsonElement jsonElement)
            {
                ApplyInJsonElement(query, column, jsonElement, mode);
                return;
            }

            ApplyInEnumerable(query, column, value, mode);
        }

        private void ApplyInJsonElement<Q>(BaseQuery<Q> query, string column, JsonElement jsonElement, InClauseMode mode)
            where Q : BaseQuery<Q>
        {
            if (jsonElement.ValueKind == JsonValueKind.Object)
            {
                ApplyInSubquery(query, column, JsonSerializer.Deserialize<SqlSelect>(jsonElement.GetRawText()), mode);
                return;
            }

            if (jsonElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException($"Unsupported JsonElement value kind {jsonElement.ValueKind} for In expression");

            string json = jsonElement.GetRawText();
            if (!IsStringJsonArray(jsonElement))
            {
                ApplyInValues(query, column, JsonSerializer.Deserialize<IEnumerable<double>>(json), mode);
                return;
            }

            // Guid[] serializes to a string array; prefer Guid binding when every element parses.
            if (IsGuidJsonArray(jsonElement))
                ApplyGuidInValues(query, column, JsonSerializer.Deserialize<IEnumerable<Guid>>(json), mode);
            else
                ApplyInValues(query, column, JsonSerializer.Deserialize<IEnumerable<string>>(json), mode);
        }

        private void ApplyInEnumerable<Q>(BaseQuery<Q> query, string column, object value, InClauseMode mode)
            where Q : BaseQuery<Q>
        {
            Type elementType = GetEnumerableElementType(value.GetType())
                ?? throw new ArgumentException($"Unsupported In list value type {value.GetType()}");

            if (elementType == typeof(string))
                ApplyInValues(query, column, (IEnumerable<string>)value, mode);
            else if (elementType == typeof(Guid))
                ApplyGuidInValues(query, column, (IEnumerable<Guid>)value, mode);
            else if (elementType == typeof(int))
                ApplyInValues(query, column, (IEnumerable<int>)value, mode);
            else if (elementType == typeof(long))
                ApplyInValues(query, column, (IEnumerable<long>)value, mode);
            else if (elementType == typeof(float))
                ApplyInValues(query, column, (IEnumerable<float>)value, mode);
            else if (elementType == typeof(double))
                ApplyInValues(query, column, (IEnumerable<double>)value, mode);
            else
                throw new ArgumentException($"Unsupported In list element type {elementType}");
        }

        private void AddExpression<Q>(BaseQuery<Q> query, bool isFirst, Expression expression) where Q : BaseQuery<Q>
        {
            object value = ResolveExpressionValue(expression);
            LogicalRelation logicalRelation = NormalizeFirstLogicalRelation(expression.LogicalRelation, isFirst);

            if (IsConjunction(logicalRelation, isFirst))
                ApplyConjunctionExpression(query, expression, value, logicalRelation);
            else if (IsDisjunction(logicalRelation))
                ApplyDisjunctionExpression(query, expression, value, logicalRelation);
        }

        private static LogicalRelation NormalizeFirstLogicalRelation(LogicalRelation logicalRelation, bool isFirst)
        {
            if (!isFirst)
                return logicalRelation;
            if (Object.Equals(logicalRelation, LogicalRelation.Or))
                return LogicalRelation.And;
            if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                return LogicalRelation.AndNot;
            return logicalRelation;
        }

        private static bool IsConjunction(LogicalRelation logicalRelation, bool isFirst) =>
            Object.Equals(logicalRelation, LogicalRelation.And)
            || Object.Equals(logicalRelation, LogicalRelation.AndNot)
            || isFirst;

        private static bool IsDisjunction(LogicalRelation logicalRelation) =>
            Object.Equals(logicalRelation, LogicalRelation.Or)
            || Object.Equals(logicalRelation, LogicalRelation.OrNot);

        private void ApplyConjunctionExpression<Q>(
            BaseQuery<Q> query, Expression expression, object value, LogicalRelation logicalRelation)
            where Q : BaseQuery<Q>
        {
            if (expression.IsRaw)
            {
                if (Object.Equals(logicalRelation, LogicalRelation.And))
                    query.WhereRaw(expression.Name, (object[])value);
                else if (Object.Equals(logicalRelation, LogicalRelation.AndNot))
                    throw new ArgumentException("Logical relation `AndNot' is not support for raw expressions");
                return;
            }

            ApplyTypedRelation(query, expression, value, logicalRelation, isOr: false);
        }

        private void ApplyDisjunctionExpression<Q>(
            BaseQuery<Q> query, Expression expression, object value, LogicalRelation logicalRelation)
            where Q : BaseQuery<Q>
        {
            if (expression.IsRaw)
            {
                if (Object.Equals(logicalRelation, LogicalRelation.Or))
                    query.OrWhereRaw(expression.Name, (object[])value);
                else if (Object.Equals(logicalRelation, LogicalRelation.OrNot))
                    throw new ArgumentException("Logical relation `OrNot' is not support for raw expressions");
                return;
            }

            ApplyTypedRelation(query, expression, value, logicalRelation, isOr: true);
        }

        private void ApplyTypedRelation<Q>(
            BaseQuery<Q> query, Expression expression, object value, LogicalRelation logicalRelation, bool isOr)
            where Q : BaseQuery<Q>
        {
            bool negate = isOr
                ? Object.Equals(logicalRelation, LogicalRelation.OrNot)
                : Object.Equals(logicalRelation, LogicalRelation.AndNot);
            Relation relation = expression.Relation;

            if (TryApplyComparison(query, expression.Name, value, relation, negate, isOr))
                return;
            if (Object.Equals(relation, Relation.In))
            {
                ApplyInExpression(query, expression, GetInClauseMode(logicalRelation));
                return;
            }
            if (TryApplyStringMatch(query, expression.Name, value, relation, negate, isOr))
                return;
            if (TryApplyNullOrTrue(query, expression.Name, relation, negate, isOr))
                return;
            if (Object.Equals(relation, Relation.Exists))
            {
                ApplyExists(query, (SqlSelect)value, negate, isOr);
                return;
            }

            throw new ArgumentException($"Unknown expression relation {expression.Relation}");
        }

        private static readonly Dictionary<string, (string Positive, string Negative)> ComparisonOperators =
            new(StringComparer.Ordinal)
            {
                [Relation.EqualTo.Value] = (Relation.EqualTo.ToString(), "!="),
                [Relation.GreaterThan.Value] = (Relation.GreaterThan.ToString(), Relation.LessThanOrEqualTo.ToString()),
                [Relation.GreaterThanOrEqualTo.Value] = (Relation.GreaterThanOrEqualTo.ToString(), Relation.LessThan.ToString()),
                [Relation.LessThan.Value] = (Relation.LessThan.ToString(), Relation.GreaterThanOrEqualTo.ToString()),
                [Relation.LessThanOrEqualTo.Value] = (Relation.LessThanOrEqualTo.ToString(), Relation.GreaterThan.ToString()),
            };

        private static bool TryApplyComparison<Q>(
            BaseQuery<Q> query, string name, object value, Relation relation, bool negate, bool isOr)
            where Q : BaseQuery<Q>
        {
            string op = ResolveComparisonOperator(relation, negate);
            if (op == null)
                return false;
            if (isOr)
                query.OrWhere(name, op, value);
            else
                query.Where(name, op, value);
            return true;
        }

        private static string ResolveComparisonOperator(Relation relation, bool negate)
        {
            if (relation?.Value == null
                || !ComparisonOperators.TryGetValue(relation.Value, out (string Positive, string Negative) pair))
                return null;
            return negate ? pair.Negative : pair.Positive;
        }

        private static bool TryApplyStringMatch<Q>(
            BaseQuery<Q> query, string name, object value, Relation relation, bool negate, bool isOr)
            where Q : BaseQuery<Q>
        {
            if (Object.Equals(relation, Relation.StartsWith))
            {
                ApplyStartsWith(query, name, value, negate, isOr);
                return true;
            }
            if (Object.Equals(relation, Relation.EndsWith))
            {
                ApplyEndsWith(query, name, value, negate, isOr);
                return true;
            }
            if (Object.Equals(relation, Relation.Contains))
            {
                ApplyContains(query, name, value, negate, isOr);
                return true;
            }
            return false;
        }

        private static void ApplyStartsWith<Q>(BaseQuery<Q> query, string name, object value, bool negate, bool isOr)
            where Q : BaseQuery<Q>
        {
            if (isOr)
            {
                if (negate) query.OrWhereNotStarts(name, value);
                else query.OrWhereStarts(name, value);
            }
            else
            {
                if (negate) query.WhereNotStarts(name, value);
                else query.WhereStarts(name, value);
            }
        }

        private static void ApplyEndsWith<Q>(BaseQuery<Q> query, string name, object value, bool negate, bool isOr)
            where Q : BaseQuery<Q>
        {
            if (isOr)
            {
                if (negate) query.OrWhereNotEnds(name, value);
                else query.OrWhereEnds(name, value);
            }
            else
            {
                if (negate) query.WhereNotEnds(name, value);
                else query.WhereEnds(name, value);
            }
        }

        private static void ApplyContains<Q>(BaseQuery<Q> query, string name, object value, bool negate, bool isOr)
            where Q : BaseQuery<Q>
        {
            if (isOr)
            {
                if (negate) query.OrWhereNotContains(name, value);
                else query.OrWhereContains(name, value);
            }
            else
            {
                if (negate) query.WhereNotContains(name, value);
                else query.WhereContains(name, value);
            }
        }

        private static bool TryApplyNullOrTrue<Q>(
            BaseQuery<Q> query, string name, Relation relation, bool negate, bool isOr)
            where Q : BaseQuery<Q>
        {
            if (Object.Equals(relation, Relation.NullValue))
            {
                ApplyNullValue(query, name, negate, isOr);
                return true;
            }
            if (Object.Equals(relation, Relation.TrueValue))
            {
                ApplyTrueValue(query, name, negate, isOr);
                return true;
            }
            return false;
        }

        private static void ApplyNullValue<Q>(BaseQuery<Q> query, string name, bool negate, bool isOr)
            where Q : BaseQuery<Q>
        {
            if (isOr)
            {
                if (negate) query.OrWhereNotNull(name);
                else query.OrWhereNull(name);
            }
            else
            {
                if (negate) query.WhereNotNull(name);
                else query.WhereNull(name);
            }
        }

        private static void ApplyTrueValue<Q>(BaseQuery<Q> query, string name, bool negate, bool isOr)
            where Q : BaseQuery<Q>
        {
            if (isOr)
            {
                if (negate) query.OrWhereFalse(name);
                else query.OrWhereTrue(name);
            }
            else
            {
                if (negate) query.WhereFalse(name);
                else query.WhereTrue(name);
            }
        }

        private void ApplyExists<Q>(BaseQuery<Q> query, SqlSelect sqlSelect, bool negate, bool isOr)
            where Q : BaseQuery<Q>
        {
            Query existsQuery = BuildSelectQuery(new Query(), sqlSelect);
            if (isOr)
            {
                if (negate) query.OrWhereNotExists(existsQuery);
                else query.OrWhereExists(existsQuery);
            }
            else
            {
                if (negate) query.WhereNotExists(existsQuery);
                else query.WhereExists(existsQuery);
            }
        }

        private void BuildUpdateQuery(Query query, SqlUpdate sqlUpdate)
        {
            query.From(sqlUpdate.Table);

            // Common Table Expressions
            if (sqlUpdate.CommonTableExpressions != null)
            {
                foreach (CommonTableExpression commonTableExpression in sqlUpdate.CommonTableExpressions)
                {
                    Query cte = new Query();
                    if (commonTableExpression.RawSql != null)
                    {
                        query.WithRaw(commonTableExpression.Alias, commonTableExpression.RawSql);
                    }
                    else
                    {
                        BuildSelectQuery(cte, commonTableExpression.Select);
                        query.With(commonTableExpression.Alias, cte);
                    }
                }
            }

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
                fields.Add(field.Name, UnwrapJsonValue(field.Value));
            }

            query.AsUpdate(fields);
        }

        private void BuildInsertQuery(Query query, SqlInsert sqlInsert)
        {
            query.From(sqlInsert.Table);

            // Common Table Expressions
            if (sqlInsert.CommonTableExpressions != null)
            {
                foreach (CommonTableExpression commonTableExpression in sqlInsert.CommonTableExpressions)
                {
                    Query cte = new Query();
                    if (commonTableExpression.RawSql != null)
                    {
                        query.WithRaw(commonTableExpression.Alias, commonTableExpression.RawSql);
                    }
                    else
                    {
                        BuildSelectQuery(cte, commonTableExpression.Select);
                        query.With(commonTableExpression.Alias, cte);
                    }
                }
            }

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
                    fields.Add(field.Name, UnwrapJsonValue(field.Value));
                }

                query.AsInsert(fields);
            }
        }

        private void BuildDeleteQuery(Query query, SqlDelete sqlDelete)
        {
            query.From(sqlDelete.Table);

            // Common Table Expressions
            if (sqlDelete.CommonTableExpressions != null)
            {
                foreach (CommonTableExpression commonTableExpression in sqlDelete.CommonTableExpressions)
                {
                    Query cte = new Query();
                    if (commonTableExpression.RawSql != null)
                    {
                        query.WithRaw(commonTableExpression.Alias, commonTableExpression.RawSql);
                    }
                    else
                    {
                        BuildSelectQuery(cte, commonTableExpression.Select);
                        query.With(commonTableExpression.Alias, cte);
                    }
                }
            }

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
