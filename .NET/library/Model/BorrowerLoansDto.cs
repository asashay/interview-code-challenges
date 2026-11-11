namespace OneBeyondApi.Model
{
    public record BorrowerLoansDto(
        Guid BorrowerId,
        string Name,
        string EmailAddress,
        List<string> Titles);
}
