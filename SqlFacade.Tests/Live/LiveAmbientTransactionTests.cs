// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Transactions;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    /// <summary>
    /// Live ambient <see cref="TransactionScope"/> tests against enlisting SQL engines
    /// (SQLite excluded). Discovered when <c>SQLFACADE_LIVE_ENGINES</c> is set to an
    /// enlisting engine or <c>all</c>.
    /// <para>
    /// Example: <c>SQLFACADE_LIVE_ENGINES=postgres</c> or <c>SQLFACADE_LIVE_ENGINES=all</c>
    /// </para>
    /// </summary>
    [TestFixtureSource(typeof(LiveAmbientTransactionFixtureSource), nameof(LiveAmbientTransactionFixtureSource.Engines))]
    [Category("Live")]
    public class LiveAmbientTransactionTests
    {
        private readonly DbType _dbType;
        private LiveEngineHost _host;

        public LiveAmbientTransactionTests(DbType dbType)
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
        public void Abort_RollsBackWrites()
        {
            ISqlFacade sql = _host.Sql;

            using (NewReadCommittedScope())
            {
                sql.ExecuteSqlWrite(new SqlInsert("canvas")
                    .WithField(new Field("id", "tx-abort"))
                    .WithField(new Field("color", "red"))
                    .WithField(new Field("ordering", 1)));

                Assert.That(
                    sql.GetTotalNumResults(new SqlSelect("canvas").WithField(new Field("id"))),
                    Is.EqualTo(1),
                    "Row should be visible inside the open ambient transaction.");
            }

            Assert.That(
                sql.GetTotalNumResults(new SqlSelect("canvas").WithField(new Field("id"))),
                Is.EqualTo(0),
                "Writes should roll back when the ambient scope is not completed.");
        }

        [Test]
        public void Complete_CommitsWrites()
        {
            ISqlFacade sql = _host.Sql;

            using (TransactionScope scope = NewReadCommittedScope())
            {
                sql.ExecuteSqlWrite(new SqlInsert("canvas")
                    .WithField(new Field("id", "tx-commit"))
                    .WithField(new Field("color", "green"))
                    .WithField(new Field("ordering", 1)));
                scope.Complete();
            }

            Assert.That(
                sql.GetTotalNumResults(new SqlSelect("canvas").WithField(new Field("id"))),
                Is.EqualTo(1));

            LiveCanvasRow row = sql.GetSingleResult<LiveCanvasRow>(
                new SqlSelect("canvas")
                    .WithField(new Field("id", "Id"))
                    .WithField(new Field("color", "Color"))
                    .WithField(new Field("ordering", "Ordering"))
                    .WithWhere(new Filter().WithExpression(new Expression("id", "tx-commit"))));
            Assert.That(row.Color, Is.EqualTo("green"));
        }

        [Test]
        public void Abort_RollsBackMultipleFacadeCallsTogether()
        {
            ISqlFacade sql = _host.Sql;

            using (NewReadCommittedScope())
            {
                sql.ExecuteSqlWrite(new SqlInsert("canvas")
                    .WithField(new Field("id", "tx-a"))
                    .WithField(new Field("color", "a"))
                    .WithField(new Field("ordering", 1)));
                sql.ExecuteSqlWrite(new SqlInsert("canvas")
                    .WithField(new Field("id", "tx-b"))
                    .WithField(new Field("color", "b"))
                    .WithField(new Field("ordering", 2)));
            }

            Assert.That(
                sql.GetTotalNumResults(new SqlSelect("canvas").WithField(new Field("id"))),
                Is.EqualTo(0));
        }

        [Test]
        public void Complete_CommitsMultipleFacadeCallsTogether()
        {
            ISqlFacade sql = _host.Sql;

            using (TransactionScope scope = NewReadCommittedScope())
            {
                sql.ExecuteMultiSqlWrite(new List<ISqlWrite>
                {
                    new SqlInsert("canvas").WithField(new Field("id", "tx-m1")).WithField(new Field("color", "x")).WithField(new Field("ordering", 1)),
                    new SqlInsert("canvas").WithField(new Field("id", "tx-m2")).WithField(new Field("color", "y")).WithField(new Field("ordering", 2)),
                });
                scope.Complete();
            }

            Assert.That(
                sql.GetTotalNumResults(new SqlSelect("canvas").WithField(new Field("id"))),
                Is.EqualTo(2));
        }

        [Test]
        public void ExternalFailureBeforeComplete_RollsBackWrites()
        {
            ISqlFacade sql = _host.Sql;

            try
            {
                using TransactionScope scope = NewReadCommittedScope();
                sql.ExecuteSqlWrite(new SqlInsert("canvas")
                    .WithField(new Field("id", "tx-ext"))
                    .WithField(new Field("color", "z"))
                    .WithField(new Field("ordering", 1)));
                throw new InvalidOperationException("simulated external failure");
            }
            catch (InvalidOperationException)
            {
                // expected
            }

            Assert.That(
                sql.GetTotalNumResults(new SqlSelect("canvas").WithField(new Field("id"))),
                Is.EqualTo(0));
        }

        [Test]
        public void DefaultSerializableScope_ThrowsOnJoin()
        {
            ISqlFacade sql = _host.Sql;

            Exception ex = Assert.Catch(() =>
            {
                using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
                sql.ExecuteSqlWrite(new SqlInsert("canvas")
                    .WithField(new Field("id", "tx-ser"))
                    .WithField(new Field("color", "bad"))
                    .WithField(new Field("ordering", 1)));
                scope.Complete();
            });

            Assert.That(ex, Is.Not.Null);
            Assert.That(
                ex!.Message + ex,
                Does.Contain("transaction").IgnoreCase.Or.Contain("isolation").IgnoreCase);
        }

        private static TransactionScope NewReadCommittedScope() =>
            new TransactionScope(
                TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
                TransactionScopeAsyncFlowOption.Enabled);
    }
}
