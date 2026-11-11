using Microsoft.AspNetCore.Mvc;
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
        context.Catalogue.AddRange(
            firstCopy,
            new BookStock { Book = firstCopy.Book, OnLoanTo = first },
            Copy("Other title", second, null));
        context.SaveChanges();

        var results = new OnLoanRepository().GetLoans();

        Assert.Equal(2, results.Count);
        Assert.Equal(new[] { "Shared title", "Shared title" },
            results.Single(result => result.BorrowerId == first.Id).Titles);
        Assert.Equal(new[] { "Other title" },
            results.Single(result => result.BorrowerId == second.Id).Titles);
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
