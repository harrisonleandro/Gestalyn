using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ProjectManager.IntegrationTests.Api;

/// <summary>
/// Controller registrado apenas nos testes para provocar uma exceção não tratada.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(Route)]
public sealed class ThrowingTestController : ControllerBase
{
    public const string Route = "/__tests/throw";

    public const string SensitiveMessage = "detalhe-interno-que-nao-deve-vazar";

    [HttpGet]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Ações de controller precisam ser métodos de instância.")]
    public IActionResult Get() => throw new InvalidOperationException(SensitiveMessage);
}
