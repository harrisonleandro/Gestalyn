# Fase 01: Fundação da solução .NET

- **Estado:** Completed (critérios verificados em 2026-11-10; veja [o relatório](../reports/FASE-01-RELATORIO.md))
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md), seção 9, Fase 01

## Objetivo

Criar a estrutura inicial da solução e configurar o ambiente de desenvolvimento, de testes e de integração contínua, sem funcionalidades de negócio.

## Escopo

- Solution `ProjectManager.sln` e os projetos Domain, Application, Infrastructure, API, Web e CLI.
- Projetos de teste UnitTests e IntegrationTests.
- Referências entre projetos conforme a seção 3.1 da especificação mestre.
- **API:** DI, OpenAPI, health check, Problem Details e configuração por ambiente.
- **Web:** Blazor Web App com layout, navegação, tema escuro e consumo da API por HTTP.
- **CLI:** executável `projectctl` com `--help` e `--version`.
- Padrões de código (`.editorconfig`, `Directory.Build.props`, analisadores e Central Package Management).
- Git, documentação inicial e GitHub Actions (build e testes).

## Fora de escopo

- Entidades de domínio, banco de dados, EF Core, migrations (Fase 02).
- Autenticação e Identity (Fase 03), organizações e permissões (Fase 04).
- Endpoints de negócio sob `/api/v1`.
- Projetos `ProjectManager.MCP`, `ProjectManager.Worker`, `ProjectManager.ArchitectureTests` e `ProjectManager.EndToEndTests`. Pela especificação, só são criados quando agregarem valor.
- Pastas `docs/api`, `docs/deployment`, `docs/security`, `docs/operations`, `scripts/` e `infrastructure/`. São criadas quando houver conteúdo real, porque o Git não versiona pastas vazias.
- Publicação em ambiente cloud (Fase 14).

## Dependências

- .NET SDK 10.0.404 (instalado via winget nesta fase).
- Git 2.55 e repositório GitHub `harrisonleandro/Gestalyn` com Actions habilitado.

## Tarefas

| ID | Tarefa | Estado | Evidência |
|---|---|---|---|
| F01-001 | Inspecionar ambiente e repositório | Completed | Diretório vazio, sem Git, sem SDK, sem especificação (relatório §12) |
| F01-002 | Verificar SDK .NET 10 | Completed | SDK ausente; instalado 10.0.404, runtime 10.0.15 |
| F01-003 | Verificar Git | Completed | Git 2.55.0; `git init -b main` |
| F01-004 | Criar Solution | Completed | `ProjectManager.sln` (formato `.sln`) |
| F01-005 | Criar projetos iniciais | Completed | 6 projetos `src/` e 2 `tests/` |
| F01-006 | Configurar referências | Completed | `ProjectReferenceRulesTests` (7 casos) aprovados; mutação Web → Domain detectada |
| F01-007 | Configurar API | Completed | `/health` 200, `/openapi/v1.json` 200 (Development) e 404 (Production), Problem Details 404/500 |
| F01-008 | Configurar Blazor | Completed | `/` e `/status` renderizadas; status da API via HTTP; falha da API tratada |
| F01-009 | Configurar CLI | Completed | `--help`, `--version`, sem argumentos e comando inválido (códigos 0/0/0/1) |
| F01-010 | Configurar testes | Completed | xUnit v3; 12 unitários e 6 de integração |
| F01-011 | Configurar padrões de código | Completed | Build com 0 avisos e 0 erros com `TreatWarningsAsErrors` |
| F01-012 | Configurar Git e documentação | Completed | README, AGENTS.md, arquitetura, ADR-001, especificação, PROJECT_STATUS |
| F01-013 | Configurar GitHub Actions | Completed | CI aprovado nas execuções 42897692133 e 42901411495 |

## Arquivos previstos

Raiz (`global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitattributes`, `.gitignore`, `README.md`, `AGENTS.md`, `ProjectManager.sln`), `.github/workflows/ci.yml`, projetos em `src/` e `tests/` e documentação em `docs/`. O detalhamento está em [PROJECT_STRUCTURE.md](../architecture/PROJECT_STRUCTURE.md).

## Critérios de aceitação

| Critério | Estado |
|---|---|
| Solution criada | Atendido |
| Projetos compilando | Atendido |
| Referências corretas | Atendido |
| API iniciando | Atendido |
| Web iniciando | Atendido |
| CLI executando | Atendido |
| Testes executando | Atendido |
| OpenAPI disponível | Atendido (Development) |
| CI executando build e testes | Atendido |
| Documentação inicial criada | Atendido |

## Estratégia de testes

- **Unitários:** regras de dependência entre projetos (leitura dos `.csproj`) e comportamento da CLI com saída injetável.
- **Integração:** API hospedada em memória (`WebApplicationFactory`), verificando health check, OpenAPI por ambiente e respostas de erro padronizadas.
- **Execução real:** API, Web e CLI iniciadas e verificadas manualmente, inclusive os caminhos de falha.
- **CI:** restore, build Release e testes em `ubuntu-latest`.

## Riscos

| Risco | Mitigação |
|---|---|
| Versões de pacotes incompatíveis com .NET 10 | Linhas 10.0.x e versões estáveis compatíveis, centralizadas em CPM |
| Regras de arquitetura degradarem com o tempo | Teste automatizado de referências entre projetos |
| Diferenças entre ambiente local (Windows) e CI (Linux) | CI executa os mesmos comandos; a primeira falha do CI foi corrigida |

## Definição de conclusão

Todos os critérios de aceitação verificados com evidência real. Build sem avisos, testes aprovados localmente e no CI, documentação atualizada e alterações versionadas no Git.
