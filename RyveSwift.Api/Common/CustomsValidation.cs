using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Common;

public static class CustomsValidation
{
    private static readonly HashSet<string> VagueDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ANY",
        "CARGO",
        "FREIGHT",
        "GENERAL GOODS",
        "GENERAL MERCHANDISE",
        "GIFT",
        "GOODS",
        "ITEM",
        "ITEMS",
        "MERCHANDISE",
        "MISC",
        "MISCELLANEOUS",
        "OTHER",
        "PACKAGE",
        "PACKAGES",
        "PRODUCT",
        "PRODUCTS",
        "SAMPLE",
        "SAMPLES",
        "STUFF",
        "THINGS"
    };

    public static List<FieldError> ValidateCustomsItemRequests(
        IReadOnlyList<CustomsItemRequest> items,
        bool requireHsCode)
    {
        var errors = new List<FieldError>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var prefix = $"customsItems[{i}]";

            ValidateDescription(item.Description, $"{prefix}.description", errors);
            ValidateHsCode(item.HsCode, $"{prefix}.hsCode", requireHsCode, errors);

            if (item.Quantity <= 0)
                errors.Add(new FieldError($"{prefix}.quantity", "Quantity must be greater than 0."));

            if (item.UnitPrice <= 0)
                errors.Add(new FieldError($"{prefix}.unitPrice", "Unit price must be greater than 0."));
        }

        return errors;
    }

    public static List<FieldError> ValidateCustomsItems(
        IReadOnlyList<CustomsItem> items,
        bool requireHsCode)
    {
        var errors = new List<FieldError>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var prefix = $"customsItems[{i}]";

            ValidateDescription(item.Description, $"{prefix}.description", errors);
            ValidateHsCode(item.HsCode, $"{prefix}.hsCode", requireHsCode, errors);

            if (item.Quantity <= 0)
                errors.Add(new FieldError($"{prefix}.quantity", "Quantity must be greater than 0."));

            if (item.UnitPrice <= 0)
                errors.Add(new FieldError($"{prefix}.unitPrice", "Unit price must be greater than 0."));
        }

        return errors;
    }

    public static string NormalizeDescription(string? description) =>
        string.Join(' ', (description ?? "").Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static string NormalizeHsCode(string? hsCode) =>
        string.Join("", (hsCode ?? "").Trim().Where(char.IsDigit));

    private static void ValidateDescription(string? description, string field, List<FieldError> errors)
    {
        var normalized = NormalizeDescription(description);
        if (normalized.Length < 3)
        {
            errors.Add(new FieldError(field, "Customs item description must be at least 3 characters."));
            return;
        }

        var normalizedKey = normalized.ToUpperInvariant();
        if (VagueDescriptions.Contains(normalizedKey))
        {
            errors.Add(new FieldError(field,
                $"Description '{normalized}' is not specific enough. Provide the actual product name."));
        }
    }

    private static void ValidateHsCode(string? hsCode, string field, bool requireHsCode, List<FieldError> errors)
    {
        var normalized = NormalizeHsCode(hsCode);
        var hasInvalidCharacters = (hsCode ?? "").Any(c =>
            !char.IsDigit(c) && c is not ' ' and not '.' and not '-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            if (requireHsCode)
                errors.Add(new FieldError(field, "HS code is required for parcel customs items."));
            return;
        }

        if (hasInvalidCharacters ||
            normalized.Length is < 6 or > 10 ||
            normalized.All(c => c == '0') ||
            normalized.All(c => c == '9'))
        {
            errors.Add(new FieldError(field,
                $"HS code '{hsCode}' is invalid. Provide a real 6- to 10-digit HS code."));
        }
    }
}
