namespace RyveSwift.Api.Dtos;

public record CreateAddressRequest(
    string ContactName,
    string? CompanyName,
    string? Email,
    string Phone,
    string CountryCode,
    string CityName,
    string? PostalCode,
    string AddressLine1,
    string? AddressLine2,
    string? AddressLine3,
    bool IsDefaultSender);

public record UpdateAddressRequest(
    string ContactName,
    string? CompanyName,
    string? Email,
    string Phone,
    string CountryCode,
    string CityName,
    string? PostalCode,
    string AddressLine1,
    string? AddressLine2,
    string? AddressLine3,
    bool IsDefaultSender);

public record AddressResponse(
    Guid Id,
    string ContactName,
    string? CompanyName,
    string? Email,
    string Phone,
    string CountryCode,
    string CityName,
    string? PostalCode,
    string AddressLine1,
    string? AddressLine2,
    string? AddressLine3,
    bool IsDefaultSender,
    DateTime CreatedAt);
