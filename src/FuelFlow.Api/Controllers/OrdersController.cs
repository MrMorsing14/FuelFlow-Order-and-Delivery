using FuelFlow.Application.Common;
using FuelFlow.Application.Orders;
using FuelFlow.Domain.Pricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
public sealed class OrdersController(OrderService orders) : ControllerBase
{
    [HttpPost("api/quotes")]
    public Task<PriceBreakdown> Quote(OrderRequest request, CancellationToken ct) =>
        orders.QuoteAsync(User.CustomerId(), request, ct);

    [HttpPost("api/orders")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Place(OrderRequest request, CancellationToken ct)
    {
        var order = await orders.PlaceAsync(User.CustomerId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpGet("api/orders")]
    // The parameter must NOT be called "page": the binder would read the query key "page" as the prefix of the whole object.
    public Task<PagedResult<OrderDto>> List([FromQuery] PageRequest paging, CancellationToken ct) =>
        orders.ListAsync(User.CustomerId(), paging, ct);

    [HttpGet("api/orders/{id:guid}")]
    public Task<OrderDto> Get(Guid id, CancellationToken ct) => orders.GetAsync(User.CustomerId(), id, ct);

    [HttpPost("api/orders/{id:guid}/cancel")]
    public Task<OrderDto> Cancel(Guid id, CancelOrderRequest request, CancellationToken ct) =>
        orders.CancelAsync(User.CustomerId(), id, request, ct);
}
