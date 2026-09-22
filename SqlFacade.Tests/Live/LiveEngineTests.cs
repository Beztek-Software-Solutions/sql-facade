// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    /// <summary>
    /// Cross-engine live suite. Discovered only when <c>SQLFACADE_LIVE_ENGINES</c> is set.
    /// <para>
    /// One engine: <c>SQLFACADE_LIVE_ENGINES=postgres</c><br/>
    /// All engines: <c>SQLFACADE_LIVE_ENGINES=all</c>
    /// </para>
    /// </summary>
    [TestFixtureSource(typeof(LiveEngineFixtureSource), nameof(LiveEngineFixtureSource.Engines))]
    [Category("Live")]
    public class LiveEngineTests
    {
        private static readonly Guid IdAlpha = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid IdBeta = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid IdGamma = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        private readonly DbType _dbType;
        private LiveEngineHost _host;

        public LiveEngineTests(DbType dbType)
        {
            _dbType = dbType;
        }

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            try
            {
                _host = await LiveEngineHost.StartAsync(_dbType).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                Assert.Inconclusive(ex.Message);
            }
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            if (_host != null)
                await _host.DisposeAsync().ConfigureAwait(false);
        }

        [SetUp]
        public void SetUp()
        {
            Assume.That(_host, Is.Not.Null);
            LiveSchema.Reset(_host.Sql);
        }

        [Test]
        public void Crud_InsertUpdateDelete_RoundTrips()
        {
            ISqlFacade sql = _host.Sql;

            int inserted = sql.ExecuteSqlWrite(new SqlInsert("canvas")
                .WithField(new Field("id", "c1"))
                .WithField(new Field("color", "green"))
                .WithField(new Field("ordering", 10)));
            Assert.That(inserted, Is.EqualTo(1));

            LiveCanvasRow row = sql.GetSingleResult<LiveCanvasRow>(
                new SqlSelect("canvas")
                    .WithField(new Field("id", "Id"))
                    .WithField(new Field("color", "Color"))
                    .WithField(new Field("ordering", "Ordering"))
                    .WithWhere(new Filter().WithExpression(new Expression("id", "c1"))));
            Assert.That(row.Color, Is.EqualTo("green"));
            Assert.That(row.Ordering, Is.EqualTo(10));

            Assert.That(sql.ExecuteSqlWrite(new SqlUpdate("canvas")
                .WithField(new Field("color", "blue"))
                .WithFilter(new Expression("id", "c1"))), Is.EqualTo(1));

            row = sql.GetSingleResult<LiveCanvasRow>(
                new SqlSelect("canvas")
                    .WithField(new Field("id", "Id"))
                    .WithField(new Field("color", "Color"))
                    .WithField(new Field("ordering", "Ordering"))
                    .WithWhere(new Filter().WithExpression(new Expression("id", "c1"))));
            Assert.That(row.Color, Is.EqualTo("blue"));

            Assert.That(sql.ExecuteSqlWrite(new SqlDelete("canvas")
                .WithFilter(new Expression("id", "c1"))), Is.EqualTo(1));
            Assert.That(sql.GetTotalNumResults(new SqlSelect("canvas").WithField(new Field("id"))), Is.EqualTo(0));
        }

        [Test]
        public void Pagination_WithTotal_ReturnsExpectedPage()
        {
            ISqlFacade sql = _host.Sql;
            sql.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("canvas").WithField(new Field("id", "p1")).WithField(new Field("color", "a")).WithField(new Field("ordering", 1)),
                new SqlInsert("canvas").WithField(new Field("id", "p2")).WithField(new Field("color", "b")).WithField(new Field("ordering", 2)),
                new SqlInsert("canvas").WithField(new Field("id", "p3")).WithField(new Field("color", "c")).WithField(new Field("ordering", 3)),
            });

            var select = new SqlSelect("canvas")
                .WithField(new Field("id", "Id"))
                .WithField(new Field("color", "Color"))
                .WithField(new Field("ordering", "Ordering"))
                .WithSort(new Sort("ordering"));

            PagedResults<LiveCanvasRow> page = sql.GetPagedResults<LiveCanvasRow>(select, pageNum: 2, pageSize: 1, retrieveTotalNumResults: true);
            Assert.That(page.PagedList, Has.Count.EqualTo(1));
            Assert.That(page.PagedList[0].Id, Is.EqualTo("p2"));
            Assert.That(page, Is.InstanceOf<PagedResultsWithTotal<LiveCanvasRow>>());
            Assert.That(((PagedResultsWithTotal<LiveCanvasRow>)page).TotalResults, Is.EqualTo(3));
        }

        [Test]
        public void NestedList_WithGrandchildren_MapsTypedLists()
        {
            ISqlFacade sql = _host.Sql;
            sql.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("canvas").WithField(new Field("id", "c-green")).WithField(new Field("color", "green")).WithField(new Field("ordering", 1)),
                new SqlInsert("canvas").WithField(new Field("id", "c-blue")).WithField(new Field("color", "blue")).WithField(new Field("ordering", 2)),
                new SqlInsert("canvas_stroke").WithField(new Field("id", "s2")).WithField(new Field("canvas_id", "c-green"))
                    .WithField(new Field("label", "second")).WithField(new Field("sort_ord", 2)),
                new SqlInsert("canvas_stroke").WithField(new Field("id", "s1")).WithField(new Field("canvas_id", "c-green"))
                    .WithField(new Field("label", "first")).WithField(new Field("sort_ord", 1)),
                new SqlInsert("stroke_tag").WithField(new Field("id", "t1")).WithField(new Field("stroke_id", "s1")).WithField(new Field("tag", "alpha")),
                new SqlInsert("stroke_tag").WithField(new Field("id", "t2")).WithField(new Field("stroke_id", "s1")).WithField(new Field("tag", "beta")),
            });

            NestedList strokes = new NestedList<LiveStrokeDto>("Strokes",
                new SqlSelect(new Table("canvas_stroke", "s"))
                    .WithField(new Field("s.id", "Id"))
                    .WithField(new Field("s.label", "Label"))
                    .WithField(new Field("s.sort_ord", "SortOrd"))
                    .WithNestedList(new NestedList<LiveTagDto>("Tags",
                        new SqlSelect(new Table("stroke_tag", "tg"))
                            .WithField(new Field("tg.id", "Id"))
                            .WithField(new Field("tg.tag", "Tag")),
                        new Expression("tg.stroke_id", "s.id")))
                    .WithSort(new Sort("s.sort_ord")),
                new Expression("s.canvas_id", "c.id"));

            var select = new SqlSelect(new Table("canvas", "c"))
                .WithField(new Field("c.id", "Id"))
                .WithField(new Field("c.color", "Color"))
                .WithNestedList(strokes)
                .WithWhere(new Filter().WithExpression(new Expression("c.id", "c-green")));

            LiveCanvasWithStrokes row = sql.GetSingleResult<LiveCanvasWithStrokes>(select);
            Assert.That(row.Strokes, Has.Count.EqualTo(2));
            Assert.That(row.Strokes[0].Id, Is.EqualTo("s1"));
            Assert.That(row.Strokes[0].Tags.Select(t => t.Tag).ToList(), Is.EquivalentTo(new[] { "alpha", "beta" }));
            Assert.That(row.Strokes[1].Tags, Is.Null.Or.Empty);
        }

        [Test]
        public void GuidInList_MatchesSeededRows()
        {
            ISqlFacade sql = _host.Sql;
            sql.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("entity")
                    .WithField(new Field("id", LiveSchema.GuidInsertValue(_dbType, IdAlpha)))
                    .WithField(new Field("name", "alpha")),
                new SqlInsert("entity")
                    .WithField(new Field("id", LiveSchema.GuidInsertValue(_dbType, IdBeta)))
                    .WithField(new Field("name", "beta")),
                new SqlInsert("entity")
                    .WithField(new Field("id", LiveSchema.GuidInsertValue(_dbType, IdGamma)))
                    .WithField(new Field("name", "gamma")),
            });

            var select = new SqlSelect("entity")
                .WithField(new Field("name", "Name"))
                .WithWhere(new Filter().WithExpression(
                    new Expression("id", new[] { IdAlpha, IdGamma }).WithRelation(Relation.In)))
                .WithSort(new Sort("name"));

            IList<LiveEntityRow> rows = sql.GetResults<LiveEntityRow>(select);
            Assert.That(rows.Select(r => r.Name).ToList(), Is.EqualTo(new[] { "alpha", "gamma" }));
        }

        [Test]
        public void MultiWrite_RunsInSingleTransaction()
        {
            ISqlFacade sql = _host.Sql;
            IList<int> counts = sql.ExecuteMultiSqlWrite(new List<ISqlWrite>
            {
                new SqlInsert("canvas").WithField(new Field("id", "m1")).WithField(new Field("color", "red")).WithField(new Field("ordering", 1)),
                new SqlInsert("canvas").WithField(new Field("id", "m2")).WithField(new Field("color", "red")).WithField(new Field("ordering", 2)),
            });
            Assert.That(counts, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(sql.GetTotalNumResults(new SqlSelect("canvas").WithField(new Field("id"))), Is.EqualTo(2));
        }
    }
}
