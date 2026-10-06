// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System.Collections;
    using System.Linq;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    /// <summary>
    /// Feeds ambient <see cref="System.Transactions.TransactionScope"/> live tests one fixture
    /// per selected enlisting engine. Uses <c>SQLFACADE_LIVE_ENGINES</c> but never SQLite
    /// (Microsoft.Data.Sqlite does not enlist). When the env var is unset, yields nothing.
    /// </summary>
    public static class LiveAmbientTransactionFixtureSource
    {
        public static IEnumerable Engines()
        {
            foreach (DbType dbType in LiveEngineSelection.Resolve().Where(e => e != DbType.SQLITE))
            {
                yield return new TestFixtureData(dbType)
                    .SetArgDisplayNames(dbType.ToString());
            }
        }
    }
}
