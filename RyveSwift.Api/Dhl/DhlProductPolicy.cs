namespace RyveSwift.Api.Dhl;

public static class DhlProductPolicy
{
    private static readonly Dictionary<string, string> HiddenProductCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["D"] = "DHL Express Documents",
        ["N"] = "DHL Domestic Express",
        ["H"] = "DHL Economy Select",
        ["W"] = "DHL Economy Select",
        ["X"] = "DHL Express Easy",
        ["C"] = "DHL Medical Express"
    };

    private static readonly Dictionary<string, string> HiddenProductNameFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DOMESTIC EXPRESS"] = "DHL Domestic Express",
        ["EXPRESS DOMESTIC"] = "DHL Domestic Express",
        ["ECONOMY SELECT"] = "DHL Economy Select",
        ["EXPRESS EASY"] = "DHL Express Easy",
        ["MEDICAL EXPRESS"] = "DHL Medical Express"
    };

    public static bool TryGetHiddenServiceName(string? productCode, string? productName, out string serviceName)
    {
        if (!string.IsNullOrWhiteSpace(productCode) &&
            HiddenProductCodes.TryGetValue(productCode.Trim(), out serviceName!))
            return true;

        var normalizedName = Normalize(productName);
        foreach (var hiddenProduct in HiddenProductNameFragments)
        {
            if (normalizedName.Contains(hiddenProduct.Key, StringComparison.OrdinalIgnoreCase))
            {
                serviceName = hiddenProduct.Value;
                return true;
            }
        }

        serviceName = "";
        return false;
    }

    public static bool TryGetHiddenServiceName(DhlProduct product, out string serviceName) =>
        TryGetHiddenServiceName(product.ProductCode, product.ProductName, out serviceName) ||
        TryGetHiddenServiceName(product.LocalProductCode, product.ProductName, out serviceName);

    public static bool IsHiddenService(DhlProduct product) =>
        TryGetHiddenServiceName(product, out _);

    public static string HiddenServiceMessage(string serviceName) =>
        $"{serviceName} is not available for selection. Please choose DHL Express Worldwide for eligible international parcel shipments.";

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? "").Trim().ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
