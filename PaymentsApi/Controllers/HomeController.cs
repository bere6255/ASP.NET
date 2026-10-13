using Microsoft.AspNetCore.Mvc;
using PaymentsApi.Models.Responses;

namespace PaymentsApi.Controllers;

[ApiController]
[Route("")]
public sealed class HomeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(ApiResponse<object>.Ok(new { }, "Welcome to Payment API"));
    }
}