using Microsoft.EntityFrameworkCore;
using OneBeyondApi.Model;

namespace OneBeyondApi.DataAccess
{
    public class ReservationRepository(TimeProvider timeProvider, TitleLockProvider titleLocks) : IReservationRepository
    {
        public ReservationCreationResult Create(Guid borrowerId, Guid bookId, int loanDays)
        {
            using var context = new LibraryContext();
            var borrower = context.Borrowers.Find(borrowerId);
            if (borrower == null)
                return new(ReservationCreationStatus.BorrowerNotFound);

            var book = context.Books.Find(bookId);
            if (book == null)
                return new(ReservationCreationStatus.BookNotFound);

            // Existence checks above avoid retaining locks for unknown title IDs.
            lock (titleLocks.GetLock(bookId))
            {
                var now = timeProvider.GetUtcNow();
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeProvider.LocalTimeZone).DateTime);
                if (loanDays <= 0 || loanDays > DateOnly.MaxValue.DayNumber - today.DayNumber)
                    return new(ReservationCreationStatus.InvalidDuration);

                var active = context.Reservations.AsNoTracking()
                    .Where(item => item.BookId == bookId && item.Status == ReservationStatus.Active)
                    .ToList();
                if (active.Any(item => item.BorrowerId == borrowerId))
                    return new(ReservationCreationStatus.Duplicate);

                var copies = GetCopies(context, bookId);
                if (copies.Count == 0)
                    return new(ReservationCreationStatus.NoStock);

                if (ReservationSchedule.Calculate(copies, active, today).HasFreeCopy)
                    return new(ReservationCreationStatus.AvailableNow);

                var reservation = new Reservation
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    BorrowerId = borrowerId,
                    Borrower = borrower,
                    BookId = bookId,
                    Book = book,
                    LoanDays = loanDays,
                    Status = ReservationStatus.Active
                };
                context.Reservations.Add(reservation);
                context.SaveChanges();
                return new(ReservationCreationStatus.Created, reservation.Id);
            }
        }

        public ReservationDto? Get(Guid reservationId)
        {
            using var context = new LibraryContext();
            var reservation = context.Reservations.AsNoTracking().Include(item => item.Book)
                .SingleOrDefault(item => item.Id == reservationId);
            if (reservation == null)
                return null;

            DateOnly? estimate = null;
            string? explanation = null;
            if (reservation.Status == ReservationStatus.Active)
            {
                var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
                var active = context.Reservations.AsNoTracking()
                    .Where(item => item.BookId == reservation.BookId && item.Status == ReservationStatus.Active)
                    .ToList();
                var schedule = ReservationSchedule.Calculate(GetCopies(context, reservation.BookId), active, today);
                estimate = schedule.Estimates.GetValueOrDefault(reservation.Id);
                if (!estimate.HasValue)
                    explanation = "No copy has a known availability within the supported date range.";
            }

            return new ReservationDto(reservation.Id, reservation.BorrowerId, reservation.BookId,
                reservation.Book.Name, reservation.LoanDays, reservation.Status, estimate, explanation);
        }

        private static List<BookStock> GetCopies(LibraryContext context, Guid bookId) => context.Catalogue
            .AsNoTracking().Include(copy => copy.OnLoanTo)
            .Where(copy => copy.Book.Id == bookId).ToList();
    }
}
