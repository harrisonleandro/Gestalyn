# AGENTS.md: instruções permanentes para agentes de IA

Este arquivo define como agentes de IA (Claude Code, Codex, Copilot e similares) devem trabalhar neste repositório. Ele complementa o [README.md](README.md) e a documentação em `docs/`.

## 1. Fontes de verdade

1. **[Especificação mestre](docs/specifications/ESPECIFICACAO-MESTRE.md):** decisões arquiteturais aprovadas, modelo funcional, contratos da API, roadmap das 14 fases e processo obrigatório.
2. Especificação da fase em `docs/specifications/FASE-NN-*.md`.
3. Decisões arquiteturais em `docs/decisions/` (ADRs).
4. Arquitetura em `docs/architecture/`.
5. Estado atual em [docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md).

Se houver conflito entre essas fontes, ou entre elas e o código, **pare e reporte**. Não resolva o conflito silenciosamente. A especificação mestre prevalece. Desvios exigem aprovação do responsável e um ADR.

## 2. Escopo por fase

- O projeto é desenvolvido nas 14 fases da especificação mestre (seção 9). Implemente **somente** o que a fase atual autoriza.
- Não avance para a fase seguinte sem autorização explícita do responsável, nem enquanto houver critérios obrigatórios não atendidos.
- Não crie entidades, endpoints, comandos de CLI ou telas "de exemplo" que aparentem funcionar sem implementação real. Dados de demonstração devem ser claramente identificados.
- Não crie projetos vazios para preencher a estrutura. `ProjectManager.MCP` é da Fase 12; `ProjectManager.Worker` surge quando houver necessidade concreta; `ArchitectureTests` e `EndToEndTests` surgem quando agregarem valor.
- Cada endpoint de negócio é criado na fase correspondente, sob o prefixo `/api/v1`.

## 3. Regras de arquitetura (obrigatórias)

```
Application → Domain
Infrastructure → Application + Domain
API → Application + Infrastructure
Web → API somente por HTTP
CLI → API somente por HTTP (sem regras de negócio próprias)
MCP → Application ou contratos próprios (sem duplicar regras de negócio)
Worker → Application (abstrações) + Infrastructure (implementações)
```

- `Domain` não depende de nenhum outro projeto da solução nem de frameworks de infraestrutura.
- `Application` depende apenas de `Domain` e de abstrações (`Microsoft.Extensions.*.Abstractions`).
- `Web` e `CLI` **nunca** referenciam `Domain`, `Application`, `Infrastructure` ou `API`.
- As regras são verificadas por `tests/ProjectManager.UnitTests/Architecture/ProjectReferenceRulesTests.cs`. Ao criar um projeto em `src/`, adicione-o às regras do teste.
- **Multi-tenancy:** banco e schema compartilhados, UUID, `OrganizationId` nas entidades da organização, validação de pertencimento e autorização no backend e testes de isolamento. Nunca confie apenas no `OrganizationId` enviado pelo cliente ou pelo agente.
- Não introduza microsserviços, Kubernetes, Redis, mensageria distribuída ou infraestrutura complexa sem aprovação.
- Mudanças de arquitetura exigem aprovação e um ADR em `docs/decisions/`.

## 4. Padrões de código

- `Nullable`, `ImplicitUsings`, analisadores (`AnalysisMode=Recommended`) e `TreatWarningsAsErrors` estão ativos via `Directory.Build.props`. **Não desative avisos globalmente.** Supressões pontuais exigem `Justification`.
- Versões de pacotes NuGet ficam somente em `Directory.Packages.props` (Central Package Management). Não use `Version=` nos `.csproj`. Toda dependência nova exige justificativa e verificação de compatibilidade com .NET 10.
- Convenções de nomenclatura e estilo em `.editorconfig`: namespaces file-scoped, `_camelCase` para campos privados, chaves obrigatórias.
- Async/await em I/O, com `CancellationToken` propagado.
- DTOs para contratos externos e validação de entrada.
- Logs estruturados com `[LoggerMessage]` (source generator). Nunca interpole strings em mensagens de log e nunca registre tokens, senhas ou segredos.
- Configuração externa ao código (`appsettings*.json`, variáveis de ambiente, Options pattern com validação).
- Exceções tratadas explicitamente. A API responde erros em Problem Details (RFC 9457) e não expõe detalhes internos.
- Evite classes grandes, abstrações sem uso, código morto, duplicação e comentários que repetem o código.
- Código e identificadores em inglês. Textos da interface, documentação e mensagens ao usuário em português (pt-BR).

