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
    /// SQL Server (<c>FOR JSON PATH</c>).
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

        /// <param name="correlate">Join-style ON expression (both sides columns); wrapped in a <see cref="Filter"/>.</param>
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
            SqlSelect effective = SelectForCompile();
            string innerSql = CompileChildSelect(dbType, effective);
            return Wrap(dbType, innerSql, effective);
        }

        /// <summary>
        /// Child select with correlation filter merged into <see cref="SqlSelect.Where"/>
        /// (does not mutate the stored <see cref="Select"/>).
        /// </summary>
        internal SqlSelect SelectForCompile()
        {
            Validate();

            Filter where = new Filter();
            if (Select.Where != null)
                where.WithFilter(Select.Where);
            where.WithFilter(ToCorrelateFilter(Correlate));

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

        internal static string Wrap(DbType dbType, string innerSql, SqlSelect childSelect)
        {
            if (string.IsNullOrWhiteSpace(innerSql))
                throw new ArgumentException("Compiled child SQL is required.", nameof(innerSql));
            return dbType switch
            {
                DbType.POSTGRES => WrapPostgres(innerSql),
                DbType.SQLITE => WrapSqlite(innerSql, childSelect),
                DbType.SQLSERVER => WrapSqlServer(innerSql),
                _ => throw new ArgumentException($"Unsupported DbType for NestedList: {dbType}")
            };
        }

        internal static Filter ToCorrelateFilter(Filter correlate)
        {
            if (correlate == null)
                throw new ArgumentNullException(nameof(correlate));

            Filter result = new Filter(correlate.LogicalRelation ?? LogicalRelation.And);
            if (correlate.Expressions != null)
            {
                foreach (Expression expression in correlate.Expressions)
                {
                    if (expression != null)
                        result.WithExpression(ToCorrelateWhere(expression));
                }
            }
            if (correlate.Filters != null)
            {
                foreach (Filter nested in correlate.Filters)
                {
                    if (nested != null)
                        result.WithFilter(ToCorrelateFilter(nested));
                }
            }
            return result;
        }

        internal static Expression ToCorrelateWhere(Expression correlate)
        {
            if (correlate == null)
                throw new ArgumentNullException(nameof(correlate));
            if (correlate.IsRaw)
                return correlate;

            if (string.IsNullOrWhiteSpace(correlate.Name))
                throw new InvalidOperationException("Correlate Expression.Name (left column) is required.");
            if (correlate.Value == null || string.IsNullOrWhiteSpace(correlate.Value.ToString()))
                throw new InvalidOperationException(
                    "Correlate Expression.Value must be the right-hand column (Join.OnExpression semantics).");

            Relation relation = correlate.Relation ?? Relation.EqualTo;
            if (Object.Equals(relation, Relation.In)
                || Object.Equals(relation, Relation.Exists)
                || Object.Equals(relation, Relation.NullValue)
                || Object.Equals(relation, Relation.TrueValue)
                || Object.Equals(relation, Relation.StartsWith)
                || Object.Equals(relation, Relation.EndsWith)
                || Object.Equals(relation, Relation.Contains))
            {
                throw new InvalidOperationException(
                    $"Correlate does not support Relation.{relation.Value}; use comparison operators (=, <, >, …) or a raw Expression.");
            }

            string right = correlate.Value.ToString().Trim();
            string raw = $"{correlate.Name.Trim()} {relation} {right}";
            return new Expression(raw, Array.Empty<object>())
                .WithIsRaw()
                .WithLogicalRelation(correlate.LogicalRelation ?? LogicalRelation.And);
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
            if (string.IsNullOrWhiteSpace(ResultAlias))
                throw new InvalidOperationException("NestedList.ResultAlias is required.");
            if (ElementType == null)
                throw new InvalidOperationException("NestedList.ElementType is required.");
            if (Select == null)
                throw new InvalidOperationException("NestedList.Select (child SqlSelect) is required.");
            if (!HasCorrelate(Correlate))
                throw new InvalidOperationException("NestedList.Correlate is required.");
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
                _ => throw new ArgumentException($"Unsupported DbType: {dbType}")
            };
            ISqlFacade facade = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(dbType, connectionString));
            return facade.GetSql(child, isParameterized: false);
        }

        private static string WrapPostgres(string innerSql) =>
            "(SELECT COALESCE(json_agg(row_to_json(_j))::text, json_build_array()::text) FROM ("
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
            "(SELECT COALESCE(("
            + innerSql
            + " FOR JSON PATH, INCLUDE_NULL_VALUES), CHAR(91)+CHAR(93)))";

        internal static string JsonKeyFor(Field field)
        {
            if (field.Value != null && !string.IsNullOrWhiteSpace(field.Value.ToString()))
                return field.Value.ToString().Trim();
            string name = field.Name.Trim();
            int dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(dot + 1) : name;
        }

        private static string EscapeSqliteString(string s) => s.Replace("'", "''");

        private static string QuoteSqliteIdent(string ident)
        {
            if (ident.All(c => char.IsLetterOrDigit(c) || c == '_'))
                return ident;
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
