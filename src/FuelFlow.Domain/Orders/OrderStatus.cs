namespace FuelFlow.Domain.Orders;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Scheduled,
    OutForDelivery,
    Delivered,
    Cancelled,
}
