// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System.Collections.Generic;

    public class PagedResults<T>
    {
        public int PageNum { get; }
        public int PageSize { get; }
        public IList<T> PagedList { get; }

        public PagedResults(int pageNum, int pageSize, IList<T> pagedList)
        {
            PageNum = pageNum;
            PageSize = pageSize;
            PagedList = pagedList;
        }
    }
}