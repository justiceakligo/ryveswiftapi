namespace RyveSwift.Api.Dtos;

public record EmailPreferenceTokenRequest(string Token);

public record EmailPreferenceResponse(
    bool EmailUnsubscribed,
    DateTime? EmailUnsubscribedAt,
    string Message);
