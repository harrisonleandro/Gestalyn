namespace ProjectManager.Application.Abstractions.Email;

public interface IEmailSender
{
    /// <exception cref="EmailDeliveryException">O provedor de e-mail recusou ou não pôde receber a mensagem.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string TextBody);

public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException()
        : base("Não foi possível entregar o e-mail ao provedor.")
    {
    }

    public EmailDeliveryException(string message)
        : base(message)
    {
    }

    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
