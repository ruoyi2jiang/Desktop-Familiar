using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

/// <summary>Projects a newer document into the types this build can display. The source stays intact.</summary>
internal static class CompatibleLibraryReader
{
    public static DesktopCompanionData Read(JsonElement root, JsonSerializerOptions options)
    {
        var known = ReadValue(root, typeof(DesktopCompanionData), options);
        var data = known?.Deserialize<DesktopCompanionData>(options)
            ?? throw new InvalidDataException("The library has no readable configuration.");
        // A cleanup request belongs to the version that understands all of the asset references.
        data.PendingCompanionCleanup = [];
        return data;
    }

    private static JsonNode? ReadValue(JsonElement value, Type type, JsonSerializerOptions options)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        var metadata = options.GetTypeInfo(type);
        if (metadata.Kind == JsonTypeInfoKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object) return null;
            var result = new JsonObject();
            foreach (var field in value.EnumerateObject())
            {
                var property = metadata.Properties.FirstOrDefault(p =>
                    p.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
                if (property?.Set != null && ReadValue(field.Value, property.PropertyType, options) is { } child)
                    result[property.Name] = child;
            }
            return result;
        }
        if (metadata.Kind == JsonTypeInfoKind.Dictionary && type.IsGenericType
            && type.GetGenericArguments() is [var keyType, var valueType] && keyType == typeof(string))
        {
            if (value.ValueKind != JsonValueKind.Object) return null;
            var entries = new JsonObject();
            foreach (var field in value.EnumerateObject())
            {
                if (ReadValue(field.Value, valueType, options) is { } child)
                    entries[field.Name] = child;
                else if (field.Value.ValueKind == JsonValueKind.Null
                    && (!valueType.IsValueType || Nullable.GetUnderlyingType(valueType) != null))
                    entries[field.Name] = null;
            }
            return entries;
        }
        if (metadata.Kind == JsonTypeInfoKind.Enumerable
            && type.IsGenericType && type.GetGenericArguments() is [var itemType])
        {
            if (value.ValueKind != JsonValueKind.Array) return null;
            var items = new JsonArray();
            foreach (var item in value.EnumerateArray())
                if (ReadValue(item, itemType, options) is { } child) items.Add(child);
            return items;
        }
        try
        {
            return JsonSerializer.SerializeToNode(value.Deserialize(type, options), type, options);
        }
        catch (JsonException)
        {
            // An unfamiliar enum or scalar shape uses this build's default.
            return null;
        }
    }
}
