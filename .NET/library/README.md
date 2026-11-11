# Library Coding Challenges
This project contains a basic API for a fictional library. The library has a catalogue which is made up of a stock of books. It is a .net 8 web api project, using an in memory database - if you want to add additional test data, look at SeedData.cs.

Each book stock record in the catalogue has an associated Book record, and if currently "On Loan" has a loan end date and an associated "Borrower"
It is possible via the API to retrieve all book stock records in the Catalogue, or to search by Author Name and/or Book Title. Both searches use a Contains search on the relevant Author/Book attributes.

Books have a name, an associated Author record, a Book Format and an ISBN number.

Authors have a name.

Borrowers have a name and an email address.

There are basic APIs for Books, Authors and Borrowers that will list all entities and allow addition of new records. It is not currently possible via the API to modify the Book Stock in the Catalogue.

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

Run the API with `dotnet run --project .NET/library/OneBeyondApi.csproj` from the
repository root, then use `/OnLoan` or the Swagger UI at `/swagger` on the printed URL.
Run the tests with `dotnet test .NET/library/OneBeyondApi.sln`.

# Mobile Engineer Challenge
Implement a mobile application using MAUI that provides the following functionality
1.	Simple login matching on Borrower Name and Email Address (the library system provider has not heard of security!)
2.	Provide a view of the catalogue, this should only show one entry for each unique book. This should show at least the author name, title of the book, format and ISBN, along with the total number of copies of the book in the libraries catalogue.
3.	For any books allow the user to add to a wishlist. The wishlist should be maintained in app and users can add/remove books from it. 
4.	Add functionality in the app to display an app generated push notification when an item on the users wishlist's curent on loan date is reached (or passed).

