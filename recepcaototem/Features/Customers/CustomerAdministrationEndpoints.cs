using GestaoPredio.Domain.Customers;
using GestaoPredio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using recepcaototem.Features.Auth;
using recepcaototem.Features.Common;
using recepcaototem.Features.Professionals;
using recepcaototem.Features.Whatsapp;

namespace recepcaototem.Features.Customers;

public sealed record CustomerAdministrationResponse(Guid Id, string Name, string Phone, bool IsActive,
    bool HasAccount, WhatsappOptInResponse WhatsAppOptIn, DateTimeOffset CreatedAt, string ConcurrencyToken);

/// <summary>
/// Reception and administration view of the customer records created by bookings and self-registration. Deactivating
/// one is how a duplicate or mistyped record stops being usable: an inactive customer cannot book, check in or be
/// notified. Nothing here deletes a record, because appointments and audit entries point at it.
/// </summary>
public static class CustomerAdministrationEndpoints
{
    public static IEndpointRouteBuilder MapCustomerAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/customers").RequireAuthorization("Operations");
        group.MapGet("", List);
        group.MapPost("/{id:guid}/activate", Activate).AddEndpointFilter<AntiforgeryFilter>();
        group.MapPost("/{id:guid}/deactivate", Deactivate).AddEndpointFilter<AntiforgeryFilter>();
        return endpoints;
    }

    private static async Task<IResult> List(int? page, int? pageSize, string? status, string? search,
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!PagingQuery.TryCreate(page, pageSize, status, search, out var paging, out var error))
            return Results.BadRequest(error);

        var query = db.Customers.AsNoTracking();
        query = paging!.Status switch
        {
            PagingQuery.Active => query.Where(x => x.IsActive),
            PagingQuery.Inactive => query.Where(x => !x.IsActive),
            _ => query
        };
        if (paging.Search is { } term)
        {
            // Customers have no normalized name column, so the name is matched case-insensitively and the phone by its
            // digits, which is how reception has the number at hand (+55 69 9 9953-8007 typed as 69999538007).
            var digits = new string(term.Where(char.IsDigit).ToArray());
            query = digits.Length > 0
                ? query.Where(x => x.NormalizedPhone.Contains(digits) || EF.Functions.ILike(x.Name, $"%{term}%"))
                : query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%"));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var customers = await query
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize)
            .ToListAsync(cancellationToken);
        return Results.Ok(new PagedResponse<CustomerAdministrationResponse>(
            customers.Select(ToResponse).ToArray(), paging.Page, paging.PageSize, totalCount));
    }

    private static Task<IResult> Activate(Guid id, ConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
        ChangeStatus(id, request, true, context, db, time, cancellationToken);

    private static Task<IResult> Deactivate(Guid id, ConcurrencyRequest request, HttpContext context,
        ApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
        ChangeStatus(id, request, false, context, db, time, cancellationToken);

    private static async Task<IResult> ChangeStatus(Guid id, ConcurrencyRequest request, bool isActive,
        HttpContext context, ApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        if (!ConcurrencyToken.TryDecode(request.ConcurrencyToken, out var expectedVersion))
            return ProfessionalEndpoints.InvalidToken();

        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (customer is null) return Results.NotFound();
        if (customer.Version != expectedVersion) return ProfessionalEndpoints.Modified();
        if (customer.IsActive == isActive) return Results.Ok(ToResponse(customer));

        db.Entry(customer).Property(x => x.Version).OriginalValue = expectedVersion;
        var now = time.GetUtcNow();
        if (isActive) customer.Activate(now); else customer.Deactivate(now);
        var audit = ProfessionalEndpoints.CreateAudit(context, customer.Id,
            isActive ? "CUSTOMER_ACTIVATED" : "CUSTOMER_DEACTIVATED", now);
        audit.TargetEntityType = "CUSTOMER";
        db.AuditEntries.Add(audit);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ProfessionalEndpoints.Modified();
        }
        return Results.Ok(ToResponse(customer));
    }

    private static CustomerAdministrationResponse ToResponse(Customer customer) => new(
        customer.Id, customer.Name, customer.Phone, customer.IsActive, customer.ApplicationUserId is not null,
        WhatsappOptInEndpoints.Response(customer.WhatsAppOptIn), customer.CreatedAt,
        ConcurrencyToken.Encode(customer.Version));
}
