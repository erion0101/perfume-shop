using PerfumeShop.Shared.Kernel;

namespace PerfumeShop.Modules.Inventory.Domain;

/// <summary>Supplier and invoice of a goods receipt; shared by all batches received together.</summary>
internal sealed record GoodsReceipt(
    Guid ReceiptId,
    string SupplierName,
    string SupplierInvoiceNumber,
    DateOnly SupplierInvoiceDate,
    DateOnly ReceivedOn);

/// <summary>
/// Units of one variant received under one batch code on one supplier invoice: our proof of
/// authenticity. Consumed first-in, first-out when orders ship.
/// </summary>
internal sealed class StockBatch : Entity<Guid>
{
    public const int BatchCodeMaxLength = 64;
    public const int SupplierMaxLength = 200;
    public const int InvoiceNumberMaxLength = 100;

    private StockBatch() { }

    private StockBatch(Guid id, Guid variantId, int sequence, GoodsReceipt receipt, string batchCode, int quantity, Money unitCost, DateTime createdAtUtc) : base(id)
    {
        VariantId = variantId;
        Sequence = sequence;
        ReceiptId = receipt.ReceiptId;
        BatchCode = batchCode;
        QuantityReceived = quantity;
        QuantityRemaining = quantity;
        UnitCost = unitCost;
        SupplierName = receipt.SupplierName;
        SupplierInvoiceNumber = receipt.SupplierInvoiceNumber;
        SupplierInvoiceDate = receipt.SupplierInvoiceDate;
        ReceivedOn = receipt.ReceivedOn;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid VariantId { get; private set; }

    /// <summary>1, 2, 3… per variant in receiving order: the FIFO tie-breaker for batches received the same day.</summary>
    public int Sequence { get; private set; }

    /// <summary>Groups the batches of one delivery.</summary>
    public Guid ReceiptId { get; private set; }

    public string BatchCode { get; private set; } = null!;

    public int QuantityReceived { get; private set; }

    public int QuantityRemaining { get; private set; }

    /// <summary>Purchase price per unit, EUR.</summary>
    public Money UnitCost { get; private set; } = null!;

    public string SupplierName { get; private set; } = null!;

    public string SupplierInvoiceNumber { get; private set; } = null!;

    public DateOnly SupplierInvoiceDate { get; private set; }

    public DateOnly ReceivedOn { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    internal static Result<StockBatch> Create(Guid variantId, int sequence, GoodsReceipt receipt, string? batchCode, int quantity, Money unitCost, DateTime nowUtc)
    {
        batchCode = batchCode?.Trim();
        if (string.IsNullOrEmpty(batchCode) || batchCode.Length > BatchCodeMaxLength)
        {
            return InventoryErrors.BatchCodeRequired;
        }

        if (string.IsNullOrWhiteSpace(receipt.SupplierName) || receipt.SupplierName.Trim().Length > SupplierMaxLength)
        {
            return InventoryErrors.SupplierRequired;
        }

        if (string.IsNullOrWhiteSpace(receipt.SupplierInvoiceNumber) || receipt.SupplierInvoiceNumber.Trim().Length > InvoiceNumberMaxLength)
        {
            return InventoryErrors.InvoiceNumberRequired;
        }

        var today = DateOnly.FromDateTime(nowUtc);
        if (receipt.SupplierInvoiceDate > today || receipt.ReceivedOn > today || receipt.ReceivedOn < receipt.SupplierInvoiceDate.AddDays(-30))
        {
            return InventoryErrors.DatesInvalid;
        }

        if (quantity is < 1 or > StockItem.MaxQuantity)
        {
            return InventoryErrors.QuantityInvalid;
        }

        if (unitCost.Currency != Money.Eur)
        {
            return InventoryErrors.UnitCostInvalid;
        }

        var normalized = receipt with { SupplierName = receipt.SupplierName.Trim(), SupplierInvoiceNumber = receipt.SupplierInvoiceNumber.Trim() };
        return new StockBatch(Guid.CreateVersion7(), variantId, sequence, normalized, batchCode, quantity, unitCost, nowUtc);
    }

    internal void Take(int quantity) => QuantityRemaining -= quantity;

    internal void PutBack(int quantity) => QuantityRemaining += quantity;
}
