using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;

namespace QTS.Controllers;

[ApiController]
[Route("api/health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public ApiResponse<object> Get() => ApiResponse<object>.Ok(new
    {
        status = "ok",
        time = DateTime.UtcNow
    });
}
