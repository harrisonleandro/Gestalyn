using Microsoft.AspNetCore.Mvc;
using ProjectManager.API.Authentication;
using ProjectManager.API.Common;
using ProjectManager.Application.Organizations;

namespace ProjectManager.API.Controllers;

[ApiController]
[Route("api/v1/invitations")]
public sealed class InvitationsController(IOrganizationService organizations) : ControllerBase
{
    /// <summary>
    /// Aceita um convite com o token recebido por e-mail. O e-mail confirmado do usuário autenticado
    /// precisa ser o e-mail convidado.
    /// </summary>
    [HttpPost("accept")]
    [ProducesResponseType<AcceptedInvitation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetSession(out var userId, out _))
        {
            return Unauthorized();
        }

        var result = await organizations.AcceptInvitationAsync(userId, request, cancellationToken);

        return result.Succeeded ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
