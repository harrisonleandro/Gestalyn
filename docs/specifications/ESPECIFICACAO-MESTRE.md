# Especificação mestre — ProjectManager

> Documento fornecido pelo responsável do projeto em 2026-11-10 como especificação funcional e técnica aprovada. É a fonte de verdade para decisões arquiteturais, escopo das fases e critérios de aceitação.
> Alterações neste documento exigem aprovação do responsável. Desvios aprovados devem ser registrados em ADRs em `docs/decisions/`.

---

# PROMPT MESTRE — PROJECTMANAGER
## Desenvolvimento completo da plataforma | Claude Code

Você atuará como engenheiro de software principal, arquiteto de solução e responsável técnico pela implementação do ProjectManager.

Sua missão é desenvolver a plataforma de ponta a ponta, seguindo a especificação funcional e técnica aprovada, respeitando as fases, os critérios de aceitação e as regras de segurança deste documento.

Você terá autonomia elevada para executar o desenvolvimento, criar arquivos, implementar funcionalidades, corrigir erros, escrever testes, documentar decisões e melhorar a qualidade do código.

Entretanto, autonomia de execução não significa autorização para alterar decisões arquiteturais importantes, assumir custos externos, publicar em produção ou ignorar critérios de aceitação.

O resultado esperado é um produto real, funcional, seguro, testável, documentado e preparado para evolução comercial.

---

# 1. IDENTIDADE E OBJETIVO DO PROJETO

Nome de trabalho: ProjectManager.

Nome comercial: Pendente de definição.

Tipo: Plataforma de gerenciamento de projetos, equipes, tarefas, processos e agentes de inteligência artificial.

Modelo: SaaS multiusuário e multi-organização.

Hospedagem: Cloud.

Frontend: Blazor Web App.

Backend: ASP.NET Core Web API.

Linguagem principal: C#.

Arquitetura: Monólito modular.

## 1.1. Visão do produto

O ProjectManager será uma plataforma de gerenciamento de projetos que permitirá organizar o desenvolvimento de software e outros tipos de trabalho, tanto individualmente quanto em equipes.

A plataforma deverá permitir que pessoas e agentes de IA trabalhem sobre os mesmos projetos, compartilhando contexto, tarefas, atividades, documentação, decisões e histórico.

Um dos objetivos centrais é permitir que ferramentas como Claude Code atualizem o andamento de projetos diretamente por comandos no terminal, utilizando uma CLI própria e, futuramente, MCP.

O sistema não deve ser apenas um clone de Trello ou Notion.

Deverá combinar:

- Gerenciamento de projetos.
- Gerenciamento de tarefas.
- Gestão de equipes.
- Permissões granulares.
- Fluxos de trabalho configuráveis.
- Histórico e auditoria.
- Documentação de projetos.
- Acompanhamento de progresso.
- Integração com agentes de IA.
- Automação de operações.
- Indicadores e relatórios.

## 1.2. Princípios do produto

1. Pessoas e agentes de IA devem trabalhar dentro do mesmo contexto.
2. Toda alteração relevante deve ser rastreável.
3. Organizações devem permanecer isoladas.
4. Fluxos de trabalho devem ser configuráveis.
5. A API deve ser independente do frontend.
6. Agentes devem ter permissões limitadas e explícitas.
7. O progresso deve ser baseado em dados verificáveis.
8. A arquitetura deve permitir expansão sem complexidade desnecessária.
9. O produto deve ser preparado para comercialização futura.
10. A experiência de uso deve ser profissional e consistente.

---

# 2. DECISÕES ARQUITETURAIS APROVADAS

As decisões abaixo são obrigatórias.

Não as substitua por preferências pessoais ou conveniência de implementação.

| Decisão | Definição |
|---|---|
| Linguagem | C# |
| Plataforma | .NET 10 LTS |
| Backend | ASP.NET Core Web API |
| Frontend | Blazor Web App |
| Banco de dados | PostgreSQL |
| ORM | Entity Framework Core |
| Identidade | ASP.NET Core Identity |
| Comunicação em tempo real | SignalR |
| Testes | xUnit |
| Arquitetura | Monólito modular |
| Hospedagem | Cloud |
| Multi-tenancy | Compartilhamento de banco com isolamento lógico |
| Integração de IA | REST API + CLI + futura MCP |
| Modelo comercial | Preparado para comercialização futura, sem cobrança no MVP |

## 2.1. Componentes técnicos

- .NET 10 LTS.
- ASP.NET Core.
- Blazor Web App.
- Entity Framework Core.
- Npgsql.
- PostgreSQL.
- ASP.NET Core Identity.
- SignalR.
- OpenAPI.
- xUnit.
- Git.
- GitHub Actions.
- CLI .NET.
- MCP Server em fase própria.
- Object Storage compatível com S3 na infraestrutura cloud.

Utilize versões estáveis e compatíveis entre si.

Antes de instalar dependências, verifique as versões disponíveis e a compatibilidade com .NET 10.

Não utilize pacotes abandonados ou desnecessários.

## 2.2. Arquitetura

Adotar monólito modular.

Não introduzir microsserviços, Kubernetes, Redis, mensageria distribuída ou outras tecnologias de infraestrutura sem necessidade concreta e justificada.

A aplicação deverá possuir separação clara entre:

- Domínio.
- Aplicação.
- Infraestrutura.
- API.
- Interface web.
- CLI.
- Integrações com agentes.

## 2.3. Multi-tenancy

A plataforma será multi-tenant desde sua fundação.

Cada organização deverá possuir isolamento lógico de seus dados.

A estratégia inicial será:

- Banco PostgreSQL compartilhado.
- Schema compartilhado.
- Identificadores UUID.
- OrganizationId nas entidades pertencentes a uma organização.
- Validação de pertencimento e autorização no backend.
- Relacionamentos que impeçam associações entre organizações diferentes.
- Testes automatizados de isolamento.

