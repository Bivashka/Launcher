using BivLauncher.Api.Contracts.Admin;
using BivLauncher.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BivLauncher.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/admin/support/developer")]
public sealed class AdminDeveloperSupportController(
    IOptions<DeveloperSupportOptions> options) : ControllerBase
{
    [HttpGet]
    public ActionResult<DeveloperSupportDto> Get()
    {
        var current = options.Value;
        return Ok(new DeveloperSupportDto(
            DisplayName: Normalize(current.DisplayName, "Bivashka"),
            Telegram: Normalize(current.Telegram, "https://t.me/bivashka"),
            Discord: Normalize(current.Discord, "bivashka"),
            Website: Normalize(current.Website, "https://github.com/bivashka"),
            Notes: Normalize(current.Notes, "Official developer support contact. Not editable from admin UI.")));
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
