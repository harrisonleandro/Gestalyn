namespace ProjectManager.API.Agents;

/// <summary>
/// Marca um controller como API de agentes (ADR-016): recebe a política <c>Agent</c> e a guarda de organização. Todas
/// as rotas dele precisam ficar sob <c>/api/v1/agents</c>, e nenhuma rota sob esse prefixo pode ficar sem esta marca;
/// a API não inicia se uma das duas regras for violada.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AgentApiAttribute : Attribute;
