using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PaymentsApi.Models.DTOs;
using PaymentsApi.Models.Responses;
using PaymentsApi.Services;

namespace PaymentsApi.Controllers;

[ApiController]
[Authorize]
[Route("api/transfers")]
public sealed class TransfersController(
    ITransferService transferService,
    IValidator<string> idempotencyKeyValidator) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("transfers")]
    [ProducesResponseType(typeof(ApiResponse<TransferReceipt>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        TransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var validation = await idempotencyKeyValidator.ValidateAsync(
            idempotencyKey ?? string.Empty, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = new Dictionary<string, string[]>
            {
                ["idempotencyKey"] = validation.Errors.Select(error => error.ErrorMessage).ToArray()
            };
            return BadRequest(ApiResponse<object>.Fail("Validation failed.", errors));
        }

        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var userId))
            return Unauthorized(ApiResponse<object>.Fail("Invalid authentication subject.", new { }));

        var (receipt, statusCode) = await transferService.TransferAsync(
            userId, idempotencyKey!, request, cancellationToken);
        return StatusCode(statusCode, receipt);
    }
}
