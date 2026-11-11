using Microsoft.AspNetCore.Mvc;
using ProjectManager.Application.Common;

namespace ProjectManager.API.Common;

internal static class ResultProblems
{
    /// <summary>Converte um erro de caso de uso em Problem Details com o status correspondente.</summary>
    public static ObjectResult ToProblem(this ControllerBase controller, ResultError error)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(error);

        var statusCode = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return controller.Problem(statusCode: statusCode, title: error.Message);
    }
}
