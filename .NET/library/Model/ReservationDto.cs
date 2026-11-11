namespace OneBeyondApi.Model
{
    public record ReservationDto(
        Guid ReservationId,
        Guid BorrowerId,
        Guid BookId,
        string Title,
        int LoanDays,
        ReservationStatus Status,
        DateOnly? EstimatedAvailableOn,
        string? Explanation);

    public record ReservationCreatedDto(Guid ReservationId);
}
