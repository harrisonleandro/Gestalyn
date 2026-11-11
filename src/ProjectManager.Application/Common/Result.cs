namespace ProjectManager.Application.Common;

/// <summary>Categorias de falha de um caso de uso, mapeadas para status HTTP pela API.</summary>
public enum ErrorKind
{
    /// <summary>Recurso inexistente ou não visível para o usuário (404).</summary>
    NotFound,

    /// <summary>Usuário identificado, mas sem permissão para a operação (403).</summary>
    Forbidden,

    /// <summary>A operação viola uma regra de negócio no estado atual (409).</summary>
    Conflict,

    /// <summary>Entrada inválida (400).</summary>
    Validation,
}

public sealed record ResultError(ErrorKind Kind, string Message);

/// <summary>Resultado de um caso de uso: sucesso ou um erro esperado (nunca exceções para fluxo de negócio).</summary>
public class Result
{
    protected Result(ResultError? error) => Error = error;

    public ResultError? Error { get; }

    public bool Succeeded => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(ErrorKind kind, string message) => new(new ResultError(kind, message));

    public static Result<T> Success<T>(T value) => new(value, null);

    public static Result<T> Failure<T>(ErrorKind kind, string message) => new(default, new ResultError(kind, message));
}

public sealed class Result<T> : Result
{
    internal Result(T? value, ResultError? error)
        : base(error) => Value = value;

    public T? Value { get; }
}
