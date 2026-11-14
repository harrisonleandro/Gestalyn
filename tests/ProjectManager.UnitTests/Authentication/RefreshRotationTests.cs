using Microsoft.Extensions.Options;
using ProjectManager.Infrastructure.Identity;

namespace ProjectManager.UnitTests.Authentication;

/// <summary>Regra de decisão, derivação do sucessor e opções da rotação idempotente (ADR-012).</summary>
public sealed class RefreshRotationTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    private const int MaxReplays = 5;

    [Fact]
    public void UnusedToken_IsRotated()
    {
        var token = NewToken();

        Assert.Equal(RefreshAction.Rotate, Decide(token, successorReplayable: false, Now));
    }

    [Fact]
    public void ConsumedTokenWithinTheWindow_AndSuccessorUnused_IsReplayed()
    {
        var token = Consumed(NewToken(), Now);

        Assert.Equal(RefreshAction.Replay, Decide(token, successorReplayable: true, Now + Window));
    }

    [Fact]
    public void ConsumedToken_WhoseSuccessorWasUsed_RevokesEvenWithinTheWindow()
    {
        var token = Consumed(NewToken(), Now);

        Assert.Equal(RefreshAction.RevokeForReuse, Decide(token, successorReplayable: false, Now + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ConsumedTokenAfterTheWindow_Revokes()
    {
        var token = Consumed(NewToken(), Now);

        Assert.Equal(RefreshAction.RevokeForReuse, Decide(token, successorReplayable: true, Now + Window + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void ZeroWindow_RevokesEveryReuse()
    {
        var token = Consumed(NewToken(), Now);

        Assert.Equal(RefreshAction.RevokeForReuse, RefreshDecision.Decide(token, successorReplayable: true, Now + TimeSpan.FromTicks(1), TimeSpan.Zero, MaxReplays));
    }

    [Fact]
    public void ReplayLimitReached_Revokes()
    {
        var token = Consumed(NewToken(), Now);

        Assert.Equal(RefreshAction.RevokeForReuse, RefreshDecision.Decide(token, successorReplayable: true, Now, Window, maxReplays: 0));
    }

    [Fact]
    public void RevokedOrExpiredSession_IsRejected_WithoutRevokingAgain()
    {
        var revoked = Consumed(NewToken(), Now);
        revoked.Session.Revoke(Now, SessionRevocationReason.Logout);
        var expired = NewToken(sessionLifetime: TimeSpan.FromMinutes(1));

        Assert.Equal(RefreshAction.Reject, Decide(revoked, successorReplayable: true, Now));
        Assert.Equal(RefreshAction.Reject, Decide(expired, successorReplayable: false, Now + TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void ReplaceWith_LinksTheSuccessor_AndCannotHappenTwice()
    {
        var token = NewToken();
        var successor = new UserRefreshToken(token.Session, new string('b', 64));

        token.ReplaceWith(successor, Now);

        Assert.Equal((Now, successor.Id), (token.ConsumedAt, token.ReplacedByTokenId));
        Assert.Throws<InvalidOperationException>(() => token.ReplaceWith(successor, Now));
    }

    [Fact]
    public void Successor_IsDeterministic_DistinctPerToken_AndDependsOnTheKey()
    {
        var derivation = new RefreshTokenDerivation(Options.Create(OptionsWithKey('k')));
        var otherKey = new RefreshTokenDerivation(Options.Create(OptionsWithKey('x')));

        var successor = derivation.Successor("token-a");

        Assert.Equal(successor, derivation.Successor("token-a"));
        Assert.NotEqual(successor, derivation.Successor("token-b"));
        Assert.NotEqual(successor, otherKey.Successor("token-a"));
        Assert.NotEqual("token-a", successor);
        Assert.Equal(43, successor.Length);
    }

    [Theory]
    [InlineData("00:00:00", 0, true)]
    [InlineData("00:00:30", 10, true)]
    [InlineData("00:00:31", 5, false)]
    [InlineData("-00:00:01", 5, false)]
    [InlineData("00:00:10", -1, false)]
    [InlineData("00:00:10", 11, false)]
    public void Options_LimitTheReplayWindowAndTheReplayCount(string window, int maxReplays, bool valid)
    {
        var options = OptionsWithKey('k');
        options.RefreshReplayWindow = TimeSpan.Parse(window, System.Globalization.CultureInfo.InvariantCulture);
        options.MaxRefreshReplays = maxReplays;

        Assert.Equal(valid, options.IsValid());
    }

    private static RefreshAction Decide(UserRefreshToken token, bool successorReplayable, DateTimeOffset now) =>
        RefreshDecision.Decide(token, successorReplayable, now, Window, MaxReplays);

    private static UserRefreshToken NewToken(TimeSpan? sessionLifetime = null) =>
        new(new UserSession(Guid.CreateVersion7(), Now + (sessionLifetime ?? TimeSpan.FromDays(30))), new string('a', 64));

    private static UserRefreshToken Consumed(UserRefreshToken token, DateTimeOffset at)
    {
        token.ReplaceWith(new UserRefreshToken(token.Session, new string('c', 64)), at);
        return token;
    }

    private static JwtOptions OptionsWithKey(char keyCharacter) => new()
    {
        Issuer = "gestalyn-tests",
        Audience = "gestalyn-tests",
        SigningKey = new string(keyCharacter, JwtOptions.MinimumSigningKeyBytes),
    };
}
