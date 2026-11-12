using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OneBeyondApi.Controllers;
using OneBeyondApi.DataAccess;
using OneBeyondApi.Model;
using Xunit;

namespace OneBeyondApi.Tests;

[Collection("Library database")]
public class OnLoanTests : IDisposable
{
    private readonly TitleLockProvider _locks = new();

    public OnLoanTests()
    {
        using var context = new LibraryContext();
        context.Database.EnsureDeleted();
    }

    [Fact]
    public void GetReturnsOkWithEmptyListWhenNoLoansExist()
    {
        var controller = new OnLoanController(CreateRepository());

        var response = Assert.IsType<OkObjectResult>(controller.Get().Result);

        Assert.Equal(200, response.StatusCode);
        Assert.Empty(Assert.IsType<List<BorrowerLoansDto>>(response.Value));
    }

    [Fact]
    public void GetLoansExcludesAvailableCopiesAndBorrowersWithoutLoans()
    {
        using var context = new LibraryContext();
        context.Borrowers.Add(Borrower("No loans"));
        context.Catalogue.Add(Copy("Available", null, null));
        context.SaveChanges();

        Assert.Empty(CreateRepository().GetLoans());
    }

    [Fact]
    public void GetLoansIncludesOverdueFutureAndUndatedLoans()
    {
        using var context = new LibraryContext();
        var borrower = Borrower("Reader");
        context.Catalogue.AddRange(
            Copy("Overdue", borrower, new DateTime(2000, 1, 1)),
            Copy("Future", borrower, new DateTime(2100, 1, 1)),
            Copy("Undated", borrower, null));
        context.SaveChanges();

        var result = Assert.Single(CreateRepository().GetLoans());

        Assert.Equal(borrower.Id, result.BorrowerId);
        Assert.Equal(borrower.Name, result.Name);
        Assert.Equal(borrower.EmailAddress, result.EmailAddress);
        Assert.Equal(new[] { "Future", "Overdue", "Undated" }, result.Titles);
    }

