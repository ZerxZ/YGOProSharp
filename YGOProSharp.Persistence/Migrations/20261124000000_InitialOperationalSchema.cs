using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace YGOProSharp.Persistence.Migrations;

[DbContext(typeof(OperationalDbContext))]
[Migration("20261124000000_InitialOperationalSchema")]
public partial class InitialOperationalSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "panel_roles",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Code = table.Column<string>(maxLength: 64, nullable: false),
                Name = table.Column<string>(maxLength: 128, nullable: false),
                Description = table.Column<string>(maxLength: 512, nullable: false),
                IsBuiltIn = table.Column<bool>(nullable: false),
                PermissionsJson = table.Column<string>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_panel_roles", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_panel_roles_Code", table: "panel_roles", column: "Code", unique: true);

        migrationBuilder.CreateTable(
            name: "panel_users",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Username = table.Column<string>(maxLength: 64, nullable: false),
                NormalizedUsername = table.Column<string>(maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(maxLength: 128, nullable: false),
                PasswordHash = table.Column<string>(maxLength: 512, nullable: false),
                Enabled = table.Column<bool>(nullable: false),
                MustChangePassword = table.Column<bool>(nullable: false),
                FailedLoginCount = table.Column<int>(nullable: false),
                LockedUntil = table.Column<DateTime>(nullable: true),
                PermissionsOverrideJson = table.Column<string>(nullable: true),
                LastLoginAt = table.Column<DateTime>(nullable: true),
                LastLoginIp = table.Column<string>(maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_panel_users", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_panel_users_NormalizedUsername", table: "panel_users", column: "NormalizedUsername", unique: true);

        migrationBuilder.CreateTable(
            name: "panel_user_roles",
            columns: table => new
            {
                UserId = table.Column<Guid>(nullable: false),
                RoleId = table.Column<Guid>(nullable: false),
                AssignedAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_panel_user_roles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(name: "FK_panel_user_roles_panel_users_UserId", column: x => x.UserId, principalTable: "panel_users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(name: "FK_panel_user_roles_panel_roles_RoleId", column: x => x.RoleId, principalTable: "panel_roles", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_panel_user_roles_RoleId", table: "panel_user_roles", column: "RoleId");

        migrationBuilder.CreateTable(
            name: "panel_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                UserId = table.Column<Guid>(nullable: false),
                TokenPrefix = table.Column<string>(maxLength: 32, nullable: false),
                TokenSha256 = table.Column<string>(maxLength: 64, nullable: false),
                CsrfToken = table.Column<string>(maxLength: 64, nullable: false),
                Ip = table.Column<string>(maxLength: 64, nullable: true),
                UserAgent = table.Column<string>(maxLength: 512, nullable: true),
                ExpiresAt = table.Column<DateTime>(nullable: false),
                RevokedAt = table.Column<DateTime>(nullable: true),
                LastSeenAt = table.Column<DateTime>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_panel_sessions", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_panel_sessions_TokenSha256", table: "panel_sessions", column: "TokenSha256", unique: true);
        migrationBuilder.CreateIndex(name: "IX_panel_sessions_UserId", table: "panel_sessions", column: "UserId");

        migrationBuilder.CreateTable(
            name: "mcp_tokens",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                OwnerUserId = table.Column<Guid>(nullable: false),
                OwnerUsername = table.Column<string>(maxLength: 64, nullable: false),
                Name = table.Column<string>(maxLength: 128, nullable: false),
                PreferredPlayerName = table.Column<string>(maxLength: 32, nullable: true),
                PersonaStyle = table.Column<string>(maxLength: 128, nullable: true),
                TokenPrefix = table.Column<string>(maxLength: 32, nullable: false),
                TokenSha256 = table.Column<string>(maxLength: 64, nullable: false),
                McpRole = table.Column<string>(maxLength: 32, nullable: false),
                AllowedRoomsJson = table.Column<string>(nullable: true),
                MaxSessions = table.Column<int>(nullable: false),
                RequireApproval = table.Column<bool>(nullable: false),
                ExpiresAt = table.Column<DateTime>(nullable: true),
                RevokedAt = table.Column<DateTime>(nullable: true),
                LastUsedAt = table.Column<DateTime>(nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_mcp_tokens", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_mcp_tokens_TokenSha256", table: "mcp_tokens", column: "TokenSha256", unique: true);
        migrationBuilder.CreateIndex(name: "IX_mcp_tokens_OwnerUserId", table: "mcp_tokens", column: "OwnerUserId");

        migrationBuilder.CreateTable(
            name: "player_profiles",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                PlayerName = table.Column<string>(maxLength: 64, nullable: false),
                NormalizedName = table.Column<string>(maxLength: 64, nullable: false),
                VpassHash = table.Column<string>(maxLength: 512, nullable: true),
                ClientKey = table.Column<string>(maxLength: 128, nullable: true),
                ChatColor = table.Column<int>(nullable: false),
                WinCount = table.Column<int>(nullable: false),
                LoseCount = table.Column<int>(nullable: false),
                FleeCount = table.Column<int>(nullable: false),
                WinCombo = table.Column<int>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_player_profiles", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_player_profiles_NormalizedName", table: "player_profiles", column: "NormalizedName", unique: true);

        migrationBuilder.CreateTable(
            name: "duel_records",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RoomName = table.Column<string>(maxLength: 128, nullable: false),
                RoomIdentifier = table.Column<string>(maxLength: 64, nullable: false),
                Mode = table.Column<int>(nullable: false),
                Region = table.Column<int>(nullable: false),
                MasterRule = table.Column<int>(nullable: false),
                StartLp = table.Column<int>(nullable: false),
                HostInfoJson = table.Column<string>(nullable: false),
                RandomDeckMode = table.Column<string>(maxLength: 32, nullable: false),
                IsSuddenDeath = table.Column<bool>(nullable: false),
                DuelCount = table.Column<int>(nullable: false),
                TurnCount = table.Column<int>(nullable: false),
                WinnerTeam = table.Column<int>(nullable: false),
                WinReason = table.Column<int>(nullable: false),
                Seed = table.Column<uint>(nullable: false),
                YrpPath = table.Column<string>(maxLength: 512, nullable: true),
                ObserverFramesPath = table.Column<string>(maxLength: 512, nullable: true),
                DecisionTimelineJson = table.Column<string>(nullable: true),
                StartedAt = table.Column<DateTime>(nullable: false),
                EndedAt = table.Column<DateTime>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_duel_records", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_duel_records_EndedAt", table: "duel_records", column: "EndedAt");
        migrationBuilder.CreateIndex(name: "IX_duel_records_RoomIdentifier", table: "duel_records", column: "RoomIdentifier");

        migrationBuilder.CreateTable(
            name: "duel_record_players",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                DuelRecordId = table.Column<long>(nullable: false),
                SeatIndex = table.Column<int>(nullable: false),
                TeamId = table.Column<int>(nullable: false),
                PlayerName = table.Column<string>(maxLength: 64, nullable: false),
                RealName = table.Column<string>(maxLength: 64, nullable: false),
                SeatType = table.Column<string>(maxLength: 32, nullable: false),
                ClientIp = table.Column<string>(maxLength: 64, nullable: true),
                StartDeckYdk = table.Column<string>(nullable: true),
                IngameDeckYdk = table.Column<string>(nullable: true),
                IsWinner = table.Column<bool>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_duel_record_players", x => x.Id);
                table.ForeignKey(name: "FK_duel_record_players_duel_records_DuelRecordId", column: x => x.DuelRecordId, principalTable: "duel_records", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_duel_record_players_DuelRecordId_SeatIndex", table: "duel_record_players", columns: ["DuelRecordId", "SeatIndex"]);

        migrationBuilder.CreateTable(
            name: "bans",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TargetType = table.Column<string>(maxLength: 32, nullable: false),
                TargetValue = table.Column<string>(maxLength: 128, nullable: false),
                Reason = table.Column<string>(maxLength: 512, nullable: false),
                FleeCount = table.Column<int>(nullable: false),
                ExpiresAt = table.Column<DateTime>(nullable: true),
                CreatedByUserId = table.Column<Guid>(nullable: true),
                CreatedByName = table.Column<string>(maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_bans", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_bans_TargetType_TargetValue", table: "bans", columns: ["TargetType", "TargetValue"]);

        migrationBuilder.CreateTable(
            name: "tournament_decks",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TournamentTag = table.Column<string>(maxLength: 64, nullable: false),
                PlayerName = table.Column<string>(maxLength: 64, nullable: false),
                NormalizedPlayerName = table.Column<string>(maxLength: 64, nullable: false),
                YdkContent = table.Column<string>(nullable: false),
                DeckHash = table.Column<string>(maxLength: 64, nullable: false),
                MainCount = table.Column<int>(nullable: false),
                ExtraCount = table.Column<int>(nullable: false),
                SideCount = table.Column<int>(nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_tournament_decks", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_tournament_decks_TournamentTag_NormalizedPlayerName", table: "tournament_decks", columns: ["TournamentTag", "NormalizedPlayerName"], unique: true);

        migrationBuilder.CreateTable(
            name: "diy_proposals",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                PackageId = table.Column<string>(maxLength: 128, nullable: false),
                ProposerTokenId = table.Column<Guid>(nullable: true),
                Title = table.Column<string>(maxLength: 256, nullable: false),
                DiffJson = table.Column<string>(nullable: false),
                Status = table.Column<string>(maxLength: 32, nullable: false),
                ReviewerUserId = table.Column<Guid>(nullable: true),
                ReviewComment = table.Column<string>(maxLength: 1024, nullable: true),
                ReviewedAt = table.Column<DateTime>(nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_diy_proposals", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_diy_proposals_Status", table: "diy_proposals", column: "Status");

        migrationBuilder.CreateTable(
            name: "audit_logs",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("Sqlite:Autoincrement", true)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ActorType = table.Column<string>(maxLength: 32, nullable: false),
                ActorId = table.Column<string>(maxLength: 64, nullable: true),
                ActorName = table.Column<string>(maxLength: 64, nullable: false),
                Action = table.Column<string>(maxLength: 128, nullable: false),
                PermissionChecked = table.Column<string>(maxLength: 64, nullable: true),
                Target = table.Column<string>(maxLength: 256, nullable: true),
                Ip = table.Column<string>(maxLength: 64, nullable: true),
                Result = table.Column<string>(maxLength: 64, nullable: false),
                DetailJson = table.Column<string>(nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_audit_logs", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_audit_logs_CreatedAt", table: "audit_logs", column: "CreatedAt");
        migrationBuilder.CreateIndex(name: "IX_audit_logs_Action", table: "audit_logs", column: "Action");
        migrationBuilder.CreateIndex(name: "IX_audit_logs_Result", table: "audit_logs", column: "Result");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "audit_logs");
        migrationBuilder.DropTable(name: "diy_proposals");
        migrationBuilder.DropTable(name: "tournament_decks");
        migrationBuilder.DropTable(name: "bans");
        migrationBuilder.DropTable(name: "duel_record_players");
        migrationBuilder.DropTable(name: "duel_records");
        migrationBuilder.DropTable(name: "player_profiles");
        migrationBuilder.DropTable(name: "mcp_tokens");
        migrationBuilder.DropTable(name: "panel_sessions");
        migrationBuilder.DropTable(name: "panel_user_roles");
        migrationBuilder.DropTable(name: "panel_users");
        migrationBuilder.DropTable(name: "panel_roles");
    }
}
