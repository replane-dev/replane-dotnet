using Replane;
using Replane.Testing;
using UnitTesting.Services;

namespace UnitTesting;

public class PricingServiceTests
{
    [Fact]
    public void CalculateDiscount_WhenDiscountsDisabled_ReturnsZero()
    {
        // Arrange
        using var config = TestClient.Create(new Dictionary<string, object?>
        {
            ["discounts-enabled"] = false
        });
        var service = new PricingService(config);

        // Act
        var discount = service.CalculateDiscount("user-1", "premium", 100m);

        // Assert
        Assert.Equal(0m, discount);
    }

    [Fact]
    public void CalculateDiscount_WithBaseDiscount_AppliesCorrectly()
    {
        // Arrange
        using var config = TestClient.Create(new Dictionary<string, object?>
        {
            ["discounts-enabled"] = true,
            ["base-discount-percent"] = 10.0
        });
        var service = new PricingService(config);

        // Act
        var discount = service.CalculateDiscount("user-1", "free", 100m);

        // Assert
        Assert.Equal(10m, discount); // 10% of 100
    }

    [Fact]
    public void CalculateDiscount_ForPremiumUser_IncludesPremiumBonus()
    {
        // Arrange
        using var config = TestClient.Create();

        // Set up base config
        config.Set("discounts-enabled", true);
        config.Set("base-discount-percent", 5.0);

        // Set up premium bonus with override
        config.SetConfigWithOverrides(
            "premium-discount-bonus",
            value: 0.0,
            overrides:
            [
                new OverrideData
                {
                    Name = "premium-users",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "in",
                            Property = "plan",
                            Expected = new List<object?> { "premium", "enterprise" }
                        }
                    ],
                    Value = 10.0
                }
            ]);

        var service = new PricingService(config);

        // Act
        var freeUserDiscount = service.CalculateDiscount("user-1", "free", 100m);
        var premiumUserDiscount = service.CalculateDiscount("user-2", "premium", 100m);

        // Assert
        Assert.Equal(5m, freeUserDiscount);   // 5% base only
        Assert.Equal(15m, premiumUserDiscount); // 5% base + 10% premium
    }

    [Fact]
    public void CalculateDiscount_ForLargeOrder_IncludesLargeOrderBonus()
    {
        // Arrange
        using var config = TestClient.Create(new Dictionary<string, object?>
        {
            ["discounts-enabled"] = true,
            ["base-discount-percent"] = 5.0,
            ["large-order-threshold"] = 500m,
            ["large-order-bonus-percent"] = 5.0
        });
        var service = new PricingService(config);

        // Act
        var smallOrderDiscount = service.CalculateDiscount("user-1", "free", 200m);
        var largeOrderDiscount = service.CalculateDiscount("user-1", "free", 600m);

        // Assert
        Assert.Equal(10m, smallOrderDiscount);  // 5% of 200 (no bonus)
        Assert.Equal(60m, largeOrderDiscount);  // 10% of 600 (5% base + 5% large order)
    }

    [Fact]
    public void CalculateDiscount_RespectsMaxDiscount()
    {
        // Arrange
        using var config = TestClient.Create(new Dictionary<string, object?>
        {
            ["discounts-enabled"] = true,
            ["base-discount-percent"] = 30.0,
            ["premium-discount-bonus"] = 30.0,
            ["max-discount-percent"] = 40.0
        });
        var service = new PricingService(config);

        // Act
        var discount = service.CalculateDiscount("user-1", "premium", 100m);

        // Assert
        // Total would be 60%, but capped at 40%
        Assert.Equal(40m, discount);
    }

    [Fact]
    public void IsFeatureEnabled_WithSegmentation_IsDeterministic()
    {
        // Arrange
        using var config = TestClient.Create();

        config.SetConfigWithOverrides(
            "new-checkout-flow",
            value: false,
            overrides:
            [
                new OverrideData
                {
                    Name = "gradual-rollout",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "segmentation",
                            Property = "user_id",
                            FromPercentage = 0,
                            ToPercentage = 50,
                            Seed = "checkout-rollout"
                        }
                    ],
                    Value = true
                }
            ]);

        var service = new PricingService(config);

        // Act - same user should always get same result
        var result1 = service.IsFeatureEnabled("new-checkout-flow", "user-123");
        var result2 = service.IsFeatureEnabled("new-checkout-flow", "user-123");
        var result3 = service.IsFeatureEnabled("new-checkout-flow", "user-123");

        // Assert - deterministic
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact]
    public void IsFeatureEnabled_WithSegmentation_DistributesAcrossUsers()
    {
        // Arrange
        using var config = TestClient.Create();

        config.SetConfigWithOverrides(
            "ab-test",
            value: false,
            overrides:
            [
                new OverrideData
                {
                    Name = "test-group",
                    Conditions =
                    [
                        new ConditionData
                        {
                            Operator = "segmentation",
                            Property = "user_id",
                            FromPercentage = 0,
                            ToPercentage = 50,
                            Seed = "ab-test-seed"
                        }
                    ],
                    Value = true
                }
            ]);

        var service = new PricingService(config);

        // Act - test many users
        var trueCount = 0;
        for (int i = 0; i < 100; i++)
        {
            if (service.IsFeatureEnabled("ab-test", $"user-{i}"))
            {
                trueCount++;
            }
        }

        // Assert - roughly 50% should be in test group (allow some variance)
        Assert.InRange(trueCount, 30, 70);
    }
}
