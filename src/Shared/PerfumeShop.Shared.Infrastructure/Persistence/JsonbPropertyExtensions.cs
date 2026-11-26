using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PerfumeShop.Shared.Infrastructure.Persistence;

/// <summary>Stores a value object (or a list of them) as a PostgreSQL jsonb column.</summary>
public static class JsonbPropertyExtensions
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<T> HasJsonbConversion<T>(this PropertyBuilder<T> property)
    {
        property
            .HasConversion(
                value => Serialize(value),
                json => Deserialize<T>(json),
                new ValueComparer<T>(
                    (left, right) => Serialize(left) == Serialize(right),
                    value => Serialize(value).GetHashCode(StringComparison.Ordinal),
                    value => Deserialize<T>(Serialize(value))))
            .HasColumnType("jsonb");

        return property;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializerOptions)
        ?? throw new InvalidOperationException($"Could not deserialize jsonb value to {typeof(T).Name}.");
}
