// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql.Test.Live
{
    using System.Collections;
    using System.Collections.Generic;
    using Beztek.Facade.Sql;
    using NUnit.Framework;

    /// <summary>
    /// Feeds <see cref="LiveEngineTests"/> one fixture per selected engine.
    /// When <c>SQLFACADE_LIVE_ENGINES</c> is unset, yields nothing (no live tests discovered).
    /// </summary>
    public static class LiveEngineFixtureSource
    {
        public static IEnumerable Engines()
        {
            foreach (DbType dbType in LiveEngineSelection.Resolve())
            {
                yield return new TestFixtureData(dbType)
                    .SetArgDisplayNames(dbType.ToString());
            }
        }
    }
}
