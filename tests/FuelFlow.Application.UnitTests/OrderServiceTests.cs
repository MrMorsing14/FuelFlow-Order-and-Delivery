using FluentValidation;
using FuelFlow.Application.Auth;
using FuelFlow.Application.Common;
using FuelFlow.Application.Customers;
using FuelFlow.Application.Orders;
using FuelFlow.Application.Products;
using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Pricing;
using FuelFlow.Domain.Products;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace FuelFlow.Application.UnitTests;

public class OrderServiceTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IAddressRepository _addresses = Substitute.For<IAddressRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));
    private readonly Customer _customer = Customer.CreatePrivate("Anna Hansen", "+4512345678", "anna@example.com");
    private readonly DeliveryAddress _address;
    private readonly Product _product;
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _address = DeliveryAddress.Create(_customer.Id, "Vejen 1", "2100", "København Ø", 3000);
        _product = Product.Create(ProductType.Diesel, 10m, _time.GetUtcNow().AddDays(-1));
        _customers.GetAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        _addresses.GetAsync(_address.Id, _customer.Id, Arg.Any<CancellationToken>()).Returns(_address);
        _products.GetAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);

        _service = new OrderService(
            _orders, _customers, _addresses, _products, _unitOfWork, new PriceCalculator(new PricingOptions()), _time,
            new OrderRequestValidator(), new CancelOrderRequestValidator(), new PageRequestValidator());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OrderRequest Request(int litres = 1000) => new(_product.Id, _address.Id, litres);

    [Fact]
    public async Task PlaceAsync_ValidRequest_AddsPendingOrderWithPriceSnapshotAndSaves()
    {
        var dto = await _service.PlaceAsync(_customer.Id, Request(), Ct);

        dto.Status.ShouldBe(OrderStatus.Pending);
        dto.Price.TotalIncVat.ShouldBe(12_500m);
        dto.CreatedAt.ShouldBe(_time.GetUtcNow());
        _orders.Received(1).Add(Arg.Is<Order>(o => o.Id == dto.Id && o.CustomerId == _customer.Id));
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task QuoteAsync_ValidRequest_ReturnsPriceAndSavesNothing()
    {
        var price = await _service.QuoteAsync(_customer.Id, Request(500), Ct);

        price.SmallOrderFee.ShouldBe(250m);
        _orders.DidNotReceive().Add(Arg.Any<Order>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(499)]
    [InlineData(3001)]
    public async Task PlaceAsync_QuantityOutsideLimits_ThrowsAndSavesNothing(int litres)
    {
        await Should.ThrowAsync<DomainValidationException>(() => _service.PlaceAsync(_customer.Id, Request(litres), Ct));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceAsync_AddressNotOwnedByCustomer_ThrowsNotFound()
    {
        _addresses.GetAsync(_address.Id, _customer.Id, Arg.Any<CancellationToken>()).Returns((DeliveryAddress?)null);

        await Should.ThrowAsync<NotFoundException>(() => _service.PlaceAsync(_customer.Id, Request(), Ct));
    }

    [Fact]
    public async Task PlaceAsync_UnknownProduct_ThrowsNotFound()
    {
        await Should.ThrowAsync<NotFoundException>(
            () => _service.PlaceAsync(_customer.Id, new OrderRequest(Guid.NewGuid(), _address.Id, 1000), Ct));
    }

    [Fact]
    public async Task GetAsync_OrderOfAnotherCustomer_ThrowsNotFound()
    {
        // The repository returns null for (orderId, customerId) pairs that do not match.
        await Should.ThrowAsync<NotFoundException>(() => _service.GetAsync(_customer.Id, Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task CancelAsync_OwnPendingOrder_CancelsAndSaves()
    {
        var order = Order.Place(_customer, _address, _product, 1000, new PriceCalculator(new PricingOptions()), _time.GetUtcNow());
        _orders.GetAsync(order.Id, _customer.Id, Arg.Any<CancellationToken>()).Returns(order);

        var dto = await _service.CancelAsync(_customer.Id, order.Id, new CancelOrderRequest("Changed my mind"), Ct);

        dto.Status.ShouldBe(OrderStatus.Cancelled);
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task CancelAsync_BlankReason_ThrowsValidation()
    {
        await Should.ThrowAsync<ValidationException>(
            () => _service.CancelAsync(_customer.Id, Guid.NewGuid(), new CancelOrderRequest(" "), Ct));
    }

    [Fact]
    public async Task ListAsync_InvalidPaging_ThrowsBeforeQueryingTheRepository()
    {
        await Should.ThrowAsync<ValidationException>(() => _service.ListAsync(_customer.Id, new PageRequest(1, 101), Ct));

        await _orders.DidNotReceiveWithAnyArgs().PageAsync(default, default!, TestContext.Current.CancellationToken);
    }
}

public class PageRequestValidatorTests
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 100, true)]
    [InlineData(5, 20, true)]
    [InlineData(0, 20, false)]
    [InlineData(-1, 20, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 101, false)]
    public void Validate_PageAndSize_AcceptsOnlyPositivePageAndSizeUpToHundred(int page, int size, bool valid)
    {
        new PageRequestValidator().Validate(new PageRequest(page, size)).IsValid.ShouldBe(valid);
    }
}

public class ProductServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));
    private readonly ProductService _service;

    public ProductServiceTests() =>
        _service = new ProductService(
            _products, _unitOfWork, new PriceCalculator(new PricingOptions()), _time, new ChangePriceRequestValidator(_time));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangePriceAsync_NoValidFrom_AppliesNowAndSaves()
    {
        var product = Product.Create(ProductType.Diesel, 10m, _time.GetUtcNow().AddDays(-1));
        _products.GetAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var change = await _service.ChangePriceAsync(product.Id, new ChangePriceRequest(12m, null), Ct);

        change.ValidFrom.ShouldBe(_time.GetUtcNow());
        product.PriceAt(_time.GetUtcNow()).ShouldBe(12m);
        product.PriceHistory.Count.ShouldBe(2);
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task ChangePriceAsync_ValidFromInThePast_ThrowsValidation()
    {
        var request = new ChangePriceRequest(12m, _time.GetUtcNow().AddSeconds(-1));

        await Should.ThrowAsync<ValidationException>(() => _service.ChangePriceAsync(Guid.NewGuid(), request, Ct));
    }

    [Fact]
    public async Task ChangePriceAsync_UnknownProduct_ThrowsNotFound()
    {
        await Should.ThrowAsync<NotFoundException>(
            () => _service.ChangePriceAsync(Guid.NewGuid(), new ChangePriceRequest(12m, null), Ct));
    }

    [Fact]
    public async Task ListAsync_PriceChangeScheduledForLater_ShowsPriceInForceNow()
    {
        var product = Product.Create(ProductType.Diesel, 10m, _time.GetUtcNow().AddDays(-1));
        product.ChangePrice(12m, _time.GetUtcNow().AddDays(1));
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns([product]);

        var list = await _service.ListAsync(Ct);

        list.Single().PricePerLitreExVat.ShouldBe(10m);
        list.Single().PricePerLitreIncVat.ShouldBe(12.50m);
    }
}
