using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Persistence.Caching;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Persistence;

public sealed partial class OperationalStore
{
    // ==================== 5. 对局战报落库与云录像 R#<id> ====================

    public async Task<DuelRecordEntity> SaveDuelRecordAsync(
        SaveDuelRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        DuelRecordEntity record = new()
        {
            RoomName = request.RoomName,
            RoomIdentifier = request.RoomIdentifier,
            Mode = request.Mode,
            Region = request.Region,
            MasterRule = request.MasterRule,
            StartLp = request.StartLp,
            HostInfoJson = request.HostInfoJson,
            RandomDeckMode = request.RandomDeckMode,
            IsSuddenDeath = request.IsSuddenDeath,
            DuelCount = request.DuelCount,
            TurnCount = request.TurnCount,
            WinnerTeam = request.WinnerTeam,
            WinReason = request.WinReason,
            Seed = request.Seed,
            DecisionTimelineJson = request.DecisionTimelineJson,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt
        };

        foreach (SaveDuelPlayerSlot slot in request.Players)
        {
            record.Players.Add(new DuelRecordPlayerEntity
            {
                SeatIndex = slot.SeatIndex,
                TeamId = slot.TeamId,
                PlayerName = slot.PlayerName,
                RealName = slot.RealName,
                SeatType = slot.SeatType,
                ClientIp = slot.ClientIp,
                StartDeckYdk = slot.StartDeckYdk,
                IngameDeckYdk = slot.IngameDeckYdk,
                IsWinner = slot.IsWinner
            });
        }

        db.DuelRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        string replayDir = Path.GetFullPath(_options.ReplayDirectory);
        Directory.CreateDirectory(replayDir);

        bool updatedFiles = false;
        if (request.YrpBytes is { Length: > 0 })
        {
            string yrpFileName = $"replay_{record.Id}.yrp";
            string fullYrpPath = Path.Combine(replayDir, yrpFileName);
            await File.WriteAllBytesAsync(fullYrpPath, request.YrpBytes, cancellationToken).ConfigureAwait(false);
            record.YrpPath = yrpFileName;
            updatedFiles = true;
        }

        if (request.ObserverPacketFrames is { Length: > 0 })
        {
            string obsFileName = $"replay_{record.Id}.obs";
            string fullObsPath = Path.Combine(replayDir, obsFileName);
            await File.WriteAllBytesAsync(fullObsPath, request.ObserverPacketFrames, cancellationToken).ConfigureAwait(false);
            record.ObserverFramesPath = obsFileName;
            updatedFiles = true;
        }

        if (updatedFiles)
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Saved duel record R#{ReplayId} for room '{RoomName}' (Mode={Mode}, WinnerTeam={WinnerTeam}).", record.Id, record.RoomName, record.Mode, record.WinnerTeam);
        return record;
    }

    public async Task<IReadOnlyList<DuelRecordEntity>> ListDuelRecordsAsync(
        int limit = 50,
        long? beforeId = null,
        string? playerFilter = null,
        CancellationToken cancellationToken = default)
    {
        int clamped = Math.Clamp(limit, 1, 200);
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        IQueryable<DuelRecordEntity> q = db.DuelRecords
            .AsNoTracking()
            .Include(r => r.Players);

        if (beforeId.HasValue)
            q = q.Where(r => r.Id < beforeId.Value);

        if (!string.IsNullOrWhiteSpace(playerFilter))
        {
            string kw = playerFilter.Trim();
            q = q.Where(r => r.RoomName.Contains(kw) || r.Players.Any(p => p.PlayerName.Contains(kw)));
        }

        return await q.OrderByDescending(r => r.Id).Take(clamped).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DuelRecordEntity?> GetDuelRecordByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        return await db.DuelRecords
            .AsNoTracking()
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<byte[]?> GetReplayYrpBytesAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync<byte[]?>(
            "replay",
            $"yrp:{id}",
            async ct =>
            {
                DuelRecordEntity? record = await GetDuelRecordByIdAsync(id, ct).ConfigureAwait(false);
                if (record is null || string.IsNullOrWhiteSpace(record.YrpPath))
                    return null;

                string fullPath = ResolveReplayFilePath(record.YrpPath);
                return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false) : null;
            },
            ttl: TimeSpan.FromMinutes(10),
            l1Ttl: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetReplayObserverFramesAsync(long id, CancellationToken cancellationToken = default)
    {
        DuelRecordEntity? record = await GetDuelRecordByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null || string.IsNullOrWhiteSpace(record.ObserverFramesPath))
            return null;

        string fullPath = ResolveReplayFilePath(record.ObserverFramesPath);
        return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false) : null;
    }

    private string ResolveReplayFilePath(string storedRelativeOrFull)
    {
        string replayRoot = Path.GetFullPath(_options.ReplayDirectory);
        string fileName = Path.GetFileName(storedRelativeOrFull);
        return Path.Combine(replayRoot, fileName);
    }
}
