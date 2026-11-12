using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OneBeyondApi.Controllers;
using OneBeyondApi.DataAccess;
using OneBeyondApi.Model;
using Xunit;

namespace OneBeyondApi.Tests;

[Collection("Library database")]
public class ReservationTests : IDisposable
{
    private readonly TestClock _clock = new();
    private readonly TitleLockProvider _locks = new();
    private readonly Book _book;
    private readonly List<Borrower> _borrowers;

    public ReservationTests()
    {
        using var context = new LibraryContext();
        context.Database.EnsureDeleted();
        _book = NewBook("Reserved title");
        _borrowers = Enumerable.Range(0, 12).Select(index => new Borrower
        {
            Name = $"Reader {index}", EmailAddress = $"reader{index}@example.com"
        }).ToList();
        context.Books.Add(_book);
        context.Borrowers.AddRange(_borrowers);
        context.SaveChanges();
    }

    [Fact]
    public void CreateReturnsGuidAndLocationWithoutChangingTheLoan()
    {
        var copyId = AddCopy(new DateTime(2026, 11, 15));
        var controller = new ReservationsController(Repository());
        var result = Assert.IsType<CreatedAtActionResult>(controller.Post(new(_borrowers[0].Id, _book.Id, 7)));
        var created = Assert.IsType<ReservationCreatedDto>(result.Value);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(nameof(ReservationsController.Get), result.ActionName);
        Assert.Equal(created.ReservationId, result.RouteValues!["reservationId"]);
        Assert.NotEqual(Guid.Empty, created.ReservationId);
        var response = Assert.IsType<OkObjectResult>(controller.Get(created.ReservationId).Result);
        var dto = Assert.IsType<ReservationDto>(response.Value);
        Assert.Equal(new DateOnly(2026, 11, 15), dto.EstimatedAvailableOn);
        Assert.Equal(ReservationStatus.Active, dto.Status);
        Assert.Equal(_book.Name, dto.Title);
        using var context = new LibraryContext();
        var saved = Assert.Single(context.Reservations);
        Assert.Equal(_clock.UtcNow, saved.CreatedAtUtc);
        Assert.Equal(7, saved.LoanDays);
        Assert.Equal(_borrowers[0].Id, saved.BorrowerId);
        var copy = context.Catalogue.Include(stock => stock.OnLoanTo).Single(stock => stock.Id == copyId);
        Assert.Equal(_borrowers[11].Id, copy.OnLoanTo!.Id);
        Assert.Equal(new DateTime(2026, 11, 15), copy.LoanEndDate);
    }

