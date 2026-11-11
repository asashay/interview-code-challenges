using Microsoft.AspNetCore.Mvc;
using OneBeyondApi.DataAccess;
using OneBeyondApi.Model;

namespace OneBeyondApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ReservationsController(IReservationRepository repository) : ControllerBase
    {
        [HttpPost]
        [ProducesResponseType(typeof(ReservationCreatedDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public IActionResult Post([FromBody] CreateReservationRequest request)
        {
            var result = repository.Create(request.BorrowerId!.Value, request.BookId!.Value, request.LoanDays!.Value);
            return result.Status switch
            {
                ReservationCreationStatus.Created => CreatedAtAction(nameof(Get),
                    new { reservationId = result.ReservationId }, new ReservationCreatedDto(result.ReservationId!.Value)),
                ReservationCreationStatus.InvalidDuration => Problem(statusCode: 400,
                    title: "Loan duration must be positive and produce a supported due date."),
                ReservationCreationStatus.BorrowerNotFound => Problem(statusCode: 404, title: "Borrower not found."),
                ReservationCreationStatus.BookNotFound => Problem(statusCode: 404, title: "Book not found."),
                ReservationCreationStatus.NoStock => Problem(statusCode: 409, title: "This title has no copies in stock."),
                ReservationCreationStatus.Duplicate => Problem(statusCode: 409, title: "An active reservation already exists for this borrower and title."),
                ReservationCreationStatus.AvailableNow => Problem(statusCode: 409, title: "This title is available now; no reservation was created."),
                _ => throw new InvalidOperationException("Unexpected reservation result.")
            };
        }

        [HttpGet("{reservationId}")]
        [ProducesResponseType(typeof(ReservationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public ActionResult<ReservationDto> Get(Guid reservationId)
        {
            var reservation = repository.Get(reservationId);
            return reservation == null
                ? Problem(statusCode: 404, title: "Reservation not found.")
                : Ok(reservation);
        }
    }
}
