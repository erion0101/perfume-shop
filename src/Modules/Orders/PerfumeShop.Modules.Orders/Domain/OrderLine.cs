namespace PerfumeShop.Modules.Orders.Domain;

/// <summary>What was bought, frozen at purchase time: later catalog changes never alter it.</summary>
internal sealed class OrderLine
{
    private OrderLine() { }

    internal OrderLine(Guid orderId, OrderLineDraft draft)
    {
        Id = draft.LineId;
        OrderId = orderId;
        VariantId = draft.VariantId;
        FragranceId = draft.FragranceId;
        Sku = draft.Sku;
        BrandName = draft.BrandName;
        FragranceName = draft.FragranceName;
        VariantType = draft.VariantType;
        SizeValue = draft.SizeValue;
        SizeUnit = draft.SizeUnit;
        UnitPrice = draft.UnitPrice;
        Quantity = draft.Quantity;
        LineTotal = draft.UnitPrice * draft.Quantity;
        VatAmount = Vat.Of(LineTotal);
    }

    /// <summary>Also the Inventory reservation key.</summary>
    public Guid Id { get; private init; }

    public Guid OrderId { get; private init; }

    public Guid VariantId { get; private init; }

    public Guid FragranceId { get; private init; }

    public string Sku { get; private init; } = null!;

    public string BrandName { get; private init; } = null!;

    public string FragranceName { get; private init; } = null!;

    public string VariantType { get; private init; } = null!;

    public decimal? SizeValue { get; private init; }

    public string? SizeUnit { get; private init; }

    /// <summary>EUR, VAT included.</summary>
    public decimal UnitPrice { get; private init; }

    public int Quantity { get; private init; }

    public decimal LineTotal { get; private init; }

    public decimal VatAmount { get; private init; }

    /// <summary>Which batches were shipped (set on Ship).</summary>
    public IReadOnlyList<LineBatch> Batches { get; private set; } = [];

    internal void RecordBatches(IReadOnlyList<LineBatch> batches) => Batches = batches;
}
