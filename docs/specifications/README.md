# Especificações

Este diretório deve conter a **Especificação Técnica v1.0** do ProjectManager, que é a referência funcional e arquitetural do projeto.

## Situação em 2026-11-10 (Fase 01)

A especificação técnica v1.0 **não estava disponível no repositório** durante a execução da Fase 01. A fundação técnica foi construída usando apenas as decisões fornecidas no prompt da Fase 01:

- C# / .NET 10 LTS, ASP.NET Core Web API, Blazor Web App, PostgreSQL, EF Core, ASP.NET Core Identity, SignalR, xUnit, Git e GitHub Actions.
- Arquitetura em monólito modular, plataforma multiusuário, multi-organização (multi-tenant), hospedada em nuvem.
- Integrações futuras: REST API, CLI `projectctl`, MCP para agentes e GitHub.

Decisões que dependem da especificação foram **deliberadamente adiadas** e não foram presumidas:

- Modelo de multi-tenancy (banco único com discriminador, schema por tenant ou banco por tenant).
- Estratégia de autenticação da API para Web, CLI e agentes (cookies, JWT, tokens pessoais, OAuth).
- Modelo de permissões e papéis.
- Modelo de domínio (organizações, projetos, tarefas, fases, atividades, agentes).

**Ação necessária:** adicionar a especificação v1.0 neste diretório antes do início da Fase 02.
