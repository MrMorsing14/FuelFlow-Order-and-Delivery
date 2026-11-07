using FuelFlow.Application.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<IReadOnlyList<ProductDto>> List(CancellationToken ct) => products.ListAsync(ct);

    [HttpPut("{id:guid}/price")]
    [Authorize(Roles = "Admin")]
    public Task<PriceChangeDto> ChangePrice(Guid id, ChangePriceRequest request, CancellationToken ct) =>
        products.ChangePriceAsync(id, request, ct);
}
