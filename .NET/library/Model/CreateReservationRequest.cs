using System.ComponentModel.DataAnnotations;

namespace OneBeyondApi.Model
{
    public record CreateReservationRequest(
        [Required] Guid? BorrowerId,
        [Required] Guid? BookId,
        [Required, Range(1, int.MaxValue)] int? LoanDays);
}