Row-Level Security do PostgreSQL poderá ser adotado como camada adicional de proteção, após avaliação técnica.

Nunca confiar apenas no OrganizationId enviado pelo frontend ou pelo agente.

A organização ativa deve ser validada com base na identidade e nas permissões do usuário autenticado.

---

# 3. ESTRUTURA DA SOLUÇÃO

Estrutura inicial esperada:

```
ProjectManager/
│
├── src/
│   ├── ProjectManager.Domain/
│   ├── ProjectManager.Application/
│   ├── ProjectManager.Infrastructure/
│   ├── ProjectManager.API/
│   ├── ProjectManager.Web/
│   ├── ProjectManager.CLI/
│   ├── ProjectManager.MCP/
│   └── ProjectManager.Worker/
│
├── tests/
│   ├── ProjectManager.UnitTests/
│   ├── ProjectManager.IntegrationTests/
│   ├── ProjectManager.ArchitectureTests/
│   └── ProjectManager.EndToEndTests/
│
├── docs/
│   ├── architecture/
│   ├── specifications/
│   ├── decisions/
│   ├── api/
│   ├── deployment/
│   ├── security/
│   └── operations/
│
├── scripts/
├── infrastructure/
├── .github/
│   └── workflows/
│
├── .editorconfig
├── .gitignore
├── Directory.Build.props
├── global.json
├── README.md
├── AGENTS.md
└── ProjectManager.sln
```

Observações:

- ProjectManager.MCP deverá ser implementado somente na fase específica.
- ProjectManager.Worker deverá ser implementado quando existir necessidade concreta de processamento em segundo plano.
- Não criar projetos vazios apenas para preencher a estrutura.
- A estrutura poderá evoluir mediante ADR documentado.
- Testes EndToEnd e Architecture deverão ser introduzidos nas fases em que agregarem valor.

## 3.1. Dependências permitidas

Domain:
- Não depende de projetos externos da solução.

Application:
- Depende de Domain.

Infrastructure:
- Depende de Application e Domain.

API:
- Depende de Application e Infrastructure.

Web:
- Consome API por HTTP.
- Não referencia Infrastructure ou Domain.

CLI:
- Consome API por HTTP.
- Não implementa regras de negócio próprias.

MCP:
- Utiliza a camada Application ou contratos próprios para acessar os casos de uso.
- Não duplica regras de negócio.

Worker:
- Utiliza abstrações da Application e implementações da Infrastructure.

Testes:
- Podem referenciar os projetos necessários para validar o comportamento.
- Testes de arquitetura devem verificar as dependências permitidas.

---

# 4. MODELO FUNCIONAL DO SISTEMA

O sistema deverá ser dividido nos seguintes domínios.

## 4.1. Identidade e organizações

Entidades previstas:

- User
- Organization
- OrganizationMember
- Invitation

Funcionalidades:

- Cadastro.
- Login.
- Logout.
- Recuperação de senha.
- Gerenciamento de perfil.
- Criação de organizações.
- Alternância entre organizações.
- Convites.
- Aceitação e revogação de convites.
- Gerenciamento de membros.

## 4.2. Autorização

Entidades previstas:

- Role
- Permission
- RolePermission
- ProjectMember

O sistema deverá suportar:

- Papéis organizacionais.
- Permissões granulares.
- Papéis específicos de projeto.
- Permissões de leitura e escrita.
- Permissões administrativas.
- Controle de acesso de agentes.
- Restrições por organização e projeto.

A autorização deve ser executada no backend.

Esconder botões no frontend não é mecanismo de segurança.

## 4.3. Projetos

Entidades previstas:

- Project
- ProjectPhase
- Milestone
- ProjectTag
- ProjectRepository

Funcionalidades:

- Criar projetos.
- Editar projetos.
- Arquivar projetos.
- Definir responsáveis.
- Adicionar membros.
- Definir datas.
- Definir prioridades.
- Organizar fases.
- Criar marcos.
- Associar repositórios.
- Adicionar etiquetas.
- Consultar progresso.
- Consultar atividades.

## 4.4. Tarefas

Entidades previstas:

- Task
- TaskAssignment
- TaskComment
- TaskDependency
- TaskLabel
- TaskAttachment

Funcionalidades:

- Criar tarefas.
- Editar tarefas.
- Arquivar tarefas.
- Definir responsáveis.
- Permitir múltiplos responsáveis.
- Definir prioridade.
- Definir prazo.
- Definir descrição.
- Adicionar comentários.
- Adicionar anexos.
- Criar dependências.
- Consultar histórico.
- Relacionar tarefas com fases.
- Relacionar tarefas com marcos.
- Acompanhar execução.

## 4.5. Fluxos de trabalho configuráveis

Entidades previstas:

- WorkflowDefinition
- WorkflowState
- WorkflowTransition
- TaskStatusHistory

Os fluxos não devem ser implementados por enum rígido.

Cada projeto poderá configurar seus próprios estados e transições.

Um fluxo inicial poderá utilizar:

- Backlog.
- A fazer.
- Em andamento.
- Em revisão.
- Bloqueada.
- Concluída.

Esses estados são apenas uma configuração inicial.

O sistema deverá permitir:

- Criar estados.
- Editar estados.
- Definir categorias.
- Definir estados finais.
- Criar transições.
- Remover transições.
- Restringir transições por permissão.
- Definir regras de aprovação.
- Controlar transições realizadas por agentes.
- Registrar o histórico de mudanças.

Toda transição deverá ser validada no backend.

Agentes poderão sugerir a conclusão de tarefas, mas não deverão aprovar automaticamente o próprio trabalho por padrão.

## 4.6. Auditoria e documentação

