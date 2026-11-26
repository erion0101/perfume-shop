namespace PerfumeShop.Modules.Orders.Domain;

/// <summary>One step of the order timeline: every status change, with who did it.</summary>
internal sealed class OrderStatusChange
{
    private OrderStatusChange() { }

    internal OrderStatusChange(Guid orderId, OrderStatus? from, OrderStatus to, OrderActor actor, string? note, DateTime atUtc)
    {
        OrderId = orderId;
        FromStatus = from;
        ToStatus = to;
        ActorKind = actor.Kind;
        ActorId = actor.Id;
        Note = note;
        ChangedAtUtc = atUtc;
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public Guid OrderId { get; private init; }

    public OrderStatus? FromStatus { get; private init; }

    public OrderStatus ToStatus { get; private init; }

    public ActorKind ActorKind { get; private init; }

    public string ActorId { get; private init; } = null!;

    public string? Note { get; private init; }

    public DateTime ChangedAtUtc { get; private init; }
}

/// <summary>Internal staff note, never shown to the customer.</summary>
internal sealed class OrderNote
{
    private OrderNote() { }

    internal OrderNote(Guid orderId, string authorId, string text, DateTime atUtc)
    {
        OrderId = orderId;
        AuthorId = authorId;
        Text = text;
        CreatedAtUtc = atUtc;
    }

    public Guid Id { get; private init; } = Guid.CreateVersion7();

    public Guid OrderId { get; private init; }

    public string AuthorId { get; private init; } = null!;

    public string Text { get; private init; } = null!;

    public DateTime CreatedAtUtc { get; private init; }
}

/// <summary>Raised on every status change (including placing); mapped to integration events on save.</summary>
internal sealed record OrderStatusChangedDomainEvent(Order Order, OrderStatus? From, OrderStatus To, OrderActor Actor, string? Note, DateTime AtUtc)
    : PerfumeShop.Shared.Kernel.IDomainEvent;
