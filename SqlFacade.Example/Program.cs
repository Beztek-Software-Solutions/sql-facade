// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using System.Linq;
    using Beztek.Facade.Sql;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// End-to-end tour of <c>Beztek.Facade.Sql</c>.
    /// <para>
    /// Run with SQLite in-memory so every major API is exercised against a real connection —
    /// the same pattern apps use for integration-style unit tests (pair with an app dialect helper
    /// when expression-level SQL differs between SQLite and the deployed engine).
    /// </para>
    /// <para>
    /// Each section prints the query JSON, parameterized SQL template, raw SQL, and a JSON
    /// round-trip via <see cref="ISqlFacade.DeserializeFromJson"/> (skipped when raw CTEs cannot deserialize).
    /// </para>
    /// </summary>
    static class Program
    {
        private static ISqlFacade sqlFacade;

        static void Main(string[] args)
        {
            // -------------------------------------------------------------------------
            // Bootstrap: dialect helper + facade config
            // Mirror UseSqlite with SqlFacadeConfig.DbType so generators and the facade agree.
            // Production would set UseSqlite=false (or add SQL Server branches) from the same DbType.
            // -------------------------------------------------------------------------
            ExampleSqlDialect.UseSqlite = true;

            var config = new SqlFacadeConfig(Sql.DbType.SQLITE, "Data Source=:memory:")
            {
                // Default is ReadCommitted; shown explicitly for documentation.
                TransactionIsolationLevel = System.Transactions.IsolationLevel.ReadCommitted
            };
            sqlFacade = SqlFacadeFactory.GetSqlFacade(config);

            Console.WriteLine($"Config: DbType={config.DbType}, Isolation={config.TransactionIsolationLevel}");
            Console.WriteLine($"ExampleSqlDialect.UseSqlite={ExampleSqlDialect.UseSqlite}, Now={ExampleSqlDialect.Now}");
            // Same API for file-backed SQLite: new SqlFacadeConfig(DbType.SQLITE, "Data Source=/tmp/app.db")
            Console.WriteLine("File-based SQLite uses the same API with e.g. Data Source=/tmp/app.db");
            Console.WriteLine();

            CreateDB();
            CleanDB();

            // Feature tour (order matters where later sections assume earlier seed data).
            RunBasicCrud();
            RunFiltersAndRelations();
            RunDerivedCteAndJoins();
            RunSetOperations();
            RunGroupByHaving();
            RunMetadataJoinAndDelete();
            RunPagination();
            RunWriteWithCte();
            RunNestedListExample();
            RunMultiDialectCompilationSamples();
            RunLessCommonSituations();
        }

        /// <summary>
        /// Insert / batch insert / insert…select / update, plus GetSingleResult and GetTotalNumResults.
        /// </summary>
        private static void RunBasicCrud()
        {
            // --- Batch insert (ExecuteMultiSqlWrite runs all statements in one transaction) ---
            List<ISqlWrite> sqlInsertList = new List<ISqlWrite>();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "123"))
                .WithField(new Field("color", "green"))
                // BooleanValue maps true → 1 on SQLite, true on Postgres.
                .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(true)));
            Console.WriteLine($"CanvasSql: {sqlInsert.ToString()}");
            sqlInsertList.Add(sqlInsert);

            sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "another-uuid"))
                .WithField(new Field("color", "red"))
                .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(true)));
            Console.WriteLine($"CanvasSql: {sqlInsert.ToString()}");
            sqlInsertList.Add(sqlInsert);

            sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "greencanvas"))
                .WithField(new Field("color", "green"))
                .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(true)));
            Console.WriteLine($"CanvasSql: {sqlInsert.ToString()}");
            sqlInsertList.Add(sqlInsert);

            IList<int> results = sqlFacade.ExecuteMultiSqlWrite(sqlInsertList);
            foreach (Object result in results)
            {
                Console.WriteLine(result + " row(s) inserted as part of batch");
            }
            Console.WriteLine();

            // --- Insert … SELECT (clone a row with raw literal fields) ---
            // isRaw: true means Name is emitted as SQL (not a bound parameter / column name only).
            string label = "Insert with Select";
            sqlInsert = new SqlInsert("canvas");
            SqlSelect sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("\'cloned-uuid\'", "id", true))
                .WithField(new Field("\'red\'", "color", true))
                .WithField(new Field("1", "is_active", true))
                .WithWhere(new Filter().WithExpression(new Expression("id", "another-uuid")));
            sqlInsert.WithQuery(sqlSelect);
            log(label, sqlInsert);
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            log(label, sqlInsert, $"{rowsChanged} row(s) inserted");

            // --- Update with filter ---
            label = "Update";
            SqlUpdate update = new SqlUpdate("canvas")
                .WithField(new Field("color", "yellow"))
                .WithFilter(new Expression("color", "red"));
            rowsChanged = sqlFacade.ExecuteSqlWrite(update);
            log(label, update, $"{rowsChanged} row(s) updated");

            // --- GetSingleResult: exactly one row, or default; throws if more than one ---
            label = "GetSingleResult";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("id", "123")));
            Canvas single = sqlFacade.GetSingleResult<Canvas>(sqlSelect);
            log(label, sqlSelect, $"          Single: {single}");

            // --- GetTotalNumResults: count matching rows (ignores ORDER BY / page size) ---
            label = "GetTotalNumResults";
            sqlSelect = new SqlSelect("canvas").WithField(new Field("id"));
            int total = sqlFacade.GetTotalNumResults(sqlSelect);
            log(label, sqlSelect, $"          Total rows: {total}");
        }

        /// <summary>
        /// Filters: AndNot / Or, nested Filter groups, IN, subquery IN/EXISTS,
        /// string relations, NULL, raw TrueValue, and dialect Now via WithRawExpression.
        /// </summary>
        private static void RunFiltersAndRelations()
        {
            // LogicalRelation.AndNot: first expression AND NOT (second).
            string label = "Query with AndNot";
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"));
            Filter filter = new Filter()
                .WithExpression(new Expression("v.id", "another-uuid"))
                .WithExpression(new Expression("v.color", "green")
                    .WithRelation(Relation.EqualTo)
                    .WithLogicalRelation(LogicalRelation.AndNot));
            sqlSelect.WithWhere(filter);
            PrintCanvases(label, sqlSelect);

            // LogicalRelation.Or between two expressions in one Filter.
            label = "Query with Or";
            sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"));
            filter = new Filter()
                .WithExpression(new Expression("v.id", "another-uuid"))
                .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or));
            sqlSelect.WithWhere(filter);
            PrintCanvases(label, sqlSelect);

            // Nested Filter: WithFilter(...) adds parentheses / nested AND-OR groups.
            label = "Nested WHERE filters";
            Filter orBranch = new Filter()
                .WithExpression(new Expression("v.id", "another-uuid"))
                .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or));
            sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithWhere(new Filter()
                    .WithFilter(orBranch)
                    .WithExpression(new Expression("v.id", "another-uuid")));
            PrintCanvases(label, sqlSelect);

            // Relation.In with a CLR array (lists work the same way).
            label = "Relation.In (array)";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new[] { "123", "greencanvas" }).WithRelation(Relation.In)))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // id IN (SELECT …) via Expression.WithSqlIn.
            label = "Subquery WithSqlIn";
            SqlSelect subQuery = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "green")));
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression().WithSqlIn("id", subQuery)))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // WHERE EXISTS (SELECT …). If the probe returns any row, all outer rows match.
            label = "WithSqlExists";
            SqlSelect existsProbe = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "yellow")));
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression().WithSqlExists(existsProbe)))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // String pattern relations (compiled to LIKE under the hood).
            label = "StartsWith / EndsWith / Contains";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("id", "green").WithRelation(Relation.StartsWith))
                    .WithExpression(new Expression("id", "canvas").WithRelation(Relation.EndsWith)
                        .WithLogicalRelation(LogicalRelation.Or))
                    .WithExpression(new Expression("color", "ell").WithRelation(Relation.Contains)
                        .WithLogicalRelation(LogicalRelation.Or)))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // IS NULL plus a raw always-true predicate (WithIsRaw requires Value as object[]).
            label = "NullValue and raw TrueValue";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("notes", null).WithRelation(Relation.NullValue))
                    .WithExpression(new Expression("1=1", Array.Empty<object>())
                        .WithIsRaw()
                        .WithRelation(Relation.TrueValue)));
            PrintCanvases(label, sqlSelect);

            // Inject dialect-specific timestamp SQL without hard-coding engine syntax in the generator.
            label = "Filter.WithRawExpression + dialect Now";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithWhere(new Filter().WithRawExpression($"{ExampleSqlDialect.Now} IS NOT NULL"));
            PrintCanvases(label, sqlSelect);
        }

        /// <summary>
        /// Derived tables, raw + SqlSelect CTEs, and InnerJoin.
        /// </summary>
        private static void RunDerivedCteAndJoins()
        {
            // FROM (SELECT …) AS v — subquery as table source.
            string label = "Derived tables";
            SqlSelect subSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithField(new Field("\'Pseudo data from derived table\'", "ExtraData", true));
            SqlSelect sqlSelect = new SqlSelect(new DerivedTable(subSelect, "v"));
            log(label, sqlSelect);
            foreach (CanvasExtended row in sqlFacade.GetResults<CanvasExtended>(sqlSelect))
            {
                Console.WriteLine($"          Derived table result: {row}");
            }
            Console.WriteLine();

            // WITH palette AS (<raw SQL>) … JOIN palette ON …
            // Note: raw CTE SQL may not round-trip through DeserializeFromJson (log() handles that).
            label = "Common table expressions (raw SQL CTE)";
            CommonTableExpression paletteCte = new CommonTableExpression("select 'yellow' as match_color", "palette");
            sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithCommonTableExpression(paletteCte)
                .WithJoin(new Join(paletteCte, new Expression("palette.match_color", "v.color")));
            PrintCanvases(label, sqlSelect);

            // WITH greens AS (<SqlSelect>) — prefer this form when you want JSON round-trip of the CTE body.
            label = "Common table expressions (SqlSelect CTE)";
            CommonTableExpression selectCte = new CommonTableExpression(
                new SqlSelect(new Table("canvas"))
                    .WithField(new Field("id"))
                    .WithField(new Field("color"))
                    .WithWhere(new Filter().WithExpression(new Expression("color", "green"))),
                "greens");
            sqlSelect = new SqlSelect("greens")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithCommonTableExpression(selectCte)
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // Seed metadata for join demos (table name is historically misspelled "metdata").
            sqlFacade.ExecuteSqlWrite(new SqlInsert("canvas-metdata")
                .WithField(new Field("id", "another-uuid"))
                .WithField(new Field("extra_data", "Some extra data")));

            // InnerJoin keeps only matching parent+child rows.
            label = "InnerJoin";
            sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithField(new Field("m.extra_data", "ExtraData"))
                .WithJoin(new Join(new Table("canvas-metdata", "m"), new Expression("m.id", "v.id"), JoinType.InnerJoin));
            log(label, sqlSelect);
            foreach (CanvasExtended row in sqlFacade.GetResults<CanvasExtended>(sqlSelect))
            {
                Console.WriteLine($"          InnerJoin result: {row}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// UNION, UNION ALL (with ORDER BY wrap), EXCEPT, INTERSECT.
        /// </summary>
        private static void RunSetOperations()
        {
            SqlSelect greenSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "green")));
            SqlSelect oneGreen = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("id", "greencanvas")));

            string label = "Set operations (UNION)";
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "yellow")))
                .WithCombine(new SqlCombine(greenSelect, SqlRelation.Union))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // When combines + sorts are both set, the facade wraps the union in a derived table
            // so ORDER BY applies to the combined result (SQLKata otherwise sorts only the first branch).
            label = "Set operations (UNION ALL + ORDER BY wrap)";
            sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "yellow")))
                .WithCombine(new SqlCombine(greenSelect, SqlRelation.UnionAll))
                .WithSort(new Sort("id", false));
            PrintCanvases(label, sqlSelect);

            label = "Set operations (EXCEPT)";
            sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "green")))
                .WithCombine(new SqlCombine(oneGreen, SqlRelation.Except));
            PrintCanvases(label, sqlSelect);

            label = "Set operations (INTERSECT)";
            sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "green")))
                .WithCombine(new SqlCombine(oneGreen, SqlRelation.Intersect));
            PrintCanvases(label, sqlSelect);
        }

        /// <summary>GROUP BY + HAVING (raw aggregate predicate).</summary>
        private static void RunGroupByHaving()
        {
            string label = "Group by and having";
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("color"))
                .WithField(new Field("count(*)", "NumCanvases", true))
                .WithGroupBy(new GroupBy("color"))
                .WithHaving(new Filter().WithExpression(new Expression("count(*) >= 1", null).WithIsRaw()));
            log(label, sqlSelect);
            Console.WriteLine($"          {sqlFacade.GetResults<object>(sqlSelect).Count} color group(s)");
            Console.WriteLine();
        }

        /// <summary>
        /// LeftJoin with an extra ON predicate, then Delete and a plain select of remaining rows.
        /// </summary>
        private static void RunMetadataJoinAndDelete()
        {
            // LeftJoin + WithJoinExpression appends AND m.extra_data = '…' on the join (not the WHERE).
            string label = "LeftJoin with join expression";
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithField(new Field("m.extra_data", "ExtraData"));
            Join complexJoin = new Join(new Table("canvas-metdata", "m"), new Expression("m.id", "v.id"), JoinType.LeftJoin)
                .WithJoinExpression(new Expression("m.extra_data", "Some extra data"));
            sqlSelect.WithJoin(complexJoin);
            log(label, sqlSelect);
            foreach (CanvasExtended row in sqlFacade.GetResults<CanvasExtended>(sqlSelect))
            {
                Console.WriteLine($"          Result: {row}");
            }
            Console.WriteLine();

            label = "Delete green rows";
            SqlDelete sqlDelete = new SqlDelete("canvas")
                .WithFilter(new Expression("color", "green"));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);
            log(label, sqlDelete, $"{rowsChanged} row(s) deleted");

            label = "Get all canvas";
            sqlSelect = new SqlSelect("canvas").WithField(new Field("id")).WithField(new Field("color"));
            PrintCanvases(label, sqlSelect);
        }

        /// <summary>
        /// PagedResultsWithTotal vs PagedResults (no total count), plus multi-column Sort.
        /// </summary>
        private static void RunPagination()
        {
            CleanDB();
            BatchWrite(10);

            string label = "Pagination with totals";
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("v.id", "uuid-211").WithRelation(Relation.GreaterThanOrEqualTo))
                    .WithExpression(new Expression("v.id", "uuid-910").WithRelation(Relation.LessThan)))
                .WithSort(new Sort("v.id"))
                .WithSort(new Sort("v.color", false)); // descending secondary sort
            int pageNum = 2;
            int pageSize = 3;
            // retrieveTotalNumResults: true → PagedResultsWithTotal (TotalResults / TotalPages).
            PagedResultsWithTotal<Canvas> pagedWithTotal =
                (PagedResultsWithTotal<Canvas>)sqlFacade.GetPagedResults<Canvas>(sqlSelect, pageNum, pageSize, true);
            log(label, sqlSelect,
                $"{pagedWithTotal.PagedList.Count} result(s) (of {pagedWithTotal.TotalResults}) retrieved from page {pageNum} (of {pagedWithTotal.TotalPages})");
            foreach (Canvas canvas in pagedWithTotal.PagedList)
            {
                Console.WriteLine($"          Result: {canvas}");
            }
            Console.WriteLine();

            // retrieveTotalNumResults: false → lighter PagedResults (no COUNT query).
            label = "Pagination without totals";
            PagedResults<Canvas> paged = sqlFacade.GetPagedResults<Canvas>(sqlSelect, 1, 4, false);
            log(label, sqlSelect, $"{paged.PagedList.Count} result(s) on page {paged.PageNum} (no total requested)");
            foreach (Canvas canvas in paged.PagedList)
            {
                Console.WriteLine($"          Result: {canvas}");
            }
            Console.WriteLine();
        }

        /// <summary>SqlInsert with a CTE source (WITH … INSERT … SELECT).</summary>
        private static void RunWriteWithCte()
        {
            CleanDB();
            string label = "Insert with CTE";
            var seed = new CommonTableExpression(
                "select 'cte-1' as id, 'purple' as color, 1 as is_active",
                "seed_rows");
            var insert = new SqlInsert("canvas")
                .WithCommonTableExpression(seed)
                .WithQuery(new SqlSelect("seed_rows")
                    .WithField(new Field("id"))
                    .WithField(new Field("color"))
                    .WithField(new Field("is_active")));
            log(label, insert);
            int rows = sqlFacade.ExecuteSqlWrite(insert);
            log(label, insert, $"{rows} row(s) inserted from CTE");
            PrintCanvases("After CTE insert", new SqlSelect("canvas").WithField(new Field("id")).WithField(new Field("color")));
        }

        /// <summary>
        /// NestedList: Expression correlate, Filter correlate (executed), T[] mapping, and grandchild lists.
        /// ResultAlias must match the DTO property name (Strokes / Tags).
        /// </summary>
        private static void RunNestedListExample()
        {
            string label = "NestedList typed child list (Expression correlate)";
            CleanDB();

            // canvas 1:N canvas_stroke 1:N stroke_tag
            sqlFacade.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("canvas").WithField(new Field("id", "c-green")).WithField(new Field("color", "green"))
                    .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(true))),
                new SqlInsert("canvas").WithField(new Field("id", "c-blue")).WithField(new Field("color", "blue"))
                    .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(true))),
                new SqlInsert("canvas_stroke").WithField(new Field("id", "s2")).WithField(new Field("canvas_id", "c-green"))
                    .WithField(new Field("label", "second")).WithField(new Field("sort_ord", 2)),
                new SqlInsert("canvas_stroke").WithField(new Field("id", "s1")).WithField(new Field("canvas_id", "c-green"))
                    .WithField(new Field("label", "first")).WithField(new Field("sort_ord", 1)),
                new SqlInsert("stroke_tag").WithField(new Field("id", "t1")).WithField(new Field("stroke_id", "s1"))
                    .WithField(new Field("tag", "alpha")),
                new SqlInsert("stroke_tag").WithField(new Field("id", "t2")).WithField(new Field("stroke_id", "s1"))
                    .WithField(new Field("tag", "beta")),
            });

            // Join-style correlate: both sides are columns (s.canvas_id = c.id).
            // Child Field aliases become JSON property names (id, label, sortOrd).
            NestedList strokes = new NestedList<StrokeDto>("Strokes",
                new SqlSelect(new Table("canvas_stroke", "s"))
                    .WithField(new Field("s.id", "id"))
                    .WithField(new Field("s.label", "label"))
                    .WithField(new Field("s.sort_ord", "sortOrd"))
                    .WithSort(new Sort("s.sort_ord")),
                new Expression("s.canvas_id", "c.id"));

            SqlSelect sqlSelect = new SqlSelect(new Table("canvas", "c"))
                .WithField(new Field("c.id", "Id"))
                .WithField(new Field("c.color", "Color"))
                .WithNestedList(strokes)
                .WithSort(new Sort("c.id"));

            log(label, sqlSelect);
            // ToSql compiles the aggregate without needing a live Postgres/SQL Server connection.
            Console.WriteLine("      Dialect SQL samples (NestedList.ToSql):");
            Console.WriteLine($"        Postgres:   {strokes.ToSql(Sql.DbType.POSTGRES)}");
            Console.WriteLine($"        SQLite:     {strokes.ToSql(Sql.DbType.SQLITE)}");
            Console.WriteLine($"        SQL Server: {strokes.ToSql(Sql.DbType.SQLSERVER)}");
            Console.WriteLine();

            foreach (CanvasWithStrokes row in sqlFacade.GetResults<CanvasWithStrokes>(sqlSelect))
            {
                Console.WriteLine($"          Canvas {row.Id} ({row.Color}): {row.Strokes?.Count ?? 0} stroke(s)");
                foreach (StrokeDto stroke in row.Strokes ?? [])
                {
                    Console.WriteLine($"            stroke id={stroke.Id} label={stroke.Label} sortOrd={stroke.SortOrd}");
                }
            }
            Console.WriteLine();

            // Same NestedList using a Filter correlate (supports And/Or nesting for complex ON logic).
            label = "NestedList Filter correlate (executed)";
            NestedList filterForm = new NestedList<StrokeDto>("Strokes",
                new SqlSelect(new Table("canvas_stroke", "s"))
                    .WithField(new Field("s.id", "id"))
                    .WithField(new Field("s.label", "label"))
                    .WithField(new Field("s.sort_ord", "sortOrd"))
                    .WithSort(new Sort("s.sort_ord")),
                new Filter().WithExpression(new Expression("s.canvas_id", "c.id")));
            sqlSelect = new SqlSelect(new Table("canvas", "c"))
                .WithField(new Field("c.id", "Id"))
                .WithField(new Field("c.color", "Color"))
                .WithNestedList(filterForm)
                .WithWhere(new Filter().WithExpression(new Expression("c.id", "c-green")));
            log(label, sqlSelect);
            CanvasWithStrokes green = sqlFacade.GetSingleResult<CanvasWithStrokes>(sqlSelect);
            Console.WriteLine($"          Canvas {green.Id}: {green.Strokes?.Count ?? 0} stroke(s) via Filter correlate");
            Console.WriteLine();

            // NestedListMapper coerces List&lt;T&gt; onto T[] when the DTO property is an array.
            label = "NestedList onto T[]";
            sqlSelect = new SqlSelect(new Table("canvas", "c"))
                .WithField(new Field("c.id", "Id"))
                .WithField(new Field("c.color", "Color"))
                .WithNestedList(new NestedList<StrokeDto>("Strokes",
                    new SqlSelect(new Table("canvas_stroke", "s"))
                        .WithField(new Field("s.id", "id"))
                        .WithField(new Field("s.label", "label"))
                        .WithField(new Field("s.sort_ord", "sortOrd")),
                    new Expression("s.canvas_id", "c.id")))
                .WithWhere(new Filter().WithExpression(new Expression("c.id", "c-green")));
            CanvasWithStrokeArray arrayRow = sqlFacade.GetSingleResult<CanvasWithStrokeArray>(sqlSelect);
            log(label, sqlSelect, $"          Strokes array length: {arrayRow.Strokes?.Length ?? 0}");

            // Grandchild: stroke NestedList embeds another NestedList for tags.
            label = "NestedList grandchild (stroke tags)";
            NestedList taggedStrokes = new NestedList<StrokeWithTagsDto>("Strokes",
                new SqlSelect(new Table("canvas_stroke", "s"))
                    .WithField(new Field("s.id", "Id"))
                    .WithField(new Field("s.label", "Label"))
                    .WithField(new Field("s.sort_ord", "SortOrd"))
                    .WithNestedList(new NestedList<StrokeTagDto>("Tags",
                        new SqlSelect(new Table("stroke_tag", "tg"))
                            .WithField(new Field("tg.id", "Id"))
                            .WithField(new Field("tg.tag", "Tag")),
                        new Expression("tg.stroke_id", "s.id")))
                    .WithSort(new Sort("s.sort_ord")),
                new Expression("s.canvas_id", "c.id"));
            sqlSelect = new SqlSelect(new Table("canvas", "c"))
                .WithField(new Field("c.id", "Id"))
                .WithField(new Field("c.color", "Color"))
                .WithNestedList(taggedStrokes)
                .WithWhere(new Filter().WithExpression(new Expression("c.id", "c-green")));
            log(label, sqlSelect);
            CanvasWithTaggedStrokes tagged = sqlFacade.GetSingleResult<CanvasWithTaggedStrokes>(sqlSelect);
            foreach (StrokeWithTagsDto stroke in tagged.Strokes ?? [])
            {
                string tags = stroke.Tags == null ? "" : string.Join(",", stroke.Tags.Select(t => t.Tag));
                Console.WriteLine($"          stroke {stroke.Id} tags=[{tags}]");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Compile the same SqlSelect for SQLite / Postgres / SQL Server via GetSql (no live remote DB).
        /// Also shows flipping the app dialect helper between branches.
        /// </summary>
        private static void RunMultiDialectCompilationSamples()
        {
            Console.WriteLine("Multi-dialect GetSql samples (compile only; no live Postgres/SQL Server connection):");
            var select = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id", "Id"))
                .WithField(new Field("v.color", "Color"))
                .WithWhere(new Filter().WithExpression(new Expression("v.color", "green")))
                .WithSort(new Sort("v.id"));

            // Factory caches by config; connection strings here are only for compiler selection.
            ISqlFacade postgres = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(Sql.DbType.POSTGRES, "Host=localhost;Database=x;Username=x;Password=x"));
            ISqlFacade sqlServer = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(Sql.DbType.SQLSERVER, "Server=localhost;Database=x;Trusted_Connection=True;"));

            Console.WriteLine($"      SQLite:     {sqlFacade.GetSql(select, false)}");
            Console.WriteLine($"      Postgres:   {postgres.GetSql(select, false)}");
            Console.WriteLine($"      SQL Server: {sqlServer.GetSql(select, false)}");
            Console.WriteLine();

            // Demonstrate helper branch output without changing the runtime SQLite facade.
            bool previous = ExampleSqlDialect.UseSqlite;
            ExampleSqlDialect.UseSqlite = false;
            Console.WriteLine($"Dialect helper (Postgres branch): Now={ExampleSqlDialect.Now}, Bool(true)={ExampleSqlDialect.BooleanValue(true)}, CastToText(id)={ExampleSqlDialect.CastToText("id")}");
            ExampleSqlDialect.UseSqlite = previous;
            Console.WriteLine($"Dialect helper (SQLite branch):   Now={ExampleSqlDialect.Now}, Bool(true)={ExampleSqlDialect.BooleanValue(true)}, CastToText(id)={ExampleSqlDialect.CastToText("id")}");
            Console.WriteLine();
        }

        /// <summary>
        /// Less common / edge situations that complete the feature tour:
        /// CTE on update/delete, Relation.In with List&lt;T&gt;, nested CTEs, remaining Relation operators,
        /// and a short-lived file-based SQLite facade (same API as :memory:).
        /// Postgres/SQL Server live execution is intentionally omitted — use GetSql / NestedList.ToSql
        /// (above) for dialect review without cloud credentials; point SqlFacadeConfig at a real
        /// connection string when you need a live runtime against those engines.
        /// </summary>
        private static void RunLessCommonSituations()
        {
            Console.WriteLine("=== Less common situations ===");
            Console.WriteLine();

            CleanDB();
            sqlFacade.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("canvas").WithField(new Field("id", "a")).WithField(new Field("color", "red"))
                    .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(true))),
                new SqlInsert("canvas").WithField(new Field("id", "b")).WithField(new Field("color", "green"))
                    .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(true))),
                new SqlInsert("canvas").WithField(new Field("id", "c")).WithField(new Field("color", "blue"))
                    .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(true))),
                new SqlInsert("canvas").WithField(new Field("id", "d")).WithField(new Field("color", "yellow"))
                    .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(false))),
            });

            // --- Relation.In with List<T> (arrays were shown earlier; lists bind the same way) ---
            string label = "Relation.In (List<T>)";
            SqlSelect sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new List<string> { "a", "c" }).WithRelation(Relation.In)))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // --- Remaining comparison / negation Relation operators (EqualTo, In, StartsWith, etc. already covered) ---
            label = "Relation.GreaterThan / LessThanOrEqualTo";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("id", "a").WithRelation(Relation.GreaterThan))
                    .WithExpression(new Expression("id", "d").WithRelation(Relation.LessThanOrEqualTo)))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            label = "Relation.GreaterThanOrEqualTo / LessThan";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("id", "b").WithRelation(Relation.GreaterThanOrEqualTo))
                    .WithExpression(new Expression("id", "d").WithRelation(Relation.LessThan)))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // NOT IN via Relation.In + LogicalRelation.AndNot (there is no separate NotIn enum value).
            label = "Not In (In + AndNot)";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new[] { "a", "b" })
                        .WithRelation(Relation.In)
                        .WithLogicalRelation(LogicalRelation.AndNot)))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // OrNot: first predicate OR NOT (second).
            label = "LogicalRelation.OrNot";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("color", "red"))
                    .WithExpression(new Expression("color", "green")
                        .WithLogicalRelation(LogicalRelation.OrNot)))
                .WithSort(new Sort("id"));
            PrintCanvases(label, sqlSelect);

            // --- Nested CTEs: multiple WITH clauses, then wrap the select as another CTE ---
            label = "Nested common table expressions";
            CommonTableExpression cte1 = new CommonTableExpression(
                "select 'a' as id, 'c1v1' as v1 union all select 'b' as id, 'c1v2' as v1", "c1");
            CommonTableExpression cte2 = new CommonTableExpression(
                "select 'a' as id, 'c2v1' as v2 union all select 'b' as id, 'c2v2' as v2", "c2");
            sqlSelect = new SqlSelect(new Table("canvas"))
                .WithCommonTableExpression(cte1)
                .WithCommonTableExpression(cte2)
                .WithField(new Field("canvas.id", "id"))
                .WithField(new Field("c1.v1", "v1"))
                .WithField(new Field("c2.v2", "v2"))
                .WithJoin(new Join(cte1, new Expression("c1.id", "canvas.id")))
                .WithJoin(new Join(cte2, new Expression("c2.id", "canvas.id")))
                .WithSort(new Sort("canvas.id"));
            log(label, sqlSelect);
            foreach (object row in sqlFacade.GetResults<object>(sqlSelect))
            {
                Console.WriteLine($"          Nested CTE row: {row}");
            }
            // Wrapping a select that already has CTEs bubbles those CTEs up to the outer select.
            SqlSelect nestedWrap = new SqlSelect(new CommonTableExpression(sqlSelect, "agg"))
                .WithField(new Field("count(*)", "num", true));
            Console.WriteLine($"          Bubbled CTE count on wrap: {nestedWrap.CommonTableExpressions?.Count ?? 0}");
            int nestedCount = sqlFacade.GetSingleResult<int>(nestedWrap);
            log("Nested CTE wrap (count)", nestedWrap, $"          count={nestedCount}");

            // --- Update / Delete with CTE (WITH … UPDATE / WITH … DELETE) ---
            label = "Update with CTE";
            var updateTargets = new CommonTableExpression("select 'a' as id union all select 'c' as id", "targets");
            SqlUpdate update = new SqlUpdate("canvas")
                .WithCommonTableExpression(updateTargets)
                .WithField(new Field("color", "teal"))
                .WithFilter(new Expression().WithSqlIn("id",
                    new SqlSelect("targets").WithField(new Field("id"))));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(update);
            log(label, update, $"{rowsChanged} row(s) updated via CTE targets");
            PrintCanvases("After CTE update", new SqlSelect("canvas")
                .WithField(new Field("id")).WithField(new Field("color")).WithSort(new Sort("id")));

            label = "Delete with CTE";
            var deleteTargets = new CommonTableExpression("select 'd' as id", "doomed");
            SqlDelete delete = new SqlDelete("canvas")
                .WithCommonTableExpression(deleteTargets)
                .WithFilter(new Expression().WithSqlIn("id",
                    new SqlSelect("doomed").WithField(new Field("id"))));
            rowsChanged = sqlFacade.ExecuteSqlWrite(delete);
            log(label, delete, $"{rowsChanged} row(s) deleted via CTE targets");
            PrintCanvases("After CTE delete", new SqlSelect("canvas")
                .WithField(new Field("id")).WithField(new Field("color")).WithSort(new Sort("id")));

            // --- File-based SQLite: same ISqlFacade API as :memory:, separate connection string ---
            RunFileBasedSqliteDemo();
        }

        /// <summary>
        /// Opens a short-lived file SQLite DB, creates a table, writes/reads via the facade, then deletes the file.
        /// Demonstrates that file vs in-memory is only a connection-string choice.
        /// </summary>
        private static void RunFileBasedSqliteDemo()
        {
            string path = Path.Combine(Path.GetTempPath(), $"sql-facade-example-{Guid.NewGuid():N}.db");
            try
            {
                Console.WriteLine($"File-based SQLite demo: {path}");
                ISqlFacade fileFacade = SqlFacadeFactory.GetSqlFacade(
                    new SqlFacadeConfig(Sql.DbType.SQLITE, $"Data Source={path}"));

                using (IDbConnection con = fileFacade.GetSqlFacadeConfig().GetConnection())
                {
                    con.Open();
                    using var cmd = new SqliteCommand(
                        "CREATE TABLE IF NOT EXISTS note(id TEXT PRIMARY KEY, body TEXT)",
                        (SqliteConnection)con);
                    cmd.ExecuteNonQuery();
                }

                int inserted = fileFacade.ExecuteSqlWrite(new SqlInsert("note")
                    .WithField(new Field("id", "n1"))
                    .WithField(new Field("body", "hello from file sqlite")));
                SqlSelect select = new SqlSelect("note")
                    .WithField(new Field("id"))
                    .WithField(new Field("body"))
                    .WithWhere(new Filter().WithExpression(new Expression("id", "n1")));
                string body = fileFacade.GetSingleResult<string>(
                    new SqlSelect("note").WithField(new Field("body"))
                        .WithWhere(new Filter().WithExpression(new Expression("id", "n1"))));

                Console.WriteLine($"      Inserted={inserted}, body={body}");
                Console.WriteLine($"      Raw Sql: {fileFacade.GetSql(select, false)}");
                Console.WriteLine();
            }
            finally
            {
                // Close is a no-op only for the shared :memory: connection; file DBs can be deleted after use.
                DropDB(path);
                if (File.Exists(path))
                    Console.WriteLine($"      Warning: could not delete {path}");
                else
                    Console.WriteLine("      File SQLite temp DB deleted.");
                Console.WriteLine();
            }
        }

        /// <summary>Shared helper: log query + print Canvas rows.</summary>
        private static void PrintCanvases(string label, SqlSelect sqlSelect)
        {
            log(label, sqlSelect);
            foreach (Canvas canvas in sqlFacade.GetResults<Canvas>(sqlSelect))
            {
                Console.WriteLine($"          Result: {canvas}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Create sample schema. Uses raw ADO.NET once; all subsequent work goes through ISqlFacade.
        /// Hierarchy: canvas → canvas_stroke → stroke_tag; canvas-metdata is 1:1 optional extension.
        /// </summary>
        public static void CreateDB()
        {
            string d0 = "DROP TABLE IF EXISTS stroke_tag";
            string d1 = "DROP TABLE IF EXISTS canvas_stroke";
            string d2 = "DROP TABLE IF EXISTS `canvas-metdata`";
            string d3 = "DROP TABLE IF EXISTS canvas";
            string c1 = "CREATE TABLE canvas(id TEXT PRIMARY KEY, color TEXT, is_active INT, notes TEXT)";
            string c2 = "CREATE TABLE `canvas-metdata`(id TEXT PRIMARY KEY, extra_data TEXT, FOREIGN KEY (id) references canvas (id))";
            string c3 = "CREATE TABLE canvas_stroke(id TEXT PRIMARY KEY, canvas_id TEXT, label TEXT, sort_ord INT, FOREIGN KEY (canvas_id) references canvas (id))";
            string c4 = "CREATE TABLE stroke_tag(id TEXT PRIMARY KEY, stroke_id TEXT, tag TEXT, FOREIGN KEY (stroke_id) references canvas_stroke (id))";

            using (IDbConnection con = sqlFacade.GetSqlFacadeConfig().GetConnection())
            {
                foreach (string stm in new[] { d0, d1, d2, d3, c1, c2, c3, c4 })
                {
                    using var cmd = new SqliteCommand(stm, (SqliteConnection)con);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Clear all sample tables via SqlDelete (child tables first).</summary>
        private static void CleanDB()
        {
            foreach (var table in new[] { "stroke_tag", "canvas_stroke", "canvas-metdata", "canvas" })
            {
                string label = $"Delete {table}";
                SqlDelete sqlDelete = new SqlDelete(table);
                int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);
                log(label, sqlDelete, $"{rowsChanged} row(s) deleted");
            }
        }

        /// <summary>Optional helper for file-based SQLite cleanup.</summary>
        public static void DropDB(string dbFileName)
        {
            File.Delete(dbFileName);
        }

        /// <summary>Seed N canvases for pagination demos (ids uuid-0 … uuid-N-1).</summary>
        private static IList<int> BatchWrite(int numCanvases)
        {
            List<ISqlWrite> sqlInserts = new List<ISqlWrite>();
            for (int index = 0; index < numCanvases; index++)
            {
                sqlInserts.Add(new SqlInsert("canvas")
                    .WithField(new Field("id", $"uuid-{index}"))
                    .WithField(new Field("color", $"color-{index}"))
                    .WithField(new Field("is_active", ExampleSqlDialect.BooleanValue(true))));
            }
            return sqlFacade.ExecuteMultiSqlWrite(sqlInserts);
        }

        /// <summary>
        /// Print query JSON, parameterized template, raw SQL, and DeserializeFromJson round-trip.
        /// Raw CTEs without parameterless constructors may skip deserialization.
        /// </summary>
        private static void log(String label, ISql CanvasSql, string summaryLog = null)
        {
            Console.WriteLine($"{label}: {CanvasSql.ToString()}");
            Console.WriteLine($"      Sql Template: {sqlFacade.GetSql(CanvasSql, true)}");
            Console.WriteLine($"      Raw Sql: {sqlFacade.GetSql(CanvasSql, false)}");
            try
            {
                Console.WriteLine($"      Deserialized: {sqlFacade.DeserializeFromJson(CanvasSql.ToString()).ToString()}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"      Deserialized: (skipped — {e.GetType().Name}: raw CTE / non-round-trip JSON)");
            }
            if (summaryLog != null)
            {
                Console.WriteLine(summaryLog);
                Console.WriteLine();
            }
        }
    }
}