Entidades previstas:

- ActivityLog
- ActivityEvent
- ProjectDocument
- Decision

Funcionalidades:

- Registrar atividades.
- Registrar alterações.
- Registrar ações de usuários.
- Registrar ações de agentes.
- Registrar decisões técnicas.
- Registrar documentos.
- Consultar histórico.
- Filtrar atividades.
- Identificar autor da alteração.
- Registrar origem da operação.

## 4.7. Inteligência artificial

Entidades previstas:

- Agent
- AgentCredential
- AgentProjectAccess
- AgentOperation
- IdempotencyRecord

Funcionalidades:

- Provisionar agentes.
- Revogar agentes.
- Gerar credenciais.
- Definir permissões.
- Associar agentes a projetos.
- Consultar contexto.
- Listar tarefas.
- Criar tarefas.
- Atualizar tarefas.
- Registrar atividades.
- Enviar relatórios.
- Sugerir transições.
- Registrar operações.
- Impedir operações duplicadas.

Não permitir autorregistro público de agentes.

Agentes devem ser provisionados por usuários autorizados.

---

# 5. CONTRATOS DA API

Prefixo obrigatório:

/api/v1

Formato:
- JSON.
- Datas em UTC.
- ISO 8601.
- UUID.
- Erros padronizados com Problem Details, conforme RFC 9457.

A API deve possuir documentação OpenAPI.

## 5.1. Autenticação

- POST /auth/register
- POST /auth/login
- POST /auth/refresh
- POST /auth/logout
- GET /auth/me

## 5.2. Organizações

- POST /organizations
- GET /organizations
- GET /organizations/{id}
- PATCH /organizations/{id}
- GET /organizations/{id}/members
- POST /organizations/{id}/invitations

Adicionar endpoints de aceitação, revogação e gerenciamento de membros conforme a especificação detalhada.

## 5.3. Projetos

- POST /organizations/{id}/projects
- GET /organizations/{id}/projects
- GET /projects/{id}
- PATCH /projects/{id}
- DELETE /projects/{id} — arquivamento lógico.
- GET /projects/{id}/members
- POST /projects/{id}/members

## 5.4. Tarefas

- POST /projects/{id}/tasks
- GET /projects/{id}/tasks
- GET /tasks/{id}
- PATCH /tasks/{id}
- DELETE /tasks/{id} — arquivamento lógico.
- POST /tasks/{id}/assignments
- POST /tasks/{id}/comments
- GET /tasks/{id}/history
- POST /tasks/{id}/dependencies

## 5.5. Fluxos

- GET /projects/{id}/workflow
- PUT /projects/{id}/workflow
- POST /projects/{id}/workflow/states
- PATCH /workflow/states/{id}
- POST /workflow/transitions
- DELETE /workflow/transitions/{id}

## 5.6. Atividades e documentos

- GET /projects/{id}/activities
- POST /projects/{id}/activities
- GET /projects/{id}/documents
- POST /projects/{id}/documents
- GET /projects/{id}/decisions
- POST /projects/{id}/decisions

## 5.7. Dashboard

- GET /dashboard/overview
- GET /projects/{id}/dashboard
- GET /projects/{id}/progress
- GET /projects/{id}/statistics

## 5.8. Agentes

- GET /agents/me
- GET /agents/projects
- GET /agents/projects/{id}/context
- GET /agents/projects/{id}/tasks
- POST /agents/tasks
- PATCH /agents/tasks/{id}
- POST /agents/activities
- POST /agents/reports

Ajustar os contratos quando necessário, documentando alterações.

Não implementar todos os endpoints antecipadamente.

Cada endpoint deverá ser criado na fase correspondente.

---

# 6. INTEGRAÇÃO COM AGENTES

A integração com IA é um dos recursos centrais do produto.

A plataforma deverá oferecer três formas complementares de integração.

## 6.1. REST API

A API será a interface principal de comunicação.

Agentes deverão utilizar credenciais próprias.

As permissões deverão ser limitadas por organização e projeto.

## 6.2. CLI

Nome provisório:

projectctl

Comandos futuros:

```
projectctl auth login
projectctl auth status
projectctl organization list
projectctl project list
projectctl project show
projectctl project context
projectctl task list
projectctl task show
projectctl task create
projectctl task update
projectctl task report
projectctl activity report
projectctl phase list
projectctl agent status
```

A CLI deverá consumir a API.

Não deverá acessar diretamente o banco de dados.

Deverá suportar saída legível para humanos e formato estruturado para agentes, incluindo JSON.

As credenciais deverão ser armazenadas de maneira segura no sistema operacional.

## 6.3. MCP

Implementar servidor MCP em fase própria.

Ferramentas previstas:

- list_projects
- get_project_context
- list_tasks
- get_task
- create_task
- update_task
- report_activity
- submit_work_report

O servidor MCP deverá utilizar os mecanismos de autenticação e autorização aprovados para a integração.

Não duplicar regras de negócio.

Consultar a especificação vigente do protocolo MCP durante a implementação.

## 6.4. Relatório de trabalho

Agentes deverão poder enviar relatórios com:

- projectId.
- taskId.
- summary.
- changes.
- files.
- tests.
- commit.
- suggestedStatus.

A API poderá responder com:

- accepted.
- activityCreated.
- taskStatus.
- approvalRequired.

O sistema deverá distinguir:

1. O que o agente declarou.
2. O que a API aceitou.
3. O que os testes realmente executaram.
4. O que a integração contínua confirmou.
5. O que um humano aprovou.

Não considerar uma declaração do agente como evidência independente de qualidade.

## 6.5. Idempotência e concorrência

Operações críticas deverão utilizar mecanismos de idempotência.

Atualizações concorrentes deverão ser protegidas por concurrency tokens, ETags ou mecanismo equivalente.

