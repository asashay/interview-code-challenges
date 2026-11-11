using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OneBeyondApi.Controllers;
using OneBeyondApi.DataAccess;
using OneBeyondApi.Model;
using Xunit;

namespace OneBeyondApi.Tests;

// All tests sharing the starter's named in-memory database live in this class
// so xUnit runs them sequentially, with a fresh database for each test.
public class OnLoanTests : IDisposable
{
    public OnLoanTests()
    {
        using var context = new LibraryContext();
        context.Database.EnsureDeleted();
    }

    [Fact]
    public void GetReturnsOkWithEmptyListWhenNoLoansExist()
    {
        var controller = new OnLoanController(new OnLoanRepository());

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

        Assert.Empty(new OnLoanRepository().GetLoans());
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

        var result = Assert.Single(new OnLoanRepository().GetLoans());

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

        var results = new OnLoanRepository().GetLoans();

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
        var repository = new OnLoanRepository();
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
        var repository = new OnLoanRepository();

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
        var repository = new OnLoanRepository();
        var controller = new OnLoanController(repository);

        var response = Assert.IsType<ObjectResult>(controller.Return(new ReturnBookRequest(Guid.NewGuid())));

        Assert.Equal(404, response.StatusCode);
        Assert.Equal(404, Assert.IsType<ProblemDetails>(response.Value).Status);
        Assert.Equal("Borrowed", Assert.Single(Assert.Single(repository.GetLoans()).Titles));
    }

    [Fact]
    public void ReturnAvailableCopyAndRepeatedReturnProduceConflict()
    {
        using var context = new LibraryContext();
        var available = Copy("Available", null, null);
        var borrowed = Copy("Borrowed", Borrower("Reader"), new DateTime(2100, 1, 1));
        context.Catalogue.AddRange(available, borrowed);
        context.SaveChanges();
        var controller = new OnLoanController(new OnLoanRepository());

        var availableResponse = Assert.IsType<ObjectResult>(controller.Return(new ReturnBookRequest(available.Id)));
        Assert.Equal(409, availableResponse.StatusCode);
        Assert.Equal(409, Assert.IsType<ProblemDetails>(availableResponse.Value).Status);

        Assert.IsType<NoContentResult>(controller.Return(new ReturnBookRequest(borrowed.Id)));
        var repeatedResponse = Assert.IsType<ObjectResult>(controller.Return(new ReturnBookRequest(borrowed.Id)));
        Assert.Equal(409, repeatedResponse.StatusCode);
        Assert.Equal(409, Assert.IsType<ProblemDetails>(repeatedResponse.Value).Status);
        Assert.Empty(new OnLoanRepository().GetLoans());
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
