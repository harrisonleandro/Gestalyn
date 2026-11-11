using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ProjectManager.Application.Abstractions.Authentication;
using ProjectManager.Infrastructure.Identity;

namespace ProjectManager.UnitTests.Authentication;

public sealed class TokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static JwtOptions ValidOptions => new()
    {
        Issuer = "gestalyn-api",
        Audience = "gestalyn",
        SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes),
    };

    [Fact]
    public void RefreshTokens_AreUrlSafeUniqueAnd256Bits()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => RefreshTokens.Generate()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, token =>
        {
            Assert.Equal(43, token.Length); // 32 bytes em Base64Url sem preenchimento
            Assert.Matches("^[A-Za-z0-9_-]+$", token);
        });
    }

    [Fact]
    public void RefreshTokenHash_IsDeterministicHexAndHidesTheToken()
    {
        var token = RefreshTokens.Generate();

        var hash = RefreshTokens.Hash(token);

        Assert.Equal(hash, RefreshTokens.Hash(token));
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9A-F]+$", hash);
        Assert.DoesNotContain(token, hash, StringComparison.Ordinal);
        Assert.NotEqual(hash, RefreshTokens.Hash(RefreshTokens.Generate()));
    }

    [Fact]
    public async Task AccessToken_ContainsUserSessionAndConfiguredLifetime()
    {
        var options = ValidOptions;
        var issuer = new AccessTokenIssuer(Options.Create(options), new FixedTimeProvider(Now));
        var userId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();

        var (token, expiresAt) = issuer.Issue(userId, sessionId);

        Assert.Equal(Now + options.AccessTokenLifetime, expiresAt);

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = options.CreateSigningKey(),
            ValidateLifetime = false,
        });

        Assert.True(validation.IsValid, validation.Exception?.Message);
        Assert.Equal(userId.ToString(), validation.Claims[AuthClaimTypes.Subject]);
        Assert.Equal(sessionId.ToString(), validation.Claims[AuthClaimTypes.SessionId]);
        Assert.Equal(SecurityAlgorithms.HmacSha256, new JsonWebToken(token).Alg);
    }

    [Theory]
    [InlineData("chave-curta")]
    [InlineData("")]
    public void JwtOptions_RejectSigningKeysShorterThan256Bits(string signingKey)
    {
        var options = ValidOptions;
        options.SigningKey = signingKey;

        Assert.False(options.IsValid());
        Assert.True(ValidOptions.IsValid());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