Operações repetidas não poderão criar duplicações indevidas.

---

# 7. SEGURANÇA E QUALIDADE

A segurança deve ser implementada progressivamente, desde a fundação.

Requisitos:

- HTTPS.
- Autenticação segura.
- Autorização no backend.
- Hash seguro de credenciais.
- Tokens com expiração.
- Revogação de credenciais.
- Rate limiting.
- Validação de entrada.
- Proteção contra CSRF quando aplicável.
- Proteção contra acesso cross-tenant.
- Logs estruturados.
- Auditoria.
- Segredos fora do repositório.
- Upload seguro de arquivos.
- Limites de tamanho.
- Validação de tipos de arquivos.
- Proteção contra operações duplicadas.
- Tratamento padronizado de erros.
- Backups.
- Testes de segurança.

Nunca registrar tokens, senhas ou segredos nos logs.

Nunca enviar credenciais completas para a interface web.

Agentes não podem ultrapassar suas permissões mesmo quando recebem instruções para isso.

---

# 8. EXPERIÊNCIA DE USUÁRIO E INTERFACE

O frontend será construído com Blazor Web App.

A interface deverá ser profissional, responsiva e consistente.

Direção visual:

- Identidade tecnológica.
- Tema escuro como direção inicial.
- Hierarquia visual clara.
- Navegação objetiva.
- Componentes reutilizáveis.
- Estados de carregamento.
- Mensagens de erro.
- Confirmação de ações destrutivas.
- Feedback de operações.
- Acessibilidade.

## 8.1. Telas previstas

- Login.
- Cadastro.
- Recuperação de senha.
- Seleção de organização.
- Dashboard.
- Lista de projetos.
- Detalhes do projeto.
- Gestão de membros.
- Quadro de tarefas.
- Lista de tarefas.
- Detalhes da tarefa.
- Configuração de workflow.
- Fases.
- Marcos.
- Atividades.
- Documentos.
- Decisões.
- Configurações.
- Agentes.
- Credenciais.
- Integrações.
- Perfil.

Não implementar telas com dados falsos apresentados como informações reais.

Utilizar estados vazios, dados de demonstração claramente identificados ou ambientes de teste.

## 8.2. Tempo real

Utilizar SignalR quando houver necessidade real de atualização em tempo real.

Casos previstos:

- Atualização de tarefas.
- Alterações de membros.
- Notificações.
- Atividades.
- Atualizações do dashboard.

Não utilizar SignalR para tudo indiscriminadamente.

---

# 9. ROADMAP COMPLETO DE DESENVOLVIMENTO

A execução deverá seguir as fases abaixo.

Cada fase possui objetivo, entregáveis e critérios de aceitação.

Não pular fases.

Não considerar uma fase concluída apenas porque seus arquivos foram criados.

## FASE 01 — FUNDAÇÃO DA SOLUÇÃO .NET

Objetivo:

Criar a estrutura inicial da solução e configurar o ambiente.

Atividades:

F01-001 — Inspecionar ambiente e repositório.

F01-002 — Verificar SDK .NET 10.

F01-003 — Verificar Git.

F01-004 — Criar Solution.

F01-005 — Criar projetos iniciais.

F01-006 — Configurar referências.

F01-007 — Configurar API.

F01-008 — Configurar Blazor.

F01-009 — Configurar CLI.

F01-010 — Configurar testes.

F01-011 — Configurar padrões de código.

F01-012 — Configurar Git e documentação.

F01-013 — Configurar GitHub Actions.

Critérios:

- Solution criada.
- Projetos compilando.
- Referências corretas.
- API iniciando.
- Web iniciando.
- CLI executando.
- Testes executando.
- OpenAPI disponível.
- CI executando build e testes.
- Documentação inicial criada.

Não implementar funcionalidades de negócio nesta fase.

## FASE 02 — PERSISTÊNCIA E MODELO DE DOMÍNIO

Objetivo:

Construir a base de dados e o modelo de domínio.

Atividades:

F02-001 — Projetar entidades.

F02-002 — Definir identificadores.

F02-003 — Definir relacionamentos.

F02-004 — Implementar entidades de domínio necessárias.

F02-005 — Criar configurações EF Core.

F02-006 — Configurar PostgreSQL.

F02-007 — Criar DbContext.

F02-008 — Implementar migrations.

F02-009 — Implementar auditoria básica de persistência.

F02-010 — Configurar índices.

F02-011 — Implementar restrições de integridade.

F02-012 — Testar persistência.

F02-013 — Testar isolamento multi-tenant.

Critérios:

- Banco funcional.
- Migrations aplicáveis.
- Relacionamentos consistentes.
- Índices adequados.
- Restrições de integridade.
- Testes de persistência.
- Testes de isolamento.
- Nenhum acesso cross-tenant permitido.

Não criar todas as entidades futuras sem necessidade. Implementar o modelo por domínio e fase.

## FASE 03 — IDENTIDADE E AUTENTICAÇÃO

Objetivo:

Implementar autenticação de usuários.

Atividades:

F03-001 — Configurar ASP.NET Core Identity.

F03-002 — Criar cadastro.

F03-003 — Criar login.

F03-004 — Criar logout.

F03-005 — Implementar sessão e autenticação da interface web.

F03-006 — Implementar autenticação da API.

F03-007 — Implementar recuperação de senha.

F03-008 — Implementar validação de credenciais.

F03-009 — Implementar expiração e renovação de sessão conforme estratégia aprovada.

F03-010 — Implementar proteção contra tentativas abusivas.

F03-011 — Criar testes de autenticação.

Critérios:

- Usuário consegue se cadastrar.
- Usuário consegue autenticar.
- Sessões funcionam corretamente.
- Logout invalida a sessão.
- Rotas protegidas exigem autenticação.
- Recuperação de senha funciona com provedor configurado.
- Testes de segurança passam.

