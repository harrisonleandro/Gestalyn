# Especificações

| Documento | Conteúdo |
|---|---|
| [ESPECIFICACAO-MESTRE.md](ESPECIFICACAO-MESTRE.md) | **Especificação funcional e técnica aprovada.** Decisões arquiteturais, modelo funcional, contratos da API, roadmap das 14 fases e processo obrigatório. É a fonte de verdade do projeto. |
| [FASE-01-FUNDACAO.md](FASE-01-FUNDACAO.md) | Especificação da Fase 01: fundação da solução .NET. |
| [FASE-02-PERSISTENCIA.md](FASE-02-PERSISTENCIA.md) | Especificação da Fase 02: persistência e modelo de domínio (Completed). |
| [FASE-03-IDENTIDADE.md](FASE-03-IDENTIDADE.md) | Especificação da Fase 03: identidade e autenticação (Completed). |
| [FASE-04-ORGANIZACOES.md](FASE-04-ORGANIZACOES.md) | Especificação da Fase 04: organizações e permissões (In Review). |
| [FASE-05-PROJETOS.md](FASE-05-PROJETOS.md) | Especificação da Fase 05: projetos e equipes (In Review). |

Cada fase recebe um documento próprio (`FASE-NN-<TEMA>.md`) antes da implementação, com objetivo, escopo, fora de escopo, dependências, tarefas, arquivos previstos, critérios de aceitação, estratégia de testes, riscos e definição de conclusão (seção 12, etapa B, da especificação mestre).

## Histórico

- **2026-11-10, Fase 01 (primeira execução):** nenhuma especificação estava disponível no repositório. A fundação foi construída com as decisões do prompt da Fase 01, e modelo de multi-tenancy, autenticação e modelo de domínio foram adiados.
- **2026-11-10, especificação mestre recebida:** registrada em `ESPECIFICACAO-MESTRE.md`. A estratégia de multi-tenancy passa a estar definida (banco e schema compartilhados, `OrganizationId`, UUID, isolamento validado no backend). A estratégia detalhada de autenticação (sessão da Web, tokens da API e credenciais de agentes) continua a ser definida nas Fases 03 e 10.
- **2026-11-11, nome comercial:** a especificação mestre passou a registrar o nome comercial **Gestalyn** e a CLI `gestalyn` (antes `projectctl`), com aprovação do responsável ([ADR-005](../decisions/ADR-005-NOME-COMERCIAL-GESTALYN.md)).
