using Replane;

namespace UnitTesting.Services;

/// <summary>
/// Example service that uses Replane for feature flags and configuration.
/// This demonstrates how to structure code for testability by depending on IReplaneClient.
/// </summary>
public class PricingService
{
    private readonly IReplaneClient _config;

    public PricingService(IReplaneClient config)
    {
        _config = config;
    }

    public decimal CalculateDiscount(string userId, string plan, decimal orderTotal)
    {
        var context = new ReplaneContext
        {
            ["user_id"] = userId,
            ["plan"] = plan,
            ["order_total"] = (double)orderTotal
        };

        // Check if discounts are enabled
        var discountsEnabled = _config.Get<bool>("discounts-enabled", context, defaultValue: true);
        if (!discountsEnabled)
        {
            return 0;
        }

        // Get base discount percentage
        var baseDiscount = _config.Get<double>("base-discount-percent", context, defaultValue: 0);

        // Check for premium bonus
        var premiumBonus = _config.Get<double>("premium-discount-bonus", context, defaultValue: 0);

        // Check for large order bonus
        var largeOrderThreshold = _config.Get<decimal>("large-order-threshold", context, defaultValue: 500m);
        var largeOrderBonus = orderTotal >= largeOrderThreshold
            ? _config.Get<double>("large-order-bonus-percent", context, defaultValue: 0)
            : 0;

        var totalDiscountPercent = baseDiscount + premiumBonus + largeOrderBonus;
        var maxDiscount = _config.Get<double>("max-discount-percent", context, defaultValue: 50);

        return (decimal)Math.Min(totalDiscountPercent, maxDiscount) / 100m * orderTotal;
    }

    public bool IsFeatureEnabled(string featureName, string userId)
    {
        var context = new ReplaneContext { ["user_id"] = userId };
        return _config.Get<bool>(featureName, context, defaultValue: false);
    }
}
