namespace YGOProSharp.Tests;

public sealed class PersistenceStoreTests
{
    [Fact]
    public async Task PersistenceStoreEnforcesSqliteWalArgon2RbacCeilingTicketsAndBansAsync()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpPersistTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            PersistenceOptions options = new()
            {
                DbProvider = OperationalDbProvider.Sqlite,
                ConnectionString = $"Data Source={Path.Combine(tempDir, "ygoprosharp.db")}",
                ReplayDirectory = Path.Combine(tempDir, "replays"),
                Argon2MemoryKb = 4096,
                Argon2Iterations = 1,
                Argon2Parallelism = 1,
                MaxLoginFailures = 3,
                LockoutMinutes = 10,
                LoginRateLimitCount = 50
            };

            await using (OperationalStore store = new(options))
            {
                BootstrapAdminResult boot = await store.InitializeAsync();
                AssertTrue(boot.CreatedNewAdmin);
                AssertTrue(boot.MustChangePassword);
                AssertTrue(!string.IsNullOrWhiteSpace(boot.GeneratedOneTimePassword) && boot.GeneratedOneTimePassword!.Length >= 20);

                DatabaseHealthSnapshot dbHealth = await store.Database.CheckHealthAsync();
                AssertTrue(dbHealth.IsHealthy);
                AssertEqual("wal", dbHealth.JournalMode.ToLowerInvariant());
                AssertEqual(5, (await store.ListRolesAsync()).Count);

                await using (OperationalDbContext migCheckDb = store.Database.CreateDbContext())
                {
                    string[] appliedMigrations = (await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetAppliedMigrationsAsync(migCheckDb.Database)).ToArray();
                    AssertTrue(appliedMigrations.Contains("20261124000000_InitialOperationalSchema"));
                    AssertTrue(appliedMigrations.Contains("20261126000000_AddPhase5EvaluationsReviewsAndMemories"));
                }

                // 1. 初始管理员登录、密码最少 6 位校验与自定义用户名/密码设置（未填项保持默认）
                LoginResult firstLogin = await store.LoginAsync("admin", boot.GeneratedOneTimePassword!, "127.0.0.1", "UnitTest");
                AssertTrue(firstLogin.Success && firstLogin.Principal is not null && firstLogin.Principal.MustChangePassword);

                // 少于 6 位字符被拒绝
                (bool tooShortRejected, _) = await store.ChangeOwnPasswordAsync(
                    firstLogin.Principal!.UserId,
                    firstLogin.Principal.SessionId,
                    boot.GeneratedOneTimePassword!,
                    "12345",
                    "127.0.0.1");
                AssertFalse(tooShortRejected);

                // 6 位自定义密码通过，且支持同时自定义管理员用户名
                (bool changedOk, _) = await store.ChangeOwnPasswordAsync(
                    firstLogin.Principal!.UserId,
                    firstLogin.Principal.SessionId,
                    boot.GeneratedOneTimePassword!,
                    "123456",
                    "127.0.0.1",
                    newUsername: "custom_admin");
                AssertTrue(changedOk);

                AuthenticatedPrincipal? adminPrincipal = await store.ValidateSessionAsync(firstLogin.SessionToken);
                AssertTrue(adminPrincipal is not null && !adminPrincipal.MustChangePassword);
                AssertEqual("custom_admin", adminPrincipal!.Username);

                // 创建用户时未填写用户名与密码则自动采用默认值
                (bool defaultUserCreated, _, PanelUserEntity? autoUser, string? autoGenPw) = await store.CreateUserAsync(
                    adminPrincipal,
                    "",
                    "",
                    null,
                    [BuiltInRoleCodes.AiOperator],
                    null,
                    mustChangePassword: true,
                    ip: "127.0.0.1");
                AssertTrue(defaultUserCreated && autoUser is not null && !string.IsNullOrWhiteSpace(autoGenPw));
                AssertTrue(autoUser!.Username.StartsWith("ai_operator_", StringComparison.OrdinalIgnoreCase));

                // 管理员直接为该用户指定 6 位自定义密码与自定义用户名
                (bool renameOk, _) = await store.UpdateUserAsync(
                    adminPrincipal,
                    autoUser.Id,
                    displayName: null,
                    enabled: null,
                    unlockAccount: true,
                    roleCodes: null,
                    permissionsOverride: null,
                    updateOverride: false,
                    ip: "127.0.0.1",
                    newUsername: "player_custom");
                AssertTrue(renameOk);

                (bool customResetOk, _, string? noTempPw) = await store.ResetUserPasswordAsync(
                    adminPrincipal,
                    autoUser.Id,
                    "127.0.0.1",
                    customPassword: "654321");
                AssertTrue(customResetOk && noTempPw is null);
                LoginResult customUserLogin = await store.LoginAsync("player_custom", "654321", "127.0.0.1", "UnitTest");
                AssertTrue(customUserLogin.Success && customUserLogin.Principal is not null && !customUserLogin.Principal.MustChangePassword);

                // 2. 创建附加了 user:manage 的子管理员，验证权限不放大铁律（不能创建 super_admin 或超出自身权限的账号）
                (bool subCreated, _, _, _) = await store.CreateUserAsync(
                    adminPrincipal!,
                    "sub_judge",
                    "Sub Judge",
                    "Judge#Strong2026!",
                    [BuiltInRoleCodes.Judge],
                    new UserPermissionsOverride([RbacPermissions.UserManage], []),
                    mustChangePassword: false,
                    ip: "127.0.0.1");
                AssertTrue(subCreated);

                LoginResult subLogin = await store.LoginAsync("sub_judge", "Judge#Strong2026!", "127.0.0.1", "UnitTest");
                AssertTrue(subLogin.Success && subLogin.Principal is not null);

                (bool escalatedUserOk, _, _, _) = await store.CreateUserAsync(
                    subLogin.Principal!,
                    "evil_root",
                    "Evil Root",
                    "Evil#Strong2026!",
                    [BuiltInRoleCodes.SuperAdmin],
                    null,
                    mustChangePassword: false,
                    ip: "127.0.0.1");
                AssertFalse(escalatedUserOk);

                IReadOnlyList<AuditLogEntity> escalationAudits = await store.QueryAuditLogsAsync(limit: 10, actionFilter: "user.create");
                AssertTrue(escalationAudits.Any(a => a.Result == "privilege_escalation_blocked"));

                // 3. 创建 streamer 只读直播员，验证仅能签发 spectator 级 MCP Token，严禁越权签发 player / admin
                (bool streamerCreated, _, _, _) = await store.CreateUserAsync(
                    adminPrincipal!,
                    "obs_caster",
                    "OBS Caster",
                    "Stream#Strong2026!",
                    [BuiltInRoleCodes.Streamer],
                    null,
                    mustChangePassword: false,
                    ip: "127.0.0.1");
                AssertTrue(streamerCreated);

                LoginResult streamerLogin = await store.LoginAsync("obs_caster", "Stream#Strong2026!", "127.0.0.1", "UnitTest");
                AssertTrue(streamerLogin.Success && streamerLogin.Principal is not null && streamerLogin.Principal.IsStreamerOnly);

                (bool issuePlayerBlocked, _, _) = await store.IssueMcpTokenAsync(
                    streamerLogin.Principal!,
                    "IllegalPlayer",
                    McpRoleCodes.Player,
                    null,
                    1,
                    true,
                    12,
                    "127.0.0.1");
                AssertFalse(issuePlayerBlocked);

                (bool issueSpectatorOk, _, IssuedMcpTokenResult? specIssued) = await store.IssueMcpTokenAsync(
                    streamerLogin.Principal!,
                    "LegalSpectator",
                    McpRoleCodes.Spectator,
                    ["ShowMatch"],
                    2,
                    false,
                    12,
                    "127.0.0.1");
                AssertTrue(issueSpectatorOk && specIssued is not null);

                ValidatedMcpToken? firstValidated = await store.ValidateMcpTokenAsync(specIssued!.RawToken);
                ValidatedMcpToken? secondValidated = await store.ValidateMcpTokenAsync(specIssued.RawToken);
                AssertTrue(firstValidated is not null && secondValidated is not null);
                AssertEqual(McpRoleCodes.Spectator, secondValidated!.McpRole);
                AssertTrue(store.Cache.GetStats().L1Hits >= 1);

                AssertTrue(await store.RevokeMcpTokenAsync(streamerLogin.Principal!, specIssued.Entity.Id, "127.0.0.1"));
                AssertTrue(await store.ValidateMcpTokenAsync(specIssued.RawToken) is null);

                // 4. 验证连续输错密码触发账号防爆破锁定
                for (int i = 0; i < 3; i++)
                {
                    LoginResult bad = await store.LoginAsync("sub_judge", "WrongPassword!", "127.0.0.1", "UnitTest");
                    AssertFalse(bad.Success);
                }
                LoginResult lockedAttempt = await store.LoginAsync("sub_judge", "Judge#Strong2026!", "127.0.0.1", "UnitTest");
                AssertFalse(lockedAttempt.Success);
                AssertTrue(lockedAttempt.StatusCode.Contains("locked", StringComparison.OrdinalIgnoreCase));

                // 5. 验证原子一次性 Ticket（GETDEL 语义）与滑动窗口限流
                string wsTicket = await store.Cache.IssueOneTimeTicketAsync("ws", "session-xyz", TimeSpan.FromSeconds(30));
                AssertEqual("session-xyz", (await store.Cache.ConsumeOneTimeTicketAsync("ws", wsTicket))!);
                AssertTrue(await store.Cache.ConsumeOneTimeTicketAsync("ws", wsTicket) is null);

                AssertTrue((await store.Cache.CheckSlidingWindowAsync("test:ip1", 2, TimeSpan.FromSeconds(60))).Allowed);
                AssertTrue((await store.Cache.CheckSlidingWindowAsync("test:ip1", 2, TimeSpan.FromSeconds(60))).Allowed);
                AssertFalse((await store.Cache.CheckSlidingWindowAsync("test:ip1", 2, TimeSpan.FromSeconds(60))).Allowed);

                // 6. 验证 CIDR 封禁匹配、玩家昵称$密码 (vpass) 保护与云录像落库
                (bool banOk, _, _) = await store.CreateBanAsync(adminPrincipal!, "cidr", "10.88.0.0/16", "Abuse subnet", 60, "127.0.0.1");
                AssertTrue(banOk);
                AssertTrue(await store.CheckBanMatchAsync("10.88.12.34", "NormalPlayer", null) is not null);
                AssertTrue(await store.CheckBanMatchAsync("10.89.0.1", "NormalPlayer", null) is null);

                AssertTrue((await store.VerifyPlayerVpassAsync("Kaiba", "BlueEyes")).Allowed);
                AssertFalse((await store.VerifyPlayerVpassAsync("Kaiba", null)).Allowed);
                AssertFalse((await store.VerifyPlayerVpassAsync("Kaiba", "WrongPass")).Allowed);
                AssertTrue((await store.VerifyPlayerVpassAsync("Kaiba", "BlueEyes")).Allowed);

                byte[] sampleYrp = [0x59, 0x52, 0x50, 0x32, 0x01, 0x02];
                DuelRecordEntity savedDuel = await store.SaveDuelRecordAsync(new SaveDuelRecordRequest(
                    RoomName: "FinalTable",
                    RoomIdentifier: "M#FinalTable",
                    Mode: 1,
                    Region: 0,
                    MasterRule: 5,
                    StartLp: 8000,
                    HostInfoJson: "{}",
                    RandomDeckMode: "None",
                    IsSuddenDeath: false,
                    DuelCount: 2,
                    TurnCount: 7,
                    WinnerTeam: 0,
                    WinReason: 1,
                    Seed: 42U,
                    StartedAt: DateTime.UtcNow.AddMinutes(-5),
                    EndedAt: DateTime.UtcNow,
                    YrpBytes: sampleYrp,
                    ObserverPacketFrames: null,
                    Players:
                    [
                        new SaveDuelPlayerSlot(0, 0, "Yugi", "Yugi", "human", "127.0.0.1", "#main\n89631139", null, true),
                        new SaveDuelPlayerSlot(1, 1, "Kaiba", "Kaiba", "human", "127.0.0.1", "#main\n46986414", null, false)
                    ]));
                AssertEqual(1L, savedDuel.Id);
                byte[]? loadedYrp = await store.GetReplayYrpBytesAsync(1L);
                AssertTrue(loadedYrp is not null);
                AssertSequenceEqual(sampleYrp, loadedYrp!);
            }

            // 7. 验证已有数据库二次启动：未传 AdminUser/AdminPassword 保留原设置；显式传入时覆盖自定义管理员用户名与密码
            await using (OperationalStore reopenDefault = new(options))
            {
                BootstrapAdminResult bootKeep = await reopenDefault.InitializeAsync();
                AssertFalse(bootKeep.CreatedNewAdmin);
                AssertEqual("custom_admin", bootKeep.Username);
            }

            await using (OperationalStore reopenCustom = new(options with { BootstrapAdminUser = "boss", BootstrapAdminPassword = "888888" }))
            {
                BootstrapAdminResult bootOverride = await reopenCustom.InitializeAsync();
                AssertEqual("boss", bootOverride.Username);
                AssertFalse(bootOverride.MustChangePassword);
                LoginResult bossLogin = await reopenCustom.LoginAsync("boss", "888888", "127.0.0.1", "UnitTest");
                AssertTrue(bossLogin.Success && bossLogin.Principal is not null && !bossLogin.Principal.MustChangePassword);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

}
