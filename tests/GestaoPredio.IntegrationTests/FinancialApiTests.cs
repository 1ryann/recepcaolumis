using System.Net;
using System.Net.Http.Json;
using GestaoPredio.Domain.Leases;
using GestaoPredio.Domain.Professionals;
using GestaoPredio.Domain.Rooms;
using GestaoPredio.Domain.Security;
using GestaoPredio.Domain.Tenants;
using GestaoPredio.Application.Finance;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestaoPredio.IntegrationTests;

[Collection(ModulesDatabaseCollection.Name)]
public sealed class FinancialApiTests(ModulesApiFactory factory)
{
    [Fact]
    public async Task Materialization_is_idempotent_and_admin_list_exposes_frozen_amounts()
    {
        await factory.ResetAsync();
        var now = new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);
        var tenant = Tenant.Create("Pagador Financeiro", TenantKind.Individual, now);
        var professional = Professional.Create("Profissional Financeiro", "Teste", "65999990003", now);
        var room = Room.Create("Sala Financeira", null, 50m, 100m, now);
        var lease = Lease.Create(tenant.Id, professional.Id, room.Id, LeaseMode.Monthly, 50m,
            now, 31, now, null, 31, now);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(tenant, professional, room, lease);
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.Leases.CountAsync());
        }
        await using (var calcScope = factory.Services.CreateAsyncScope())
        {
            var calculator = calcScope.ServiceProvider.GetRequiredService<IFinancialChargeCalculator>();
            Assert.Equal(2, calculator.Calculate(lease, new DateTimeOffset(2026, 4, 3, 4, 0, 0, TimeSpan.Zero)).Count);
        }
        var email = $"finance-{Guid.NewGuid():N}@lumis.test";
        await factory.CreateUserAsync(email, "Valid-Password-123!", [SystemRoles.Administrador]);
        Assert.Equal(HttpStatusCode.NoContent, (await factory.LoginAsync(email, "Valid-Password-123!")).StatusCode);

        var first = await factory.PostWithCsrfAsync("/api/admin/finance/materialize", new { throughDate = "2026-04-02" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(2, (await first.Content.ReadFromJsonAsync<MaterializationResponse>())!.CreatedCount);
        var second = await factory.PostWithCsrfAsync("/api/admin/finance/materialize", new { throughDate = "2026-04-02" });
        Assert.Equal(0, (await second.Content.ReadFromJsonAsync<MaterializationResponse>())!.CreatedCount);
        await using (var verifyScope = factory.Services.CreateAsyncScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(2, await verifyDb.FinancialCharges.CountAsync());
            var joinedCount = await (from charge in verifyDb.FinancialCharges
                                     join p in verifyDb.Professionals on charge.ProfessionalId equals p.Id
                                     join t in verifyDb.Tenants on charge.TenantId equals t.Id
                                     select charge).CountAsync();
            Assert.Equal(2, joinedCount);
        }

        var pageResponse = await factory.Client.GetAsync("/api/admin/finance/charges?status=all");
        var pageText = await pageResponse.Content.ReadAsStringAsync();
        var page = System.Text.Json.JsonSerializer.Deserialize<ChargePage>(pageText, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.True(page is not null, pageText);
        Assert.True(page!.TotalCount == 2, pageText);
        Assert.All(page.Items, item => Assert.Equal(50m, item.CalculatedAmount));
        Assert.All(page.Items, item => Assert.Equal(50m, item.FinalAmount));

        var target = page.Items[0];
        var adjusted = await factory.PostWithCsrfAsync($"/api/admin/finance/charges/{target.Id}/adjust", new
        {
            finalAmount = 40m, adjustmentReason = "Concessão aprovada", concurrencyToken = target.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.OK, adjusted.StatusCode);
        var adjustedBody = (await adjusted.Content.ReadFromJsonAsync<ChargeResponse>())!;
        Assert.Equal(40m, adjustedBody.FinalAmount);
        var paid = await factory.PostWithCsrfAsync($"/api/admin/finance/charges/{target.Id}/pay", new
        {
            concurrencyToken = adjustedBody.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal("PAID", (await paid.Content.ReadFromJsonAsync<ChargeResponse>())!.Status);
    }

    private sealed record MaterializationResponse(DateOnly ThroughDate, int CreatedCount);
    private sealed record ChargePage(IReadOnlyList<ChargeResponse> Items, int Page, int PageSize, int TotalCount);
    private sealed record ChargeResponse(Guid Id, decimal CalculatedAmount, decimal FinalAmount, string Status, string ConcurrencyToken);
}
