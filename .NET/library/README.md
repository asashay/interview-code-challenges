# Library Coding Challenges
This project contains a basic API for a fictional library. The library has a catalogue which is made up of a stock of books. It is a .net 8 web api project, using an in memory database - if you want to add additional test data, look at SeedData.cs.

Each book stock record in the catalogue has an associated Book record, and if currently "On Loan" has a loan end date and an associated "Borrower"
It is possible via the API to retrieve all book stock records in the Catalogue, or to search by Author Name and/or Book Title. Both searches use a Contains search on the relevant Author/Book attributes.

Books have a name, an associated Author record, a Book Format and an ISBN number.

Authors have a name.

Borrowers have a name and an email address.

There are basic APIs for Books, Authors and Borrowers that will list all entities and allow addition of new records. The On Loan API also allows borrowed copies in the Catalogue to be returned.

Full docs (and a running instance of the API) can be found at https://library-api.onebeyond.cloud/swagger/index.html

# .NET Engineer Challenge

Extend the existing API to add the following functionality:
1. Add an "On Loan" end point with functionality to get/query the details of all borrowers with active loans and the titles of books they have on loan.
2. Extend the "On Loan" end point to allow books on loan to be returned. 
3. If books are returned after their loan end date then a fine should be raised against the borrower (data model for fines and relationships with borrowers are left to the candidate to define)
4. Add functionality to allow a borrower to reserve a particular title that is currently on loan (also consider the case of multiple borrowers all wanting to borrow the same book). The borrower should also be able to query via the API to find out when the book will be available for them.


# Implemented: On Loan

`GET /OnLoan` returns all borrowers with active loans, grouped by borrower ID.
Each result contains `borrowerId`, `name`, `emailAddress`, and a `titles` array.
A copy is on loan when `OnLoanTo` is assigned, including overdue copies.
Titles contain one entry per borrowed copy, so borrowing multiple copies of a
title produces repeated entries. Borrowers without active loans are excluded.
If there are no active loans, the endpoint returns HTTP 200 with `[]`.

“Get/query” is interpreted as retrieving all borrowers with active loans and
their borrowed book titles. No search filters were specified, so none are included.

`POST /OnLoan` returns one borrowed copy. Obtain the copy's `id` from
`GET /Catalogue/GetCatalogue` and send it as `bookStockId` in the JSON body.
The GET On Loan response contains titles only, without copy IDs.

Example POST body:

```json
{
  "bookStockId": "11111111-1111-1111-1111-111111111111"
}
```

A successful return clears the copy's borrower and due date and responds with
HTTP 204 (no body). An unknown copy returns 404; a copy that is not on loan
(including a repeated return) returns 409. These errors use ProblemDetails.
A missing, null, or malformed `bookStockId` returns 400.
The returned copy disappears from `GET /OnLoan`; borrowers disappear after their
last copy is returned. Overdue and undated loans can be returned.

Late returns create a stored fine linked to the borrower and the returned copy.
Lateness compares calendar dates using the server's local date: returning at any
time on the due date is on time, while returning on a later date creates a fine.
Missing due dates do not create fines. The clock is supplied through `TimeProvider`.

Each fine stores the original due date and return date as `DateOnly` snapshots,
which remain after the loan fields are cleared. Days late can be calculated as
`ReturnedDate.DayNumber - LoanEndDate.DayNumber`; no day count or monetary amount
is stored. A repeated return still responds with 409 and does not add another fine.
The fine and cleared loan are saved in one `SaveChanges()` call.

Fines are stored only, without a fines endpoint or changes to existing API
responses. Payment tracking, general loan history, and concurrent-return protection
are not implemented. The in-memory database needs no migrations and loses all
records, including fines, on restart.

Run the API with `dotnet run --project .NET/library/OneBeyondApi.csproj` from the
repository root, then use `/OnLoan` or the Swagger UI at `/swagger` on the printed URL.
Run the tests with `dotnet test .NET/library/OneBeyondApi.sln`.

