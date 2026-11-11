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
    }
}
