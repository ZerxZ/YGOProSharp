using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace YGOProSharp.Persistence.Migrations;

[DbContext(typeof(OperationalDbContext))]
internal sealed class OperationalDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        OperationalDbContext.ConfigureOperationalModel(modelBuilder);
    }
}