# Implemented: Reservations

`POST /Reservations` reserves a title for a requested number of days:

```json
{
  "borrowerId": "11111111-1111-1111-1111-111111111111",
  "bookId": "22222222-2222-2222-2222-222222222222",
  "loanDays": 7
}
```

Use borrower IDs from `GET /Borrower/GetBorrowers` and book IDs from
`GET /Book/GetBooks` (or the catalogue's nested `book.id`, not the copy's `id`).
Success returns HTTP 201, `{ "reservationId": "..." }`, and a Location header
pointing to `GET /Reservations/{reservationId}`. The public ID is a random GUID.

Creation returns 400 for missing/malformed fields or a duration that is not
positive or cannot produce a supported due date from today. Unknown borrowers
or books return 404. A duplicate active reservation for the same borrower/title,
a title without stock, or a freely available copy produces 409 with ProblemDetails.
The "available now" response creates no reservation: callers can collect through
the future collection flow. A copy already needed by an active reservation is
not freely available, and a borrowed copy due today is not physically available.

`GET /Reservations/{reservationId}` returns the reservation ID, borrower ID, book
ID, title, loan duration, status (`Active` or `Closed`), `estimatedAvailableOn`,
and `explanation`. Unknown IDs return 404; malformed GUIDs return 400. Closed
reservations have no estimate. When an active reservation's date cannot be
calculated, its estimate is null and the explanation describes the limitation.

Estimates are recalculated from all copies and active reservations for that title.
The queue sorts by `CreatedAtUtc`, then GUID to break identical timestamps; ties
are deterministic but do not guarantee arrival order. Each reservation uses the
copy with the earliest estimated availability, then advances that copy's schedule
by its requested duration. Equal dates prefer physically available copies, then
copy ID. No copy assignment or estimated date is stored on the reservation.

Available copies start at today's local date. Borrowed copies start at the later
of today and their due date, so overdue estimates move forward as time passes.
Missing due dates are unknown; other copies with known dates can still supply
the queue. Estimates assume same-day collection and overdue returns today; they
are not guarantees. Date calculations outside the supported range return an
unknown estimate rather than overflowing.

Creation uses a singleton lock per title across the duplicate/availability checks
and insertion. Different titles use independent locks; lock entries remain for
the application's lifetime. This protects concurrent creation in one API process,
not multiple processes, transactions, or concurrent loan-return operations.

Collection is outside these endpoints: a future collection operation must assign
the copy's `OnLoanTo`, set `LoanEndDate`, and close the corresponding reservation.
The closed reservation then stops contributing to estimates, while the actual
loan contributes instead. Reservations never close automatically, and creation
does not start a loan. No cancellation, expiry, or payment features are included.

There is no authentication or ownership check. Random GUIDs reduce enumeration
risk but anyone possessing a reservation ID can query it. The in-memory provider
requires no migration and loses reservations on restart.

To try the seeded data in Swagger, reserve "Agile Project Management - A Primer",
whose only copy is borrowed, then follow the returned Location. "The Importance
of Clay" and "Rust Development Cookbook" initially have free copies and return
409 on reservation creation. Returning the borrowed Agile copy through `POST
/OnLoan` updates its reservation estimates without closing the reservations.

# Mobile Engineer Challenge
Implement a mobile application using MAUI that provides the following functionality
1.	Simple login matching on Borrower Name and Email Address (the library system provider has not heard of security!)
2.	Provide a view of the catalogue, this should only show one entry for each unique book. This should show at least the author name, title of the book, format and ISBN, along with the total number of copies of the book in the libraries catalogue.
3.	For any books allow the user to add to a wishlist. The wishlist should be maintained in app and users can add/remove books from it. 
4.	Add functionality in the app to display an app generated push notification when an item on the users wishlist's curent on loan date is reached (or passed).

