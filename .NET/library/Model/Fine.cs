namespace OneBeyondApi.Model
{
    public class Fine
    {
        public Guid Id { get; set; }
        public Guid BorrowerId { get; set; }
        public required Borrower Borrower { get; set; }
        public Guid BookStockId { get; set; }
        public required BookStock BookStock { get; set; }
        public DateOnly LoanEndDate { get; set; }
        public DateOnly ReturnedDate { get; set; }
    }
}
