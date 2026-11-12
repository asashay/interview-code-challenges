using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OneBeyondApi.DataAccess;
using OneBeyondApi.Model;
using Xunit;

namespace OneBeyondApi.Tests;

[Collection("Library database")]
public class LibraryHttpTests : IClassFixture<LibraryApiFactory>, IDisposable
{
    private readonly HttpClient _client;
    private readonly Borrower _reader = new() { Name = "Reader", EmailAddress = "reader@example.com" };
    private readonly Borrower _waiting = new() { Name = "Waiting reader", EmailAddress = "waiting@example.com" };
    private readonly BookStock _future;
    private readonly BookStock _overdue;
    private readonly BookStock _available;
    private readonly Book _withoutStock = NewBook("No stock");

    public LibraryHttpTests(LibraryApiFactory factory)
    {
        // Starting the host runs the starter's seeding; reset only after it has finished.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var context = new LibraryContext();
        context.Database.EnsureDeleted();
        _future = new BookStock
        {
            Book = NewBook("Future title"), OnLoanTo = _reader, LoanEndDate = new DateTime(2026, 11, 15)
        };
        _overdue = new BookStock
        {
            Book = NewBook("Overdue title"), OnLoanTo = _reader, LoanEndDate = new DateTime(2026, 11, 10)
        };
        _available = new BookStock { Book = NewBook("Available title") };
        context.Catalogue.AddRange(_future, _overdue, _available);
        context.Borrowers.Add(_waiting);
        context.Books.Add(_withoutStock);
        context.SaveChanges();
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"bookStockId\":null}")]
    [InlineData("{\"bookStockId\":\"not-a-guid\"}")]
    [InlineData("{\"bookStockId\":123}")]
    public async Task ReturnRejectsInvalidJsonBodies(string json)
    {
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        await AssertProblem(await _client.PostAsync("/OnLoan", body), HttpStatusCode.BadRequest);

        using var context = new LibraryContext();
        Assert.Empty(context.Fines);
        Assert.Equal(2, context.Catalogue.Count(copy => copy.OnLoanTo != null));
    }

    [Theory]
    [InlineData("borrowerId", null)]
    [InlineData("bookId", null)]
    [InlineData("loanDays", null)]
    [InlineData("borrowerId", "null")]
    [InlineData("bookId", "null")]
    [InlineData("loanDays", "null")]
    [InlineData("borrowerId", "\"not-a-guid\"")]
    [InlineData("bookId", "\"not-a-guid\"")]
    [InlineData("loanDays", "\"seven\"")]
    [InlineData("loanDays", "0")]
    [InlineData("loanDays", "-1")]
    [InlineData("loanDays", "2147483647")]
    public async Task ReservationRejectsInvalidFieldsAndDurations(string property, string? jsonValue)
    {
        var body = new Dictionary<string, object?>
        {
            ["borrowerId"] = _waiting.Id, ["bookId"] = _future.Book.Id, ["loanDays"] = 7
        };
        if (jsonValue == null) body.Remove(property);
        else body[property] = JsonSerializer.Deserialize<JsonElement>(jsonValue);

        await AssertProblem(await _client.PostAsJsonAsync("/Reservations", body), HttpStatusCode.BadRequest);

        using var context = new LibraryContext();
        Assert.Empty(context.Reservations);
    }

    [Theory]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest)]
    [InlineData("00000000-0000-0000-0000-000000000000", HttpStatusCode.NotFound)]
    public async Task ReservationHandlesInvalidAndUnknownRouteIds(string id, HttpStatusCode expected)
    {
        await AssertProblem(await _client.GetAsync($"/Reservations/{id}"), expected);
    }

    [Fact]
    public async Task UnknownEntitiesAndConflictsReturnProblemDetails()
    {
        await AssertProblem(await _client.PostAsJsonAsync("/OnLoan", new ReturnBookRequest(Guid.NewGuid())),
            HttpStatusCode.NotFound);
        await AssertProblem(await _client.PostAsJsonAsync("/OnLoan", new ReturnBookRequest(_available.Id)),
            HttpStatusCode.Conflict);
        await AssertProblem(await _client.PostAsJsonAsync("/Reservations",
            new CreateReservationRequest(Guid.NewGuid(), _future.Book.Id, 7)), HttpStatusCode.NotFound);
        await AssertProblem(await _client.PostAsJsonAsync("/Reservations",
            new CreateReservationRequest(_waiting.Id, Guid.NewGuid(), 7)), HttpStatusCode.NotFound);
        await AssertProblem(await _client.PostAsJsonAsync("/Reservations",
            new CreateReservationRequest(_waiting.Id, _withoutStock.Id, 7)), HttpStatusCode.Conflict);
        await AssertProblem(await _client.PostAsJsonAsync("/Reservations",
            new CreateReservationRequest(_waiting.Id, _available.Book.Id, 7)), HttpStatusCode.Conflict);

        var request = new CreateReservationRequest(_waiting.Id, _future.Book.Id, 7);
        using var created = await _client.PostAsJsonAsync("/Reservations", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await AssertProblem(await _client.PostAsJsonAsync("/Reservations", request), HttpStatusCode.Conflict);
        using var context = new LibraryContext();
        Assert.Single(context.Reservations);
        Assert.Empty(context.Fines);
    }

    [Fact]
    public async Task ReservationLocationAndReturnsReflectPersistedLoanState()
    {
        var loans = await _client.GetFromJsonAsync<List<BorrowerLoansDto>>("/OnLoan");
        var reader = Assert.Single(loans!);
        Assert.Equal(_reader.Id, reader.BorrowerId);
        Assert.Equal(new[] { "Future title", "Overdue title" }, reader.Titles);

        using var created = await _client.PostAsJsonAsync("/Reservations",
            new CreateReservationRequest(_waiting.Id, _future.Book.Id, 7));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reservation = await created.Content.ReadFromJsonAsync<ReservationCreatedDto>();
        Assert.NotNull(reservation);
        Assert.NotEqual(Guid.Empty, reservation.ReservationId);
        var location = created.Headers.Location;
        Assert.NotNull(location);
        Assert.EndsWith($"/Reservations/{reservation.ReservationId}", location.ToString());

        using var details = await _client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var json = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
        Assert.Equal("Active", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("2026-11-15", json.RootElement.GetProperty("estimatedAvailableOn").GetString());
        Assert.Equal(_waiting.Id, json.RootElement.GetProperty("borrowerId").GetGuid());

        using var earlyReturn = await _client.PostAsJsonAsync("/OnLoan", new ReturnBookRequest(_future.Id));
        Assert.Equal(HttpStatusCode.NoContent, earlyReturn.StatusCode);
        Assert.Equal("", await earlyReturn.Content.ReadAsStringAsync());
        var updated = await _client.GetFromJsonAsync<ReservationDto>(location);
        Assert.NotNull(updated);
        Assert.Equal(new DateOnly(2026, 11, 11), updated.EstimatedAvailableOn);
        Assert.Equal(ReservationStatus.Active, updated.Status);
        var remaining = await _client.GetFromJsonAsync<List<BorrowerLoansDto>>("/OnLoan");
        Assert.Equal("Overdue title", Assert.Single(Assert.Single(remaining!).Titles));

        using var lateReturn = await _client.PostAsJsonAsync("/OnLoan", new ReturnBookRequest(_overdue.Id));
        Assert.Equal(HttpStatusCode.NoContent, lateReturn.StatusCode);
        await AssertProblem(await _client.PostAsJsonAsync("/OnLoan", new ReturnBookRequest(_overdue.Id)),
            HttpStatusCode.Conflict);
        Assert.Empty((await _client.GetFromJsonAsync<List<BorrowerLoansDto>>("/OnLoan"))!);
        using var context = new LibraryContext();
        var fine = Assert.Single(context.Fines);
        Assert.Equal(_reader.Id, fine.BorrowerId);
        Assert.Equal(_overdue.Id, fine.BookStockId);
        Assert.Equal(new DateOnly(2026, 11, 10), fine.LoanEndDate);
        Assert.Equal(new DateOnly(2026, 11, 11), fine.ReturnedDate);
        Assert.All(context.Catalogue.Include(copy => copy.OnLoanTo), copy =>
        {
            Assert.Null(copy.OnLoanTo);
            Assert.Null(copy.LoanEndDate);
        });
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)expected, json.RootElement.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("title").GetString()));
        }
    }

    private static Book NewBook(string title) => new()
    {
        Name = title, Author = new Author { Name = "Author" }, ISBN = "1234567890", Format = BookFormat.Paperback
    };

    public void Dispose()
    {
        _client.Dispose();
        using var context = new LibraryContext();
        context.Database.EnsureDeleted();
    }
}

public class LibraryApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider());
        });
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
