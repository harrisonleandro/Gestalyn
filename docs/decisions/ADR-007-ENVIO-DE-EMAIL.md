# ADR-007: Envio de e-mail

- **Status:** Aceito
- **Data:** 2026-11-11
- **Fase:** 03 (Identidade e autenticação). Decisão D4 aprovada pelo responsável.

## Contexto

Confirmação de e-mail e recuperação de senha exigem envio real de e-mail ("recuperação de senha funciona com provedor configurado"). O provedor de produção depende da escolha de hospedagem (Fase 14), e a especificação proíbe assumir custos sem autorização.

## Decisão

- Abstração `IEmailSender` (Application) e implementação **SMTP com MailKit** (Infrastructure), configurada pela seção `Email` (`Host`, `Port`, `Security`, `UserName`, `Password`, `FromAddress`, `FromName`), validada na inicialização.
- **Mailpit** (`axllent/mailpit:v1.29`) recebe todos os e-mails em desenvolvimento (`docker-compose.yml`, interface em http://localhost:8025) e nos testes de integração (Testcontainers). Os testes leem as mensagens pela API HTTP do Mailpit e seguem os links reais.
- Falhas de entrega viram `EmailDeliveryException`. A operação do usuário não falha por causa disso, para não revelar estado, mas a falha é registrada em log de erro, e o usuário pode pedir o reenvio.
- As mensagens são texto puro, em português, sem dados sensíveis além do link com token de uso único.
- **Produção (Fase 14):** qualquer provedor com SMTP e TLS (`Security=StartTls` ou `SslOnConnect`) funciona sem mudança de código. Credenciais ficam em variáveis de ambiente ou em cofre.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Remetente que só escreve no log | Não cumpre o critério de provedor configurado; não testa o fluxo real |
| `System.Net.Mail.SmtpClient` | Desaconselhado pela Microsoft para código novo |
| SDK de um provedor específico | Antecipa a escolha da Fase 14 e cria dependência de serviço pago |

## Consequências

- O envio é síncrono na requisição: a latência do SMTP soma ao tempo de resposta. Quando houver um Worker, o envio pode ir para uma fila.
- Os testes de integração exigem Docker também para o Mailpit.