    [Fact]
    public void MissingEntitiesNoStockAndUnknownReservationAreHandled()
    {
        var repository = Repository();
        Assert.Equal(ReservationCreationStatus.BorrowerNotFound, repository.Create(Guid.NewGuid(), _book.Id, 7).Status);
        Assert.Equal(ReservationCreationStatus.BookNotFound, repository.Create(_borrowers[0].Id, Guid.NewGuid(), 7).Status);
        Assert.Equal(ReservationCreationStatus.NoStock, repository.Create(_borrowers[0].Id, _book.Id, 7).Status);
        Assert.Null(repository.Get(Guid.NewGuid()));
        AssertNoReservations();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void InvalidDurationDoesNotCreateReservation(int days)
    {
        AddCopy(new DateTime(2026, 11, 15));
        Assert.Equal(ReservationCreationStatus.InvalidDuration, Repository().Create(_borrowers[0].Id, _book.Id, days).Status);
        AssertNoReservations();
    }

    [Theory]
    [InlineData(1, 0, ReservationCreationStatus.AvailableNow)]
    [InlineData(1, 1, ReservationCreationStatus.Created)]
    [InlineData(2, 1, ReservationCreationStatus.AvailableNow)]
    public void FreeCopiesAccountForEarlierReservations(int copies, int reservations, ReservationCreationStatus expected)
    {
        for (var i = 0; i < copies; i++) AddCopy(null, available: true);
        for (var i = 0; i < reservations; i++) AddReservation(i, 7);
        Assert.Equal(expected, Repository().Create(_borrowers[10].Id, _book.Id, 3).Status);
        using var context = new LibraryContext();
        Assert.Equal(reservations + (expected == ReservationCreationStatus.Created ? 1 : 0), context.Reservations.Count());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void BorrowedCopiesDueTodayOrOverdueAreNotPhysicallyAvailable(int dueDay)
    {
        AddCopy(new DateTime(2026, 11, dueDay));
        var id = Create(0, 7);
        Assert.Equal(new DateOnly(2026, 11, 11), Repository().Get(id)!.EstimatedAvailableOn);
    }

    [Fact]
    public void TiedAvailabilityUsesPhysicallyAvailableCopyBeforeBorrowedCopy()
    {
        AddCopy(new DateTime(2026, 11, 11));
        AddCopy(null, available: true);
        AddReservation(0, 7);
        Assert.Equal(ReservationCreationStatus.Created, Repository().Create(_borrowers[1].Id, _book.Id, 3).Status);
    }

    [Fact]
    public void MultipleCopiesAndDifferentDurationsUseEarliestAvailableCopy()
    {
        AddCopy(new DateTime(2026, 11, 15));
        AddCopy(new DateTime(2026, 11, 17));
        var alice = AddReservation(0, 7);
        var bob = AddReservation(1, 3);
        var carol = AddReservation(2, 5);
        Assert.Equal(new DateOnly(2026, 11, 15), Repository().Get(alice.Id)!.EstimatedAvailableOn);
        Assert.Equal(new DateOnly(2026, 11, 17), Repository().Get(bob.Id)!.EstimatedAvailableOn);
        Assert.Equal(new DateOnly(2026, 11, 20), Repository().Get(carol.Id)!.EstimatedAvailableOn);
    }

    [Fact]
    public void EqualTimestampsUseGuidOrderAndEarlierTimestampsTakePriority()
    {
        AddCopy(new DateTime(2026, 11, 15));
        var lowId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var highId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var high = AddReservation(0, 3, id: highId, created: _clock.UtcNow);
        var low = AddReservation(1, 7, id: lowId, created: _clock.UtcNow);
        var older = AddReservation(2, 2, created: _clock.UtcNow.AddMinutes(-1));
        Assert.Equal(new DateOnly(2026, 11, 15), Repository().Get(older.Id)!.EstimatedAvailableOn);
        Assert.Equal(new DateOnly(2026, 11, 17), Repository().Get(low.Id)!.EstimatedAvailableOn);
        Assert.Equal(new DateOnly(2026, 11, 24), Repository().Get(high.Id)!.EstimatedAvailableOn);
    }

    [Fact]
    public void OverdueEstimatesMoveWithClockAndThenUseActualReturn()
    {
        var copyId = AddCopy(new DateTime(2026, 11, 10));
        var first = AddReservation(0, 7);
        var second = AddReservation(1, 3);
        Assert.Equal(new DateOnly(2026, 11, 18), Repository().Get(second.Id)!.EstimatedAvailableOn);
        _clock.UtcNow = _clock.UtcNow.AddDays(2);
        Assert.Equal(new DateOnly(2026, 11, 13), Repository().Get(first.Id)!.EstimatedAvailableOn);
        Assert.Equal(new DateOnly(2026, 11, 20), Repository().Get(second.Id)!.EstimatedAvailableOn);
        Assert.Equal(ReturnBookResult.Returned, new OnLoanRepository(_clock, _locks).ReturnBook(copyId));
        Assert.Equal(new DateOnly(2026, 11, 13), Repository().Get(first.Id)!.EstimatedAvailableOn);
        using var context = new LibraryContext();
        Assert.All(context.Reservations, reservation => Assert.Equal(ReservationStatus.Active, reservation.Status));
    }

    [Fact]
    public void LocalDateIsUsedForOverdueEstimate()
    {
        AddCopy(new DateTime(2026, 11, 11));
        var reservation = AddReservation(0, 7);
        _clock.UtcNow = new DateTimeOffset(2026, 11, 11, 23, 30, 0, TimeSpan.Zero);
        _clock.Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test");
        Assert.Equal(new DateOnly(2026, 11, 12), Repository().Get(reservation.Id)!.EstimatedAvailableOn);
    }

    [Fact]
    public void UnknownDueDateProducesExplanationUntilAnotherCopyHasKnownAvailability()
    {
        AddCopy(null);
        var id = Create(0, 7);
        var result = Repository().Get(id)!;
        Assert.Null(result.EstimatedAvailableOn);
        Assert.NotNull(result.Explanation);
        AddCopy(new DateTime(2026, 11, 15));
        Assert.Equal(new DateOnly(2026, 11, 15), Repository().Get(id)!.EstimatedAvailableOn);
    }

    [Fact]
    public void DateOverflowReturnsUnknownRatherThanThrowing()
    {
        AddCopy(DateTime.MaxValue.Date);
        var first = AddReservation(0, 7);
        var second = AddReservation(1, 3);
        Assert.Null(Repository().Get(first.Id)!.EstimatedAvailableOn);
        Assert.Null(Repository().Get(second.Id)!.EstimatedAvailableOn);
        AddCopy(new DateTime(2026, 11, 15));
        Assert.Equal(new DateOnly(2026, 11, 22), Repository().Get(second.Id)!.EstimatedAvailableOn);
    }

    [Fact]
    public void DuplicateIsRejectedButClosedReservationDoesNotBlockNewRequest()
    {
        AddCopy(new DateTime(2026, 11, 15));
        var original = Create(0, 7);
        Assert.Equal(ReservationCreationStatus.Duplicate, Repository().Create(_borrowers[0].Id, _book.Id, 7).Status);
        using (var context = new LibraryContext())
        {
            context.Reservations.Find(original)!.Status = ReservationStatus.Closed;
            context.SaveChanges();
        }
        var closed = Repository().Get(original)!;
        Assert.Equal(ReservationStatus.Closed, closed.Status);
        Assert.Null(closed.EstimatedAvailableOn);
        Assert.Null(closed.Explanation);
        var next = Create(0, 3);
        Assert.Equal(new DateOnly(2026, 11, 15), Repository().Get(next)!.EstimatedAvailableOn);
    }

    [Fact]
    public void SimulatedCollectionReplacesReservationWithActualLoan()
    {
        var copyId = AddCopy(new DateTime(2026, 11, 11));
        var first = AddReservation(0, 7);
        var second = AddReservation(1, 3);
        using (var context = new LibraryContext())
        {
            var copy = context.Catalogue.Include(stock => stock.OnLoanTo).Single(stock => stock.Id == copyId);
            copy.OnLoanTo = context.Borrowers.Find(_borrowers[0].Id);
            copy.LoanEndDate = new DateTime(2026, 11, 18);
            context.Reservations.Find(first.Id)!.Status = ReservationStatus.Closed;
            context.SaveChanges();
        }
        Assert.Equal(new DateOnly(2026, 11, 18), Repository().Get(second.Id)!.EstimatedAvailableOn);
    }

    [Fact]
    public async Task ParallelDuplicateRequestsCreateExactlyOneReservation()
    {
        AddCopy(new DateTime(2026, 11, 15));
        using var start = new ManualResetEventSlim();
        var requests = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return Repository().Create(_borrowers[0].Id, _book.Id, 7);
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(results, result => result.Status == ReservationCreationStatus.Created);
        Assert.Equal(7, results.Count(result => result.Status == ReservationCreationStatus.Duplicate));
        using var context = new LibraryContext();
        Assert.Single(context.Reservations);
    }

    [Fact]
    public async Task ParallelDifferentBorrowersGetDistinctReservationsAndDeterministicEstimates()
    {
        AddCopy(new DateTime(2026, 11, 15));
        var requests = _borrowers.Take(8).Select(borrower => Task.Run(() => Repository().Create(borrower.Id, _book.Id, 1)));
        var results = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(results, result => Assert.Equal(ReservationCreationStatus.Created, result.Status));
        Assert.Equal(8, results.Select(result => result.ReservationId).Distinct().Count());
        var ordered = results.Select(result => result.ReservationId!.Value).OrderBy(id => id).ToList();
        for (var i = 0; i < ordered.Count; i++)
            Assert.Equal(new DateOnly(2026, 11, 15).AddDays(i), Repository().Get(ordered[i])!.EstimatedAvailableOn);
    }

    [Fact]
    public async Task HoldingOneTitleLockDoesNotBlockAnotherTitle()
    {
        Guid otherId;
        using (var context = new LibraryContext())
        {
            var other = NewBook("Another title");
            context.Catalogue.Add(new BookStock { Book = other, OnLoanTo = context.Borrowers.Find(_borrowers[11].Id), LoanEndDate = new DateTime(2026, 11, 15) });
            context.SaveChanges();
            otherId = other.Id;
        }
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (_locks.GetLock(_book.Id))
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var result = await Task.Run(() => Repository().Create(_borrowers[0].Id, otherId, 7)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ReservationCreationStatus.Created, result.Status);
        }
        finally
        {
            release.Set();
            await holder;
        }
    }

    [Fact]
    public async Task ExceptionInsideTitleLockDoesNotBlockLaterRequest()
    {
        AddCopy(new DateTime(2026, 11, 15));
        var failing = new ReservationRepository(new ThrowingClock(), _locks);
        Assert.Throws<InvalidOperationException>(() => failing.Create(_borrowers[0].Id, _book.Id, 7));
        var result = await Task.Run(() => Repository().Create(_borrowers[0].Id, _book.Id, 7)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ReservationCreationStatus.Created, result.Status);
    }

    private ReservationRepository Repository() => new(_clock, _locks);

    private Guid Create(int borrower, int days)
    {
        var result = Repository().Create(_borrowers[borrower].Id, _book.Id, days);
        Assert.Equal(ReservationCreationStatus.Created, result.Status);
        return result.ReservationId!.Value;
    }

    private Guid AddCopy(DateTime? dueDate, bool available = false)
    {
        using var context = new LibraryContext();
        var copy = new BookStock
        {
            Book = context.Books.Find(_book.Id)!,
            OnLoanTo = available ? null : context.Borrowers.Find(_borrowers[11].Id),
            LoanEndDate = dueDate
        };
        context.Catalogue.Add(copy);
        context.SaveChanges();
        return copy.Id;
    }

    private Reservation AddReservation(int borrower, int days, Guid? id = null, DateTimeOffset? created = null)
    {
        using var context = new LibraryContext();
        var reservation = new Reservation
        {
            Id = id ?? Guid.NewGuid(),
            CreatedAtUtc = created ?? _clock.UtcNow.AddMinutes(borrower),
            BorrowerId = _borrowers[borrower].Id,
            Borrower = context.Borrowers.Find(_borrowers[borrower].Id)!,
            BookId = _book.Id,
            Book = context.Books.Find(_book.Id)!,
            LoanDays = days,
            Status = ReservationStatus.Active
        };
        context.Reservations.Add(reservation);
        context.SaveChanges();
        return reservation;
    }

    private static Book NewBook(string title) => new()
    {
        Name = title, Author = new Author { Name = "Author" }, ISBN = "1234567890", Format = BookFormat.Paperback
    };

    private static void AssertNoReservations()
    {
        using var context = new LibraryContext();
        Assert.Empty(context.Reservations);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override TimeZoneInfo LocalTimeZone => Zone;
    }

    private sealed class ThrowingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Simulated failure inside lock.");
    }

    public void Dispose()
    {
        using var context = new LibraryContext();
        context.Database.EnsureDeleted();
    }
}