    [Fact]
    public void GetLoansGroupsByIdAndPreservesMultipleCopiesOfATitle()
    {
        using var context = new LibraryContext();
        var first = Borrower("Same name");
        var second = Borrower("Same name");
        var firstCopy = Copy("Shared title", first, null);
        var secondCopy = new BookStock { Book = firstCopy.Book, OnLoanTo = first };
        context.Catalogue.AddRange(
            firstCopy,
            secondCopy,
            Copy("Other title", second, null));
        context.SaveChanges();

        var results = CreateRepository().GetLoans();

        Assert.Equal(2, results.Count);
        Assert.Equal(new[] { "Shared title", "Shared title" },
            results.Single(result => result.BorrowerId == first.Id).Titles);
        Assert.Equal(new[] { "Other title" },
            results.Single(result => result.BorrowerId == second.Id).Titles);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2100)]
    [InlineData(null)]
    public void ReturnClearsLoanAndRemovesBorrowerAfterFinalReturn(int? dueYear)
    {
        using var context = new LibraryContext();
        var borrower = Borrower("Reader");
        var copy = Copy("Borrowed", borrower,
            dueYear.HasValue ? new DateTime(dueYear.Value, 1, 1) : null);
        context.Catalogue.Add(copy);
        context.SaveChanges();
        var repository = CreateRepository();
        var controller = new OnLoanController(repository);

        Assert.IsType<NoContentResult>(controller.Return(new ReturnBookRequest(copy.Id)));

        using var freshContext = new LibraryContext();
        var returned = freshContext.Catalogue.Include(stock => stock.OnLoanTo)
            .Single(stock => stock.Id == copy.Id);
        Assert.Null(returned.OnLoanTo);
        Assert.Null(returned.LoanEndDate);
        Assert.Empty(repository.GetLoans());
        Assert.True(freshContext.Borrowers.Any(reader => reader.Id == borrower.Id));
        Assert.True(freshContext.Books.Any(book => book.Id == copy.Book.Id));
    }

    [Fact]
    public void ReturnOnlyAffectsSelectedCopyAndPreservesOtherLoans()
    {
        using var context = new LibraryContext();
        var borrower = Borrower("Reader");
        var otherBorrower = Borrower("Other reader");
        var dueDate = new DateTime(2100, 1, 1);
        var selected = Copy("Shared title", borrower, dueDate);
        var remaining = new BookStock
        {
            Book = selected.Book, OnLoanTo = borrower, LoanEndDate = dueDate
        };
        var other = new BookStock
        {
            Book = selected.Book, OnLoanTo = otherBorrower, LoanEndDate = dueDate
        };
        context.Catalogue.AddRange(selected, remaining, other);
        context.SaveChanges();
        var repository = CreateRepository();

        Assert.Equal(ReturnBookResult.Returned, repository.ReturnBook(selected.Id));

        var loans = repository.GetLoans();
        Assert.Equal(2, loans.Count);
        Assert.Equal("Shared title", Assert.Single(loans.Single(loan => loan.BorrowerId == borrower.Id).Titles));
        Assert.Equal("Shared title", Assert.Single(loans.Single(loan => loan.BorrowerId == otherBorrower.Id).Titles));
        using var freshContext = new LibraryContext();
        var copies = freshContext.Catalogue.Include(stock => stock.OnLoanTo).ToList();
        Assert.Null(copies.Single(stock => stock.Id == selected.Id).OnLoanTo);
        Assert.Equal(borrower.Id, copies.Single(stock => stock.Id == remaining.Id).OnLoanTo!.Id);
        Assert.Equal(otherBorrower.Id, copies.Single(stock => stock.Id == other.Id).OnLoanTo!.Id);
        Assert.Equal(dueDate, freshContext.Catalogue.Single(stock => stock.Id == remaining.Id).LoanEndDate);
        Assert.Equal(dueDate, freshContext.Catalogue.Single(stock => stock.Id == other.Id).LoanEndDate);
    }

    [Fact]
    public void ReturnUnknownCopyReturnsNotFoundWithoutChangingLoans()
    {
        using var context = new LibraryContext();
        var copy = Copy("Borrowed", Borrower("Reader"), new DateTime(2100, 1, 1));
        context.Catalogue.Add(copy);
        context.SaveChanges();
        var repository = CreateRepository();
        var controller = new OnLoanController(repository);

        var response = Assert.IsType<ObjectResult>(controller.Return(new ReturnBookRequest(Guid.NewGuid())));

        Assert.Equal(404, response.StatusCode);
        Assert.Equal(404, Assert.IsType<ProblemDetails>(response.Value).Status);
        Assert.Equal("Borrowed", Assert.Single(Assert.Single(repository.GetLoans()).Titles));
        using var freshContext = new LibraryContext();
        Assert.Empty(freshContext.Fines);
    }

    [Fact]
    public void ReturnAvailableCopyAndRepeatedReturnProduceConflict()
    {
        using var context = new LibraryContext();
        var available = Copy("Available", null, null);
        var borrowed = Copy("Borrowed", Borrower("Reader"), new DateTime(2100, 1, 1));
        context.Catalogue.AddRange(available, borrowed);
        context.SaveChanges();
        var controller = new OnLoanController(CreateRepository());

        var availableResponse = Assert.IsType<ObjectResult>(controller.Return(new ReturnBookRequest(available.Id)));
        Assert.Equal(409, availableResponse.StatusCode);
        Assert.Equal(409, Assert.IsType<ProblemDetails>(availableResponse.Value).Status);

        Assert.IsType<NoContentResult>(controller.Return(new ReturnBookRequest(borrowed.Id)));
        var repeatedResponse = Assert.IsType<ObjectResult>(controller.Return(new ReturnBookRequest(borrowed.Id)));
        Assert.Equal(409, repeatedResponse.StatusCode);
        Assert.Equal(409, Assert.IsType<ProblemDetails>(repeatedResponse.Value).Status);
        Assert.Empty(CreateRepository().GetLoans());
        using var freshContext = new LibraryContext();
        Assert.Empty(freshContext.Fines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(null)]
    public void ReturnOnTimeOrWithoutDueDateDoesNotCreateFine(int? daysUntilDue)
    {
        using var context = new LibraryContext();
        // The fixed clock is at 23:59:59: a midnight deadline today is still on time.
        var dueDate = daysUntilDue.HasValue
            ? new DateTime(2026, 11, 11).AddDays(daysUntilDue.Value)
            : (DateTime?)null;
        var copy = Copy("Borrowed", Borrower("Reader"), dueDate);
        context.Catalogue.Add(copy);
        context.SaveChanges();

        Assert.Equal(ReturnBookResult.Returned, CreateRepository().ReturnBook(copy.Id));

        using var freshContext = new LibraryContext();
        Assert.Empty(freshContext.Fines);
        Assert.Null(freshContext.Catalogue.Single(stock => stock.Id == copy.Id).LoanEndDate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(365)]
    public void LateReturnStoresFineWithOriginalBorrowerCopyAndDates(int daysLate)
    {
        using var context = new LibraryContext();
        var borrower = Borrower("Late reader");
        var returnedDate = new DateOnly(2026, 11, 11);
        var dueDate = returnedDate.AddDays(-daysLate);
        var copy = Copy("Borrowed", borrower, dueDate.ToDateTime(new TimeOnly(18, 30)));
        context.Catalogue.Add(copy);
        context.SaveChanges();
        var controller = new OnLoanController(CreateRepository());

        Assert.IsType<NoContentResult>(controller.Return(new ReturnBookRequest(copy.Id)));
        var repeat = Assert.IsType<ObjectResult>(controller.Return(new ReturnBookRequest(copy.Id)));
        Assert.Equal(409, repeat.StatusCode);

        using var freshContext = new LibraryContext();
        var fine = Assert.Single(freshContext.Fines
            .Include(item => item.Borrower)
            .Include(item => item.BookStock).ThenInclude(stock => stock.OnLoanTo));
        Assert.NotEqual(Guid.Empty, fine.Id);
        Assert.Equal(borrower.Id, fine.BorrowerId);
        Assert.Equal(borrower.Name, fine.Borrower.Name);
        Assert.Equal(copy.Id, fine.BookStockId);
        Assert.Equal(copy.Id, fine.BookStock.Id);
        Assert.Equal(dueDate, fine.LoanEndDate);
        Assert.Equal(returnedDate, fine.ReturnedDate);
        Assert.Equal(daysLate, fine.ReturnedDate.DayNumber - fine.LoanEndDate.DayNumber);
        Assert.Null(fine.BookStock.OnLoanTo);
        Assert.Null(fine.BookStock.LoanEndDate);
        Assert.Empty(CreateRepository().GetLoans());
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(-2, 0)]
    public void FineUsesLocalCalendarDateRatherThanUtcDate(int offsetHours, int expectedFines)
    {
        using var context = new LibraryContext();
        var copy = Copy("Borrowed", Borrower("Reader"), new DateTime(2026, 11, 11));
        context.Catalogue.Add(copy);
        context.SaveChanges();
        // UTC is still November 11, but the +02:00 local date is November 12.
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test local", TimeSpan.FromHours(offsetHours), "Test local", "Test local");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 11, 23, 59, 59, TimeSpan.Zero), zone);

        Assert.Equal(ReturnBookResult.Returned, new OnLoanRepository(clock, _locks).ReturnBook(copy.Id));

        using var freshContext = new LibraryContext();
        Assert.Equal(expectedFines, freshContext.Fines.Count());
        if (expectedFines == 1)
        {
            Assert.Equal(new DateOnly(2026, 11, 12), Assert.Single(freshContext.Fines).ReturnedDate);
        }
    }

    [Fact]
    public void FineKeepsOriginalBorrowerWhenCopyIsBorrowedAndReturnedAgain()
    {
        Guid copyId;
        Guid firstBorrowerId;
        using (var context = new LibraryContext())
        {
            var firstBorrower = Borrower("First reader");
            var copy = Copy("Borrowed", firstBorrower, new DateTime(2026, 11, 8));
            context.Catalogue.Add(copy);
            context.SaveChanges();
            copyId = copy.Id;
            firstBorrowerId = firstBorrower.Id;
        }
        var repository = CreateRepository();
        Assert.Equal(ReturnBookResult.Returned, repository.ReturnBook(copyId));

        Guid secondBorrowerId;
        using (var context = new LibraryContext())
        {
            var secondBorrower = Borrower("Second reader");
            context.Borrowers.Add(secondBorrower);
            var copy = context.Catalogue.Single(stock => stock.Id == copyId);
            copy.OnLoanTo = secondBorrower;
            copy.LoanEndDate = new DateTime(2026, 11, 10);
            context.SaveChanges();
            secondBorrowerId = secondBorrower.Id;
        }
        Assert.Equal(ReturnBookResult.Returned, repository.ReturnBook(copyId));

        using var freshContext = new LibraryContext();
        var fines = freshContext.Fines.Where(fine => fine.BookStockId == copyId).ToList();
        Assert.Equal(2, fines.Count);
        Assert.Equal(new DateOnly(2026, 11, 8), fines.Single(fine => fine.BorrowerId == firstBorrowerId).LoanEndDate);
        Assert.Equal(new DateOnly(2026, 11, 10), fines.Single(fine => fine.BorrowerId == secondBorrowerId).LoanEndDate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentReturnsCompleteOnceAndCreateAtMostOneFine(bool overdue)
    {
        using var context = new LibraryContext();
        var copy = Copy("Borrowed", Borrower("Reader"), new DateTime(2026, 11, overdue ? 10 : 12));
        context.Catalogue.Add(copy);
        context.SaveChanges();
        using var clock = new BlockingTimeProvider();
        var first = RunOnWorker(() => new OnLoanRepository(clock, _locks).ReturnBook(copy.Id));
        Task<ReturnBookResult>? second = null;
        try
        {
            await clock.Entered.WaitAsync(TimeSpan.FromSeconds(5));
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            second = RunOnWorker(() =>
            {
                secondStarted.SetResult();
                return CreateRepository().ReturnBook(copy.Id);
            });
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The first call has loaded the loan but cannot save until the clock is released.
            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(200)));
        }
        finally
        {
            clock.Release();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (second != null) await second.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(ReturnBookResult.Returned, await first);
        Assert.Equal(ReturnBookResult.NotOnLoan, await second!);
        using var freshContext = new LibraryContext();
        var returned = freshContext.Catalogue.Include(stock => stock.OnLoanTo).Single();
        Assert.Null(returned.OnLoanTo);
        Assert.Null(returned.LoanEndDate);
        Assert.Equal(overdue ? 1 : 0, freshContext.Fines.Count());
        if (overdue)
        {
            var fine = Assert.Single(freshContext.Fines);
            Assert.Equal(copy.Id, fine.BookStockId);
            Assert.Equal(copy.OnLoanTo!.Id, fine.BorrowerId);
        }
    }

    [Fact]
    public async Task ReturnExceptionReleasesLockAndPreservesLoanForRetry()
    {
        using var context = new LibraryContext();
        var copy = Copy("Borrowed", Borrower("Reader"), new DateTime(2026, 11, 10));
        context.Catalogue.Add(copy);
        context.SaveChanges();
        var failing = new OnLoanRepository(new ThrowingTimeProvider(), _locks);

        Assert.Throws<InvalidOperationException>(() => failing.ReturnBook(copy.Id));

        using (var freshContext = new LibraryContext())
        {
            Assert.Empty(freshContext.Fines);
            Assert.NotNull(freshContext.Catalogue.Include(stock => stock.OnLoanTo).Single().OnLoanTo);
        }
        // Retry on another thread so a leaked, reentrant monitor cannot hide the failure.
        var retry = await RunOnWorker(() => CreateRepository().ReturnBook(copy.Id)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ReturnBookResult.Returned, retry);
        using var savedContext = new LibraryContext();
        Assert.Single(savedContext.Fines);
    }

    [Fact]
    public async Task ReturningAnotherTitleDoesNotWaitForBlockedReturn()
    {
        using var context = new LibraryContext();
        var firstCopy = Copy("First", Borrower("First reader"), new DateTime(2026, 11, 10));
        var otherCopy = Copy("Other", Borrower("Other reader"), new DateTime(2026, 11, 10));
        context.Catalogue.AddRange(firstCopy, otherCopy);
        context.SaveChanges();
        using var clock = new BlockingTimeProvider();
        var first = RunOnWorker(() => new OnLoanRepository(clock, _locks).ReturnBook(firstCopy.Id));
        Task<ReturnBookResult>? other = null;
        try
        {
            await clock.Entered.WaitAsync(TimeSpan.FromSeconds(5));
            other = RunOnWorker(() => CreateRepository().ReturnBook(otherCopy.Id));
            Assert.Equal(ReturnBookResult.Returned, await other.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(first.IsCompleted);
        }
        finally
        {
            clock.Release();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (other != null) await other.WaitAsync(TimeSpan.FromSeconds(5));
        }
        using var freshContext = new LibraryContext();
        Assert.Equal(2, freshContext.Fines.Count());
    }

    [Fact]
    public async Task ReservationWaitsForReturnAndSeesTheAvailableCopy()
    {
        using var context = new LibraryContext();
        var copy = Copy("Borrowed", Borrower("Reader"), new DateTime(2026, 11, 10));
        var waitingBorrower = Borrower("Waiting reader");
        context.Catalogue.Add(copy);
        context.Borrowers.Add(waitingBorrower);
        context.SaveChanges();
        using var clock = new BlockingTimeProvider();
        var returning = RunOnWorker(() => new OnLoanRepository(clock, _locks).ReturnBook(copy.Id));
        Task<ReservationCreationResult>? reserving = null;
        try
        {
            await clock.Entered.WaitAsync(TimeSpan.FromSeconds(5));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            reserving = RunOnWorker(() =>
            {
                started.SetResult();
                var repository = new ReservationRepository(new FixedTimeProvider(
                    new DateTimeOffset(2026, 11, 11, 23, 59, 59, TimeSpan.Zero), TimeZoneInfo.Utc), _locks);
                return repository.Create(waitingBorrower.Id, copy.Book.Id, 7);
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotSame(reserving, await Task.WhenAny(reserving, Task.Delay(200)));
        }
        finally
        {
            clock.Release();
            await returning.WaitAsync(TimeSpan.FromSeconds(5));
            if (reserving != null) await reserving.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(ReservationCreationStatus.AvailableNow, (await reserving!).Status);
        using var freshContext = new LibraryContext();
        Assert.Empty(freshContext.Reservations);
    }

    private static Task<T> RunOnWorker<T>(Func<T> action) => Task.Factory.StartNew(
        action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private sealed class BlockingTimeProvider : TimeProvider, IDisposable
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();
        public Task Entered => _entered.Task;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow()
        {
            _entered.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Return was not released.");
            return new DateTimeOffset(2026, 11, 11, 23, 59, 59, TimeSpan.Zero);
        }

        public void Release() => _release.Set();
        public void Dispose() => _release.Dispose();
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Simulated clock failure.");
    }

    private OnLoanRepository CreateRepository() => new(new FixedTimeProvider(
        new DateTimeOffset(2026, 11, 11, 23, 59, 59, TimeSpan.Zero), TimeZoneInfo.Utc), _locks);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localTimeZone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => localTimeZone;
    }

    private static Borrower Borrower(string name) => new()
    {
        Name = name,
        EmailAddress = "reader@example.com"
    };

    private static BookStock Copy(string title, Borrower? borrower, DateTime? dueDate) => new()
    {
        Book = new Book
        {
            Name = title,
            Author = new Author { Name = "Author" },
            ISBN = "1234567890",
            Format = BookFormat.Paperback
        },
        OnLoanTo = borrower,
        LoanEndDate = dueDate
    };

    public void Dispose()
    {
        using var context = new LibraryContext();
        context.Database.EnsureDeleted();
    }
}
