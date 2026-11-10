# Relatório final: Fase 01, Fundação da solução .NET

- **Data:** 2026-11-10
- **Ambiente:** Windows 11 Pro 10.0.26200 · .NET SDK 10.0.404 (runtime 10.0.15) · Git 2.55.0 · GitHub Actions `ubuntu-latest`
- **Estado:** Completed
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 01: Fundação da solução .NET. A especificação da fase está em [FASE-01-FUNDACAO.md](../specifications/FASE-01-FUNDACAO.md).

## 2. Objetivo

Criar a estrutura inicial da solução e configurar o ambiente (build, padrões de código, testes, CI e documentação), sem funcionalidades de negócio.

## 3. Resumo das implementações

- **Solução:** `ProjectManager.sln` com 6 projetos de código e 2 de testes. As referências seguem a seção 3.1 da especificação mestre e são verificadas por teste.
- **Configuração comum:**
  - `global.json`, fixando o SDK 10.0.404.
  - `Directory.Build.props`: nullable, implicit usings, analisadores `Recommended`, avisos como erros e versão única do produto.
  - `Directory.Packages.props` (Central Package Management).
  - `.editorconfig` com convenções de nomenclatura.
- **API:**
  - Pontos de composição de DI por camada.
  - OpenAPI 3.1 nativo, exposto somente em Development.
  - `/health` reportando apenas componentes existentes.
  - Problem Details (RFC 9457) para 404 e 500 via `GlobalExceptionHandler`, sem expor detalhes internos.
  - Logs estruturados com `[LoggerMessage]`: JSON fora de Development, texto simples em Development.
  - HTTPS e HSTS fora de Development.
- **Web:**
  - Blazor Web App em tema escuro, com layout, navegação (Início, Status do sistema) e selo de ambiente.
  - Página de status que consulta a API por HTTP com `HttpClient` tipado.
  - `Api:BaseUrl` validada na inicialização.
- **CLI:** `projectctl` com `--help` e `--version`. Sem argumentos, exibe a ajuda; com comando desconhecido, sai com código ≠ 0. Os grupos futuros têm convenção definida, mas nenhum comando falso foi registrado.
- **Testes:** 12 unitários e 6 de integração.
- **CI:** GitHub Actions com restore, build Release, testes e publicação dos `.trx`, em push para `main`/`develop` e em pull requests.
- **Processo e documentação:**
  - README e AGENTS.md.
  - Arquitetura, estrutura e ADR-001 (com a revisão de alinhamento).
  - Especificação mestre registrada no repositório e especificação da Fase 01.
  - `PROJECT_STATUS.md` e este relatório.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| Raiz | `ProjectManager.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitattributes`, `.gitignore`, `README.md`, `AGENTS.md` |
| CI e ferramentas | `.github/workflows/ci.yml`, `.claude/launch.json` |
| Domain | `ProjectManager.Domain.csproj` |
| Application | `ProjectManager.Application.csproj`, `DependencyInjection.cs` |
| Infrastructure | `ProjectManager.Infrastructure.csproj`, `DependencyInjection.cs` |
| API | `Program.cs`, `ErrorHandling/GlobalExceptionHandler.cs`, `Health/HealthEndpoints.cs`, `Health/HealthCheckResponseWriter.cs`, `appsettings*.json`, `Properties/launchSettings.json`, `ProjectManager.API.http`, `.csproj` |
| Web | `Program.cs`, `AppInfo.cs`, `Api/ApiOptions.cs`, `Api/ApiHealthClient.cs`, `Api/ApiHealthResult.cs`, `Components/` (App, Routes, Layout, Pages Home/Status/Error/NotFound, Shared EnvironmentBadge/ApiStatusCard), `wwwroot/app.css`, `appsettings*.json`, `Properties/launchSettings.json`, `.csproj`. Bootstrap 5.3 e `ReconnectModal` vêm do template. |
| CLI | `Program.cs`, `CliApplication.cs`, `.csproj` |
| UnitTests | `Architecture/ProjectReferenceRulesTests.cs`, `Architecture/RepositoryRoot.cs`, `Cli/CliApplicationTests.cs`, `.csproj` |
| IntegrationTests | `Infrastructure/ApiFactory.cs`, `Api/HealthEndpointTests.cs`, `Api/OpenApiDocumentTests.cs`, `Api/ErrorHandlingTests.cs`, `Api/ThrowingTestController.cs`, `.csproj` |
| Documentação | `docs/PROJECT_STATUS.md`, `docs/architecture/ARCHITECTURE.md`, `docs/architecture/PROJECT_STRUCTURE.md`, `docs/decisions/ADR-001-MODULAR-MONOLITH.md`, `docs/specifications/README.md`, `docs/specifications/ESPECIFICACAO-MESTRE.md`, `docs/specifications/FASE-01-FUNDACAO.md`, `docs/reports/FASE-01-RELATORIO.md` |

