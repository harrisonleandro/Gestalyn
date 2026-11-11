# ADR-004: Testes de integração com PostgreSQL real (Testcontainers)

- **Status:** Aceito
- **Data:** 2026-11-10
- **Fase:** 02 (Persistência e modelo de domínio)

## Contexto

Os critérios da Fase 02 exigem testes de persistência e de isolamento. As garantias mais importantes dependem do PostgreSQL: FKs compostas, `xmin`, índices únicos, `CHECK`, `ON DELETE RESTRICT` e a tradução SQL dos filtros.

## Decisão

- Os testes de integração usam **Testcontainers.PostgreSql** com a imagem `postgres:18-alpine`, a mesma versão do ambiente local (`docker-compose.yml`).
- Um contêiner por execução da suíte (`AssemblyFixture` do xUnit v3), com as **migrations reais** aplicadas, não `EnsureCreated`. Assim as próprias migrations são testadas.
- Cada teste cria as próprias organizações, com IDs únicos, sem depender de limpeza nem de ordem de execução.
- Os testes da API (`WebApplicationFactory`) usam o mesmo contêiner.
- **Pré-requisito:** Docker em execução localmente. No CI (`ubuntu-latest`), o Docker já está disponível.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| EF Core InMemory | Não é relacional: ignora FKs, índices, `CHECK` e concorrência |
| SQLite | Dialeto e restrições diferentes; não suporta `xmin` nem reproduz o PostgreSQL |
| Banco compartilhado fixo | Estado residual entre execuções, dependência de ambiente e credenciais |

## Consequências

- Os testes de integração não rodam sem Docker. Os unitários continuam independentes.
- Cada execução leva alguns segundos a mais para iniciar o contêiner.
- Na Fase 13, os testes de arquitetura e end-to-end podem reutilizar a mesma fixture.
