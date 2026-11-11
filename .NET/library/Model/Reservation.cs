namespace OneBeyondApi.Model
{
    public class Reservation
    {
        public Guid Id { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public Guid BorrowerId { get; set; }
        public required Borrower Borrower { get; set; }
        public Guid BookId { get; set; }
        public required Book Book { get; set; }
        public int LoanDays { get; set; }
        public ReservationStatus Status { get; set; }
    }
}
