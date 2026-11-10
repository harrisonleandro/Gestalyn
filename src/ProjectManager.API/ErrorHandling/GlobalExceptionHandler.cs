using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ProjectManager.API.ErrorHandling;

/// <summary>
/// Converte exceções não tratadas em respostas RFC 9457 (Problem Details),
/// sem expor detalhes internos ao cliente.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogRequestAborted(logger, request.Method, request.Path);
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        LogUnhandledException(logger, exception, request.Method, request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Erro interno do servidor.",
                Detail = "Ocorreu um erro inesperado ao processar a requisição.",
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Exceção não tratada ao processar {Method} {Path}.")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, string method, PathString path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Requisição {Method} {Path} cancelada pelo cliente.")]
    private static partial void LogRequestAborted(ILogger logger, string method, PathString path);
}
