using FuelFlow.Application.Common;
using FuelFlow.Application.Dispatch;
using FuelFlow.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Dispatcher,Admin")]
[Route("api/dispatch")]
public sealed class DispatchController(DispatchService dispatch) : ControllerBase
{
    // Parameters are named "filter" and "paging" on purpose: a parameter named like a property ("page", "status")
    // would make the binder treat that query key as a prefix and ignore the real values.
    [HttpGet("orders")]
    public Task<PagedResult<DispatchOrderDto>> ListOrders(
        [FromQuery] DispatchOrderFilter filter, [FromQuery] PageRequest paging, CancellationToken ct) =>
        dispatch.ListOrdersAsync(filter, paging, ct);

    /// <summary>The configured delivery regions, for filters and for creating delivery windows.</summary>
    [HttpGet("regions")]
    public IReadOnlyList<RegionDto> Regions() => dispatch.ListRegions();

    [HttpPost("orders/{id:guid}/confirm")]
    public Task<OrderDto> Confirm(Guid id, CancellationToken ct) => dispatch.ConfirmAsync(id, ct);

    [HttpPost("orders/{id:guid}/out-for-delivery")]
    public Task<OrderDto> OutForDelivery(Guid id, CancellationToken ct) => dispatch.StartDeliveryAsync(id, ct);

    [HttpPost("orders/{id:guid}/deliver")]
    public Task<OrderDto> Deliver(Guid id, DeliverOrderRequest request, CancellationToken ct) =>
        dispatch.DeliverAsync(id, request, ct);

    [HttpGet("delivery-windows")]
    public Task<IReadOnlyList<DeliveryWindowDto>> ListWindows([FromQuery] DeliveryWindowFilter filter, CancellationToken ct) =>
        dispatch.ListWindowsAsync(filter, ct);

    [HttpPost("delivery-windows")]
    [ProducesResponseType<DeliveryWindowDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateWindow(CreateDeliveryWindowRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await dispatch.CreateWindowAsync(request, ct));
}
