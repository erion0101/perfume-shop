using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;

namespace PerfumeShop.Api;

/// <summary>
/// [Flags] enums (Season, Occasion) are sent as arrays of names, but the generator describes them as a bare string.
/// List their single-flag names so the TypeScript client gets a union type ("None"/"All" are not valid inputs).
/// </summary>
internal static class OpenApiFlagsEnums
{
    public static OpenApiOptions AddFlagsEnumNames(this OpenApiOptions options) =>
        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
            if (type.IsEnum && type.IsDefined(typeof(FlagsAttribute), inherit: false))
            {
                schema.Enum = [.. Enum.GetNames(type).Where(n => n is not ("None" or "All")).Select(n => (JsonNode)JsonValue.Create(n))];
            }

            return Task.CompletedTask;
        });
}
