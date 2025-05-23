// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using Beztek.Facade.Sql;
    using Microsoft.Data.Sqlite;
    using NUnit.Framework;

    [TestFixture]
    public class SqlFacadeUnitTest
    {
        private static ISqlFacade sqlFacade = SqlFacadeFactory.GetSqlFacade(new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:"));

        [OneTimeSetUp]
        public void InitializeClass()
        {
            // Create the tables for the tests
            string stm1 = "CREATE TABLE canvas(id TEXT PRIMARY KEY, color TEXT, ordering INT)";
            string stm2 = "CREATE TABLE `canvas-metdata`(id TEXT PRIMARY KEY, extra_data TEXT, FOREIGN KEY (id) references canvas (id))";

            using (IDbConnection con = sqlFacade.GetSqlFacadeConfig().GetConnection())
            {
                using var cmd1 = new SqliteCommand(stm1, (SqliteConnection)con);
                cmd1.ExecuteNonQuery();

                using var cmd2 = new SqliteCommand(stm2, (SqliteConnection)con);
                cmd2.ExecuteNonQuery();
            }
        }

        [Test]
        public void TestGetSql()
        {
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "123"))
                .WithField(new Field("color", "green"));
            string expectedRawSql = "INSERT INTO \"canvas\" (\"id\", \"color\") VALUES ('123', 'green')";
            string expectedTemplateSql = "INSERT INTO \"canvas\" (\"id\", \"color\") VALUES (@p0, @p1)";
            Assert.That(expectedRawSql, Is.EqualTo(sqlFacade.GetSql(sqlInsert, false)));
            Assert.That(expectedTemplateSql, Is.EqualTo(sqlFacade.GetSql(sqlInsert, true)));
        }

        [Test]
        public void TestInsert()
        {
            CleanDB();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "123"))
                .WithField(new Field("color", "green"))
                .WithField(new Field("ordering", 888));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.That(1, Is.EqualTo(rowsChanged));
            Assert.That(String.Equals(sqlInsert.ToString(), sqlFacade.DeserializeFromJson(sqlInsert.ToString()).ToString()), Is.True);
            Assert.That(CreateCanvas("123", "green", 888), Is.EqualTo(SelectFromCanvasTable("123")));
            CleanDB();
        }

        [Test]
        public void TestInsertWithSelect()
        {
            CleanDB();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "orig-uuid"))
                .WithField(new Field("color", "red"))
                .WithField(new Field("ordering", 999));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.That(1, Is.EqualTo(rowsChanged));

            sqlInsert = new SqlInsert("canvas");
            SqlSelect sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("\'cloned-uuid\'", "id", true))
                .WithField(new Field("color"))
                .WithField(new Field("ordering"))
                .WithWhere(new Filter().WithExpression(new Expression("id", "orig-uuid")));
            sqlInsert.WithQuery(sqlSelect);
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.That(1, Is.EqualTo(rowsChanged));
            Assert.That(CreateCanvas("cloned-uuid", "red", 999), Is.EqualTo(SelectFromCanvasTable("cloned-uuid")));
            CleanDB();
        }

        [Test]
        public void TestUpdate()
        {
            CleanDB();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "orig-uuid"))
                .WithField(new Field("color", "red"))
                .WithField(new Field("ordering", 777));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.That(1, Is.EqualTo(rowsChanged));

            SqlUpdate sqlUpdate = new SqlUpdate("canvas")
                .WithField(new Field("color", "yellow"))
                .WithFilter(new Expression("color", "red"));
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlUpdate);
            Assert.That(1, Is.EqualTo(rowsChanged));
            Assert.That(String.Equals(sqlUpdate.ToString(), sqlFacade.DeserializeFromJson(sqlUpdate.ToString()).ToString()), Is.True);
            Assert.That(CreateCanvas("orig-uuid", "yellow", 777), Is.EqualTo(SelectFromCanvasTable("orig-uuid")));
            CleanDB();
        }

        [Test]
        public void TestDelete()
        {
            CleanDB();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "orig-uuid"))
                .WithField(new Field("color", "red"))
                .WithField(new Field("ordering", 666));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.That(1, Is.EqualTo(rowsChanged));
            Assert.That(CreateCanvas("orig-uuid", "red", 666), Is.EqualTo(SelectFromCanvasTable("orig-uuid")));

            SqlDelete sqlDelete = new SqlDelete("canvas")
                .WithFilter(new Expression("color", "red"));
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);
            Assert.That(1, Is.EqualTo(rowsChanged));
            Assert.That(String.Equals(sqlDelete.ToString(), sqlFacade.DeserializeFromJson(sqlDelete.ToString()).ToString()), Is.True);
            Assert.That(SelectFromCanvasTable("orig-uuid"), Is.Null);
            CleanDB();
        }

        [Test]
        public void TestCommonTableExpressions()
        {
            CleanDB();
            InsertThreeCanvases();

            // Common Table Expression with SqlSelect
            SqlSelect subSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithField(new Field("\'Pseudo data from derived table\'", "ExtraData", true));
            Assert.That(String.Equals(subSelect.ToString(), sqlFacade.DeserializeFromJson(subSelect.ToString()).ToString()), Is.True);
            CommonTableExpression cte1 = new CommonTableExpression(subSelect, "c1");
            SqlSelect sqlSelect = new SqlSelect("c1").WithCommonTableExpression(cte1);
            IList<CanvasExtended> canvasExtendedList = sqlFacade.GetResults<CanvasExtended>(sqlSelect);
            List<CanvasExtended> expectedCanvasExtendedList = new List<CanvasExtended>();
            expectedCanvasExtendedList.Add(CreateCanvasExtended("123", "green", "Pseudo data from derived table"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("greencanvas", "green", "Pseudo data from derived table"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("cloned-uuid", "yellow", "Pseudo data from derived table"));
            int index = 0;
            foreach (CanvasExtended canvasExtended in expectedCanvasExtendedList)
            {
                Assert.That(expectedCanvasExtendedList[index], Is.EqualTo(canvasExtended));
                index++;
            }

            // Common Table Expression with Raw SQL Query
            CommonTableExpression cte2 = new CommonTableExpression("select 'red' as col1", "c2");
            sqlSelect = new SqlSelect("canvas")
                    .WithField(new Field("color"))
                    .WithCommonTableExpression(cte2)
                    .WithJoin(new Join(cte2, new Expression("c2.col1", "canvas.color")));
            Assert.That("red", Is.EqualTo(sqlFacade.GetSingleResult<string>(sqlSelect)));

            CleanDB();
        }

        [Test]
        public void TestNestedCommonTableExpressions()
        {
            CleanDB();
            InsertThreeCanvases();

            CommonTableExpression cte1 = new CommonTableExpression("select '123' as id, 'c1v1' as v1 union all select 'another-uuid' as id, 'c1v2' as v1", "c1");
            CommonTableExpression cte2 = new CommonTableExpression("select '123' as id, 'c2v1' as v2 union all select 'another-uuid' as id, 'c2v2' as v2", "c2");
            CommonTableExpression cte3 = new CommonTableExpression("select '123' as id, 'c3v1' as v3 union all select 'another-uuid' as id, 'c3v2' as v3", "c3");
            SqlSelect sqlSelect = new SqlSelect("canvas")
                .WithCommonTableExpression(cte1)
                .WithCommonTableExpression(cte2)
                .WithCommonTableExpression(cte3)
                .WithField(new Field("canvas.id", "id"))
                .WithField(new Field("c1.v1", "v1"))
                .WithField(new Field("c2.v2", "v2"))
                .WithField(new Field("c3.v3", "v3"))
                .WithJoin(new Join(cte1, new Expression("c1.id", "canvas.id")))
                .WithJoin(new Join(cte2, new Expression("c2.id", "canvas.id")))
                .WithJoin(new Join(cte3, new Expression("c3.id", "canvas.id")));

            List<object> var = sqlFacade.GetResults<object>(sqlSelect).ToList();
            Assert.That(2, Is.EqualTo(var.Count));

            SqlSelect nestedCteSelect = new SqlSelect(new CommonTableExpression(sqlSelect, "agg"))
                .WithField(new Field("count(*)","num", true));
            // Check that the three nested CTEs bubble up to the nestedSqlSelect
            Assert.That(3, Is.EqualTo(nestedCteSelect.CommonTableExpressions.Count));

            // Check that the count from the nested select matches the original select
            int count = sqlFacade.GetSingleResult<int>(nestedCteSelect);
            Assert.That(var.Count, Is.EqualTo(count));

            CleanDB();
        }

        [Test]
        public void TestSelectFromDerivedTable()
        {
            CleanDB();
            InsertThreeCanvases();
            SqlSelect subSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithField(new Field("\'Pseudo data from derived table\'", "ExtraData", true));
            Assert.That(String.Equals(subSelect.ToString(), sqlFacade.DeserializeFromJson(subSelect.ToString()).ToString()), Is.True);
            SqlSelect sqlSelect = new SqlSelect(new DerivedTable(subSelect, "v"));
            Assert.That(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()), Is.True);
            IList<CanvasExtended> canvasExtendedList = sqlFacade.GetResults<CanvasExtended>(sqlSelect);
            List<CanvasExtended> expectedCanvasExtendedList = new List<CanvasExtended>();
            expectedCanvasExtendedList.Add(CreateCanvasExtended("123", "green", "Pseudo data from derived table"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("greencanvas", "green", "Pseudo data from derived table"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("cloned-uuid", "yellow", "Pseudo data from derived table"));
            int index = 0;
            foreach (CanvasExtended canvasExtended in expectedCanvasExtendedList)
            {
                Assert.That(expectedCanvasExtendedList[index], Is.EqualTo(canvasExtended));
                index++;
            }

            CleanDB();
        }

        [Test]
        public void TestSelectSortAndPagination()
        {
            CleanDB();
            int numCanvasesToInsert = 1000;
            IList<int> results = BatchWrite(numCanvasesToInsert);
            Assert.That(numCanvasesToInsert, Is.EqualTo(results.Count));
            foreach (Object result in results)
            {
                Assert.That(1, Is.EqualTo(result));
            }

            SqlSelect sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithField(new Field("v.ordering"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("v.id", "uuid-211").WithRelation(Relation.GreaterThanOrEqualTo))
                    .WithExpression(new Expression("v.id", "uuid-910").WithRelation(Relation.LessThan)))
                .WithSort(new Sort("v.id"))
                .WithSort(new Sort("v.color", false));
            int pageNumber = 3;
            int pageSize = 30;
            PagedResultsWithTotal<Canvas> pagedResultsWithTotal = (PagedResultsWithTotal<Canvas>)sqlFacade.GetPagedResults<Canvas>(sqlSelect, pageNumber, pageSize, true);
            Assert.That(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()), Is.True);
            Assert.That(pageNumber, Is.EqualTo(pagedResultsWithTotal.PageNum));
            int totalCount = pagedResultsWithTotal.TotalResults;
            int totalPages = pagedResultsWithTotal.TotalPages;
            // Check total results
            int expectedTotalResults = sqlFacade.GetTotalNumResults(sqlSelect);
            Assert.That(expectedTotalResults, Is.EqualTo(totalCount));
            // Check max results per page
            Assert.That(30, Is.EqualTo(pagedResultsWithTotal.PagedList.Count));
            // Check some arbitrary result which validates the sorting and retrieval
            Assert.That(CreateCanvas("uuid-27", "color-27", 31027), Is.EqualTo(pagedResultsWithTotal.PagedList[4]));
            // Check the pagedResults for the last page
            int expectedNumInLastPage = totalCount - (totalPages - 1) * pagedResultsWithTotal.PageSize;
            PagedResults<Canvas> pagedResults = sqlFacade.GetPagedResults<Canvas>(sqlSelect, totalPages, pagedResultsWithTotal.PageSize);
            Assert.That(expectedNumInLastPage, Is.EqualTo(pagedResults.PagedList.Count));

            CleanDB();
        }

        [Test]
        public void TestInList()
        {
            CleanDB();
            BatchWrite(1000);
            
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithField(new Field("ordering"))
                .WithSort(new Sort("id"));

            Canvas canvas13 = CreateCanvas("uuid-13", "color-13", 31013);
            Canvas canvas127 = CreateCanvas("uuid-127", "color-127", 31127);
            Canvas canvas336 = CreateCanvas("uuid-336", "color-336", 31336);

            sqlSelect.WithWhere(new Filter().WithExpression(new Expression("id", new string[] { "uuid-13", "uuid-336" })
                                    .WithRelation(Relation.In)
                                    .WithLogicalRelation(LogicalRelation.And)));
            // serialize query and then deserialize it.
            string jsonQuery = sqlSelect.ToString();
            sqlSelect = (SqlSelect)sqlFacade.DeserializeFromJson(jsonQuery);
                                    
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.That(2, Is.EqualTo(results.Count));
            Assert.That(canvas13, Is.EqualTo(results[0]));
            Assert.That(canvas336, Is.EqualTo(results[1]));

            sqlSelect.WithWhere(new Filter().WithExpression(new Expression("id", new List<string> { "uuid-13", "uuid-336" })
                                    .WithRelation(Relation.In)
                                    .WithLogicalRelation(LogicalRelation.And)));
            // serialize query and then deserialize it.
            jsonQuery = sqlSelect.ToString();
            sqlSelect = (SqlSelect)sqlFacade.DeserializeFromJson(jsonQuery);
                                    
            results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.That(2, Is.EqualTo(results.Count));
            Assert.That(canvas13, Is.EqualTo(results[0]));
            Assert.That(canvas336, Is.EqualTo(results[1]));

            sqlSelect.WithWhere(new Filter().WithExpression(new Expression("ordering", new int[] { 31013, 31336 })
                                    .WithRelation(Relation.In)
                                    .WithLogicalRelation(LogicalRelation.And)));
            // serialize query and then deserialize it.
            jsonQuery = sqlSelect.ToString();
            sqlSelect = (SqlSelect)sqlFacade.DeserializeFromJson(jsonQuery);

            results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.That(2, Is.EqualTo(results.Count));
            Assert.That(canvas13, Is.EqualTo(results[0]));
            Assert.That(canvas336, Is.EqualTo(results[1]));

            sqlSelect.WithWhere(new Filter().WithExpression(new Expression("ordering", new List<int> { 31013, 31336 })
                                    .WithRelation(Relation.In)
                                    .WithLogicalRelation(LogicalRelation.And)));
            // serialize query and then deserialize it.
            jsonQuery = sqlSelect.ToString();
            sqlSelect = (SqlSelect)sqlFacade.DeserializeFromJson(jsonQuery);

            results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.That(2, Is.EqualTo(results.Count));
            Assert.That(canvas13, Is.EqualTo(results[0]));
            Assert.That(canvas336, Is.EqualTo(results[1]));
        }

        [Test]
        public void TestRelation()
        {
            CleanDB();
            BatchWrite(1000);

            SqlSelect sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithField(new Field("ordering"))
                .WithSort(new Sort("id"));

            Canvas canvas13 = CreateCanvas("uuid-13", "color-13", 31013);
            Canvas canvas127 = CreateCanvas("uuid-127", "color-127", 31127);
            Canvas canvas336 = CreateCanvas("uuid-336", "color-336", 31336);

            // No filters - all results
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.That(1000, Is.EqualTo(results.Count));
            Assert.That(canvas127, Is.EqualTo(results[32]));

            // Iterate all possible combinations of logical relations in the filter
            string[] runParams = new string[] { "andFirst", "andSecond", "orFirst", "orSecond" };
            foreach (string runParam in runParams)
            {
                // Setting the logical relations and flags for each iteration
                LogicalRelation logicalRelation = LogicalRelation.And;
                LogicalRelation negationRelation = LogicalRelation.AndNot;
                if (String.Equals("orFirst", runParam) || String.Equals("orSecond", runParam))
                {
                    logicalRelation = LogicalRelation.Or;
                    negationRelation = LogicalRelation.OrNot;
                }
                bool isAnd = String.Equals(runParam, "andFirst") || String.Equals(runParam, "andSecond");
                bool isFirst = String.Equals(runParam, "andFirst") || String.Equals(runParam, "orFirst");

                // Equals
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-127")
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(1, Is.EqualTo(results.Count));
                Assert.That(canvas127, Is.EqualTo(results[0]));

                // Greater Than
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.GreaterThan)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(753, Is.EqualTo(results.Count));
                Assert.That(canvas336, Is.EqualTo(results[17]));

                // Greater Than with Negation
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.LessThanOrEqualTo)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(753, Is.EqualTo(results.Count));
                Assert.That(canvas336, Is.EqualTo(results[17]));

                // Lesser Than
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.LessThan)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(246, Is.EqualTo(results.Count));
                Assert.That(canvas13, Is.EqualTo(results[35]));

                // Lesser Than with Negation
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.GreaterThanOrEqualTo)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(246, Is.EqualTo(results.Count));
                Assert.That(canvas13, Is.EqualTo(results[35]));

                // Greater Than or Equal To
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.GreaterThanOrEqualTo)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(754, Is.EqualTo(results.Count));
                Assert.That(canvas336, Is.EqualTo(results[18]));

                // Greater Than or Equal To with negation
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.LessThan)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(754, Is.EqualTo(results.Count));
                Assert.That(canvas336, Is.EqualTo(results[18]));

                // Lesser Than or Equal To
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.LessThanOrEqualTo)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(247, Is.EqualTo(results.Count));
                Assert.That(canvas13, Is.EqualTo(results[35]));

                // Lesser Than or Equal To with negation
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.GreaterThan)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(247, Is.EqualTo(results.Count));
                Assert.That(canvas13, Is.EqualTo(results[35]));

                // In and Not In only make sense with "and" and not with "or"
                if (Object.Equals(logicalRelation, LogicalRelation.And) || Object.Equals(logicalRelation, LogicalRelation.AndNot))
                {
                    // In List
                    sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                            .WithExpression(new Expression("id", new string[] { "uuid-13", "uuid-336" })
                                            .WithRelation(Relation.In)
                                            .WithLogicalRelation(logicalRelation)));
                    // serialize query and then deserialize it.
                    string jsonQuery = sqlSelect.ToString();
                    sqlSelect = (SqlSelect)sqlFacade.DeserializeFromJson(jsonQuery);

                    results = sqlFacade.GetResults<Canvas>(sqlSelect);
                    Assert.That(2, Is.EqualTo(results.Count));
                    Assert.That(canvas13, Is.EqualTo(results[0]));
                    Assert.That(canvas336, Is.EqualTo(results[1]));

                    // Not In List
                    sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                            .WithExpression(new Expression("id", new string[] { "uuid-13", "uuid-336" })
                                            .WithRelation(Relation.In)
                                            .WithLogicalRelation(negationRelation)));
                    results = sqlFacade.GetResults<Canvas>(sqlSelect);
                    Assert.That(998, Is.EqualTo(results.Count));
                }

                // Null
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", null)
                                        .WithRelation(Relation.NullValue)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(0, Is.EqualTo(results.Count));

                // Not Null
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", null)
                                        .WithRelation(Relation.NullValue)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(1000, Is.EqualTo(results.Count));

                // True raw
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("1=1", null)
                                        .WithIsRaw()
                                        .WithRelation(Relation.TrueValue)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(1000, Is.EqualTo(results.Count));

                // False raw
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("1=2", null)
                                        .WithIsRaw()
                                        .WithRelation(Relation.TrueValue)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);

                // Raw negation should throw and Argument exception
                try
                {
                    sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                            .WithExpression(new Expression("1=2", null)
                                            .WithIsRaw()
                                            .WithLogicalRelation(negationRelation)
                                            .WithRelation(Relation.TrueValue)));
                    results = sqlFacade.GetResults<Canvas>(sqlSelect);
                    Assert.That(0, Is.EqualTo(results.Count));
                    // If we get here the test failed, because an exception was not thrown
                    Assert.That(false, Is.True);
                }
                catch (Exception e)
                {
                    if (e is ArgumentException)
                    {
                        // If we get here the test passed, because an ArgumentException was thrown
                        Assert.That(true, Is.True);
                    }
                    else
                    {
                        Assert.That(false, Is.True);
                    }
                }

                // Exists
                sqlSelect.WithWhere(new Filter().WithExpression(new Expression("id", "uuid-32")));
                results = sqlFacade.GetResults<Canvas>(new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlExists(sqlSelect)
                                                .WithLogicalRelation(logicalRelation))));
                Assert.That(1000, Is.EqualTo(results.Count));

                // Not Exists
                sqlSelect.WithWhere(new Filter().WithExpression(new Expression("id", "uuid-32")));
                results = sqlFacade.GetResults<Canvas>(new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlExists(sqlSelect)
                                                .WithLogicalRelation(negationRelation))));
                Assert.That(0, Is.EqualTo(results.Count));

                // Exists with no results
                sqlSelect.WithWhere(new Filter().WithExpression(new Expression("id", "non-existent")));
                results = sqlFacade.GetResults<Canvas>(new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlExists(sqlSelect)
                                                .WithLogicalRelation(logicalRelation))));
                Assert.That(0, Is.EqualTo(results.Count));

                // In Subquery
                SqlSelect subQuery = new SqlSelect("canvas").WithField(new Field("id"))
                                        .WithWhere(new Filter().WithExpression(new Expression("id", "uuid-32")));
                results = sqlFacade.GetResults<Canvas>(new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlIn("id", subQuery)
                                                .WithLogicalRelation(logicalRelation))));
                Assert.That(1, Is.EqualTo(results.Count));

                // Not In Subquery
                subQuery = new SqlSelect("canvas").WithField(new Field("id"))
                                        .WithWhere(new Filter().WithExpression(new Expression("id", "uuid-32")));
                SqlSelect currQuery = new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlIn("id", subQuery)
                                                .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(currQuery);
                Assert.That(((!isAnd) && (!isFirst) ? 999 : 1000), Is.EqualTo(results.Count));

                // Starts With
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-1")
                                        .WithRelation(Relation.StartsWith)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(111, Is.EqualTo(results.Count));

                // Does not start With
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-1")
                                        .WithRelation(Relation.StartsWith)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(889, Is.EqualTo(results.Count));

                // Ends With
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "1")
                                        .WithRelation(Relation.EndsWith)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(100, Is.EqualTo(results.Count));

                // Does not end With
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "1")
                                        .WithRelation(Relation.EndsWith)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(900, Is.EqualTo(results.Count));

                // Contains
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "d-1")
                                        .WithRelation(Relation.Contains)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(111, Is.EqualTo(results.Count));

                // Does not contain
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "d-1")
                                        .WithRelation(Relation.Contains)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.That(889, Is.EqualTo(results.Count));
            }

            CleanDB();
        }

        [Test]
        public void TestSqlCombine()
        {
            CleanDB();
            InsertThreeCanvases();

            SqlSelect sqlSelectRed = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("id", "another-uuid")));
            SqlSelect sqlSelectAllGreen = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("color", "green")));
            SqlSelect sqlSelectOneGreen = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("id", "greencanvas")));

            // Union
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                        .WithExpression(new Expression("id", "another-uuid")))
                        .WithCombine(new SqlCombine(sqlSelectAllGreen, SqlRelation.Union));
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.That(3, Is.EqualTo(results.Count));

            // UnionAll
            sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                        .WithExpression(new Expression("id", "another-uuid")))
                        .WithCombine(new SqlCombine(sqlSelectAllGreen, SqlRelation.UnionAll));
            results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.That(3, Is.EqualTo(results.Count));

            // Except
            sqlSelect = sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                        .WithExpression(new Expression("color", "green")))
                        .WithCombine(new SqlCombine(sqlSelectOneGreen, SqlRelation.Except));
            results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.That(1, Is.EqualTo(results.Count));

            // Intersect
            sqlSelect = sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                        .WithExpression(new Expression("color", "green")))
                        .WithCombine(new SqlCombine(sqlSelectOneGreen, SqlRelation.Intersect));
            results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.That(1, Is.EqualTo(results.Count));

            // Serialization and Deserialization
            Assert.That(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()), Is.True);

            CleanDB();
        }

        [Test]
        public void TestWhereWithOr()
        {
            CleanDB();
            InsertThreeCanvases();

            Canvas canvasGreen1 = CreateCanvas("123", "green", 1);
            Canvas canvasRed = CreateCanvas("another-uuid", "red", 2);
            Canvas canvasGreen2 = CreateCanvas("greencanvas", "green", 3);

            SqlSelect baseSqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithField(new Field("v.ordering"));
            // Combining with or clause
            baseSqlSelect.WithWhere(new Filter()
                    .WithExpression(new Expression("v.id", "another-uuid"))
                    .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or)));
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(baseSqlSelect);
            Assert.That(3, Is.EqualTo(results.Count));
            Assert.That(canvasGreen1, Is.EqualTo(results[0]));
            Assert.That(canvasRed, Is.EqualTo(results[1]));
            Assert.That(canvasGreen2, Is.EqualTo(results[2]));

            CleanDB();
        }

        [Test]
        public void TestNestedWhereWithOr()
        {
            CleanDB();
            InsertThreeCanvases();

            Canvas canvasGreen1 = CreateCanvas("123", "green", 1);
            Canvas canvasRed = CreateCanvas("another-uuid", "red", 2);
            Canvas canvasGreen2 = CreateCanvas("greencanvas", "green", 3);

            SqlSelect baseSqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithField(new Field("v.ordering"));
            // Combining two filters with or clause
            Filter filter1 = new Filter()
                    .WithExpression(new Expression("v.id", "another-uuid"))
                    .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or));
            baseSqlSelect.WithWhere(new Filter().WithFilter(filter1).WithFilter(filter1));
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(baseSqlSelect);
            Assert.That(3, Is.EqualTo(results.Count));
            Assert.That(canvasGreen1, Is.EqualTo(results[0]));
            Assert.That(canvasRed, Is.EqualTo(results[1]));
            Assert.That(canvasGreen2, Is.EqualTo(results[2]));

            CleanDB();
        }

        [Test]
        public void TestNestedWhereWithAnd()
        {
            CleanDB();
            InsertThreeCanvases();

            Canvas canvasGreen1 = CreateCanvas("123", "green", 10);
            Canvas canvasGreen2 = CreateCanvas("greencanvas", "green", 111);
            Canvas canvasRed = CreateCanvas("another-uuid", "red", 12);

            SqlSelect baseSqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"));
            // Combining two filters with or clause
            Filter filter1 = new Filter()
                    .WithExpression(new Expression("v.id", "another-uuid"))
                    .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or));
            // Combining two filters with and clause
            Filter filter2 = new Filter()
                    .WithExpression(new Expression("v.id", "another-uuid"))
                    .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.And));
            baseSqlSelect.WithWhere(new Filter().WithFilter(filter1).WithFilter(filter2));
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(baseSqlSelect);
            Assert.That(0, Is.EqualTo(results.Count));

            CleanDB();
        }

        // Combining one expression with two nested filters, one with a sub-nesting
        [Test]
        public void TestNestedWhereWithSubNesting()
        {
            CleanDB();
            InsertThreeCanvases();

            Canvas canvasGreen1 = CreateCanvas("123", "green", 1);
            Canvas canvasRed = CreateCanvas("another-uuid", "red", 2);
            Canvas canvasGreen2 = CreateCanvas("greencanvas", "green", 3);

            SqlSelect baseSqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithField(new Field("v.ordering"));
            Filter filter1 = new Filter()
                    .WithExpression(new Expression("v.id", "another-uuid"))
                    .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or));
            baseSqlSelect.WithWhere(new Filter()
                .WithFilter(filter1)
                .WithFilter(new Filter().WithFilter(filter1))
                .WithExpression(new Expression("v.id", "another-uuid")));
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(baseSqlSelect);
            Assert.That(1, Is.EqualTo(results.Count));
            Assert.That(canvasRed, Is.EqualTo(results[0]));

            CleanDB();
        }

        [Test]
        public void TestGroupByAndHaving()
        {
            CleanDB();
            BatchWrite(1000);

            SqlSelect sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("substr(id, 0, 7)", "uuidgroup", true))
                .WithField(new Field("count(color)", "numCanvases", true))
                .WithGroupBy(new GroupBy("uuidgroup"));
            IList<object> results = sqlFacade.GetResults<object>(sqlSelect);
            Assert.That(10, Is.EqualTo(results.Count));

            // All uuids starting with "uuid-1
            sqlSelect.WithHaving(new Filter().WithExpression(new Expression("count(color) = 1", null).WithIsRaw()));
            results = sqlFacade.GetResults<object>(sqlSelect);
            Assert.That(1, Is.EqualTo(results.Count));

            // All uuids not starting with "uuid-0" or where count(color) = 1 => should return all 10 rows.
            sqlSelect.WithHaving(new Filter().WithExpression(new Expression("substr(id, 0, 7) != 'uuid-0'", null).WithIsRaw())
                                                 .WithExpression(new Expression("count(color) = 1", null).WithIsRaw().WithLogicalRelation(LogicalRelation.Or)));
            results = sqlFacade.GetResults<object>(sqlSelect);
            Assert.That(10, Is.EqualTo(results.Count));

            // Serialization and Deserialization
            Assert.That(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()), Is.True);

            CleanDB();
        }

        [Test]
        public void TestJoins()
        {
            CleanDB();
            InsertThreeCanvases();

            // Insert extended canvas attributes in canvas-metdata table
            SqlInsert sqlInsert = new SqlInsert("canvas-metdata")
                .WithField(new Field("id", "another-uuid"))
                .WithField(new Field("extra_data", "Some extra data"));
            int rowsChanged = (int)sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.That(1, Is.EqualTo(rowsChanged));

            // Get All Extended Canvases
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithField(new Field("m.extra_data", "ExtraData"));

            // Inner join
            sqlSelect.Joins = null;
            Join innerJoin = new Join(new Table("canvas-metdata", "m"), new Expression("m.id", "v.id"), JoinType.InnerJoin);
            sqlSelect.WithJoin(innerJoin);
            Assert.That(1, Is.EqualTo(sqlFacade.GetResults<CanvasExtended>(sqlSelect).Count));

            // Left join
            sqlSelect.Joins = null;
            Join leftJoin = new Join(new Table("canvas-metdata", "m"), new Expression("m.id", "v.id"), JoinType.LeftJoin);
            sqlSelect.WithJoin(leftJoin);
            Assert.That(3, Is.EqualTo(sqlFacade.GetResults<CanvasExtended>(sqlSelect).Count));

            // ComplexJoin
            sqlSelect.Joins = null;
            Join complexJoin = new Join(new Table("canvas-metdata", "m"), new Expression("m.id", "v.id"), JoinType.LeftJoin)
                .WithJoinExpression(new Expression("m.extra_data", "Some extra data"));
            sqlSelect.WithJoin(complexJoin);
            Assert.That(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()), Is.True);
            IList<CanvasExtended> canvasExtendedList = sqlFacade.GetResults<CanvasExtended>(sqlSelect);
            List<CanvasExtended> expectedCanvasExtendedList = new List<CanvasExtended>();
            expectedCanvasExtendedList.Add(CreateCanvasExtended("123", "green"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("another-uuid", "red", "Some extra data"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("greencanvas", "green"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("cloned-uuid", "red"));
            int index = 0;
            foreach (CanvasExtended canvas in canvasExtendedList)
            {
                Assert.That(expectedCanvasExtendedList[index].ToString(), Is.EqualTo(canvas.ToString()));
                index++;
            }
            CleanDB();
        }

        // Error cases

        [Test]
        public void TestSingleSelectWithManyResults()
        {
            CleanDB();
            BatchWrite(2);
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"));
            Assert.Throws<ArgumentException>(() => sqlFacade.GetSingleResult<Canvas>(sqlSelect));

            CleanDB();
        }

        [Test]
        public void TestBadJson()
        {
            String badJson = "{\"SqlType\":\"Wrong Type\"}";
            Assert.Throws<ArgumentException>(() => sqlFacade.DeserializeFromJson(badJson));
        }

        [Test]
        public void TestExceptionSingleResultSql()
        {
            SqlSelect sqlSelect = new SqlSelect(new Table("badTable"))
                .WithField(new Field("id"))
                .WithField(new Field("color"));
            Assert.Throws<ArgumentException>(() => sqlFacade.GetSingleResult<Canvas>(sqlSelect));
        }

        [Test]
        public void TestExceptionResultsSql()
        {
            SqlSelect sqlSelect = new SqlSelect(new Table("badTable"))
                .WithField(new Field("id"))
                .WithField(new Field("color"));
            Assert.Throws<ArgumentException>(() => sqlFacade.GetResults<Canvas>(sqlSelect));
        }

        // Utility Functions

        private Canvas CreateCanvas(string uuid, string color, int ordering)
        {
            Canvas canvas = new() {
                Id = uuid,
                Color = color,
                Ordering = ordering
            };
            return canvas;
        }
        private CanvasExtended CreateCanvasExtended(string uuid, string color, string extraData = null)
        {
            CanvasExtended canvasExtended = new() {
                Id = uuid,
                Color = color,
                ExtraData = extraData
            };
            return canvasExtended;
        }

        private static void CleanDB()
        {
            // Delete Extended canvas data
            SqlDelete sqlDelete = new SqlDelete("canvas-metdata");
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);

            // Delete canvas data
            sqlDelete = new SqlDelete("canvas");
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);
        }

        private static Canvas SelectFromCanvasTable(string uuid)
        {
            SqlSelect sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithField(new Field("ordering"))
                .WithWhere(new Filter().WithExpression(new Expression("id", uuid)));
            return sqlFacade.GetSingleResult<Canvas>(sqlSelect);
        }

        private static IList<int> BatchWrite(int numCanvases)
        {
            List<ISqlWrite> sqlInserts = new List<ISqlWrite>();
            for (int index = 0; index < numCanvases; index++)
            {
                SqlInsert sqlInsert = new SqlInsert("canvas")
                    .WithField(new Field("id", $"uuid-{index.ToString()}"))
                    .WithField(new Field("color", $"color-{index.ToString()}"))
                    .WithField(new Field("ordering", index + 31000));
                sqlInserts.Add(sqlInsert);
            }
            return sqlFacade.ExecuteMultiSqlWrite(sqlInserts);
        }

        private static IList<int> InsertThreeCanvases()
        {
            List<ISqlWrite> sqlInserts = new List<ISqlWrite>();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "123"))
                .WithField(new Field("color", "green"))
                .WithField(new Field("ordering", "1"));
            sqlInserts.Add(sqlInsert);
            sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "another-uuid"))
                .WithField(new Field("color", "red"))
                .WithField(new Field("ordering", "2"));
            sqlInserts.Add(sqlInsert);
            sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "greencanvas"))
                .WithField(new Field("color", "green"))
                .WithField(new Field("ordering", "3"));
            sqlInserts.Add(sqlInsert);
            return sqlFacade.ExecuteMultiSqlWrite(sqlInserts);
        }

        private static Filter GetBaseFilter(bool isAnd, bool isFirst)
        {
            return isFirst ? new Filter() : new Filter().WithExpression(new Expression(isAnd ? "1=1" : "1=2", null).WithIsRaw());
        }
    }
}
