# Relatório de execução: Fase 01, Fundação .NET

- **Data:** 2026-11-10
- **Ambiente:** Windows 11 Pro 10.0.26200 · .NET SDK 10.0.404 (runtime 10.0.15) · Git 2.55.0

## 1. Resumo do que foi implementado

- Solução `ProjectManager.sln` com 6 projetos de código e 2 de testes, com as referências exigidas pela arquitetura. As regras de dependência são verificadas por teste.
- Configuração comum: `global.json`, `Directory.Build.props` (nullable, implicit usings, analisadores `Recommended`, avisos como erros), `Directory.Packages.props` (Central Package Management) e `.editorconfig` com convenções de nomenclatura.
- **API:** DI com pontos de composição por camada, OpenAPI só em Development, `/health` reportando apenas o que existe, Problem Details para 404/500 com `GlobalExceptionHandler`, logs estruturados (JSON fora de Development), HTTPS/HSTS fora de Development.
- **Web:** Blazor Web App em tema escuro, com layout, navegação (Início, Status do sistema), selo de ambiente e página de status que consulta a API por HTTP via `HttpClient` tipado. A configuração `Api:BaseUrl` é validada na inicialização.
- **CLI:** executável `projectctl` com `--help`, `--version`, ajuda quando chamado sem argumentos e código de saída ≠ 0 para comandos desconhecidos. A estrutura está preparada para os grupos futuros, sem comandos falsos.
- **Testes:** 12 unitários e 6 de integração.
- **CI:** workflow do GitHub Actions (restore, build Release, testes, publicação dos `.trx`).
- **Documentação:** README, AGENTS.md, ARCHITECTURE.md, PROJECT_STRUCTURE.md, ADR-001, nota de ausência da especificação e este relatório.

## 2. Estrutura final do repositório

```
ProjectManager/
├── .claude/launch.json
├── .github/workflows/ci.yml
├── docs/
│   ├── architecture/ARCHITECTURE.md, PROJECT_STRUCTURE.md
│   ├── decisions/ADR-001-MODULAR-MONOLITH.md
│   ├── reports/FASE-01-RELATORIO.md
│   └── specifications/README.md
├── src/
│   ├── ProjectManager.Domain/
│   ├── ProjectManager.Application/       DependencyInjection.cs
│   ├── ProjectManager.Infrastructure/    DependencyInjection.cs
│   ├── ProjectManager.API/               Program.cs, ErrorHandling/, Health/
│   ├── ProjectManager.Web/               Program.cs, Api/, Components/, wwwroot/
│   └── ProjectManager.CLI/               Program.cs, CliApplication.cs
├── tests/
│   ├── ProjectManager.UnitTests/         Architecture/, Cli/
│   └── ProjectManager.IntegrationTests/  Api/, Infrastructure/
├── .editorconfig  .gitattributes  .gitignore
├── Directory.Build.props  Directory.Packages.props  global.json
├── AGENTS.md  README.md
└── ProjectManager.sln
```

Detalhes por arquivo em [PROJECT_STRUCTURE.md](../architecture/PROJECT_STRUCTURE.md).

## 3. Projetos criados

| Projeto | Tipo | Referências |
|---|---|---|
| ProjectManager.Domain | classlib | — |
| ProjectManager.Application | classlib | Domain |
| ProjectManager.Infrastructure | classlib | Application, Domain |
| ProjectManager.API | ASP.NET Core Web API (controllers) | Application, Infrastructure |
| ProjectManager.Web | Blazor Web App (Interactive Server) | nenhuma (HTTP) |
| ProjectManager.CLI | console (`projectctl`) | nenhuma (HTTP) |
| ProjectManager.UnitTests | xUnit v3 | CLI |
| ProjectManager.IntegrationTests | xUnit v3 | API |

## 4. Dependências adicionadas e justificativas

| Pacote | Versão | Projeto | Justificativa |
|---|---|---|---|
| Microsoft.AspNetCore.OpenApi | 10.0.15 | API | Documento OpenAPI 3.1 nativo do .NET 10; requisito da fase. |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.15 | Application, Infrastructure | Métodos `AddApplication`/`AddInfrastructure` sem depender do ASP.NET Core. |
| System.CommandLine | 2.0.15 | CLI | Parser oficial da Microsoft; fornece `--help`/`--version` padronizados. Linha 2.0.x, alinhada ao .NET 10. |
| xunit.v3 | 3.2.2 | testes | xUnit obrigatório. A v3 fornece `TestContext.Current.CancellationToken`. |
| xunit.runner.visualstudio | 3.1.5 | testes | Execução via `dotnet test`. |
| Microsoft.NET.Test.Sdk | 18.11.1 | testes | Infraestrutura de testes do `dotnet test`. |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.15 | IntegrationTests | `WebApplicationFactory` para hospedar a API em memória. |
| Bootstrap 5.3 (arquivos estáticos) | template | Web | Incluído pelo template Blazor; usado para layout e tema escuro. |

