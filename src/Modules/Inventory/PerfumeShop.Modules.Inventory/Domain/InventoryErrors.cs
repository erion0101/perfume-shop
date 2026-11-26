using PerfumeShop.Modules.Inventory.Contracts;
using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Domain;

internal static class InventoryErrors
{
    public static readonly Error QuantityInvalid = Error.Validation("Inventory.Quantity.Invalid", "Quantity must be between 1 and 100000.");
    public static readonly Error UnitCostInvalid = Error.Validation("Inventory.Batch.UnitCostInvalid", "Unit cost must be in EUR and not negative.");
    public static readonly Error BatchCodeRequired = Error.Validation("Inventory.Batch.CodeRequired", "Batch code is required (max 64 characters). It is our proof of authenticity.");
    public static readonly Error SupplierRequired = Error.Validation("Inventory.Batch.SupplierRequired", "Supplier name is required (max 200 characters).");
    public static readonly Error InvoiceNumberRequired = Error.Validation("Inventory.Batch.InvoiceNumberRequired", "Supplier invoice number is required (max 100 characters).");
    public static readonly Error DatesInvalid = Error.Validation("Inventory.Batch.DatesInvalid", "Invoice and received dates cannot be in the future, and goods cannot arrive before the invoice date minus 30 days.");
    public static readonly Error ReasonRequired = Error.Validation("Inventory.Adjustment.ReasonRequired", "A reason is required (max 500 characters).");
    public static readonly Error AdjustmentZero = Error.Validation("Inventory.Adjustment.Zero", "Adjustment cannot be zero.");
    public static readonly Error PositiveAdjustmentNeedsBatch = Error.Validation(
        "Inventory.Adjustment.BatchRequired", "Adding stock by adjustment must name the batch it belongs to. New goods are received with an invoice instead.");
    public static readonly Error BatchOverfilled = Error.Validation("Inventory.Adjustment.BatchOverfilled", "A batch cannot hold more than was received on its invoice.");
    public static readonly Error BatchNotFound = Error.NotFound("Inventory.Batch.NotFound", "Batch not found for this variant.");
    public static readonly Error BatchInsufficient = Error.Conflict("Inventory.Batch.Insufficient", "The batch does not have that many units left.");
    public static readonly Error ThresholdInvalid = Error.Validation("Inventory.Threshold.Invalid", "Low-stock threshold must be between 0 and 1000.");
    public static readonly Error ReservationMismatch = Error.Conflict(
        InventoryErrorCodes.ReservationMismatch, "This order line is already reserved with a different variant or quantity.");
    public static readonly Error ReservationReleased = Error.Conflict(
        InventoryErrorCodes.ReservationNotActive, "The reservation was released; released stock cannot be shipped.");
    public static readonly Error ReservationNotFound = Error.NotFound(InventoryErrorCodes.ReservationNotFound, "No reservation for this order line.");
    public static readonly Error WrongVariant = Error.Validation("Inventory.Reservation.WrongVariant", "The reservation belongs to another variant.");
    public static readonly Error Inconsistent = Error.Conflict("Inventory.Stock.Inconsistent", "Batch quantities do not cover the stock on hand. Check the batches.");

    public static Error Insufficient(int available) => Error.Conflict(
        InventoryErrorCodes.InsufficientStock, $"Not enough stock: {available} available.");
}