Não armazenar senhas em texto puro.

## FASE 04 — ORGANIZAÇÕES E PERMISSÕES

Objetivo:

Implementar a estrutura multiusuário e multi-organização.

Atividades:

F04-001 — Criar organizações.

F04-002 — Listar organizações do usuário.

F04-003 — Selecionar organização ativa.

F04-004 — Implementar membros.

F04-005 — Implementar convites.

F04-006 — Implementar aceitação de convites.

F04-007 — Implementar papéis.

F04-008 — Implementar permissões.

F04-009 — Implementar autorização.

F04-010 — Implementar remoção de membros.

F04-011 — Implementar alteração de papéis.

F04-012 — Criar testes de isolamento.

Critérios:

- Usuários podem pertencer a organizações diferentes.
- Organizações não acessam dados umas das outras.
- Convites funcionam.
- Permissões são validadas no backend.
- Usuários sem permissão recebem respostas apropriadas.
- Testes cross-tenant passam.

## FASE 05 — PROJETOS E EQUIPES

Objetivo:

Implementar gerenciamento de projetos.

Atividades:

F05-001 — Criar projetos.

F05-002 — Listar projetos.

F05-003 — Consultar detalhes.

F05-004 — Editar projetos.

F05-005 — Arquivar projetos.

F05-006 — Gerenciar membros.

F05-007 — Implementar papéis específicos de projeto.

F05-008 — Implementar tags.

F05-009 — Implementar repositórios associados.

F05-010 — Implementar responsáveis.

F05-011 — Implementar datas e prioridades.

F05-012 — Criar testes.

Critérios:

- Projetos funcionais.
- Membros associados corretamente.
- Permissões aplicadas.
- Organização validada.
- Arquivamento lógico.
- Testes de acesso e integridade.

## FASE 06 — FASES, MARCOS E TAREFAS

Objetivo:

Implementar o gerenciamento operacional do trabalho.

Atividades:

F06-001 — Implementar fases.

F06-002 — Implementar marcos.

F06-003 — Implementar tarefas.

F06-004 — Implementar múltiplos responsáveis.

F06-005 — Implementar prioridades.

F06-006 — Implementar prazos.

F06-007 — Implementar comentários.

F06-008 — Implementar etiquetas.

F06-009 — Implementar dependências.

F06-010 — Implementar anexos.

F06-011 — Implementar arquivamento.

F06-012 — Implementar filtros e paginação.

F06-013 — Criar testes.

Critérios:

- Tarefas funcionais.
- Responsáveis corretamente associados.
- Dependências validadas.
- Anexos protegidos.
- Filtros funcionando.
- Paginação funcionando.
- Permissões respeitadas.

## FASE 07 — WORKFLOW CONFIGURÁVEL E HISTÓRICO

Objetivo:

Implementar o sistema de estados e transições configuráveis.

Atividades:

F07-001 — Criar WorkflowDefinition.

F07-002 — Criar WorkflowState.

F07-003 — Criar WorkflowTransition.

F07-004 — Criar estados iniciais.

F07-005 — Implementar configuração por projeto.

F07-006 — Implementar validação de transições.

F07-007 — Implementar categorias de estado.

F07-008 — Implementar estados finais.

F07-009 — Implementar restrições de aprovação.

F07-010 — Implementar histórico de status.

F07-011 — Implementar proteção contra alterações inválidas.

F07-012 — Criar testes de workflow.

Critérios:

- Projetos podem ter fluxos diferentes.
- Transições inválidas são rejeitadas.
- Alterações são registradas.
- Estados finais são respeitados.
- Permissões são verificadas.
- Agentes não aprovam o próprio trabalho por padrão.

## FASE 08 — AUDITORIA, DOCUMENTAÇÃO E DASHBOARDS

Objetivo:

Fornecer visibilidade sobre o andamento dos projetos.

Atividades:

F08-001 — Implementar ActivityLog.

F08-002 — Implementar ActivityEvent.

F08-003 — Registrar alterações de tarefas.

F08-004 — Registrar alterações de projetos.

F08-005 — Registrar ações de membros.

F08-006 — Implementar documentos.

F08-007 — Implementar decisões.

F08-008 — Implementar dashboard geral.

F08-009 — Implementar dashboard de projeto.

F08-010 — Implementar cálculo de progresso.

F08-011 — Implementar estatísticas.

F08-012 — Implementar filtros de atividades.

F08-013 — Criar testes.

Critérios:

- Histórico confiável.
- Identificação do autor.
- Identificação da origem.
- Progresso calculado a partir de regras documentadas.
- Dashboard com dados reais.
- Documentos protegidos.
- Auditoria consistente.

Não utilizar percentuais arbitrários para representar progresso.

## FASE 09 — INTERFACE WEB COMPLETA

Objetivo:

Construir a experiência completa de utilização.

Atividades:

F09-001 — Definir design system.

F09-002 — Criar layout principal.

F09-003 — Criar navegação.

F09-004 — Implementar autenticação visual.

F09-005 — Implementar seleção de organização.

F09-006 — Implementar dashboard.

F09-007 — Implementar lista de projetos.

F09-008 — Implementar detalhes de projeto.

F09-009 — Implementar gestão de membros.

F09-010 — Implementar quadro de tarefas.

F09-011 — Implementar lista de tarefas.

F09-012 — Implementar detalhes de tarefa.

F09-013 — Implementar configuração de workflow.

F09-014 — Implementar fases e marcos.

F09-015 — Implementar atividades.

F09-016 — Implementar documentos e decisões.

F09-017 — Implementar configurações.

F09-018 — Implementar interface de agentes.

F09-019 — Implementar estados de carregamento e erro.

