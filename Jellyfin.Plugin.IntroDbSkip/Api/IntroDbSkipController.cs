using System;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.IntroDbSkip.Services;

namespace Jellyfin.Plugin.IntroDbSkip.Api;

/// <summary>
/// API endpoints for IntroDB marker cache.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("IntroDbSkip")]
public class IntroDbSkipController : ControllerBase
{
    private readonly IntroMarkerStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntroDbSkipController"/> class.
    /// </summary>
    public IntroDbSkipController(IntroMarkerStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Gets a cached marker for the specified item id.
    /// </summary>
    [HttpGet("markers/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetMarker(Guid itemId)
    {
        var marker = _store.Get(itemId);
        if (marker is null)
        {
            return NotFound();
        }

        return Ok(marker);
    }
}
