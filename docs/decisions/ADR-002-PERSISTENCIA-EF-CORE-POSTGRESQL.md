# ADR-002: Persistência com EF Core e PostgreSQL

- **Status:** Aceito
- **Data:** 2026-11-10
- **Fase:** 02 (Persistência e modelo de domínio)

## Contexto

A [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md) define PostgreSQL, Entity Framework Core e Npgsql, identificadores UUID, operações transacionais, restrições de integridade e controle de concorrência (seções 2, 6.5 e 10). Faltava definir convenções e políticas concretas.

## Decisão

| Tema | Decisão |
|---|---|
| Provedor | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x sobre EF Core 10.0.x |
| Contexto | `ProjectManagerDbContext` único, em `Infrastructure/Persistence`. Configurações por entidade em `IEntityTypeConfiguration<T>`. |
| Nomenclatura | `snake_case` em tabelas, colunas, chaves e índices (`EFCore.NamingConventions`), seguindo o padrão do PostgreSQL e dispensando aspas no SQL |
| Identificadores | `Guid` **UUID v7** gerado no domínio (`Guid.CreateVersion7()`), com `ValueGeneratedNever`. É ordenável no tempo, o que reduz a fragmentação de índices, e o ID existe antes da gravação. |
| Datas | `DateTimeOffset` em UTC, gravado em `timestamp with time zone` |
| Auditoria | `CreatedAt` e `UpdatedAt` preenchidos no `SaveChanges` via `TimeProvider`. `CreatedAt` nunca é alterado. O autor (`CreatedBy`/`UpdatedBy`) entra quando houver identidade (Fases 03 e 08). |
| Concorrência | Propriedade sombra `Version` mapeada para a coluna de sistema `xmin` (`IsRowVersion`). Atualizações concorrentes geram `DbUpdateConcurrencyException`, base para ETags na API. |
| Exclusão | `ON DELETE RESTRICT` em todas as FKs. Arquivamento lógico nas fases de cada entidade, conforme a especificação. |
| Migrations | Em `Infrastructure/Persistence/Migrations`, geradas com `dotnet-ef` (ferramenta local em `dotnet-tools.json`). `DesignTimeDbContextFactory` permite gerar migrations sem banco. **Migrations não são aplicadas automaticamente na inicialização da aplicação**; a aplicação é explícita (`dotnet ef database update` em desenvolvimento; política de produção na Fase 14). |
| Configuração | Connection string `ConnectionStrings:ProjectManager`, obrigatória e validada na inicialização. Em desenvolvimento fica em `dotnet user-secrets`; nos demais ambientes, em variável de ambiente ou cofre de segredos. |
| Código gerado | Os arquivos de migrations são marcados como `generated_code` no `.editorconfig`; o restante da solução mantém os analisadores e os avisos como erros. |

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Identificadores sequenciais (`bigint`) | Contraria a especificação (UUID); IDs previsíveis expõem volume e facilitam enumeração |
| UUID v4 aleatório | Inserções espalhadas pelo índice; o v7 mantém a unicidade com ordenação temporal |
| Coluna `row_version` explícita | O `xmin` é nativo do PostgreSQL, não ocupa espaço e é atualizado automaticamente |
| Aplicar migrations na inicialização | Arriscado com múltiplas instâncias e em produção; falhas de migration derrubariam a aplicação |

## Consequências

- O UUID v7 revela o instante aproximado de criação. É aceitável para identificadores internos.
- O `xmin` muda a cada atualização da linha, inclusive por processos de manutenção do PostgreSQL. Isso pode gerar conflitos de concorrência raros e legítimos.
- Toda alteração de modelo exige uma migration. O teste `Migrations_AreFullyAppliedAndMatchTheModel` falha se o modelo divergir das migrations.
