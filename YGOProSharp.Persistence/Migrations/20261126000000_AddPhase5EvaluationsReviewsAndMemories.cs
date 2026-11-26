using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace YGOProSharp.Persistence.Migrations;

[DbContext(typeof(OperationalDbContext))]
[Migration("20261126000000_AddPhase5EvaluationsReviewsAndMemories")]
public partial class AddPhase5EvaluationsReviewsAndMemories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "deck_evaluations",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                DeckHash = table.Column<string>(maxLength: 64, nullable: false),
                DeckName = table.Column<string>(maxLength: 128, nullable: false),
                SourceType = table.Column<string>(maxLength: 32, nullable: false),
                SourceRef = table.Column<string>(maxLength: 128, nullable: true),
                ArchetypeTitle = table.Column<string>(maxLength: 128, nullable: false),
                BurstPower = table.Column<int>(nullable: false),
                Resilience = table.Column<int>(nullable: false),
                StunToxicity = table.Column<int>(nullable: false),
                FunFactor = table.Column<int>(nullable: false),
                Complexity = table.Column<int>(nullable: false),
                MetaAdaptability = table.Column<int>(nullable: false),
                OverallTier = table.Column<string>(maxLength: 8, nullable: false),
                RoutinesJson = table.Column<string>(nullable: false),
                Commentary = table.Column<string>(nullable: false),
                EvaluatorName = table.Column<string>(maxLength: 64, nullable: false),
                EvaluatorTokenId = table.Column<Guid>(nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_deck_evaluations", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_deck_evaluations_DeckHash", table: "deck_evaluations", column: "DeckHash");
        migrationBuilder.CreateIndex(name: "IX_deck_evaluations_UpdatedAt", table: "deck_evaluations", column: "UpdatedAt");

        migrationBuilder.CreateTable(
            name: "duel_reviews",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                DuelRecordId = table.Column<long>(nullable: false),
                Headline = table.Column<string>(maxLength: 200, nullable: false),
                Summary = table.Column<string>(nullable: false),
                PlayerRatingsJson = table.Column<string>(nullable: false),
                TurningPointsJson = table.Column<string>(nullable: false),
                ObservedPatternsJson = table.Column<string>(nullable: false),
                ReviewerName = table.Column<string>(maxLength: 64, nullable: false),
                ReviewerRole = table.Column<string>(maxLength: 32, nullable: false),
                ReviewerTokenId = table.Column<Guid>(nullable: true),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_duel_reviews", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_duel_reviews_DuelRecordId", table: "duel_reviews", column: "DuelRecordId");
        migrationBuilder.CreateIndex(name: "IX_duel_reviews_CreatedAt", table: "duel_reviews", column: "CreatedAt");

        migrationBuilder.CreateTable(
            name: "ai_duelist_memories",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TokenId = table.Column<Guid>(nullable: false),
                DeckHash = table.Column<string>(maxLength: 64, nullable: true),
                OpponentArchetype = table.Column<string>(maxLength: 128, nullable: true),
                OpponentName = table.Column<string>(maxLength: 64, nullable: true),
                Result = table.Column<string>(maxLength: 16, nullable: false),
                StrategicNote = table.Column<string>(maxLength: 600, nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false),
                UpdatedAt = table.Column<DateTime>(nullable: false),
                DeletedAt = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ai_duelist_memories", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_ai_duelist_memories_TokenId", table: "ai_duelist_memories", column: "TokenId");
        migrationBuilder.CreateIndex(name: "IX_ai_duelist_memories_TokenId_DeckHash", table: "ai_duelist_memories", columns: ["TokenId", "DeckHash"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ai_duelist_memories");
        migrationBuilder.DropTable(name: "duel_reviews");
        migrationBuilder.DropTable(name: "deck_evaluations");
    }
}
