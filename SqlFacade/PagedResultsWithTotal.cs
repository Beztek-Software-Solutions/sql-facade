// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;

    public class PagedResultsWithTotal<T> : PagedResults<T>
    {
        public int TotalResults { get; }
        public int TotalPages { get; }

        public PagedResultsWithTotal(int pageNum, int pageSize, IList<T> pagedList, int totalResults) : base(pageNum, pageSize, pagedList)
        {
            TotalResults = totalResults;
            TotalPages = totalResults == 0 ? 0 : 1 + (totalResults - 1) / pageSize;
        }
    }
}