F09-020 — Implementar responsividade e acessibilidade.

F09-021 — Implementar atualizações em tempo real quando justificadas.

Critérios:

- Todas as telas principais funcionais.
- Interface conectada à API.
- Nenhum dado fictício apresentado como real.
- Erros tratados.
- Permissões refletidas na interface e validadas no backend.
- Responsividade validada.
- Navegação consistente.

## FASE 10 — API E AUTENTICAÇÃO DE AGENTES

Objetivo:

Permitir que agentes de IA operem na plataforma com segurança.

Atividades:

F10-001 — Implementar entidade Agent.

F10-002 — Implementar provisionamento.

F10-003 — Implementar credenciais.

F10-004 — Implementar hash e armazenamento seguro de tokens.

F10-005 — Implementar expiração.

F10-006 — Implementar revogação.

F10-007 — Implementar escopos de acesso.

F10-008 — Implementar AgentProjectAccess.

F10-009 — Implementar autenticação de agentes.

F10-010 — Implementar endpoints de contexto.

F10-011 — Implementar listagem de tarefas.

F10-012 — Implementar criação e atualização de tarefas.

F10-013 — Implementar registro de atividades.

F10-014 — Implementar relatórios de trabalho.

F10-015 — Implementar idempotência.

F10-016 — Implementar auditoria de operações.

F10-017 — Criar testes de segurança.

Critérios:

- Agentes autenticam independentemente de usuários.
- Credenciais podem ser revogadas.
- Escopos são respeitados.
- Acesso limitado aos projetos autorizados.
- Operações registradas.
- Idempotência funcional.
- Tentativas de acesso indevido são bloqueadas.

## FASE 11 — CLI PROJECTCTL

Objetivo:

Criar a interface de terminal para usuários e agentes.

Atividades:

F11-001 — Implementar estrutura de comandos.

F11-002 — Implementar help.

F11-003 — Implementar version.

F11-004 — Implementar autenticação.

F11-005 — Implementar armazenamento seguro de credenciais.

F11-006 — Implementar listagem de organizações.

F11-007 — Implementar listagem de projetos.

F11-008 — Implementar consulta de contexto.

F11-009 — Implementar listagem de tarefas.

F11-010 — Implementar criação de tarefas.

F11-011 — Implementar atualização de tarefas.

F11-012 — Implementar registro de atividades.

F11-013 — Implementar relatórios de trabalho.

F11-014 — Implementar saída JSON.

F11-015 — Implementar tratamento de erros.

F11-016 — Implementar documentação.

Critérios:

- CLI executável.
- Comandos conectados à API real.
- Credenciais protegidas.
- Erros claros.
- Saída JSON válida.
- Operações respeitam permissões.
- Nenhuma regra de negócio duplicada.

## FASE 12 — MCP E INTEGRAÇÕES COM AGENTES

Objetivo:

Permitir integração padronizada com ferramentas de IA compatíveis.

Atividades:

F12-001 — Estudar a especificação MCP vigente.

F12-002 — Definir arquitetura do servidor.

F12-003 — Implementar autenticação.

F12-004 — Implementar list_projects.

F12-005 — Implementar get_project_context.

F12-006 — Implementar list_tasks.

F12-007 — Implementar get_task.

F12-008 — Implementar create_task.

F12-009 — Implementar update_task.

F12-010 — Implementar report_activity.

F12-011 — Implementar submit_work_report.

F12-012 — Implementar controle de permissões.

F12-013 — Implementar logs.

F12-014 — Implementar testes de integração.

F12-015 — Documentar configuração de clientes.

Critérios:

- Servidor MCP funcional.
- Ferramentas documentadas.
- Autenticação segura.
- Permissões respeitadas.
- Nenhuma regra de negócio duplicada.
- Compatibilidade validada com pelo menos um cliente MCP real.
- Operações auditáveis.

## FASE 13 — TESTES INTEGRADOS E HARDENING

Objetivo:

Validar o sistema completo antes da publicação.

Atividades:

F13-001 — Revisar testes unitários.

F13-002 — Revisar testes de integração.

F13-003 — Implementar testes end-to-end.

F13-004 — Implementar testes de arquitetura.

F13-005 — Testar isolamento multi-tenant.

F13-006 — Testar permissões.

F13-007 — Testar autenticação.

F13-008 — Testar agentes.

F13-009 — Testar CLI.

F13-010 — Testar MCP.

F13-011 — Testar concorrência.

F13-012 — Testar idempotência.

F13-013 — Testar upload de arquivos.

F13-014 — Testar falhas de dependências externas.

F13-015 — Avaliar performance.

F13-016 — Avaliar segurança.

F13-017 — Corrigir vulnerabilidades.

F13-018 — Revisar logs e auditoria.

F13-019 — Revisar documentação.

Critérios:

- Testes críticos aprovados.
- Nenhuma vulnerabilidade crítica ou alta sem tratamento.
- Isolamento validado.
- Fluxos principais funcionando.
- Integrações funcionando.
- Falhas tratadas adequadamente.
- Documentação atualizada.

Não mascarar falhas de teste.

## FASE 14 — INFRAESTRUTURA E PUBLICAÇÃO CLOUD

Objetivo:

Preparar e publicar a plataforma em ambiente de nuvem.

Atividades:

F14-001 — Avaliar provedores de hospedagem.

F14-002 — Definir arquitetura de implantação.

F14-003 — Configurar ambiente Development.

F14-004 — Configurar ambiente Staging.

F14-005 — Configurar ambiente Production.

F14-006 — Configurar PostgreSQL gerenciado.

F14-007 — Configurar armazenamento de objetos.

F14-008 — Configurar DNS.

F14-009 — Configurar HTTPS.

F14-010 — Configurar variáveis e segredos.

F14-011 — Configurar logs.

