using Articles.Api.Common;
using Articles.Application.Grammar;
using Articles.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Controllers;

public sealed record GrammarCheckRequest(string? Text);

/// <summary>Grammar and spelling suggestions for the post editor. Admin only.</summary>
[ApiController]
[Route("api/v1/admin/grammar")]
[Authorize(Policy = AdminRequirement.PolicyName)]
public class GrammarController : ControllerBase
{
    private readonly IGrammarChecker _checker;

    public GrammarController(IGrammarChecker checker) => _checker = checker;

    [HttpPost("check")]
    [ProducesResponseType(typeof(GrammarResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<GrammarResult>> Check(GrammarCheckRequest request, CancellationToken ct)
    {
        var text = request.Text ?? string.Empty;
        if (text.Length > IGrammarChecker.MaxCharacters)
            throw new DomainException("This section is too long to check in one go. Split it into two sections and check each.");

        return Ok(await _checker.CheckAsync(text, ct));
    }
}
