using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentsApi.Models.Responses;
using PaymentsApi.Services;

namespace PaymentsApi.Controllers;

[ApiController]
[Authorize]
[Route("api/accounts")]
public sealed class AccountsController(ITransferService transferService) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<Guid>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
            return Unauthorized(ApiResponse<object>.Fail("Invalid authentication subject.", new { }));
        var ids = await transferService.GetOwnedAccountIdsAsync(userId, cancellationToken);
        return Ok(ids);
    }
}