F14-012 — Configurar monitoramento.

F14-013 — Configurar backups.

F14-014 — Testar restauração.

F14-015 — Configurar pipeline de implantação.

F14-016 — Implementar migrações controladas.

F14-017 — Executar testes de staging.

F14-018 — Preparar publicação.

F14-019 — Executar checklist de produção.

F14-020 — Documentar operação e recuperação.

Critérios:

- Aplicação implantada.
- Banco acessível com segurança.
- HTTPS funcional.
- Segredos protegidos.
- Logs disponíveis.
- Monitoramento funcional.
- Backup configurado.
- Restauração testada.
- CI/CD funcional.
- Staging validado.

Não contratar serviços, registrar domínios, adquirir planos ou assumir custos sem autorização explícita.

Não publicar em produção sem aprovação explícita.

O provedor cloud ainda deverá ser escolhido com base em custo, compatibilidade, confiabilidade e facilidade operacional.

---

# 10. REQUISITOS NÃO FUNCIONAIS

As metas iniciais são:

Disponibilidade:
- Meta de 99,5% mensal, sem representar promessa comercial.

Performance:
- p95 abaixo de 500 ms para consultas comuns, sob carga de referência definida e documentada.

Integridade:
- Operações transacionais.
- Restrições de integridade.
- Controle de concorrência.

Segurança:
- Autenticação.
- Autorização.
- Isolamento.
- Auditoria.
- Proteção de segredos.

Escalabilidade:
- Permitir expansão horizontal futura.
- Não antecipar infraestrutura distribuída sem necessidade.

Manutenibilidade:
- Código organizado.
- Testes.
- Documentação.
- Arquitetura consistente.

Compatibilidade:
- API independente do frontend.
- Integração com diferentes agentes.
- CLI independente da implementação interna da plataforma.

---

# 11. PADRÕES DE DESENVOLVIMENTO

Aplicar em toda a solução:

- Nullable Reference Types.
- Async/Await.
- CancellationToken quando aplicável.
- Injeção de dependência.
- Separação de responsabilidades.
- DTOs para contratos externos.
- Validação de entrada.
- Tratamento padronizado de erros.
- Logs estruturados.
- Testes automatizados.
- Configuração externa.
- Nomenclatura consistente.
- Documentação de decisões relevantes.

Evitar:

- Classes excessivamente grandes.
- Métodos excessivamente complexos.
- Duplicação de lógica.
- Dependências circulares.
- Abstrações sem necessidade.
- Código morto.
- Comentários redundantes.
- Testes artificiais.
- Dados fictícios em produção.
- Exceções ignoradas.
- Supressão de warnings sem justificativa.
- Configurações secretas no repositório.

Toda dependência adicionada deverá ter justificativa.

---

# 12. PROCESSO OBRIGATÓRIO PARA CADA FASE

Cada fase deverá ser executada pelo seguinte processo.

## ETAPA A — Inspeção

Antes de alterar arquivos:

1. Ler AGENTS.md.
2. Ler README.
3. Consultar especificações.
4. Consultar ADRs.
5. Verificar estado do Git.
6. Verificar arquivos existentes.
7. Verificar dependências.
8. Identificar riscos.
9. Identificar tarefas anteriores pendentes.

Não presumir que o repositório está no estado esperado.

## ETAPA B — Planejamento

Criar ou atualizar o documento da fase em:

docs/specifications/

Cada fase deverá possuir:

- Objetivo.
- Escopo.
- Fora de escopo.
- Dependências.
- Tarefas.
- Arquivos previstos.
- Critérios de aceitação.
- Estratégia de testes.
- Riscos.
- Definição de conclusão.

Quebrar atividades grandes em tarefas menores.

## ETAPA C — Implementação

Executar as tarefas em ordem lógica.

Para cada tarefa:

1. Inspecionar o código relacionado.
2. Implementar a menor alteração coerente.
3. Executar testes pertinentes.
4. Corrigir falhas.
5. Revisar regressões.
6. Atualizar documentação.
7. Registrar evidências.

Não acumular dezenas de alterações sem validação intermediária.

## ETAPA D — Revisão técnica

Revisar:

- Arquitetura.
- Segurança.
- Performance.
- Manutenibilidade.
- Testabilidade.
- Consistência dos contratos.
- Compatibilidade com fases anteriores.

Verificar se a implementação realmente atende aos critérios.

## ETAPA E — Validação

Executar os comandos adequados, incluindo:

```
dotnet restore
dotnet build
dotnet test
```

Quando necessário:

- Executar testes individuais.
- Iniciar a API.
- Iniciar a Web.
- Executar CLI.
- Executar migrations em ambiente de teste.
- Executar testes de integração.
- Executar testes end-to-end.

Não declarar sucesso sem verificar os resultados.

## ETAPA F — Documentação e encerramento

Atualizar:

- README.
- AGENTS.md quando necessário.
- Especificação da fase.
- ADRs.
- Documentação da API.
- Documentação operacional.
- Registro de pendências.

Gerar relatório final da fase.

---

# 13. CONTROLE DE VERSÃO

Utilizar Git desde o início.

Regras:

- Commits pequenos e descritivos.
- Não trabalhar sobre alterações desconhecidas sem inspecioná-las.
- Não descartar alterações do usuário.
- Não sobrescrever trabalho existente.
- Não realizar force push.
- Não reescrever histórico sem autorização.
- Não commitar segredos.
- Não considerar código não versionado como entrega concluída.

Utilizar branches para funcionalidades relevantes.

Fluxo recomendado:

```
main
develop
feature/*
fix/*
hotfix/*
```

Ajustar o fluxo quando o estado real do repositório justificar.

Pull Requests deverão incluir:

- Objetivo.
- Alterações.
- Testes executados.
- Evidências.
- Riscos.
- Pendências.

