// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using System;
    using System.Collections.Generic;
    using System.Data;
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
            string stm1 = "CREATE TABLE canvas(id TEXT PRIMARY KEY, color TEXT)";
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
            Assert.AreEqual(expectedRawSql, sqlFacade.GetSql(sqlInsert, false));
            Assert.AreEqual(expectedTemplateSql, sqlFacade.GetSql(sqlInsert, true));
        }

        [Test]
        public void TestInsert()
        {
            CleanDB();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "123"))
                .WithField(new Field("color", "green"));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.AreEqual(1, rowsChanged);
            Assert.IsTrue(String.Equals(sqlInsert.ToString(), sqlFacade.DeserializeFromJson(sqlInsert.ToString()).ToString()));
            Assert.AreEqual(CreateCanvas("123", "green"), SelectFromCanvasTable("123"));
            CleanDB();
        }

        [Test]
        public void TestInsertWithSelect()
        {
            CleanDB();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "orig-uuid"))
                .WithField(new Field("color", "red"));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.AreEqual(1, rowsChanged);

            sqlInsert = new SqlInsert("canvas");
            SqlSelect sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("\'cloned-uuid\'", "id", true))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("id", "orig-uuid")));
            sqlInsert.WithQuery(sqlSelect);
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.AreEqual(1, rowsChanged);
            Assert.AreEqual(CreateCanvas("cloned-uuid", "red"), SelectFromCanvasTable("cloned-uuid"));
            CleanDB();
        }

        [Test]
        public void TestUpdate()
        {
            CleanDB();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "orig-uuid"))
                .WithField(new Field("color", "red"));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.AreEqual(1, rowsChanged);

            SqlUpdate sqlUpdate = new SqlUpdate("canvas")
                .WithField(new Field("color", "yellow"))
                .WithFilter(new Expression("color", "red"));
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlUpdate);
            Assert.AreEqual(1, rowsChanged);
            Assert.IsTrue(String.Equals(sqlUpdate.ToString(), sqlFacade.DeserializeFromJson(sqlUpdate.ToString()).ToString()));
            Assert.AreEqual(CreateCanvas("orig-uuid", "yellow"), SelectFromCanvasTable("orig-uuid"));
            CleanDB();
        }

        [Test]
        public void TestDelete()
        {
            CleanDB();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "orig-uuid"))
                .WithField(new Field("color", "red"));
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlInsert);
            Assert.AreEqual(1, rowsChanged);
            Assert.AreEqual(CreateCanvas("orig-uuid", "red"), SelectFromCanvasTable("orig-uuid"));

            SqlDelete sqlDelete = new SqlDelete("canvas")
                .WithFilter(new Expression("color", "red"));
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);
            Assert.AreEqual(1, rowsChanged);
            Assert.IsTrue(String.Equals(sqlDelete.ToString(), sqlFacade.DeserializeFromJson(sqlDelete.ToString()).ToString()));
            Assert.IsNull(SelectFromCanvasTable("orig-uuid"));
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
            Assert.IsTrue(String.Equals(subSelect.ToString(), sqlFacade.DeserializeFromJson(subSelect.ToString()).ToString()));
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
                Assert.AreEqual(expectedCanvasExtendedList[index], canvasExtended);
                index++;
            }

            // Common Table Expression with Raw SQL Query
            CommonTableExpression cte2 = new CommonTableExpression("select 'a' as col1", "c2");
            sqlSelect = new SqlSelect("c2").WithCommonTableExpression(cte2);
            Assert.AreEqual("a", sqlFacade.GetSingleResult<string>(sqlSelect));

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
            Assert.IsTrue(String.Equals(subSelect.ToString(), sqlFacade.DeserializeFromJson(subSelect.ToString()).ToString()));
            SqlSelect sqlSelect = new SqlSelect(new DerivedTable(subSelect, "v"));
            Assert.IsTrue(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()));
            IList<CanvasExtended> canvasExtendedList = sqlFacade.GetResults<CanvasExtended>(sqlSelect);
            List<CanvasExtended> expectedCanvasExtendedList = new List<CanvasExtended>();
            expectedCanvasExtendedList.Add(CreateCanvasExtended("123", "green", "Pseudo data from derived table"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("greencanvas", "green", "Pseudo data from derived table"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("cloned-uuid", "yellow", "Pseudo data from derived table"));
            int index = 0;
            foreach (CanvasExtended canvasExtended in expectedCanvasExtendedList)
            {
                Assert.AreEqual(expectedCanvasExtendedList[index], canvasExtended);
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
            Assert.AreEqual(numCanvasesToInsert, results.Count);
            foreach (Object result in results)
            {
                Assert.AreEqual(1, result);
            }

            SqlSelect sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithWhere(new Filter()
                    .WithExpression(new Expression("v.id", "uuid-211").WithRelation(Relation.GreaterThanOrEqualTo))
                    .WithExpression(new Expression("v.id", "uuid-910").WithRelation(Relation.GreaterThanOrEqualTo).WithLogicalRelation(LogicalRelation.AndNot)))
                .WithSort(new Sort("v.id"))
                .WithSort(new Sort("v.color", false));
            int pageNumber = 3;
            int pageSize = 30;
            PagedResultsWithTotal<Canvas> pagedResultsWithTotal = (PagedResultsWithTotal<Canvas>)sqlFacade.GetPagedResults<Canvas>(sqlSelect, pageNumber, pageSize, true);
            Assert.IsTrue(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()));
            Assert.AreEqual(pageNumber, pagedResultsWithTotal.PageNum);
            int totalCount = pagedResultsWithTotal.TotalResults;
            int totalPages = pagedResultsWithTotal.TotalPages;
            // Check total results
            int expectedTotalResults = sqlFacade.GetTotalNumResults(sqlSelect);
            Assert.AreEqual(expectedTotalResults, totalCount);
            // Check max results per page
            Assert.AreEqual(30, pagedResultsWithTotal.PagedList.Count);
            // Check some arbitrary result which validates the sorting and retrieval
            Assert.AreEqual(CreateCanvas("uuid-27", "color-27"), pagedResultsWithTotal.PagedList[4]);
            // Check the pagedResults for the last page
            int expectedNumInLastPage = totalCount - (totalPages - 1) * pagedResultsWithTotal.PageSize;
            PagedResults<Canvas> pagedResults = sqlFacade.GetPagedResults<Canvas>(sqlSelect, totalPages, pagedResultsWithTotal.PageSize);
            Assert.AreEqual(expectedNumInLastPage, pagedResults.PagedList.Count);

            CleanDB();
        }

        [Test]
        public void TestRelation()
        {
            CleanDB();
            BatchWrite(1000);

            SqlSelect sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithSort(new Sort("id"));

            Canvas canvas13 = CreateCanvas("uuid-13", "color-13");
            Canvas canvas127 = CreateCanvas("uuid-127", "color-127");
            Canvas canvas336 = CreateCanvas("uuid-336", "color-336");

            // No filters - all results
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.AreEqual(1000, results.Count);
            Assert.AreEqual(canvas127, results[32]);

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
                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(canvas127, results[0]);

                // Greater Than
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.GreaterThan)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(753, results.Count);
                Assert.AreEqual(canvas336, results[17]);

                // Lesser Than
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.GreaterThan)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(246, results.Count);
                Assert.AreEqual(canvas13, results[35]);

                // Greater Than or Equal To
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.GreaterThanOrEqualTo)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(754, results.Count);
                Assert.AreEqual(canvas336, results[18]);

                // Lesser Than or Equal To
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-32")
                                        .WithRelation(Relation.GreaterThanOrEqualTo)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(247, results.Count);
                Assert.AreEqual(canvas13, results[35]);

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
                    sqlSelect = (SqlSelect) sqlFacade.DeserializeFromJson(jsonQuery);
                    
                    results = sqlFacade.GetResults<Canvas>(sqlSelect);
                    Assert.AreEqual(2, results.Count);
                    Assert.AreEqual(canvas13, results[0]);
                    Assert.AreEqual(canvas336, results[1]);

                    // Not In List
                    sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                            .WithExpression(new Expression("id", new string[] { "uuid-13", "uuid-336" })
                                            .WithRelation(Relation.In)
                                            .WithLogicalRelation(negationRelation)));
                    results = sqlFacade.GetResults<Canvas>(sqlSelect);
                    Assert.AreEqual(998, results.Count);
                }

                // Null
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", null)
                                        .WithRelation(Relation.NullValue)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(0, results.Count);

                // Not Null
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", null)
                                        .WithRelation(Relation.NullValue)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(1000, results.Count);

                // True raw
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("1=1", null)
                                        .WithIsRaw()
                                        .WithRelation(Relation.TrueValue)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(1000, results.Count);

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
                    Assert.AreEqual(0, results.Count);
                    // If we get here the test failed, because an exception was not thrown
                    Assert.IsTrue(false);
                }
                catch (Exception e)
                {
                    if (e is ArgumentException)
                    {
                        // If we get here the test passed, because an ArgumentException was thrown
                        Assert.IsTrue(true);
                    }
                    else
                    {
                        Assert.IsTrue(false);
                    }
                }

                // Exists
                sqlSelect.WithWhere(new Filter().WithExpression(new Expression("id", "uuid-32")));
                results = sqlFacade.GetResults<Canvas>(new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlExists(sqlSelect)
                                                .WithLogicalRelation(logicalRelation))));
                Assert.AreEqual(1000, results.Count);

                // Not Exists
                sqlSelect.WithWhere(new Filter().WithExpression(new Expression("id", "uuid-32")));
                results = sqlFacade.GetResults<Canvas>(new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlExists(sqlSelect)
                                                .WithLogicalRelation(negationRelation))));
                Assert.AreEqual(0, results.Count);

                // Exists with no results
                sqlSelect.WithWhere(new Filter().WithExpression(new Expression("id", "non-existent")));
                results = sqlFacade.GetResults<Canvas>(new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlExists(sqlSelect)
                                                .WithLogicalRelation(logicalRelation))));
                Assert.AreEqual(0, results.Count);

                // In Subquery
                SqlSelect subQuery = new SqlSelect("canvas").WithField(new Field("id"))
                                        .WithWhere(new Filter().WithExpression(new Expression("id", "uuid-32")));
                results = sqlFacade.GetResults<Canvas>(new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlIn("id", subQuery)
                                                .WithLogicalRelation(logicalRelation))));
                Assert.AreEqual(1, results.Count);

                // Not In Subquery
                subQuery = new SqlSelect("canvas").WithField(new Field("id"))
                                        .WithWhere(new Filter().WithExpression(new Expression("id", "uuid-32")));
                SqlSelect currQuery = new SqlSelect("canvas")
                                        .WithWhere(GetBaseFilter(isAnd, isFirst)
                                                .WithExpression(new Expression()
                                                .WithSqlIn("id", subQuery)
                                                .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(currQuery);
                Assert.AreEqual(((!isAnd) && (!isFirst) ? 999 : 1000), results.Count);

                // Starts With
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-1")
                                        .WithRelation(Relation.StartsWith)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(111, results.Count);

                // Does not start With
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "uuid-1")
                                        .WithRelation(Relation.StartsWith)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(889, results.Count);

                // Ends With
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "1")
                                        .WithRelation(Relation.EndsWith)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(100, results.Count);

                // Does not end With
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "1")
                                        .WithRelation(Relation.EndsWith)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(900, results.Count);

                // Contains
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "d-1")
                                        .WithRelation(Relation.Contains)
                                        .WithLogicalRelation(logicalRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(111, results.Count);

                // Does not contain
                sqlSelect.WithWhere(GetBaseFilter(isAnd, isFirst)
                                        .WithExpression(new Expression("id", "d-1")
                                        .WithRelation(Relation.Contains)
                                        .WithLogicalRelation(negationRelation)));
                results = sqlFacade.GetResults<Canvas>(sqlSelect);
                Assert.AreEqual(889, results.Count);
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
            Assert.AreEqual(3, results.Count);

            // UnionAll
            sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                        .WithExpression(new Expression("id", "another-uuid")))
                        .WithCombine(new SqlCombine(sqlSelectAllGreen, SqlRelation.UnionAll));
            results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.AreEqual(3, results.Count);

            // Except
            sqlSelect = sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                        .WithExpression(new Expression("color", "green")))
                        .WithCombine(new SqlCombine(sqlSelectOneGreen, SqlRelation.Except));
            results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.AreEqual(1, results.Count);

            // Intersect
            sqlSelect = sqlSelect = new SqlSelect(new Table("canvas"))
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter()
                        .WithExpression(new Expression("color", "green")))
                        .WithCombine(new SqlCombine(sqlSelectOneGreen, SqlRelation.Intersect));
            results = sqlFacade.GetResults<Canvas>(sqlSelect);
            Assert.AreEqual(1, results.Count);

            // Serialization and Deserialization
            Assert.IsTrue(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()));

            CleanDB();
        }

        [Test]
        public void TestWhereWithOr()
        {
            CleanDB();
            InsertThreeCanvases();

            Canvas canvasGreen1 = CreateCanvas("123", "green");
            Canvas canvasGreen2 = CreateCanvas("greencanvas", "green");
            Canvas canvasRed = CreateCanvas("another-uuid", "red");

            SqlSelect baseSqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"));
            // Combining with or clause
            baseSqlSelect.WithWhere(new Filter()
                    .WithExpression(new Expression("v.id", "another-uuid"))
                    .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or)));
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(baseSqlSelect);
            Assert.AreEqual(3, results.Count);
            Assert.AreEqual(canvasGreen1, results[0]);
            Assert.AreEqual(canvasRed, results[1]);
            Assert.AreEqual(canvasGreen2, results[2]);

            CleanDB();
        }

        [Test]
        public void TestNestedWhereWithOr()
        {
            CleanDB();
            InsertThreeCanvases();

            Canvas canvasGreen1 = CreateCanvas("123", "green");
            Canvas canvasGreen2 = CreateCanvas("greencanvas", "green");
            Canvas canvasRed = CreateCanvas("another-uuid", "red");

            SqlSelect baseSqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"));
            // Combining two filters with or clause
            Filter filter1 = new Filter()
                    .WithExpression(new Expression("v.id", "another-uuid"))
                    .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or));
            baseSqlSelect.WithWhere(new Filter().WithFilter(filter1).WithFilter(filter1));
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(baseSqlSelect);
            Assert.AreEqual(3, results.Count);
            Assert.AreEqual(canvasGreen1, results[0]);
            Assert.AreEqual(canvasRed, results[1]);
            Assert.AreEqual(canvasGreen2, results[2]);

            CleanDB();
        }

        [Test]
        public void TestNestedWhereWithAnd()
        {
            CleanDB();
            InsertThreeCanvases();

            Canvas canvasGreen1 = CreateCanvas("123", "green");
            Canvas canvasGreen2 = CreateCanvas("greencanvas", "green");
            Canvas canvasRed = CreateCanvas("another-uuid", "red");

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
            Assert.AreEqual(0, results.Count);

            CleanDB();
        }

        // Combining one expression with two nested filters, one with a sub-nesting
        [Test]
        public void TestNestedWhereWithSubNesting()
        {
            CleanDB();
            InsertThreeCanvases();

            Canvas canvasGreen1 = CreateCanvas("123", "green");
            Canvas canvasGreen2 = CreateCanvas("greencanvas", "green");
            Canvas canvasRed = CreateCanvas("another-uuid", "red");

            SqlSelect baseSqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"));
            Filter filter1 = new Filter()
                    .WithExpression(new Expression("v.id", "another-uuid"))
                    .WithExpression(new Expression("v.color", "green").WithLogicalRelation(LogicalRelation.Or));
            baseSqlSelect.WithWhere(new Filter()
                .WithFilter(filter1)
                .WithFilter(new Filter().WithFilter(filter1))
                .WithExpression(new Expression("v.id", "another-uuid")));
            IList<Canvas> results = sqlFacade.GetResults<Canvas>(baseSqlSelect);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(canvasRed, results[0]);

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
            Assert.AreEqual(10, results.Count);

            // All uuids starting with "uuid-1
            sqlSelect.WithHaving(new Filter().WithExpression(new Expression("count(color) = 1", null).WithIsRaw()));
            results = sqlFacade.GetResults<object>(sqlSelect);
            Assert.AreEqual(1, results.Count);

            // All uuids not starting with "uuid-0" or where count(color) = 1 => should return all 10 rows.
            sqlSelect.WithHaving(new Filter().WithExpression(new Expression("substr(id, 0, 7) != 'uuid-0'", null).WithIsRaw())
                                                 .WithExpression(new Expression("count(color) = 1", null).WithIsRaw().WithLogicalRelation(LogicalRelation.Or)));
            results = sqlFacade.GetResults<object>(sqlSelect);
            Assert.AreEqual(10, results.Count);

            // Serialization and Deserialization
            Assert.IsTrue(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()));

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
            Assert.AreEqual(1, rowsChanged);

            // Get All Extended Canvases
            SqlSelect sqlSelect = new SqlSelect(new Table("canvas", "v"))
                .WithField(new Field("v.id"))
                .WithField(new Field("v.color"))
                .WithField(new Field("m.extra_data", "ExtraData"));

            // Inner join
            sqlSelect.Joins = null;
            Join innerJoin = new Join(new Table("canvas-metdata", "m"), new Expression("m.id", "v.id"), JoinType.InnerJoin);
            sqlSelect.WithJoin(innerJoin);
            Assert.AreEqual(1, sqlFacade.GetResults<CanvasExtended>(sqlSelect).Count);

            // Left join
            sqlSelect.Joins = null;
            Join leftJoin = new Join(new Table("canvas-metdata", "m"), new Expression("m.id", "v.id"), JoinType.LeftJoin);
            sqlSelect.WithJoin(leftJoin);
            Assert.AreEqual(3, sqlFacade.GetResults<CanvasExtended>(sqlSelect).Count);

            // ComplexJoin
            sqlSelect.Joins = null;
            Join complexJoin = new Join(new Table("canvas-metdata", "m"), new Expression("m.id", "v.id"), JoinType.LeftJoin)
                .WithJoinExpression(new Expression("m.extra_data", "Some extra data"));
            sqlSelect.WithJoin(complexJoin);
            Assert.IsTrue(String.Equals(sqlSelect.ToString(), sqlFacade.DeserializeFromJson(sqlSelect.ToString()).ToString()));
            IList<CanvasExtended> canvasExtendedList = sqlFacade.GetResults<CanvasExtended>(sqlSelect);
            List<CanvasExtended> expectedCanvasExtendedList = new List<CanvasExtended>();
            expectedCanvasExtendedList.Add(CreateCanvasExtended("123", "green"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("another-uuid", "red", "Some extra data"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("greencanvas", "green"));
            expectedCanvasExtendedList.Add(CreateCanvasExtended("cloned-uuid", "red"));
            int index = 0;
            foreach (CanvasExtended canvas in canvasExtendedList)
            {
                Assert.AreEqual(expectedCanvasExtendedList[index].ToString(), canvas.ToString());
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

        private Canvas CreateCanvas(string uuid, string color)
        {
            Canvas canvas = new Canvas();
            canvas.Id = uuid;
            canvas.Color = color;
            return canvas;
        }
        private CanvasExtended CreateCanvasExtended(string uuid, string color, string extraData = null)
        {
            CanvasExtended canvasExtended = new CanvasExtended();
            canvasExtended.Id = uuid;
            canvasExtended.Color = color;
            canvasExtended.ExtraData = extraData;
            return canvasExtended;
        }

        private void CleanDB()
        {
            // Delete Extended canvas data
            SqlDelete sqlDelete = new SqlDelete("canvas-metdata");
            int rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);

            // Delete canvas data
            sqlDelete = new SqlDelete("canvas");
            rowsChanged = sqlFacade.ExecuteSqlWrite(sqlDelete);
        }

        private Canvas SelectFromCanvasTable(string uuid)
        {
            SqlSelect sqlSelect = new SqlSelect("canvas")
                .WithField(new Field("id"))
                .WithField(new Field("color"))
                .WithWhere(new Filter().WithExpression(new Expression("id", uuid)));
            return sqlFacade.GetSingleResult<Canvas>(sqlSelect);
        }

        private IList<int> BatchWrite(int numCanvases)
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

        private IList<int> InsertThreeCanvases()
        {
            List<ISqlWrite> sqlInserts = new List<ISqlWrite>();
            SqlInsert sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "123"))
                .WithField(new Field("color", "green"));
            sqlInserts.Add(sqlInsert);
            sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "another-uuid"))
                .WithField(new Field("color", "red"));
            sqlInserts.Add(sqlInsert);
            sqlInsert = new SqlInsert("canvas")
                .WithField(new Field("id", "greencanvas"))
                .WithField(new Field("color", "green"));
            sqlInserts.Add(sqlInsert);
            return sqlFacade.ExecuteMultiSqlWrite(sqlInserts);
        }

        private Filter GetBaseFilter(bool isAnd, bool isFirst)
        {
            return isFirst ? new Filter() : new Filter().WithExpression(new Expression(isAnd ? "1=1" : "1=2", null).WithIsRaw());
        }
    }
}
