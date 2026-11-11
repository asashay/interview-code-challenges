using Microsoft.AspNetCore.Mvc;
using OneBeyondApi.DataAccess;
using OneBeyondApi.Model;

namespace OneBeyondApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class OnLoanController : ControllerBase
    {
        private readonly IOnLoanRepository _onLoanRepository;

        public OnLoanController(IOnLoanRepository onLoanRepository)
        {
            _onLoanRepository = onLoanRepository;
        }

        [HttpGet]
        public ActionResult<List<BorrowerLoansDto>> Get()
        {
            return Ok(_onLoanRepository.GetLoans());
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public IActionResult Return([FromBody] ReturnBookRequest request)
        {
            return _onLoanRepository.ReturnBook(request.BookStockId!.Value) switch
            {
                ReturnBookResult.Returned => NoContent(),
                ReturnBookResult.NotFound => Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Book copy not found."),
                ReturnBookResult.NotOnLoan => Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Book copy is not on loan."),
                _ => throw new InvalidOperationException("Unexpected return result.")
            };
        }
    }
}
