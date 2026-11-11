using OneBeyondApi.Model;

namespace OneBeyondApi.DataAccess
{
    public interface IReservationRepository
    {
        ReservationCreationResult Create(Guid borrowerId, Guid bookId, int loanDays);
        ReservationDto? Get(Guid reservationId);
    }

    public enum ReservationCreationStatus
    {
        Created,
        InvalidDuration,
        BorrowerNotFound,
        BookNotFound,
        NoStock,
        Duplicate,
        AvailableNow
    }

    public record ReservationCreationResult(ReservationCreationStatus Status, Guid? ReservationId = null);
}
