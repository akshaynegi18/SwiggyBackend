namespace OrderService.Services;

public static class CacheKeys
{
    public const string ORDER_PREFIX = "order:";
    public const string RECOMMENDATIONS_PREFIX = "recommendations:";
    public const string ORDER_TIMELINE_PREFIX = "timeline:";
    public const string USER_ORDERS_PREFIX = "user_orders:";

    public static string GetOrderKey(int orderId) => $"{ORDER_PREFIX}{orderId}";
    public static string GetRecommendationsKey(int userId) => $"{RECOMMENDATIONS_PREFIX}{userId}";
    public static string GetOrderTimelineKey(int orderId) => $"{ORDER_TIMELINE_PREFIX}{orderId}";
    public static string GetUserOrdersKey(int userId) => $"{USER_ORDERS_PREFIX}{userId}";
    public static string GetUserOrdersPattern(int userId) => $"{USER_ORDERS_PREFIX}{userId}:*";
}