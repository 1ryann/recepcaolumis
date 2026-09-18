using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class ModulePersistenceTests(ModulesApiFactory factory)
{
    [Fact]
    public void Factory_connection_is_guarded_before_fixture_database_work()
    {
        ModulesApiFactory.ValidateTestConfiguration("Testing", factory.ConnectionString);
        Assert.Equal(LocalPostgreSqlTestDatabase.DatabaseName, factory.DatabaseName);
        Assert.StartsWith(LocalPostgreSqlTestDatabase.SchemaPrefix, factory.SchemaName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expanded_unicode_values_persist_at_the_approved_input_limits()
    {
        await factory.ResetAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var occurredAt = factory.UtcNow;
        var professional = Professional.Create(
            new string('ß', 200),
            new string('ß', 150),
            "65999999999",
            occurredAt);
        var room = Room.Create(new string('ß', 100), null, 0m, 0m, occurredAt);
        db.Professionals.Add(professional);
        db.Rooms.Add(room);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var storedProfessional = await db.Professionals.AsNoTracking().SingleAsync(x => x.Id == professional.Id);
        var storedRoom = await db.Rooms.AsNoTracking().SingleAsync(x => x.Id == room.Id);
        Assert.Equal(400, storedProfessional.NormalizedName.Length);
        Assert.Equal(300, storedProfessional.NormalizedProfession.Length);
        Assert.Equal(200, storedRoom.NormalizedName.Length);
    }
}
