using Microsoft.EntityFrameworkCore;
using OneBeyondApi.Model;

namespace OneBeyondApi.DataAccess
{
    public class OnLoanRepository : IOnLoanRepository
    {
        public List<BorrowerLoansDto> GetLoans()
        {
            using var context = new LibraryContext();

            // A loan remains active until returned, even when its due date has passed.
            var loans = context.Catalogue
                .AsNoTracking()
                .Where(stock => stock.OnLoanTo != null)
                .Select(stock => new
                {
                    BorrowerId = stock.OnLoanTo!.Id,
                    stock.OnLoanTo.Name,
                    stock.OnLoanTo.EmailAddress,
                    Title = stock.Book.Name
                })
                .ToList();

            return loans
                .GroupBy(loan => loan.BorrowerId)
                .Select(group => new BorrowerLoansDto(
                    group.Key,
                    group.First().Name,
                    group.First().EmailAddress,
                    group.Select(loan => loan.Title).OrderBy(title => title).ToList()))
                .OrderBy(borrower => borrower.Name)
                .ThenBy(borrower => borrower.BorrowerId)
                .ToList();
        }

        public ReturnBookResult ReturnBook(Guid bookStockId)
        {
            using var context = new LibraryContext();
            var stock = context.Catalogue
                .Include(copy => copy.OnLoanTo)
                .SingleOrDefault(copy => copy.Id == bookStockId);

            if (stock == null)
            {
                return ReturnBookResult.NotFound;
            }

            if (stock.OnLoanTo == null)
            {
                return ReturnBookResult.NotOnLoan;
            }

            stock.OnLoanTo = null;
            stock.LoanEndDate = null;
            context.SaveChanges();

            return ReturnBookResult.Returned;
        }
    }
}