## 5. Arquivos modificados

O repositório estava vazio no início da fase. Depois do commit inicial, estes arquivos foram modificados:

| Arquivo | Motivo |
|---|---|
| `tests/ProjectManager.UnitTests/Cli/CliApplicationTests.cs` | Correção do teste de `--version` que falhou no primeiro CI (§12) |
| `.github/workflows/ci.yml` | `upload-artifact` v4 → v7 (Node.js 24); CI também em `develop` |
| `AGENTS.md`, `README.md`, `docs/architecture/*`, `docs/decisions/ADR-001-*`, `docs/specifications/README.md` | Alinhamento com a especificação mestre |
| `docs/reports/FASE-01-RELATORIO.md` | Registro do CI e reescrita no formato de 19 seções |

Arquivos de exemplo dos templates foram removidos (WeatherForecast, Counter, Weather, `Class1.cs`, `UnitTest1.cs`), porque implementações fictícias são proibidas.

## 6. Dependências adicionadas

| Pacote | Versão | Projeto |
|---|---|---|
| Microsoft.AspNetCore.OpenApi | 10.0.15 | API |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.15 | Application, Infrastructure |
| System.CommandLine | 2.0.15 | CLI |
| xunit.v3 | 3.2.2 | testes |
| xunit.runner.visualstudio | 3.1.5 | testes |
| Microsoft.NET.Test.Sdk | 18.11.1 | testes |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.15 | IntegrationTests |
| Bootstrap 5.3 (arquivos estáticos do template) | — | Web |
| Ferramenta: .NET SDK 10.0.404 | — | instalado via winget, com autorização |
| Action: `actions/upload-artifact` | v7 | CI |

## 7. Justificativas técnicas

- **OpenAPI nativo** em vez de Swashbuckle: é o suporte oficial do .NET 10 (OpenAPI 3.1) e não exige pacote de terceiros. A interface visual (Scalar ou Swagger UI) não foi incluída, para evitar uma dependência sem necessidade.
- **DependencyInjection.Abstractions:** permite `AddApplication`/`AddInfrastructure` sem acoplar as camadas ao ASP.NET Core.
- **System.CommandLine 2.0.x:** parser oficial, estável e alinhado ao .NET 10. A linha 3.0 já existe, mas acompanha o .NET 11.
- **xUnit v3 3.2.x:** a especificação exige xUnit, e a v3 fornece `TestContext.Current.CancellationToken`. A linha 4.x é mais recente, mas foi evitada para não adotar um major sem necessidade.
- **Mvc.Testing:** hospeda a API em memória nos testes de integração.
- **Central Package Management:** evita versões divergentes entre projetos.
- **Formato `.sln`** em vez de `.slnx` (padrão do .NET 10): segue a estrutura da especificação.
- **Blazor Interactive Server por componente:** a Web chama a API servidor a servidor, sem precisar de CORS, e é compatível com SignalR.
- **Health check sem dependências fictícias:** só reporta o que existe, conforme o prompt da fase.
- **Regras de arquitetura lidas dos `.csproj`:** detectam referências declaradas mesmo quando nenhum tipo é usado.

## 8. Testes executados

| Projeto | Teste | Verifica |
|---|---|---|
| UnitTests | `ProjectReferenceRulesTests.Project_ReferencesOnlyAllowedProjects` (6 casos) | Referências permitidas de cada projeto em `src/` |
| UnitTests | `ProjectReferenceRulesTests.SourceProjects_AreAllCoveredByRules` | Todo projeto em `src/` tem regra |
| UnitTests | `CliApplicationTests` (5) | `--version`, `--help`, `-h`, sem argumentos, comando desconhecido |
| IntegrationTests | `HealthEndpointTests` (2) | `/health` Healthy, versão e somente a verificação `self` |
| IntegrationTests | `OpenApiDocumentTests` (2) | OpenAPI em Development e 404 em Production |
| IntegrationTests | `ErrorHandlingTests` (2) | 404 e 500 em Problem Details, com `traceId` e sem vazar a exceção |

Também foram feitas uma verificação de mutação e a execução real de API, Web e CLI (§11).

