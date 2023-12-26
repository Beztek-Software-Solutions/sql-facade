// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Example
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using Beztek.Facade.Sql;
    using Microsoft.Data.Sqlite;

    static class Program
    {
        private static ISqlFacade sqlFacade;

        // Pass the correct password as the first argument when invoking this program
        static void Main(string[] args)
        {
            sqlFacade = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(Sql.DbType.SQLITE, "Data Source=:memory:"));

            // Create the tables
            CreateDB();

            // Delete database
            CleanDB();

            // Insert
            List<ISqlWrite> sqlInsertList = new List<ISqlWrite>();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "123"))
                .WithField(new Field("color", "green"));
            Console.WriteLine($"CanvasSql: {sqlInsert.ToString()}");
            sqlInsertList.Add(sqlInsert);
            sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "another-uuid"))
                .WithField(new Field("color", "red"));
            Console.WriteLine($"CanvasSql: {sqlInsert.ToString()}");
            sqlInsertList.Add(sqlInsert);
            sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "greencanvas"))
                .WithField(new Field("color", "green"));
            Console.WriteLine($"CanvasSql: {sqlInsert.ToString()}");
            sqlInsertList.Add(sqlInsert);
            IList<int> results = sqlFacade.ExecuteMultiSqlWrite(sqlInsertList);
            foreach (Object result in results)
            {
                Console.WriteLine(result + " row(s) inserted as part of batch");
            }
            Console.WriteLine();

            // Insert with select
            String label = "Insert with Select";
            sqlInsert = new SqlInsert("canvas");
            SqlSelect sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("\'cloned-uuid\'", "id", true))
                .WithField(new Field("\'red\'", "color", true))
                .WithWhere(new Filter().WithExpression(new Expression("id", "another-uuid")));
            sqlInsert.WithQuery(sqlSelect);
            log(label, sqlInsert);
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            log(label, sqlInsert, $"{rowsChanged} row(s) inserted");
            log(label, sqlSelect);

            // Update
            label = "Update";
            SqlUpdate update = new SqlUpdate("canvas")
                .WithField(new Field("color", "yellow"))
                .WithFilter(new Expression("color", "red"));
            rowsChanged = sqlFacade.ExecuteSqlWrite(update);
            log(label, update, $"{rowsChanged} row(s) updated");

            // Query
            label = "Query with two where expressions";
            sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"));
            Filter filter = new Filter()
                .WithExpression(new Expression("v.id", "another-uuid"))
                .WithExpression(new Expression("v.color", "green").WithRelation(Relation.EqualTo).WithLogicalRelation(LogicalRelation.AndNot));
            sqlSelect.WithWhere(filter);
            IList<Canvas> canvasList = sqlFacade.GetResults<Canvas>(sqlSelect);
            log(label, sqlSelect);
            foreach (Canvas canvas in canvasList)
            {
                Console.WriteLine($"          Result: {canvas.ToString()}");
            }
            Console.WriteLine();

            // Query with two where expressions combined with "Or"
            label = "Query with two where expressions combined with or";
            sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"));
            filter = new Filter()
                .WithExpression(new Expression("v.id", "another-uuid"))
                .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or));
            sqlSelect.WithWhere(filter);
            canvasList = sqlFacade.GetResults<Canvas>(sqlSelect);
            log(label, sqlSelect);
            foreach (Canvas canvas in canvasList)
            {
                Console.WriteLine($"          Result: {canvas.ToString()}");
            }
            Console.WriteLine();

            // Green canvas
            label = "Green canvas";
            sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "green")));
            canvasList = sqlFacade.GetResults<Canvas>(sqlSelect);
            log(label, sqlSelect);
            foreach (Canvas canvas in canvasList)
            {
                Console.WriteLine($"          Result: {canvas.ToString()}");
            }
            Console.WriteLine();

            // Derived Tables
            label = "Derived tables";
            SqlSelect subSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithField(new Field("\'Pseudo data from derived table\'", "ExtraData", true));
            sqlSelect = new SqlSelect(new DerivedTable(subSelect, "v"));
            log(label, sqlSelect);
            IList<CanvasExtended> canvasExtendedList = sqlFacade.GetResults<CanvasExtended>(sqlSelect);
            foreach (CanvasExtended canvasExtended in canvasExtendedList)
            {
                Console.WriteLine($"          Derived table result: {canvasExtended.ToString()}");
            }
            Console.WriteLine();

            // Insert extended canvas attributes in canvas-metdata table
            label = "Insert extended canvas attributes in canvas-metdata table";
            sqlInsert = new SqlInsert("canvas-metdata")
                .WithField(new Field("id", "another-uuid"))
                .WithField(new Field("extra_data", "Some extra data"));
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            log(label, sqlInsert, $"{rowsChanged} row(s) updated");

            // Get All Extended Canvases
            label = "Get all extended canvas";
            sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithField(new Field("m.extra_data", "ExtraData"));
            Join complexJoin = new Join(new Table("canvas-metdata", "m"), new Expression("m.id", "v.id"), JoinType.LeftJoin)
                .WithJoinExpression(new Expression("m.extra_data", "Some extra data"));
            sqlSelect.WithJoin(complexJoin);
            canvasExtendedList = sqlFacade.GetResults<CanvasExtended>(sqlSelect);
            log(label, sqlSelect);
            foreach (CanvasExtended canvasExtended in canvasExtendedList)
            {
                Console.WriteLine($"          Result: {canvasExtended.ToString()}");
            }
            Console.WriteLine();

            // Delete green rows
            label = "Delete green rows";
            SqlDelete sqlDelete = new SqlDelete("canvas")
                .WithFilter(new Expression("color", "green"));
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);
            log(label, sqlDelete, $"{rowsChanged} row(s) deleted");

            // Get All Canvases
            label = "Get all canvas";
            sqlSelect = new SqlSelect("canvas");
            canvasList = sqlFacade.GetResults<Canvas>(sqlSelect);
            log(label, sqlSelect);
            foreach (Canvas canvas in canvasList)
            {
                Console.WriteLine($"          Result: {canvas.ToString()}");
            }
            Console.WriteLine();

            // Pagination
            label = "Pagination";
            CleanDB();
            BatchWrite(10);
            sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("v.id", "uuid-211").WithRelation(Relation.GreaterThanOrEqualTo))
                    .WithExpression(new Expression("v.id", "uuid-910").WithRelation(Relation.GreaterThanOrEqualTo).WithLogicalRelation(LogicalRelation.AndNot)))
                .WithSort(new Sort("v.id"))
                .WithSort(new Sort("v.color", false));
            int pageNum = 2;
            int pageSize = 3;
            PagedResultsWithTotal<Canvas> pagedResults = (PagedResultsWithTotal<Canvas>)sqlFacade.GetPagedResults<Canvas>(sqlSelect, pageNum, pageSize, true);
            log(label, sqlSelect, $"{pagedResults.PagedList.Count} result(s) (of {pagedResults.TotalResults}) retrieved from page {pageNum} (of {pagedResults.TotalPages})");
            foreach (Canvas canvas in pagedResults.PagedList)
            {
                Console.WriteLine($"          Result: {canvas.ToString()}");
            }
        }

        public static void CreateDB()
        {
            // Drop tables if they exist
            string d1 = "DROP TABLE IF EXISTS `canvas-metdata`";
            string d2 = "DROP TABLE IF EXISTS canvas";
            // Create the tables
            string c1 = "CREATE TABLE canvas(id TEXT PRIMARY KEY, color TEXT)";
            string c2 = "CREATE TABLE `canvas-metdata`(id TEXT PRIMARY KEY, extra_data TEXT, FOREIGN KEY (id) references canvas (id))";

            using (IDbConnection con = sqlFacade.GetSqlFacadeConfig().GetConnection())
            {
                using var cmd1 = new SqliteCommand(d1, (SqliteConnection)con);
                cmd1.ExecuteNonQuery();

                using var cmd2 = new SqliteCommand(d2, (SqliteConnection)con);
                cmd2.ExecuteNonQuery();

                using var cmd3 = new SqliteCommand(c1, (SqliteConnection)con);
                cmd3.ExecuteNonQuery();

                using var cmd4 = new SqliteCommand(c2, (SqliteConnection)con);
                cmd4.ExecuteNonQuery();
            }
        }

        private static void CleanDB()
        {
            // Delete Extended canvas data
            String label = "Delete extended canvas data";
            SqlDelete sqlDelete = new SqlDelete("canvas-metdata");
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);
            log(label, sqlDelete, $"{rowsChanged} row(s) deleted");

            // Delete canvas data
            label = "Delete canvas data";
            sqlDelete = new SqlDelete("canvas");
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);
            log(label, sqlDelete, $"{rowsChanged} row(s) deleted");
        }

        public static void DropDB(string dbFileName)
        {
            File.Delete(dbFileName);
        }

        private static IList<int> BatchWrite(int numCanvases)
        {
            List<ISqlWrite> sqlInserts = new List<ISqlWrite>();
            for (int index = 0; index < numCanvases; index++)
            {
                SqlInsert sqlInsert = new SqlInsert("canvas")
                    .WithField(new Field("id", $"uuid-{index.ToString()}"))
                    .WithField(new Field("color", $"color-{index.ToString()}"));
                sqlInserts.Add(sqlInsert);
            }
            return sqlFacade.ExecuteMultiSqlWrite(sqlInserts);
        }

        private static void log(String label, ISql CanvasSql, string summaryLog = null)
        {
            Console.WriteLine($"{label}: {CanvasSql.ToString()}");
            Console.WriteLine($"      Sql Template: {sqlFacade.GetSql(CanvasSql, true)}");
            Console.WriteLine($"      Raw Sql: {sqlFacade.GetSql(CanvasSql, false)}");
            Console.WriteLine($"      Deserialized: {sqlFacade.DeserializeFromJson(CanvasSql.ToString()).ToString()}");
            if (summaryLog != null)
            {
                Console.WriteLine(summaryLog);
                Console.WriteLine();
            }
        }
    }
}
