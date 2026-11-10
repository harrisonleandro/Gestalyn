# AGENTS.md — Instruções permanentes para agentes de IA

Este arquivo define como agentes de IA (Claude, Codex, Copilot e similares) devem trabalhar neste repositório. Ele complementa o [README.md](README.md) e a documentação em `docs/`.

## 1. Fontes de verdade

1. Especificação técnica v1.0 em `docs/specifications/` (**ainda ausente**; veja `docs/specifications/README.md`).
2. Decisões arquiteturais em `docs/decisions/` (ADRs).
3. Arquitetura em `docs/architecture/`.
4. Instruções da fase em execução, fornecidas pelo responsável do projeto.

Se houver conflito entre essas fontes ou entre elas e o código, **pare e reporte**. Não resolva o conflito silenciosamente.

## 2. Escopo por fase

- O projeto é desenvolvido em fases. Implemente **somente** o que a fase atual autoriza.
- Não avance para a fase seguinte sem autorização explícita.
- Não crie entidades, endpoints, comandos de CLI ou telas "de exemplo" que aparentem funcionar sem implementação real.
- Os projetos `ProjectManager.MCP` e `ProjectManager.Worker` pertencem a fases futuras; não os crie antecipadamente.

## 3. Regras de arquitetura (obrigatórias)

```
Application → Domain
Infrastructure → Application + Domain
API → Application + Infrastructure
Web → API somente por HTTP
CLI → API somente por HTTP
```

- `Domain` não depende de nenhum outro projeto da solução nem de frameworks de infraestrutura.
- `Application` depende apenas de `Domain` e de abstrações (`Microsoft.Extensions.*.Abstractions`).
- `Web` e `CLI` **nunca** referenciam `Domain`, `Application`, `Infrastructure` ou `API`.
- Essas regras são verificadas por `tests/ProjectManager.UnitTests/Architecture/ProjectReferenceRulesTests.cs`. Ao criar um novo projeto em `src/`, adicione-o às regras do teste e documente a mudança.
- Mudanças de arquitetura exigem um novo ADR em `docs/decisions/`.

## 4. Padrões de código

- `Nullable`, `ImplicitUsings`, analisadores (`AnalysisMode=Recommended`) e `TreatWarningsAsErrors` estão ativos via `Directory.Build.props`. **Não desative avisos globalmente.** Supressões pontuais exigem `Justification`.
- Versões de pacotes NuGet ficam somente em `Directory.Packages.props` (Central Package Management). Não use `Version=` nos `.csproj`.
- Convenções de nomenclatura e estilo em `.editorconfig` (namespaces file-scoped, `_camelCase` para campos privados, chaves obrigatórias).
- Async/await em I/O, com `CancellationToken` propagado.
- Logs estruturados com `[LoggerMessage]` (source generator); nunca interpolação de strings em mensagens de log.
- Configuração externa ao código (`appsettings*.json`, variáveis de ambiente, Options pattern com validação).
- Exceções tratadas explicitamente; a API responde erros em Problem Details e não expõe detalhes internos.
- Evite classes grandes, abstrações sem uso, código duplicado e comentários que repetem o código.
- Código e identificadores em inglês; textos da interface, documentação e mensagens ao usuário em português (pt-BR).

## 5. Segurança

- **Nunca** versione segredos, connection strings reais, tokens ou chaves. Use `dotnet user-secrets` em desenvolvimento e variáveis de ambiente ou cofre de segredos nos demais ambientes.
- Não crie credenciais reais nem contas em serviços externos.
- Não configure publicação automática em produção.

## 6. Fluxo de trabalho

1. Inspecione o repositório e a documentação antes de alterar algo.
2. Apresente um plano quando a mudança for não trivial.
3. Implemente em passos pequenos.
4. Valide sempre, verificando os códigos de saída:

   ```bash
   dotnet restore
   dotnet build
   dotnet test
   ```

5. Para mudanças em API, Web ou CLI, execute a aplicação e verifique o comportamento real, não apenas a compilação.
6. Atualize a documentação afetada (README, `docs/architecture`, ADRs).
7. Não exclua nem sobrescreva arquivos existentes sem justificativa.
8. Não faça commit, push ou abra PR sem pedido explícito do responsável.

## 7. Testes

- Testes devem verificar comportamento real; não crie testes artificiais para aumentar a contagem.
- Unitários em `tests/ProjectManager.UnitTests`; integração da API em `tests/ProjectManager.IntegrationTests` (com `WebApplicationFactory`).
- Use `TestContext.Current.CancellationToken` (xUnit v3) em chamadas assíncronas.

## 8. Relato de resultados

- Reporte resultados reais de build e testes, com evidências. Nunca declare uma tarefa concluída com testes falhando ou etapas não verificadas.
- Liste explicitamente pendências e critérios não atendidos.
