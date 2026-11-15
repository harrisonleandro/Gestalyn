using ProjectManager.Application.Abstractions.Activity;
using ProjectManager.Domain.Activity;

namespace ProjectManager.Infrastructure.Activity;

/// <summary>Contexto padrão fora de uma requisição HTTP: a operação é do sistema. A API registra o seu.</summary>
internal sealed class SystemActivityContext : IActivityContext
{
    public ActivityActor Actor => ActivityActor.System;
}