## 9. Resultado do build

```
dotnet restore                       → exit 0
dotnet build -c Release --no-restore → 0 Aviso(s), 0 Erro(s), exit 0
```

Os 8 projetos compilam. A última validação local foi em 2026-11-10, às 22:15 UTC.

## 10. Resultado dos testes

```
Aprovado! – Com falha: 0, Aprovado: 12, Total: 12 - ProjectManager.UnitTests.dll (net10.0)
Aprovado! – Com falha: 0, Aprovado:  6, Total:  6 - ProjectManager.IntegrationTests.dll (net10.0)
```

| Execução no GitHub Actions | Commit | Resultado |
|---|---|---|
| [42897140748](https://github.com/harrisonleandro/Gestalyn/actions/runs/42897140748) | `af77d2f` | **failure:** 1 teste falhou (§12) |
| [42897692133](https://github.com/harrisonleandro/Gestalyn/actions/runs/42897692133) | `53c4d5a` | success: 12/12 e 6/6 |
| [42901411495](https://github.com/harrisonleandro/Gestalyn/actions/runs/42901411495) | `c6bd32a` | success |

## 11. Evidências

| Verificação | Resultado |
|---|---|
| API (Development) `GET /health` | 200: `{"status":"Healthy","version":"0.1.0",...,"checks":[{"name":"self",...}]}` |
| API (Development) `GET /openapi/v1.json` | 200: `"openapi": "3.1.1"` |
| API `GET /rota-inexistente` | 404 `application/problem+json` com `traceId` |
| API (Production) `/health` e `/openapi/v1.json` | 200 e 404; logs em JSON |
| Web (Development) `/` e `/status` | Tema escuro; card da API "Healthy · 0.1.0" obtido por HTTP |
| Web com a API parada | "Indisponível: Não foi possível conectar à API." e aviso estruturado no log |
| Web (Production) sem `Api:BaseUrl` | Não inicia: `OptionsValidationException ... 'BaseUrl' ... required` |
| CLI `--version`, `--help`, sem argumentos, comando inválido | códigos 0, 0, 0 (ajuda) e 1 |
| Mutação (referência Web → Domain adicionada temporariamente) | `ProjectReferenceRulesTests` falhou (1 de 12); a referência foi revertida |

## 12. Problemas encontrados

1. .NET SDK ausente na máquina.
2. Diretório sem repositório Git.
3. Nenhuma especificação disponível na primeira execução.
4. Certificado HTTPS de desenvolvimento não confiável.
5. O .NET 10 cria `.slnx` por padrão.
6. O template `xunit` gera xUnit v2.
7. Erros de compilação durante a implementação: escape de `'\\'`, CS8631 em `Assert.Equal` e CA1822 no controller de teste.
8. A CLI sem argumentos encerrava com código 0 e sem nenhuma saída.
9. Acentos corrompidos na saída da CLI no console do Windows.
10. Logs do `HttpClient` em nível Information com stack traces completos.
11. Artefatos de exemplo nos templates.
12. **Primeiro CI falhou:** com histórico Git, o SDK acrescenta o hash do commit à versão (`0.1.0+af77d2f…`), e o teste de `--version` esperava só `0.1.0`. Localmente o teste tinha passado porque ainda não havia commits.
13. Aviso no CI: `actions/upload-artifact@v4` usa Node.js 20, que está descontinuado.
14. O push inicial foi negado (403): o GitHub CLI estava autenticado com uma conta sem permissão de escrita.
15. A documentação divergia da especificação mestre recebida depois: MCP descrito como cliente HTTP, multi-tenancy tratado como "adiado" e ausência de `PROJECT_STATUS.md`, da especificação de fase e da branch `develop`.

## 13. Problemas corrigidos

1. SDK 10.0.404 instalado via winget, com autorização.
2. `git init -b main`.
3. Especificação mestre registrada em `docs/specifications/ESPECIFICACAO-MESTRE.md`.
4. Os perfis `http` são o padrão (API 5100, Web 5200).
5. `dotnet new sln --format sln`.
6. Migração para `xunit.v3`.
7. Os três erros foram corrigidos; a supressão de CA1822 tem justificativa.
8. Sem argumentos, a CLI exibe a ajuda.
9. `Console.OutputEncoding = UTF8`.
10. Categoria `System.Net.Http.HttpClient` ajustada para Warning.
11. Arquivos de exemplo removidos.
12. O teste compara com a versão informacional completa. O hash foi mantido na saída porque ajuda a identificar a build (commit `53c4d5a`).
13. `upload-artifact@v7`.
14. O responsável autenticou a conta correta, e o push foi feito.
15. Documentação alinhada:
    - ADR-001 revisado, ARCHITECTURE.md (MCP/Worker e multi-tenancy), AGENTS.md (processo A–F, branches) e README.
    - Especificação da fase e `PROJECT_STATUS.md` criados.
    - Branch `develop` criada e CI estendido a ela.

## 14. Pendências

- Enviar `develop` e o alinhamento documental ao GitHub. Depende de autorização de push, e a execução do CI em `develop` só fica confirmada depois disso.
- **Nome comercial:** a especificação diz "pendente de definição", e o repositório se chama Gestalyn.
- `AllowedHosts: "*"` deve ser restringido na Fase 14.
- Itens cosméticos:
  - O JSON do `/health` escapa caracteres não ASCII (o JSON continua válido).
  - Parte da ajuda do System.CommandLine aparece em inglês.
  - No Windows, uma conexão recusada em `localhost` leva cerca de 4 s antes de a Web mostrar "Indisponível".
- Terminais abertos antes da instalação do SDK precisam ser reabertos.

## 15. Critérios atendidos

| Critério (especificação mestre, Fase 01) | Evidência |
|---|---|
| Solution criada | `ProjectManager.sln` |
| Projetos compilando | Build Release com 0 avisos e 0 erros, local e no CI |
| Referências corretas | `ProjectReferenceRulesTests` 7/7 e teste de mutação |
| API iniciando | `/health` 200 em Development e Production |
| Web iniciando | `/` e `/status` renderizadas, com a API consultada por HTTP |
| CLI executando | `projectctl --help` e `--version` com código 0 |
| Testes executando | 18/18 local e no CI |
| OpenAPI disponível | `/openapi/v1.json` 200 em Development |
| CI executando build e testes | Execuções 42897692133 e 42901411495 com sucesso |
| Documentação inicial criada | README, AGENTS.md, arquitetura, ADR, especificações, status e relatório |

## 16. Critérios não atendidos

Nenhum.

## 17. Riscos identificados

| Risco | Impacto | Mitigação |
|---|---|---|
| Repositório **público**: código, histórico, especificação mestre e e-mail de autor ficam visíveis | Exposição do plano de produto | O responsável pode tornar o repositório privado; segredos nunca são versionados |
| Isolamento multi-tenant depende de disciplina em todas as consultas | Vazamento entre organizações | Fase 02: filtros globais por organização, testes cross-tenant obrigatórios e avaliação de Row-Level Security |
| Testes com PostgreSQL exigem Docker no CI | CI mais lento ou instável | Testcontainers no `ubuntu-latest` (Docker disponível); avaliar na Fase 02 |
| Regras de arquitetura ainda em `UnitTests` | Divergência com a estrutura alvo | Migrar para `ArchitectureTests` na Fase 13 (F13-004), como a especificação prevê |
| Autenticação de três tipos de cliente (Web, CLI/API, agentes) | Complexidade e falhas de segurança | ADRs nas Fases 03 e 10, antes da implementação |
| O `ubuntu-latest` migra para Ubuntu 26 | Mudanças no ambiente de CI | Acompanhar as execuções; fixar a versão do runner se houver quebra |

## 18. Estado do Git

- **Remoto:** `origin` → https://github.com/harrisonleandro/Gestalyn (público).
- **`main`:** `c6bd32a`, sincronizada com `origin/main`. Histórico: `af77d2f` (fundação), `53c4d5a` (correção do CI), `c6bd32a` (registro do CI).
- **`develop`:** criada a partir de `main`, com o commit local de alinhamento à especificação mestre. **Ainda não foi enviada** ao remoto.

## 19. Próxima fase recomendada

**Fase 02: Persistência e modelo de domínio**, após autorização. Recomendações:

1. Trabalhar em `feature/fase-02-persistencia` a partir de `develop`.
2. Criar `docs/specifications/FASE-02-PERSISTENCIA.md`, com o recorte mínimo de entidades da fase (organizações e membros, base para o isolamento). Não criar todas as entidades futuras.
3. Registrar ADRs de persistência (EF Core + Npgsql, migrations, auditoria `CreatedAt`/`UpdatedAt`/autor) e de testes com PostgreSQL (Docker Compose local e Testcontainers no CI).
4. Adicionar o health check do PostgreSQL somente quando o banco estiver configurado.
