using System.Linq.Expressions;
using TestWorkerService;

namespace TestWebApi.Extensions
{
    public static class IQueryableExtensions
    {
        public static IOrderedQueryable<TSource> OrderByExpression<TSource, TKey>(this IQueryable<TSource> source,
            Expression<Func<TSource, TKey>> keySelector, string sortDirection = "asc")
                => sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    ? source.OrderBy(keySelector)
                    : source.OrderByDescending(keySelector);

        public static IOrderedQueryable<Station> OrderbyStation(this IQueryable<Station> source, string sortKey, string sortDirection = "asc")
        {
            return sortKey.ToLowerInvariant() switch
            {
                "createdat" => source.OrderByExpression(x => x.CreatedAt, sortDirection),
                "name" => source.OrderByExpression(x => x.Name, sortDirection),
                _ => source.OrderByExpression(x => x.Name, sortDirection),
            };
        }
        public static IOrderedQueryable<SensorData> OrderbySensorData(this IQueryable<SensorData> source, string sortKey, string sortDirection = "asc")
        {
            return sortKey.ToLowerInvariant() switch
            {
                "timestamp" => source.OrderByExpression(x => x.TimeStamp, sortDirection),
                "wl" => source.OrderByExpression(x => x.WL, sortDirection),
                "batteryvoltage" => source.OrderByExpression(x => x.BatteryVoltage, sortDirection),
                _ => source.OrderByExpression(x => x.TimeStamp, sortDirection),
            };
        }
    }
}
