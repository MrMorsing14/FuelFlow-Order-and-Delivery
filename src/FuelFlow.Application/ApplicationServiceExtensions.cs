using FluentValidation;
using FuelFlow.Application.Auth;
using FuelFlow.Application.Customers;
using FuelFlow.Application.Orders;
using FuelFlow.Application.Products;
using Microsoft.Extensions.DependencyInjection;

namespace FuelFlow.Application;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(AssemblyMarker).Assembly);
        services.AddScoped<AuthService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<ProductService>();
        services.AddScoped<OrderService>();
        return services;
    }
}
