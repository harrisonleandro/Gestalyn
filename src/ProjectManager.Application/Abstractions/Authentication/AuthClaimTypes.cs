namespace ProjectManager.Application.Abstractions.Authentication;

/// <summary>
/// Claims do access token emitido pela API.
/// </summary>
public static class AuthClaimTypes
{
    public const string Subject = "sub";

    public const string SessionId = "sid";

    public const string TokenId = "jti";
}
