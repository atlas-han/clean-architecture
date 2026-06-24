using System;
using System.Collections.Generic;

namespace CleanArchitecture.Application.Common.Models
{
    public record PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
        public int Page { get; init; }
        public int PageSize { get; init; }
        public int TotalCount { get; init; }
        public int TotalPages { get; init; }
        public bool HasPrevious { get; init; }
        public bool HasNext { get; init; }

        public static PagedResult<T> Create(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
        {
            var totalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            return new PagedResult<T>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPrevious = page > 1,
                HasNext = page < totalPages
            };
        }
    }

    public static class PageRequest
    {
        public const int MaxPageSize = 100;

        public static (int Page, int PageSize) Normalize(int page, int pageSize)
        {
            var normalizedPage = Math.Max(1, page);
            var normalizedSize = Math.Clamp(pageSize, 1, MaxPageSize);
            return (normalizedPage, normalizedSize);
        }
    }
}
