using OneBeyondApi.Model;

namespace OneBeyondApi.DataAccess
{
    internal static class ReservationSchedule
    {
        internal record Result(Dictionary<Guid, DateOnly?> Estimates, bool HasFreeCopy);

        private sealed class CopySlot(BookStock copy, DateOnly today)
        {
            public Guid Id { get; } = copy.Id;
            public bool PhysicallyAvailable { get; } = copy.OnLoanTo == null;
            public bool Used { get; set; }
            public DateOnly? AvailableOn { get; set; } = copy.OnLoanTo == null
                ? today
                : copy.LoanEndDate is DateTime deadline
                    ? DateOnly.FromDateTime(deadline) < today ? today : DateOnly.FromDateTime(deadline)
                    : null;
        }

        public static Result Calculate(IEnumerable<BookStock> copies, IEnumerable<Reservation> reservations, DateOnly today)
        {
            var slots = copies.Select(copy => new CopySlot(copy, today)).ToList();
            var estimates = new Dictionary<Guid, DateOnly?>();

            foreach (var reservation in reservations
                .Where(item => item.Status == ReservationStatus.Active)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id))
            {
                var slot = slots.Where(item => item.AvailableOn.HasValue)
                    .OrderBy(item => item.AvailableOn)
                    .ThenByDescending(item => item.PhysicallyAvailable)
                    .ThenBy(item => item.Id)
                    .FirstOrDefault();

                if (slot == null)
                {
                    estimates[reservation.Id] = null;
                    continue;
                }

                var start = slot.AvailableOn!.Value;
                slot.Used = true;
                if (reservation.LoanDays > DateOnly.MaxValue.DayNumber - start.DayNumber)
                {
                    // This copy's schedule is now beyond the representable date range.
                    estimates[reservation.Id] = null;
                    slot.AvailableOn = null;
                    continue;
                }

                estimates[reservation.Id] = start;
                slot.AvailableOn = start.AddDays(reservation.LoanDays);
            }

            return new Result(estimates, slots.Any(slot => slot.PhysicallyAvailable && !slot.Used));
        }
    }
}