## 5. Segurança

- **Nunca** versione segredos, connection strings reais, tokens ou chaves. Use `dotnet user-secrets` em desenvolvimento e variáveis de ambiente ou cofre de segredos nos demais ambientes.
- A autorização é sempre feita no backend. Esconder elementos na interface não é mecanismo de segurança.
- Agentes têm permissões limitadas e explícitas e não podem ultrapassá-las, mesmo instruídos a isso.
- Não crie credenciais reais, não contrate serviços, não registre domínios, não assuma custos e não publique em produção sem aprovação explícita.

## 6. Processo obrigatório de cada fase (especificação mestre, seção 12)

1. **Inspeção:** ler este arquivo, o README, as especificações, os ADRs e o `PROJECT_STATUS.md`; verificar o estado do Git, os arquivos e as dependências; identificar riscos e pendências anteriores.
2. **Planejamento:** criar ou atualizar `docs/specifications/FASE-NN-*.md` (objetivo, escopo, fora de escopo, dependências, tarefas, arquivos previstos, critérios, estratégia de testes, riscos, definição de conclusão).
3. **Implementação:** em passos pequenos, com testes e evidências a cada tarefa.
4. **Revisão técnica:** arquitetura, segurança, performance, manutenibilidade, testabilidade, contratos e compatibilidade com fases anteriores.
5. **Validação:** `dotnet restore`, `dotnet build` e `dotnet test`, verificando os códigos de saída. Para mudanças em API, Web ou CLI, execute a aplicação e verifique o comportamento real.
6. **Encerramento:** atualizar README, AGENTS.md, a especificação da fase, os ADRs, `docs/PROJECT_STATUS.md` e gerar `docs/reports/FASE-NN-RELATORIO.md` com as 19 seções da especificação mestre (seção 17).

Uma tarefa só fica `Completed` em `PROJECT_STATUS.md` quando seus critérios forem verificados. Estados permitidos: Not Started, Planned, In Progress, Blocked, In Review, Completed.

## 7. Controle de versão

- Branches: `main` (estável), `develop` (integração), `feature/*`, `fix/*`, `hotfix/*`. Trabalho de fase acontece em `feature/*` a partir de `develop`.
- Commits locais pequenos e descritivos são permitidos quando apropriados.
- **Push, abertura de PR e merge em `main` exigem pedido explícito do responsável.**
- Nunca faça force push, não reescreva histórico, não descarte alterações do usuário e não commite segredos.
- O repositório remoto ([harrisonleandro/Gestalyn](https://github.com/harrisonleandro/Gestalyn)) é **público**: tudo o que for enviado fica visível.
- Pull Requests incluem objetivo, alterações, testes executados, evidências, riscos e pendências.

## 8. Testes

- Testes devem verificar comportamento real. Não crie testes artificiais para aumentar a contagem.
- Unitários em `tests/ProjectManager.UnitTests`; integração da API em `tests/ProjectManager.IntegrationTests` (com `WebApplicationFactory`).
- Isolamento multi-tenant e permissões exigem testes automatizados a partir da Fase 02.
- Use `TestContext.Current.CancellationToken` (xUnit v3) em chamadas assíncronas.

## 9. Relato de resultados

- Reporte resultados reais de build e testes, com evidências. Nunca declare uma tarefa ou fase concluída com testes falhando ou etapas não verificadas.
- Não omita resultados negativos. Liste explicitamente pendências, critérios não atendidos e riscos.
