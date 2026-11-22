# Telas da Web

Mapa das telas das Fases 09 e 10 ([ADR-015](../decisions/ADR-015-INTERFACE-WEB.md) e [ADR-016](../decisions/ADR-016-AGENTES.md)).
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

## Agentes

Telas da Fase 10 ([ADR-016](../decisions/ADR-016-AGENTES.md), D13). Todas exigem `agents.manage`; sem ela, o menu não mostra "Agentes" e a tela responde "Sem permissão" sem consultar a API. Todas as respostas levam `Cache-Control: no-store`.

| Rota | Tela | Ações (permissão) |
|---|---|---|
| `/o/{org}/agentes` | Lista de agentes: situação, credenciais ativas, projetos | Criar agente (`agents.manage`) |
| `/o/{org}/agentes/{id}` | Agente: credenciais (nome, prefixo, escopos, situação, expiração, último uso), projetos concedidos, operações recentes, dados e situação | Editar; desativar (com confirmação) e reativar; criar credencial; revogar credencial (com confirmação); conceder, alterar e remover acesso a projeto (`agents.manage` e, no projeto, `project.members.manage`, validado pela API). Papéis oferecidos: Colaborador e Observador |
| `/o/{org}/agentes/{id}/operacoes` | Operações do agente, inclusive as recusadas: modelo da rota, credencial, status, resultado e código da autorização, duração, repetição de idempotência. Filtros de resultado e período, com paginação | Leitura (`agents.manage`) |

**Token de credencial:** aparece uma única vez, na resposta do próprio formulário de criação, sem redirecionamento. Não vai para URL, aviso, sessão, cookie, armazenamento do navegador nem log. O botão "Copiar" usa a Clipboard API (`wwwroot/js/app.js`). A tela avisa que o token não volta e orienta criar uma credencial nova e revogar a antiga se ele se perder.

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
| `…/relatorios` | Relatórios de trabalho de agentes, com os níveis de evidência e paginação | Leitura (`project.read`) |
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
| `/o/{org}/relatorios/{id}` | Relatório de trabalho: níveis de evidência, autor "Agente: nome", resumo, alterações (Markdown), arquivos, testes e commit declarados, estado da tarefa ao receber e estado sugerido | Leitura (`project.read`) |

**Autoria de agentes:** na tarefa ("Criada por"), no histórico de estados e nas atividades, o que um agente fez aparece como "Agente: nome". A tarefa mostra também os relatórios de agentes dela.

**Níveis de evidência dos relatórios** (D8), sempre separados: "Declarado pelo agente", "Recebido e validado pela API", "Testes não verificados", "CI não verificado" e "Aprovação humana pendente" ou "Aprovado por {pessoa} em {data}". Sem tarefa, a aprovação humana não se aplica.

## Fora desta fase

As telas de integrações (seção 8.1) entram na Fase 12, com a API delas. Até lá, o menu não as mostra.