Já existem versões mais novas (xunit.v3 4.x, System.CommandLine 3.0, pacotes 11.0 do .NET 11), mas foram mantidas as linhas compatíveis e estáveis com o .NET 10 LTS. O pacote `coverlet.collector` do template foi removido porque a cobertura não é requisito desta fase.

## 5. Arquivos de configuração

`global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitattributes`, `.gitignore`, `.github/workflows/ci.yml`, `.claude/launch.json`, `src/*/appsettings*.json`, `src/*/Properties/launchSettings.json`.

## 6. Comandos executados (principais)

```
winget install --id Microsoft.DotNet.SDK.10 --exact     → SDK 10.0.404 instalado
git init -b main
dotnet new sln -n ProjectManager --format sln
dotnet new classlib|webapi --use-controllers|blazor --interactivity Server|console|xunit ...
dotnet sln add ... ; dotnet add <proj> reference ...
dotnet restore                                          → exit 0
dotnet build                                            → exit 0, 0 avisos, 0 erros
dotnet build -c Release                                 → exit 0, 0 avisos, 0 erros
dotnet test                                             → exit 0, 18/18 aprovados
dotnet run --project src/ProjectManager.CLI -- --version → "0.1.0", exit 0
projectctl.exe --help                                   → ajuda, exit 0
```

## 7. Resultado do build

```
dotnet build -c Release --no-restore
    0 Aviso(s)
    0 Erro(s)
```

Os 8 projetos compilam individualmente: Domain, Application, Infrastructure, API, Web, CLI, UnitTests e IntegrationTests.

## 8. Resultado dos testes

```
Aprovado!  – Com falha: 0, Aprovado: 12, Ignorado: 0, Total: 12 - ProjectManager.UnitTests.dll (net10.0)
Aprovado!  – Com falha: 0, Aprovado:  6, Ignorado: 0, Total:  6 - ProjectManager.IntegrationTests.dll (net10.0)
```

**Verificação de mutação:** com uma referência proibida (Web → Domain) adicionada temporariamente, `ProjectReferenceRulesTests` falhou (1 falha em 12). A referência foi removida em seguida.

**Validação em execução real:**

| Verificação | Resultado |
|---|---|
| API (Development) `GET /health` | 200, `{"status":"Healthy","version":"0.1.0",...,"checks":[{"name":"self",...}]}` |
| API (Development) `GET /openapi/v1.json` | 200, `"openapi": "3.1.1"` |
| API `GET /rota-inexistente` | 404 `application/problem+json` com `traceId` |
| API (Production) `/health` / `/openapi/v1.json` | 200 / 404; logs em JSON |
| Web (Development) `/` e `/status` | Renderizadas em tema escuro; card da API "Healthy · 0.1.0" via HTTP |
| Web com a API parada | Card mostra "Indisponível: Não foi possível conectar à API."; aviso estruturado no log |
| Web (Production) sem `Api:BaseUrl` | Não inicia: `OptionsValidationException ... 'BaseUrl' ... required` |
| CLI `--version` / `--help` / sem argumentos / comando inválido | 0 / 0 / 0 (ajuda) / 1 |

## 9. Problemas encontrados

