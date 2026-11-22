using ProjectManager.Domain.Agents;
using ProjectManager.Infrastructure.Common;

namespace ProjectManager.Infrastructure.Agents;

/// <summary>Token novo de agente: <c>gsk_</c> + 32 bytes aleatórios em base64url, com o hash e o prefixo que ficam no banco.</summary>
internal readonly record struct NewAgentToken(string Token, string Hash, string Prefix);

internal static class AgentCredentialTokens
{
    public static NewAgentToken Generate()
    {
        var token = AgentTokens.Marker + OpaqueTokens.Generate();
        return new NewAgentToken(token, Hash(token), AgentTokens.PrefixOf(token));
    }

    public static string Hash(string token) => OpaqueTokens.Hash(token);
}
