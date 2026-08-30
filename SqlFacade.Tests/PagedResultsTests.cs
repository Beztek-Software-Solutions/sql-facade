// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test
{
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    [TestFixture]
    public class PagedResultsTests
    {
        [Test]
        public void PagedResultsWithTotal_ComputesTotalPages()
        {
            var page = new PagedResultsWithTotal<string>(2, 10, new[] { "a", "b" }, totalResults: 25);
            Assert.That(page.PageNum, Is.EqualTo(2));
            Assert.That(page.PageSize, Is.EqualTo(10));
            Assert.That(page.TotalResults, Is.EqualTo(25));
            Assert.That(page.TotalPages, Is.EqualTo(3));
            Assert.That(page.PagedList, Has.Count.EqualTo(2));
        }

        [Test]
        public void PagedResults_StoresPageMetadata()
        {
            var page = new PagedResults<int>(1, 5, new[] { 1, 2, 3 });
            Assert.That(page.PageNum, Is.EqualTo(1));
            Assert.That(page.PageSize, Is.EqualTo(5));
            Assert.That(page.PagedList, Has.Count.EqualTo(3));
        }

        [Test]
        public void SqlInsert_ToString_RoundTrips()
        {
            ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:"));
            var insert = new SqlInsert("canvas")
                .WithField(new Field("id", "x"))
                .WithField(new Field("color", "green"));
            string json = insert.ToString();
            var restored = (SqlInsert)sql.DeserializeFromJson(json);
            Assert.That(restored.Table, Is.EqualTo("canvas"));
            Assert.That(restored.Fields, Has.Count.EqualTo(2));
        }

        [Test]
        public void SqlUpdate_ToString_RoundTrips()
        {
            ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:"));
            var update = new SqlUpdate("canvas")
                .WithField(new Field("color", "blue"))
                .WithFilter(new Expression("id", "1"));
            var restored = (SqlUpdate)sql.DeserializeFromJson(update.ToString());
            Assert.That(restored.Table, Is.EqualTo("canvas"));
            Assert.That(restored.Filters, Has.Count.EqualTo(1));
        }

        [Test]
        public void SqlDelete_ToString_RoundTrips()
        {
            ISqlFacade sql = SqlFacadeFactory.GetSqlFacade(
                new SqlFacadeConfig(Beztek.Facade.Sql.DbType.SQLITE, "Data Source=:memory:"));
            var delete = new SqlDelete("canvas").WithFilter(new Expression("id", "1"));
            var restored = (SqlDelete)sql.DeserializeFromJson(delete.ToString());
            Assert.That(restored.Table, Is.EqualTo("canvas"));
            Assert.That(restored.Filters, Has.Count.EqualTo(1));
        }
    }
}
