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

# Mobile Engineer Challenge
Implement a mobile application using MAUI that provides the following functionality
1.	Simple login matching on Borrower Name and Email Address (the library system provider has not heard of security!)
2.	Provide a view of the catalogue, this should only show one entry for each unique book. This should show at least the author name, title of the book, format and ISBN, along with the total number of copies of the book in the libraries catalogue.
3.	For any books allow the user to add to a wishlist. The wishlist should be maintained in app and users can add/remove books from it. 
4.	Add functionality in the app to display an app generated push notification when an item on the users wishlist's curent on loan date is reached (or passed).