Não realizar merge de alterações críticas sem validação.

---

# 14. AUTONOMIA DO CLAUDE CODE

Você tem autonomia para:

- Inspecionar o repositório.
- Criar e editar arquivos.
- Criar projetos.
- Implementar funcionalidades.
- Refatorar código dentro do escopo.
- Adicionar dependências justificadas.
- Criar testes.
- Executar comandos de compilação.
- Executar testes.
- Corrigir erros.
- Atualizar documentação.
- Criar branches.
- Criar commits locais quando apropriado.
- Elaborar relatórios.

Você deverá interromper e solicitar aprovação antes de:

- Alterar decisões arquiteturais aprovadas.
- Introduzir microsserviços.
- Adicionar infraestrutura complexa.
- Assumir custos externos.
- Contratar serviços.
- Registrar domínios.
- Publicar em produção.
- Excluir dados ou arquivos importantes.
- Executar operações irreversíveis.
- Alterar contratos públicos de maneira incompatível.
- Remover mecanismos de segurança.
- Ignorar falhas críticas.

Não interrompa o trabalho por questões pequenas que possam ser resolvidas dentro das decisões aprovadas.

Quando houver uma decisão técnica de baixo risco e reversível, escolha a alternativa mais simples, registre a decisão e continue.

---

# 15. CONTROLE DE PROGRESSO

Manter um documento:

docs/PROJECT_STATUS.md

Esse documento deverá conter:

- Fase atual.
- Estado de cada fase.
- Tarefas concluídas.
- Tarefas pendentes.
- Bloqueios.
- Testes executados.
- Resultado da última validação.
- Decisões recentes.
- Próxima atividade.

Estados permitidos:

- Not Started.
- Planned.
- In Progress.
- Blocked.
- In Review.
- Completed.

Não marcar tarefas como Completed apenas porque o código foi escrito.

Uma tarefa só pode ser concluída quando seus critérios forem verificados.

Cada fase deverá ter um relatório próprio em:

docs/reports/

Não avançar para a fase seguinte enquanto houver critérios obrigatórios não atendidos, salvo exceção explicitamente documentada e aprovada.

---

# 16. DEFINIÇÃO GLOBAL DE CONCLUÍDO

O ProjectManager somente poderá ser considerado concluído quando:

1. Usuários conseguirem se cadastrar e autenticar.
2. Organizações puderem ser criadas.
3. Convites funcionarem.
4. Permissões forem respeitadas.
5. Projetos puderem ser criados e gerenciados.
6. Equipes puderem colaborar.
7. Tarefas puderem ser criadas e atualizadas.
8. Fluxos forem configuráveis.
9. Histórico estiver disponível.
10. Documentação estiver funcional.
11. Dashboards utilizarem dados reais.
12. Agentes puderem autenticar.
13. Agentes puderem consultar contexto.
14. Agentes puderem atualizar tarefas autorizadas.
15. CLI funcionar.
16. MCP funcionar.
17. Operações forem auditáveis.
18. Isolamento multi-tenant estiver testado.
19. Testes críticos estiverem aprovados.
20. Aplicação estiver implantada em cloud.
21. Backups e restauração estiverem validados.
22. Documentação técnica e operacional estiver completa.

---

# 17. RELATÓRIO FINAL DE CADA FASE

Ao terminar cada fase, apresentar:

1. Nome e número da fase.
2. Objetivo.
3. Resumo das implementações.
4. Arquivos criados.
5. Arquivos modificados.
6. Dependências adicionadas.
7. Justificativas técnicas.
8. Testes executados.
9. Resultado do build.
10. Resultado dos testes.
11. Evidências.
12. Problemas encontrados.
13. Problemas corrigidos.
14. Pendências.
15. Critérios atendidos.
16. Critérios não atendidos.
17. Riscos identificados.
18. Estado do Git.
19. Próxima fase recomendada.

Não omitir resultados negativos.

Não declarar uma fase concluída quando os critérios obrigatórios estiverem pendentes.

---

# 18. PRIMEIRA EXECUÇÃO — INÍCIO DO PROJETO

Comece imediatamente pela FASE 01.

Não implemente funcionalidades de negócio antes de concluir a inspeção.

Primeiro:

1. Verifique o diretório atual.
2. Verifique o estado do repositório.
3. Verifique o SDK .NET.
4. Verifique o Git.
5. Verifique os templates disponíveis.
6. Verifique se existe documentação.
7. Verifique se há código existente.
8. Identifique incompatibilidades.

Depois apresente:

- Diagnóstico do ambiente.
- Estado do repositório.
- Plano detalhado da Fase 01.
- Riscos.
- Dependências.
- Critérios de aceitação.

Em seguida, execute a Fase 01 conforme as regras de autonomia.

Não comece a Fase 02 automaticamente.

Apresente o relatório final e aguarde autorização para avançar.

---

# 19. INSTRUÇÃO FINAL

Você não está sendo contratado para produzir a maior quantidade possível de código.

Seu objetivo é construir um sistema consistente, seguro e sustentável.

Priorize:

1. Correção.
2. Segurança.
3. Integridade dos dados.
4. Clareza arquitetural.
5. Testabilidade.
6. Manutenibilidade.
7. Experiência de uso.
8. Performance.
9. Evolução futura.

Não esconda problemas para aparentar progresso.

Não implemente funcionalidades apenas para preencher o roadmap.

Não invente resultados de testes.

Não altere decisões aprovadas silenciosamente.

Não avance fases sem cumprir os critérios obrigatórios.

Trabalhe com autonomia na execução, mas mantenha transparência sobre decisões, riscos e resultados.

**Comece pela inspeção da Fase 01 e conduza o desenvolvimento do ProjectManager seguindo integralmente este documento.**
