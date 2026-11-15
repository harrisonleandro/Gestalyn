# Telas da Web

Mapa das telas da Fase 09 ([ADR-015](../decisions/ADR-015-INTERFACE-WEB.md)).
- **Acesso:** todas exigem login, exceto as de conta e a de status.
- **Autorização:** as ações aparecem conforme as permissões que a API devolve, e a API valida cada uma.
- **Organização:** `{org}` é o ID da organização ativa.

## Conta e organizações

| Rota | Tela | Ações (permissão) |
|---|---|---|
| `/` | Início: sem login, entrar ou criar conta; com login, vai para a última organização ou para `/organizacoes` | — |
| `/conta/entrar`, `/conta/cadastro`, `/conta/esqueci-a-senha`, `/conta/redefinir-senha`, `/conta/confirmar-email`, `/conta/reenviar-confirmacao` | Conta (Fase 03) | — |
| `/conta/perfil` | Perfil | — |
| `/convites/aceitar` | Aceitar convite | — |
| `/organizacoes` | Organizações do usuário | Criar organização |
| `/o/{org}` | Visão geral: projetos ativos com progresso, abertas, atrasadas, concluídas em 7 dias, minhas tarefas | Novo projeto (`projects.create`) |
| `/o/{org}/configuracoes` | Organização: dados, membros, convites, papéis | Renomear (`organization.update`); papel (`members.update_role`); remover (`members.remove`); convidar e revogar (`members.invite`); sair |
| `/o/{org}/atividades` | Atividades da organização, com filtros | Leitura (`activity.read_all`) |

## Projetos

| Rota | Tela | Ações (permissão) |
|---|---|---|
| `/o/{org}/projetos` | Lista com filtros (etiqueta, prioridade, arquivados) e paginação | Novo projeto (`projects.create`) |
| `/o/{org}/projetos/novo` | Novo projeto | Criar (`projects.create`) |
| `/o/{org}/projetos/{id}` | Visão geral: progresso, tarefas por estado, atrasadas, a vencer, bloqueadas, ritmo semanal, tempo de ciclo, marcos, atividade recente | — |
| `…/quadro` | Quadro por estado, com filtros | Mover (`tasks.write`; transições com aprovação só com `tasks.approve`); nova tarefa |
| `…/tarefas` | Lista com filtros, ordenação e paginação | Nova tarefa (`tasks.write`) |
| `…/tarefas/nova` | Nova tarefa | Criar (`tasks.write`) |
| `…/planejamento` | Fases e marcos, com progresso | Criar, editar, reordenar, excluir, marcar marco atingido (`project.plan`) |
| `…/atividades` | Atividades do projeto, com filtros | Registrar nota ou reunião (`documents.write`) |
| `…/documentos` | Documentos | Novo documento (`documents.write`) |
| `…/decisoes` | Decisões, com filtro de situação | Propor (`documents.write`) |
| `…/workflow` | Estados, transições, configurações; aviso de estado sem caminho até um estado final | Tudo com `workflow.manage` |
| `…/configuracoes` | Dados, equipe, repositórios, arquivamento | Editar e repositórios (`project.update`); equipe (`project.members.manage`); arquivar (`project.archive`) |

## Recursos por ID

| Rota | Tela | Ações (permissão) |
|---|---|---|
| `/o/{org}/tarefas/{id}` | Tarefa: estado e transições disponíveis, descrição, comentários, histórico, detalhes, responsáveis, dependências, anexos | Mover (as transições disponíveis); editar, responsáveis, dependências, anexos (`tasks.write`, fora de estado final); comentar (`tasks.comment`); arquivar (`tasks.archive`) |
| `/o/{org}/tarefas/{id}/anexos/{anexo}` | Download do anexo, pela Web | Leitura do projeto |
| `/o/{org}/documentos/{id}` | Documento, versões, versão antiga (`?versao=N`) | Editar pela versão esperada (`documents.write`); arquivar e restaurar (`documents.manage`) |
| `/o/{org}/decisoes/{id}` | Decisão | Editar proposta (`documents.write`); aceitar, rejeitar, substituir (`documents.manage`) |

## Fora desta fase

As telas de agentes, credenciais e integrações (seção 8.1) entram nas Fases 10 e 12, com a API delas. Até lá, o menu não as mostra.