1. .NET SDK ausente na máquina.
2. Diretório sem repositório Git.
3. Especificação técnica v1.0 ausente.
4. Certificado HTTPS de desenvolvimento presente, mas não confiável.
5. O .NET 10 cria `.slnx` por padrão; a especificação exige `ProjectManager.sln`.
6. O template `xunit` gera projetos xUnit v2.
7. Erros de compilação durante a implementação: escape de `'\\'`, nulabilidade em `Assert.Equal` (CS8631) e CA1822 na ação do controller de teste.
8. A CLI sem argumentos encerrava com código 0 e sem nenhuma saída.
9. Acentos corrompidos na saída da CLI no console do Windows.
10. Logs do `HttpClient` em nível Information com stack traces completos, muito verbosos.
11. Artefatos de exemplo dos templates (WeatherForecast, Counter, Weather, Class1, UnitTest1).
12. **Primeira execução do CI falhou** ([run 42897140748](https://github.com/harrisonleandro/Gestalyn/actions/runs/42897140748)). O teste `--version` esperava `0.1.0`, mas recebeu `0.1.0+af77d2f…`: com o repositório já tendo um commit, o SDK acrescenta o hash à versão informacional. Localmente o teste tinha passado porque ainda não havia commits. A execução também avisou que `actions/upload-artifact@v4` usa Node.js 20, que está descontinuado.

## 10. Problemas corrigidos

1. SDK 10.0.404 instalado via winget (autorizado na aprovação do plano).
2. `git init -b main`.
3. Ausência registrada em `docs/specifications/README.md`; decisões dependentes foram adiadas, não presumidas.
4. Perfis `http` usados como padrão (API 5100, Web 5200); `https` continua disponível.
5. `dotnet new sln --format sln`.
6. Migrado para `xunit.v3` com Central Package Management.
7. Os três erros foram corrigidos; a supressão de CA1822 tem justificativa.
8. O comando raiz exibe a ajuda.
9. `Console.OutputEncoding = UTF8`.
10. Categoria `System.Net.Http.HttpClient` ajustada para Warning; o `ApiHealthClient` mantém o próprio log de aviso.
11. Arquivos de exemplo removidos (justificativa: implementações fictícias proibidas pela fase).
12. O teste agora compara a saída com a versão informacional completa. Incluir o commit no `--version` foi mantido de propósito, porque ajuda a diagnosticar. O workflow passou a usar `actions/upload-artifact@v7`, que roda em Node.js 24.

## 11. Pendências

- **Especificação técnica v1.0** ainda precisa ser adicionada.
- `AllowedHosts: "*"` deve ser restringido ao definir a hospedagem.
- Detalhes cosméticos conhecidos:
  - O JSON do `/health` escapa caracteres não ASCII (`á`); o JSON continua válido.
  - Parte dos textos padrão do System.CommandLine aparece em inglês ("Show help and usage information"), porque a localização da biblioteca é parcial.
  - No Windows, uma conexão recusada em `localhost` leva cerca de 4 s (tentativa IPv6 e depois IPv4) antes de a Web mostrar "Indisponível".
- Terminais abertos antes da instalação do SDK precisam ser reabertos para enxergar `dotnet` no PATH.

## 12. Critérios de aceitação atendidos

- [x] Estrutura de diretórios e solução conforme a especificação (sem MCP/Worker).
- [x] Referências entre projetos conforme a seção 6, verificadas por teste.
- [x] Nullable, implicit usings, `.editorconfig`, `Directory.Build.props`, convenções de nomenclatura.
- [x] Domain, Application e Infrastructure compilam, sem entidades artificiais nem banco definitivo.
- [x] A API inicia, tem DI, OpenAPI (Development), health check honesto, erros padronizados e configuração por ambiente.
- [x] A Web inicia, com layout, navegação, página inicial identificando o ProjectManager, ambiente identificado, tema escuro e consumo da API por HTTP.
- [x] A CLI executa `--help` e `--version`, a estrutura está preparada para os grupos futuros e não há comandos falsos.
- [x] `dotnet restore`, `dotnet build` e `dotnet test` terminam com código 0.
- [x] Testes unitários e de integração executam; há testes de infraestrutura da API e da CLI.
- [x] Workflow de CI criado (sem publicação em produção) e aprovado no GitHub Actions: [run 42897692133](https://github.com/harrisonleandro/Gestalyn/actions/runs/42897692133), commit `53c4d5a`, build ok, 12/12 unitários e 6/6 integração.
- [x] Código publicado em https://github.com/harrisonleandro/Gestalyn (público), branch `main`.
- [x] README, AGENTS.md, ARCHITECTURE.md, PROJECT_STRUCTURE.md e ADR-001 criados.
- [x] Nenhum segredo no repositório; nenhuma credencial criada.

## 13. Critérios não atendidos

Nenhum. Todos os critérios obrigatórios foram atendidos, inclusive a execução do CI no GitHub Actions.

## 14. Recomendação para a próxima fase

1. Adicionar a especificação técnica v1.0 em `docs/specifications/`.
2. Confirmar se **Gestalyn** (nome do repositório) é o nome comercial e, se for, atualizar a documentação.
3. Antes de codificar a Fase 02, registrar ADRs para:
   - (a) o modelo de multi-tenancy;
   - (b) a estratégia de autenticação para Web, CLI e agentes (Identity com cookies para Web e tokens para CLI/MCP);
   - (c) a persistência (PostgreSQL + EF Core, migrações).
4. Para PostgreSQL local, usar Docker Compose (o Docker já está instalado). Para os testes de integração com banco, usar Testcontainers.
5. Adicionar o health check do PostgreSQL somente quando o banco for configurado.
