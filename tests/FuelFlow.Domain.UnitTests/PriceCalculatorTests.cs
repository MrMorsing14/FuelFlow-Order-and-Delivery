using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Pricing;
using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class PriceCalculatorTests
{
    private readonly PriceCalculator _calculator = TestData.Calculator();

    [Fact]
    public void Calculate_PrivateCustomer1000Litres_AddsVatAndNoFee()
    {
        var price = _calculator.Calculate(10m, 1000, CustomerType.Private);

        price.NetAmount.ShouldBe(10_000m);
        price.SmallOrderFee.ShouldBe(0m);
        price.DiscountAmount.ShouldBe(0m);
        price.TotalExVat.ShouldBe(10_000m);
        price.VatAmount.ShouldBe(2_500m);
        price.TotalIncVat.ShouldBe(12_500m);
        price.DisplayIncVat.ShouldBeTrue();
    }

    [Theory]
    [InlineData(500, 250)]
    [InlineData(999, 250)]
    [InlineData(1000, 0)]
    [InlineData(1001, 0)]
    public void Calculate_SmallOrderBoundary_AppliesFeeOnlyBelow1000Litres(int litres, int expectedFee)
    {
        var price = _calculator.Calculate(10m, litres, CustomerType.Private);

        price.SmallOrderFee.ShouldBe(expectedFee);
    }

    [Fact]
    public void Calculate_SmallOrder_FeeIsIncludedBeforeVat()
    {
        var price = _calculator.Calculate(10m, 500, CustomerType.Private);

        price.NetAmount.ShouldBe(5_000m);
        price.TotalExVat.ShouldBe(5_250m);
        price.VatAmount.ShouldBe(1_312.50m);
        price.TotalIncVat.ShouldBe(6_562.50m);
    }

    [Theory]
    [InlineData(4999, 0)]
    [InlineData(5000, 0.02)]
    [InlineData(5001, 0.02)]
    public void Calculate_BusinessDiscountBoundary_AppliesFrom5000Litres(int litres, double expectedRate)
    {
        var price = _calculator.Calculate(10m, litres, CustomerType.Business);

        price.DiscountRate.ShouldBe((decimal)expectedRate);
    }

    [Fact]
    public void Calculate_Business5000Litres_DiscountsProductThenAddsVat()
    {
        var price = _calculator.Calculate(10m, 5000, CustomerType.Business);

        price.NetAmount.ShouldBe(50_000m);
        price.DiscountAmount.ShouldBe(1_000m);
        price.TotalExVat.ShouldBe(49_000m);
        price.VatAmount.ShouldBe(12_250m);
        price.TotalIncVat.ShouldBe(61_250m);
        price.DisplayIncVat.ShouldBeFalse();
    }

    [Fact]
    public void Calculate_Private5000Litres_GetsNoDiscount()
    {
        var price = _calculator.Calculate(10m, 5000, CustomerType.Private);

        price.DiscountAmount.ShouldBe(0m);
    }

    [Fact]
    public void Calculate_NetAmountMidpoint_RoundsAwayFromZero()
    {
        // 1001 L * 0.005 = 5.005 -> 5.01 (banker's rounding would give 5.00)
        var price = _calculator.Calculate(0.005m, 1001, CustomerType.Private);

        price.NetAmount.ShouldBe(5.01m);
    }

    [Fact]
    public void Calculate_VatMidpoint_RoundsAwayFromZero()
    {
        // net 0.1001 -> 0.10; VAT 0.10 * 25 % = 0.025 -> 0.03 (banker's rounding would give 0.02)
        var price = _calculator.Calculate(0.0001m, 1001, CustomerType.Private);

        price.NetAmount.ShouldBe(0.10m);
        price.VatAmount.ShouldBe(0.03m);
        price.TotalIncVat.ShouldBe(0.13m);
    }

    [Fact]
    public void Calculate_CustomOptions_AreUsed()
    {
        var calculator = new PriceCalculator(new PricingOptions { VatRate = 0.10m, SmallOrderThresholdLitres = 200 });

        var price = calculator.Calculate(10m, 500, CustomerType.Private);

        price.SmallOrderFee.ShouldBe(0m);
        price.VatAmount.ShouldBe(500m);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1000, 0)]
    [InlineData(1000, -5)]
    public void Calculate_NonPositiveInput_Throws(int litres, int unitPrice)
    {
        Should.Throw<Domain.Common.DomainValidationException>(
            () => _calculator.Calculate(unitPrice, litres, CustomerType.Private));
    }

    [Fact]
    public void Recalculate_BusinessDiscountOrderDeliveredBelowThreshold_KeepsOrderTimeDiscount()
    {
        var snapshot = _calculator.Calculate(10m, 5000, CustomerType.Business);

        var final = PriceCalculator.Recalculate(snapshot, 4600);

        final.NetAmount.ShouldBe(46_000m);
        final.DiscountAmount.ShouldBe(920m);
        final.TotalExVat.ShouldBe(45_080m);
        final.VatAmount.ShouldBe(11_270m);
        final.TotalIncVat.ShouldBe(56_350m);
    }

    [Fact]
    public void Recalculate_SmallOrderDeliveredAbove1000_KeepsOrderTimeFee()
    {
        var snapshot = _calculator.Calculate(10m, 900, CustomerType.Private);

        var final = PriceCalculator.Recalculate(snapshot, 990);

        final.SmallOrderFee.ShouldBe(250m);
        final.TotalExVat.ShouldBe(10_150m);
        final.TotalIncVat.ShouldBe(12_687.50m);
    }

    [Fact]
    public void Recalculate_UsesSnapshotUnitPriceAndVatRate_NotCurrentOptions()
    {
        var snapshot = new PriceCalculator(new PricingOptions { VatRate = 0.20m }).Calculate(8m, 1000, CustomerType.Private);

        var final = PriceCalculator.Recalculate(snapshot, 1100);

        final.UnitPriceExVat.ShouldBe(8m);
        final.VatRate.ShouldBe(0.20m);
        final.TotalIncVat.ShouldBe(10_560m);
    }

    [Theory]
    [InlineData(499, 5000, false)]
    [InlineData(500, 5000, true)]
    [InlineData(5000, 5000, true)]
    [InlineData(5001, 5000, false)]
    [InlineData(600, 600, true)]
    [InlineData(601, 600, false)]
    public void EnsureValidQuantity_MinimumAndTankCapacity_EnforcesBothLimits(int litres, int tank, bool valid)
    {
        var act = () => _calculator.EnsureValidQuantity(litres, tank);

        if (valid)
        {
            Should.NotThrow(act);
        }
        else
        {
            Should.Throw<Domain.Common.DomainValidationException>(act);
        }
    }
}
