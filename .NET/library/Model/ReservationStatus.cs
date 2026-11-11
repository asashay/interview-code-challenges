using System.Text.Json.Serialization;

namespace OneBeyondApi.Model
{
    [JsonConverter(typeof(JsonStringEnumConverter<ReservationStatus>))]
    public enum ReservationStatus
    {
        Active,
        Closed
    }
}
