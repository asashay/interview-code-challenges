using OneBeyondApi.Model;

namespace OneBeyondApi.DataAccess
{
    public interface IOnLoanRepository
    {
        List<BorrowerLoansDto> GetLoans();
    }
}
