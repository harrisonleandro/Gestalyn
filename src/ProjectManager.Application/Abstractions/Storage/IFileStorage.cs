namespace ProjectManager.Application.Abstractions.Storage;

/// <summary>
/// Armazenamento de arquivos por chave (ADR-011). A implementação atual grava em disco local; a Fase 14
/// troca por object storage compatível com S3 sem mudar quem usa esta interface. As chaves são geradas
/// pelo sistema (letras minúsculas, números e barras), nunca a partir de dados do cliente.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken);

    /// <summary>Abre o arquivo para leitura, ou devolve <c>null</c> se ele não existe.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
