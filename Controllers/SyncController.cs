using Controllers.ApiContracts.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services;

[Authorize]
[ApiController]
[Route("sync")]
public class SyncController : ControllerBase
{
    private readonly SyncService _syncService;

    public SyncController(SyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpPost]
    public async Task<SyncResponse> Sync(
        [FromBody] SyncRequest request,
        CancellationToken cancellationToken)
        => await _syncService.SyncAsync(request, cancellationToken);
}
