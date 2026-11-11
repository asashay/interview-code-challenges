using System.ComponentModel.DataAnnotations;

namespace OneBeyondApi.Model
{
    public record ReturnBookRequest([Required] Guid? BookStockId);
}
