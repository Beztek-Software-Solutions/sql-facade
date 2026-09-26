// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Dialect-portable correlated subquery that aggregates a child <see cref="SqlSelect"/> into a JSON
    /// array, then maps it onto a typed list property on the parent row
    /// (e.g. <c>List&lt;DonationDto&gt; Donations</c>).
    /// <para>
    /// Construct like other facade objects: <c>new NestedList&lt;DonationDto&gt;("Donations", childSelect, correlate)</c>.
    /// <see cref="Correlate"/> is required (Join-style column=column <see cref="Expression"/> or a full <see cref="Filter"/>).
    /// The result alias must match the parent property name.
    /// </para>
    /// <para>
    /// Engines: Postgres (<c>json_agg(row_to_json(...))</c>), SQLite (<c>json_group_array(json_object(...))</c>),
    /// SQL Server (<c>FOR JSON PATH</c>), MySQL / MariaDB (<c>JSON_ARRAYAGG(JSON_OBJECT(...))</c> with
    /// engine-specific nested-JSON handling), Oracle (<c>JSON_ARRAYAGG(JSON_OBJECT(... FORMAT JSON))</c>).
    /// </para>
    /// </summary>
    public class NestedList
    {
        public string ResultAlias { get; set; }

        /// <summary>Child list query. Must include explicit <see cref="SqlSelect.Fields"/> (JSON property names = field aliases).</summary>
        public SqlSelect Select { get; set; }

        /// <summary>
        /// Required parent correlation filter. Expressions use <see cref="Join.OnExpression"/> semantics
        /// (both sides columns). Full <see cref="Filter"/> nesting and <see cref="LogicalRelation"/> apply.
        /// </summary>
        public Filter Correlate { get; set; }

        /// <summary>Assembly-qualified name of the list element type (for JSON round-trip of <see cref="SqlSelect"/>).</summary>
        public string ElementTypeName { get; set; }

        private Type _elementType;

        /// <summary>CLR type of each element in the mapped list (e.g. <c>typeof(DonationDto)</c>).</summary>
        [JsonIgnore]
        public Type ElementType
        {
            get
            {
                if (_elementType != null)
                    return _elementType;
                if (string.IsNullOrWhiteSpace(ElementTypeName))
                    return null;
                _elementType = Type.GetType(ElementTypeName, throwOnError: false);
                return _elementType;
            }
            set
            {
                _elementType = value;
                ElementTypeName = value?.AssemblyQualifiedName;
            }
        }

        public NestedList() { }

        /// <param name="resultAlias">Parent DTO property name that receives the mapped list.</param>
        /// <param name="select">Child list query; field aliases become JSON property names.</param>
        /// <param name="correlate">Join-style ON expression (both sides columns); wrapped in a <see cref="Filter"/>.</param>
        /// <param name="elementType">CLR type of each list element.</param>
        public NestedList(string resultAlias, SqlSelect select, Expression correlate, Type elementType)
            : this(resultAlias, select, WrapCorrelate(correlate), elementType)
        {
        }

        public NestedList(string resultAlias, SqlSelect select, Filter correlate, Type elementType)
        {
            if (string.IsNullOrWhiteSpace(resultAlias))
                throw new ArgumentException("Result alias is required.", nameof(resultAlias));
            if (select == null)
                throw new ArgumentNullException(nameof(select));
            if (correlate == null)
                throw new ArgumentNullException(nameof(correlate));
            if (!HasCorrelate(correlate))
                throw new ArgumentException("Correlate must contain at least one expression or nested filter.", nameof(correlate));
            if (elementType == null)
                throw new ArgumentNullException(nameof(elementType));

            ResultAlias = resultAlias.Trim();
            Select = select;
            Correlate = correlate;
            ElementType = elementType;
        }

        /// <summary>
        /// Builds the scalar subquery SQL for the given dialect by compiling <see cref="Select"/>
        /// (plus <see cref="Correlate"/>) then wrapping it in the engine-specific JSON array aggregate.
        /// </summary>
        public string ToSql(DbType dbType)
        {
            Validate();
            SqlSelect effective = SelectForCompile(dbType);
            string innerSql = CompileChildSelect(dbType, effective);
            return Wrap(dbType, innerSql, effective);
        }

        /// <summary>
        /// Child select with correlation filter merged into <see cref="SqlSelect.Where"/>
        /// (does not mutate the stored <see cref="Select"/>).
        /// </summary>
        internal SqlSelect SelectForCompile(DbType dbType)
        {
            Validate();

            Filter where = new Filter();
            if (Select.Where != null)
                where.WithFilter(Select.Where);
            where.WithFilter(ToCorrelateFilter(Correlate, dbType));

            return new SqlSelect
            {
                Table = Select.Table,
                FromDerivedTable = Select.FromDerivedTable,
                CommonTableExpressions = Select.CommonTableExpressions,
                Fields = Select.Fields,
                NestedLists = Select.NestedLists,
                Joins = Select.Joins,
                Where = where,
                GroupBys = Select.GroupBys,
                Having = Select.Having,
                Sorts = Select.Sorts,
                SqlCombines = Select.SqlCombines
            };
        }

        /// <summary>Backward-compatible overload; defaults correlate quoting to SQLite/Postgres style.</summary>
        internal SqlSelect SelectForCompile() => SelectForCompile(DbType.SQLITE);

        internal static string Wrap(DbType dbType, string innerSql, SqlSelect childSelect)
        {
            if (string.IsNullOrWhiteSpace(innerSql))
                throw new ArgumentException("Compiled child SQL is required.", nameof(innerSql));
            return dbType switch
            {
                DbType.POSTGRES => WrapPostgres(innerSql),
                DbType.SQLITE => WrapSqlite(innerSql, childSelect),
                DbType.SQLSERVER => WrapSqlServer(innerSql),
                DbType.MYSQL => WrapMySql(innerSql, childSelect),
                DbType.MARIADB => WrapMariaDb(innerSql, childSelect),
                DbType.ORACLE => WrapOracle(innerSql, childSelect),
                _ => throw new ArgumentException($"Unsupported DbType for NestedList: {dbType}")
            };
        }

        internal static Filter ToCorrelateFilter(Filter correlate, DbType dbType)
        {
            if (correlate == null)
                throw new ArgumentNullException(nameof(correlate));

            Filter result = new Filter(correlate.LogicalRelation ?? LogicalRelation.And);
            CopyCorrelateExpressions(correlate, result, dbType);
            CopyCorrelateNestedFilters(correlate, result, dbType);
            return result;
        }

        private static void CopyCorrelateExpressions(Filter correlate, Filter result, DbType dbType)
        {
            if (correlate.Expressions == null)
                return;
            foreach (Expression expression in correlate.Expressions)
            {
                if (expression != null)
                    result.WithExpression(ToCorrelateWhere(expression, dbType));
            }
        }

        private static void CopyCorrelateNestedFilters(Filter correlate, Filter result, DbType dbType)
        {
            if (correlate.Filters == null)
                return;
            foreach (Filter nested in correlate.Filters)
            {
                if (nested != null)
                    result.WithFilter(ToCorrelateFilter(nested, dbType));
            }
        }

        internal static Filter ToCorrelateFilter(Filter correlate) =>
            ToCorrelateFilter(correlate, DbType.SQLITE);

        internal static Expression ToCorrelateWhere(Expression correlate, DbType dbType)
        {
            if (correlate == null)
                throw new ArgumentNullException(nameof(correlate));
            if (correlate.IsRaw)
                return correlate;

            EnsureCorrelateColumns(correlate);
            EnsureCorrelateRelationAllowed(correlate.Relation ?? Relation.EqualTo);

            string left = QuoteCorrelateIdent(correlate.Name.Trim(), dbType);
            string right = QuoteCorrelateIdent(correlate.Value.ToString().Trim(), dbType);
            string raw = $"{left} {correlate.Relation ?? Relation.EqualTo} {right}";
            return new Expression(raw, Array.Empty<object>())
                .WithIsRaw()
                .WithLogicalRelation(correlate.LogicalRelation ?? LogicalRelation.And);
        }

        private static void EnsureCorrelateColumns(Expression correlate)
        {
            if (string.IsNullOrWhiteSpace(correlate.Name))
                throw new InvalidOperationException("Correlate Expression.Name (left column) is required.");
            if (correlate.Value == null || string.IsNullOrWhiteSpace(correlate.Value.ToString()))
                throw new InvalidOperationException(
                    "Correlate Expression.Value must be the right-hand column (Join.OnExpression semantics).");
        }

        private static readonly Relation[] UnsupportedCorrelateRelations =
        [
            Relation.In,
            Relation.Exists,
            Relation.NullValue,
            Relation.TrueValue,
            Relation.StartsWith,
            Relation.EndsWith,
            Relation.Contains
        ];

        private static void EnsureCorrelateRelationAllowed(Relation relation)
        {
            foreach (Relation unsupported in UnsupportedCorrelateRelations)
            {
                if (Object.Equals(relation, unsupported))
                {
                    throw new InvalidOperationException(
                        $"Correlate does not support Relation.{relation.Value}; use comparison operators (=, <, >, …) or a raw Expression.");
                }
            }
        }

        internal static Expression ToCorrelateWhere(Expression correlate) =>
            ToCorrelateWhere(correlate, DbType.SQLITE);

        private static string QuoteCorrelateIdent(string name, DbType dbType)
        {
            // Leave complex/raw fragments alone.
            if (name.IndexOfAny(new[] { ' ', '(', ')', '\'', ',', '+' }) >= 0)
                return name;

            string[] parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return dbType switch
            {
                DbType.MYSQL or DbType.MARIADB =>
                    string.Join(".", parts.Select(p => "`" + p.Replace("`", "``") + "`")),
                DbType.SQLSERVER =>
                    string.Join(".", parts.Select(p => "[" + p.Replace("]", "]]") + "]")),
                _ =>
                    string.Join(".", parts.Select(p => "\"" + p.Replace("\"", "\"\"") + "\""))
            };
        }

        private static Filter WrapCorrelate(Expression correlate)
        {
            if (correlate == null)
                throw new ArgumentNullException(nameof(correlate));
            return new Filter().WithExpression(correlate);
        }

        private static bool HasCorrelate(Filter correlate)
        {
            if (correlate == null)
                return false;
            if (correlate.Expressions != null && correlate.Expressions.Count > 0)
                return true;
            if (correlate.Filters != null && correlate.Filters.Count > 0)
                return true;
            return false;
        }

        private void Validate()
        {
            EnsureNestedListConfigured();
            ValidateChildFields();
        }

        private void EnsureNestedListConfigured()
        {
            if (string.IsNullOrWhiteSpace(ResultAlias))
                throw new InvalidOperationException("NestedList.ResultAlias is required.");
            if (ElementType == null)
                throw new InvalidOperationException("NestedList.ElementType is required.");
            if (Select == null)
                throw new InvalidOperationException("NestedList.Select (child SqlSelect) is required.");
            if (!HasCorrelate(Correlate))
                throw new InvalidOperationException("NestedList.Correlate is required.");
        }

        private void ValidateChildFields()
        {
            if (Select.Fields == null || Select.Fields.Count == 0)
                throw new InvalidOperationException(
                    "NestedList child SqlSelect must have explicit Fields (alias = JSON property name).");

            foreach (Field field in Select.Fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Name))
                    throw new InvalidOperationException("Each child Field needs a Name (SQL expression or column).");
                string key = JsonKeyFor(field);
                if (key.Contains('\'') || key.Contains('"'))
                    throw new InvalidOperationException($"JSON key contains quotes: {key}");
            }
        }

        private static string CompileChildSelect(DbType dbType, SqlSelect child)
        {
            string connectionString = dbType switch
            {
                DbType.SQLITE => "Data Source=:memory:",
                DbType.POSTGRES => "Host=localhost;Database=x;Username=x;Password=x",
                DbType.SQLSERVER => "Server=localhost;Database=x;Trusted_Connection=True;",
                DbType.MYSQL or DbType.MARIADB => "Server=localhost;Database=x;User ID=x;Password=x",
                DbType.ORACLE => "User Id=x;Password=x;Data Source=localhost:1521/XEPDB1",
                _ => throw new ArgumentException($"Unsupported DbType: {dbType}")
            };
            ISqlFacade facade = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(dbType, connectionString));
            return facade.GetSql(child, isParameterized: false);
        }

        /// <summary>
        /// Keep the aggregate typed as <c>json</c> (not <c>::text</c>). Text forces
        /// <c>row_to_json</c> to embed grandchild NestedList columns as JSON strings, which breaks mapping.
        /// </summary>
        private static string WrapPostgres(string innerSql) =>
            // Avoid a literal '[]' — SqlKata SelectRaw mangles [] into "".
            "(SELECT COALESCE(json_agg(row_to_json(_j)), json_build_array()) FROM ("
            + innerSql
            + ") AS _j)";

        private static string WrapSqlite(string innerSql, SqlSelect childSelect)
        {
            // Include NestedList columns (grandchildren). json() is required so nested JSON
            // arrays are embedded as arrays, not double-encoded strings.
            IEnumerable<string> fieldArgs = (childSelect.Fields ?? Array.Empty<Field>()).Select(f =>
            {
                string key = JsonKeyFor(f);
                return $"'{EscapeSqliteString(key)}', {QuoteSqliteIdent(key)}";
            });
            IEnumerable<string> nestedArgs = (childSelect.NestedLists ?? Array.Empty<NestedList>())
                .Where(n => n != null && !string.IsNullOrWhiteSpace(n.ResultAlias))
                .Select(n =>
                {
                    string key = n.ResultAlias.Trim();
                    return $"'{EscapeSqliteString(key)}', json({QuoteSqliteIdent(key)})";
                });
            string objArgs = string.Join(", ", fieldArgs.Concat(nestedArgs));
            if (string.IsNullOrWhiteSpace(objArgs))
                throw new InvalidOperationException(
                    "NestedList SQLite wrap requires at least one Field or NestedList on the child select.");
            return "(SELECT COALESCE(json_group_array(json_object(" + objArgs + ")), json_array()) FROM ("
                + innerSql
                + ") AS _j)";
        }

        private static string WrapSqlServer(string innerSql) =>
            // JSON_QUERY keeps nested NestedList columns typed as JSON inside FOR JSON PATH
            // (otherwise SQL Server string-escapes grandchild arrays).
            "JSON_QUERY((SELECT COALESCE(("
            + innerSql
            + " FOR JSON PATH, INCLUDE_NULL_VALUES), CHAR(91)+CHAR(93))))";

        /// <summary>
        /// MySQL has a native binary JSON type: <c>CAST(... AS JSON)</c> keeps grandchild NestedList
        /// columns typed so <c>JSON_OBJECT</c> embeds arrays instead of escaped strings.
        /// Requires MySQL 8.0+ for reliable <c>JSON_ARRAYAGG</c> / CTE usage with this facade.
        /// <para>
        /// Aggregates over the child FROM/WHERE directly (no derived-table wrap) so outer
        /// correlation works on both MySQL and MariaDB.
        /// </para>
        /// </summary>
        private static string WrapMySql(string innerSql, SqlSelect childSelect) =>
            WrapMySqlFamily(childSelect, DbType.MYSQL, nestedSql => $"CAST({nestedSql} AS JSON)");

        /// <summary>
        /// MariaDB's <c>JSON</c> is LONGTEXT-with-validation, not MySQL's binary JSON. Nested JSON
        /// values lose "JSON-ness" and get double-escaped inside <c>JSON_OBJECT</c> unless re-parsed
        /// via <c>JSON_EXTRACT(..., '$')</c>. Do not reuse the MySQL cast.
        /// <para>
        /// Same non-derived-table shape as MySQL — MariaDB rejects outer column refs inside
        /// <c>FROM (subquery) AS _j</c>.
        /// </para>
        /// </summary>
        private static string WrapMariaDb(string innerSql, SqlSelect childSelect) =>
            WrapMySqlFamily(childSelect, DbType.MARIADB, nestedSql => $"JSON_EXTRACT({nestedSql}, '$')");

        private static string WrapMySqlFamily(SqlSelect childSelect, DbType dbType, Func<string, string> nestJson)
        {
            IEnumerable<string> fieldArgs = (childSelect.Fields ?? Array.Empty<Field>()).Select(f =>
            {
                string key = JsonKeyFor(f);
                // Use the SQL expression (e.g. ch.id), not an alias from a derived table.
                string expr = string.IsNullOrWhiteSpace(f.Name) ? QuoteBacktickIdent(key) : f.Name.Trim();
                return $"'{EscapeSqlString(key)}', {expr}";
            });
            IEnumerable<string> nestedArgs = (childSelect.NestedLists ?? Array.Empty<NestedList>())
                .Where(n => n != null && !string.IsNullOrWhiteSpace(n.ResultAlias))
                .Select(n =>
                {
                    string key = n.ResultAlias.Trim();
                    string nestedSql = n.ToSql(dbType);
                    return $"'{EscapeSqlString(key)}', {nestJson(nestedSql)}";
                });
            string objArgs = string.Join(", ", fieldArgs.Concat(nestedArgs));
            if (string.IsNullOrWhiteSpace(objArgs))
                throw new InvalidOperationException(
                    "NestedList MySQL/MariaDB wrap requires at least one Field or NestedList on the child select.");

            // Compile FROM/JOIN/WHERE/ORDER with a dummy select list, then replace the SELECT clause
            // so correlation (e.g. p.id) stays at the same query level as JSON_ARRAYAGG.
            SqlSelect shell = new SqlSelect
            {
                Table = childSelect.Table,
                FromDerivedTable = childSelect.FromDerivedTable,
                CommonTableExpressions = childSelect.CommonTableExpressions,
                Joins = childSelect.Joins,
                Where = childSelect.Where,
                GroupBys = childSelect.GroupBys,
                Having = childSelect.Having,
                Sorts = childSelect.Sorts,
                SqlCombines = childSelect.SqlCombines,
                Fields = new List<Field> { new Field("1", "_x", isRaw: true) }
            };
            string shellSql = CompileChildSelect(dbType, shell);
            int fromIdx = shellSql.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
            if (fromIdx < 0)
                throw new InvalidOperationException("Unable to compile MySQL/MariaDB NestedList FROM clause.");
            string fromWhereOrder = shellSql.Substring(fromIdx);
            return "(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(" + objArgs + ")), JSON_ARRAY())"
                + fromWhereOrder + ")";
        }

        /// <summary>
        /// Oracle 19c+ NestedList via <c>JSON_ARRAYAGG</c> + <c>JSON_SERIALIZE</c>.
        /// Order goes inside <c>JSON_ARRAYAGG(... ORDER BY ...)</c> (trailing ORDER BY in a scalar
        /// subquery is ORA-00907). Empty arrays use <c>CHR(91)||CHR(93)</c> so SqlKata cannot mangle <c>[]</c>.
        /// </summary>
        private static string WrapOracle(string innerSql, SqlSelect childSelect)
        {
            string objArgs = BuildOracleJsonObjectArgs(childSelect);
            string orderInsideAgg = BuildOracleOrderInsideAgg(childSelect);
            string fromWhere = CompileOracleFromWhere(childSelect);
            return "(SELECT COALESCE(JSON_SERIALIZE(JSON_ARRAYAGG(JSON_OBJECT("
                + objArgs
                + " NULL ON NULL RETURNING CLOB)"
                + orderInsideAgg
                + " NULL ON NULL RETURNING CLOB) RETURNING VARCHAR2(4000)), CHR(91)||CHR(93))"
                + fromWhere + ")";
        }

        private static string BuildOracleJsonObjectArgs(SqlSelect childSelect)
        {
            IEnumerable<string> fieldArgs = (childSelect.Fields ?? Array.Empty<Field>()).Select(OracleFieldArg);
            IEnumerable<string> nestedArgs = (childSelect.NestedLists ?? Array.Empty<NestedList>())
                .Where(n => n != null && !string.IsNullOrWhiteSpace(n.ResultAlias))
                .Select(OracleNestedArg);
            string objArgs = string.Join(", ", fieldArgs.Concat(nestedArgs));
            if (string.IsNullOrWhiteSpace(objArgs))
                throw new InvalidOperationException(
                    "NestedList Oracle wrap requires at least one Field or NestedList on the child select.");
            return objArgs;
        }

        private static string OracleFieldArg(Field f)
        {
            string key = JsonKeyFor(f);
            string expr = string.IsNullOrWhiteSpace(f.Name)
                ? QuoteDoubleIdent(key)
                : QuoteCorrelateIdent(f.Name.Trim(), DbType.ORACLE);
            return $"'{EscapeSqlString(key)}' VALUE {expr}";
        }

        private static string OracleNestedArg(NestedList n)
        {
            string key = n.ResultAlias.Trim();
            return $"'{EscapeSqlString(key)}' VALUE {n.ToSql(DbType.ORACLE)} FORMAT JSON";
        }

        private static string BuildOracleOrderInsideAgg(SqlSelect childSelect)
        {
            if (childSelect.Sorts == null || childSelect.Sorts.Count == 0)
                return "";
            var orderParts = childSelect.Sorts.Select(s =>
            {
                string col = QuoteCorrelateIdent(s.Name.Trim(), DbType.ORACLE);
                return s.IsAscending ? col : col + " DESC";
            });
            return " ORDER BY " + string.Join(", ", orderParts);
        }

        private static string CompileOracleFromWhere(SqlSelect childSelect)
        {
            SqlSelect shell = new SqlSelect
            {
                Table = childSelect.Table,
                FromDerivedTable = childSelect.FromDerivedTable,
                CommonTableExpressions = childSelect.CommonTableExpressions,
                Joins = childSelect.Joins,
                Where = childSelect.Where,
                GroupBys = childSelect.GroupBys,
                Having = childSelect.Having,
                SqlCombines = childSelect.SqlCombines,
                Fields = new List<Field> { new Field("1", "_x", isRaw: true) }
            };
            string shellSql = CompileChildSelect(DbType.ORACLE, shell);
            int fromIdx = shellSql.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
            if (fromIdx < 0)
                throw new InvalidOperationException("Unable to compile Oracle NestedList FROM clause.");
            return shellSql.Substring(fromIdx);
        }

        internal static string JsonKeyFor(Field field)
        {
            if (field.Value != null && !string.IsNullOrWhiteSpace(field.Value.ToString()))
                return field.Value.ToString().Trim();
            string name = field.Name.Trim();
            int dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(dot + 1) : name;
        }

        private static string EscapeSqliteString(string s) => EscapeSqlString(s);

        private static string EscapeSqlString(string s) => s.Replace("'", "''");

        private static string QuoteSqliteIdent(string ident)
        {
            if (ident.All(c => char.IsLetterOrDigit(c) || c == '_'))
                return ident;
            return "\"" + ident.Replace("\"", "\"\"") + "\"";
        }

        private static string QuoteBacktickIdent(string ident)
        {
            if (ident.All(c => char.IsLetterOrDigit(c) || c == '_'))
                return ident;
            return "`" + ident.Replace("`", "``") + "`";
        }

        private static string QuoteDoubleIdent(string ident)
        {
            if (ident.All(c => char.IsLetterOrDigit(c) || c == '_'))
                return "\"" + ident + "\"";
            return "\"" + ident.Replace("\"", "\"\"") + "\"";
        }
    }

    /// <summary>
    /// Typed constructor sugar for <see cref="NestedList"/> — same pattern as constructing other facade objects.
    /// </summary>
    public class NestedList<TElement> : NestedList
    {
        public NestedList() { }

        public NestedList(string resultAlias, SqlSelect select, Expression correlate)
            : base(resultAlias, select, correlate, typeof(TElement))
        {
        }

        public NestedList(string resultAlias, SqlSelect select, Filter correlate)
            : base(resultAlias, select, correlate, typeof(TElement))
        {
        }
    }
}
