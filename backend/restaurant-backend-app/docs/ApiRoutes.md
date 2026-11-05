# API Routes

## Auth

### `POST` /register
```json
{
    "firstName": "John",
    "lastName": "Doe",
    "email": "john_doe@mail.com",
    "password": "1234SecurePassword!",
    "confirmPassword": "1234SecurePassword!"
}
```

Response: 
`200 OK`
```json
{
    "message":"User registered and confirmed successfully"
}
```

### `POST` /signin
```json
{
    "email": "john_doe@mail.com",
    "password": "1234SecurePassword!",
}
```

Response: `200 OK`
```json
{
    "message": "Sign in successful",
    "idToken": "token",
    "accessToken": "access token",
}
```

## Feedback

### `GET` /feedback
Query parameters:
- `locationId` (string, required)
- `type` (Service | CuisineExperience)
- `sort` (TopRatedFirst | LowRatedFirst | NewestFirst | OldestFirst)
- `page` (integer, required)

Response: `200 OK`
```json
{
  "items": [
    {
      "reservationId": "string",
      "locationId": "string",
      "userId": "string|null",
      "comment": "string|null",
      "rating": 5,
      "createdAt": "2026-01-01T12:00:00Z",
      "type": "string",
      "username": "string|null"
    }
  ],
  "totalPages": 1,
  "currentPage": 1
}
```

### `POST` /feedback
Request body:
```json
{
  "reservationId": "string",
  "userId": "string|null",
  "comment": "string|null",
  "rating": 5,
  "type": "Service | CuisineExperience"
}
```

Response: `200 OK`
```json
{
  "reservationId": "string",
  "locationId": "string",
  "userId": "string|null",
  "comment": "string|null",
  "rating": 5,
  "createdAt": "2026-01-01T12:00:00Z",
  "type": "string",
  "username": "string|null"
}
```

### `PUT` /feedback
Request body:
```json
{
  "reservationId": "string",
  "userId": "string|null",
  "comment": "string|null",
  "rating": 5,
  "type": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Feedback updated successfully."
}
```

## Locations

### `POST` /locations
Request body:
```json
{
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageBase64": "string|null",
  "rating": 4.5
}
```

Response: `201 Created`
```json
{
  "id": "string",
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageUrl": "string|null",
  "rating": 4.5
}
```

### `GET` /locations
Response: `200 OK`
```json
[
  {
    "id": "string",
    "address": "string",
    "description": "string",
    "totalCapacity": 50,
    "averageOccupancy": "string",
    "imageUrl": "string|null",
    "rating": 4.5
  }
]
```

### `GET` /locations/{id}/dishes
Response: `200 OK`
```json
[
  {
    "id": "string",
    "name": "string",
    "imageUrl": "string",
    "price": 12.34,
    "state": "string",
    "weight": "string",
    "dishType": "string",
    "description": "string",
    "calories": "string",
    "proteins": "string",
    "fats": "string",
    "carbohydrates": "string",
    "vitamins": "string"
  }
]
```

### `DELETE` /locations/{locationId}/dishes/{dishId}
Response: `200 OK`
```json
{
  "message": "Dish removed from location successfully."
}
```


### `GET` /locations/{id}
Response: `200 OK`
```json
{
  "id": "string",
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageUrl": "string|null",
  "rating": 4.5
}
```

### `PUT` /locations/{id}
Request body:
```json
{
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageBase64": "string|null",
  "rating": 4.5
}
```
Response: 200 OK
```json
{
  "id": "string",
  "address": "string",
  "description": "string",
  "totalCapacity": 50,
  "averageOccupancy": "string",
  "imageUrl": "string|null",
  "rating": 4.5
}
```

### `DELETE` /locations/{id}
Response: 200 OK
```json
{
  "message": "Location deleted successfully"
}
```

## Waiters

### `POST` /waiters
Request body:
```json
{
  "email": "waiter@example.com",
  "locationId": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Email added successfully"
}
```

### `GET` /waiters/{userId}/location
Response: `200 OK`
```json
{
  "locationId": "string"
}
```

### `DELETE` /waiters
Request body:
```json
{
  "email": "waiter@example.com",
  "locationId": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Email deleted successfully"
}
```

## Dishes

### `POST` /dishes
Request body:
```json
{
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageBase64": "string|null",
  "state": "string",
  "locationIds": ["string"]
}
```

Response: `201 Created`
```json
{
  "id": "string",
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageUrl": "string",
  "state": "string"
}
```

### `GET` /dishes
Optional query parameters:
- `dishType` (Appetizer | MainCourse | Dessert)
- `sortBy` (PriceAscending, PriceDescending, PopularityAscending, PopularityDescending)
- `name`

Response: `200 OK`
```json
[
  {
    "id": "string",
    "name": "string",
    "imageUrl": "string",
    "price": 12.34,
    "state": "string",
    "weight": "string",
    "dishType": "string",
    "description": "string",
    "calories": "string",
    "proteins": "string",
    "fats": "string",
    "carbohydrates": "string",
    "vitamins": "string"
  }
]
```

### `PUT` /dishes/{id}
Request body:
```json
{
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageBase64": "string|null",
  "state": "string"
}
```
Response: 200 OK
```json
{
  "id": "string",
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageUrl": "string",
  "state": "string"
}
```

### `DELETE` /dishes/{id}
Query parameters:
  force (boolean, optional)
Response: 200 OK
```json
{
  "message": "Dish '{id}' deleted successfully."
}
```


### `GET` /dishes/popular
Response: `200 OK`
```json
[
  {
    "id": "string",
    "name": "string",
    "imageUrl": "string",
    "price": 12.34,
    "state": "string",
    "weight": "string",
    "dishType": "string",
    "description": "string",
    "calories": "string",
    "proteins": "string",
    "fats": "string",
    "carbohydrates": "string",
    "vitamins": "string"
  }
]
```

### `GET` /dishes/{id}
Response: `200 OK`
```json
{
  "id": "string",
  "name": "string",
  "description": "string",
  "dishType": "string",
  "price": 12.34,
  "weight": "string",
  "calories": "string",
  "proteins": "string",
  "fats": "string",
  "carbohydrates": "string",
  "vitamins": "string",
  "imageUrl": "string",
  "state": "string"
}
```

## Reservations

### `POST` /reservations
Request body:
```json
{
  "tableId": "string",
  "customerEmail": "string|null",
  "startTime": "2026-08-07T19:00:00Z",
  "endTime": "2026-08-07T20:30:00Z",
  "guestCount": 4,
  "customerId": "string|null"
}
```

Response: `201 Created`
```json
{
  "id": "string",
  "tableId": "string",
  "startTime": "2026-08-07T19:00:00Z",
  "endTime": "2026-08-07T20:30:00Z",
  "guestCount": 4,
  "status": "Reserved",
  "customerId": "string|null",
  "waiterId": "string",
  "locationAddress": "string",
  "restaurantName": "Green & Tasty",
  "hasSubmittedPreOrder": false
}
```

### `GET` /reservations
Response: `200 OK`
```json
[
  {
    "id": "string",
    "tableId": "string",
    "startTime": "2026-08-07T19:00:00Z",
    "endTime": "2026-08-07T20:30:00Z",
    "guestCount": 4,
    "status": "Reserved",
    "customerId": "string|null",
    "waiterId": "string",
    "locationAddress": "string",
    "restaurantName": "Green & Tasty",
    "hasSubmittedPreOrder": false
  }
]
```

### `GET` /reservations/{reservationId}
Response: `200 OK`
```json
{
  "id": "string",
  "tableId": "string",
  "startTime": "2026-08-07T19:00:00Z",
  "endTime": "2026-08-07T20:30:00Z",
  "guestCount": 4,
  "status": "Reserved",
  "customerId": "string|null",
  "waiterId": "string",
  "locationAddress": "string",
  "restaurantName": "Green & Tasty",
  "hasSubmittedPreOrder": false
}
```

### `PATCH` /reservations/{reservationId}/status
Request body:
```json
{
  "newStatus": "Completed"
}
```

Response: `200 OK`
```json
{
  "message": "Reservation status updated successfully."
}
```

### `GET` /reservations/{reservationId}/invite-token
Returns the invite token of the reservation's group **in any group / reservation state** (`Open`, full, `Submitted`, `Closed`):
the Owner screen resolves its group through `invite-token → /guest/invites/{token}`, including the read-only screen after submit.
Guests still cannot join a non-open or full group — `POST /guest/sessions` rejects it.

Response: `200 OK`
```json
{
  "token": "string"
}
```
**Owner only**: `401` without a Cognito token / `sub` · `403` the caller is not the reservation's customer (also for a
reservation without a customer). Other errors: `404` reservation not found · `404` the reservation has no group reservation ·
`400` reservation for one guest (`guestCount < 2`). Before the security stage any authenticated user could get any token (IDOR).

### `GET` /reservations/{reservationId}/feedback
Response: `200 OK`
```json
{
  "service": {
    "reservationId": "string",
    "locationId": "string",
    "userId": "string|null",
    "comment": "string|null",
    "rating": 5,
    "createdAt": "2026-08-07T21:00:00Z",
    "type": "string",
    "username": "string|null"
  },
  "cuisineExperience": {
    "reservationId": "string",
    "locationId": "string",
    "userId": "string|null",
    "comment": "string|null",
    "rating": 5,
    "createdAt": "2026-08-07T21:00:00Z",
    "type": "string",
    "username": "string|null"
  }
}
```

### `GET` /reservations/{reservationId}/guest-session-id
Owner only (Cognito). Returns the id of the owner's own guest session in the group of this reservation.
Errors and the lazy creation of the owner session — see [Group pre-order (Owner)](#group-pre-order-owner).

Response: `200 OK`
```json
{
  "guestSessionId": "owner-<groupReservationId>"
}
```


### `DELETE` /reservations
Request body:
```json
{
  "id": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Reservation cancelled successfully"
}
```

### `PUT` /reservations
Request body:
```json
{
  "reservationId": "string",
  "startTime": "2026-08-07T19:15:00Z",
  "endTime": "2026-08-07T20:45:00Z",
  "guestCount": 5
}
```

Response: `200 OK`
```json
{
  "id": "string",
  "tableId": "string",
  "startTime": "2026-08-07T19:15:00Z",
  "endTime": "2026-08-07T20:45:00Z",
  "guestCount": 5,
  "status": "Reserved",
  "customerId": "string|null",
  "waiterId": "string",
  "locationAddress": "string",
  "restaurantName": "Green & Tasty",
  "hasSubmittedPreOrder": false
}
```

### `PATCH` /reservations/{reservationId}/order/confirmations/{dishId}
Response: `200 OK`
```json
{
  "message": "Pre order confirmed successfully"
}
```

### `PATCH` /reservations/{reservationId}/order/cancellations/{dishId}
Response: `200 OK`
```json
"Pre order dish canceled successfully"
```

### `PATCH` /reservations/{reservationId}/table
Request body:
```json
{
  "tableId": "string"
}
```

Response: `200 OK`
```json
"Your reservation table was updated successfully."
```

## Tables

### `POST` /tables
Request body:
```json
{
  "locationId": "string",
  "capacity": 4,
  "tableNumber": 10
}
```

### `PUT` /tables/{id}
Request body:
```json
{
  "capacity": 4,
  "tableNumber": 10
}
```
Response: 200 OK
```json
{
  "id": "string",
  "locationId": "string",
  "capacity": 4,
  "tableNumber": 10
}
```

### `DELETE` /tables/{id}
Response: 200 OK
```json
{
  "message": "Table deleted successfully"
}
```

Response: `201 Created`
```json
{
  "id": "string",
  "locationId": "string",
  "capacity": 4,
  "tableNumber": 10
}
```

### `GET` /available-tables
Query parameters:
- `locationId` (string, optional)
- `capacity` (integer, optional)
- `startTime` (ISO 8601 string, optional)

Response: `200 OK`
```json
[
  {
    "id": "string",
    "locationId": "string",
    "capacity": 4,
    "tableNumber": 10
  }
]
```

### `GET` /locations/{locationId}/tables/all
Response: `200 OK`
```json
[
  {
    "id": "string",
    "locationId": "string",
    "capacity": 4,
    "tableNumber": 10
  }
]
```

### `GET` /tables/{id}/available-time-slots
Query parameters:
- `startTime` (ISO 8601 string, required)

Response: `200 OK`
```json
[
  {
    "start": "2026-08-07T19:00:00Z",
    "end": "2026-08-07T20:30:00Z"
  }
]
```

## User Profile

### `GET` /me
Response: `200 OK`
```json
{
  "id": "string",
  "firstName": "string",
  "lastName": "string",
  "email": "string",
  "role": "string"
}
```

### `PUT` /me
Request body:
```json
{
  "firstName": "string",
  "lastName": "string"
}
```

Response: `200 OK`
```json
"User information updated successfully."
```

### `PUT` /me/change-password
Request body:
```json
{
  "oldPassword": "string",
  "newPassword": "string",
  "accessToken": "string"
}
```

Response: `200 OK`
```json
{
  "message": "Password changed successfully."
}
```

### `GET` /users
Query parameters:
- `fullName` (string, required)

Response: `200 OK`
```json
[
  {
    "id": "string",
    "firstName": "string",
    "lastName": "string",
    "email": "string",
    "role": "string"
  }
]
```

### `GET` /users/{id}/profile-image
Response: `200 OK`
```json
{
  "presignedUrl": "https://..."
}
```

If the user has not uploaded an image yet, the route returns:
```json
"Profile image is not uploaded so far."
```

### `PUT` /me/profile-image
Request body:
```json
{
  "base64Image": "string"
}
```

Response: `200 OK`
```json
"Photo was uploaded successfully!"
```

### `GET` /users/{id}/name
Response: `200 OK`
```json
{
  "firstName": "string",
  "lastName": "string"
}
```

## Orders

### `GET` /orders
Response: `200 OK`
```json
[
  {
    "orderId": "string",
    "reservationId": "string",
    "reservationAddress": "string",
    "reservationTime": "string",
    "orderItems": [
      {
        "dish": {
          "id": "string",
          "name": "string",
          "description": "string",
          "dishType": "string",
          "price": 12.34,
          "weight": "string",
          "calories": "string",
          "proteins": "string",
          "fats": "string",
          "carbohydrates": "string",
          "vitamins": "string",
          "imageUrl": "string",
          "state": "string"
        },
        "quantity": 2
      }
    ],
    "status": "Created"
  }
]
```

### `GET` /orders/{reservationId}
Response: `200 OK`
```json
{
  "orderId": "string",
  "reservationId": "string",
  "reservationAddress": "string",
  "reservationTime": "string",
  "orderItems": [
    {
      "dish": {
        "id": "string",
        "name": "string",
        "description": "string",
        "dishType": "string",
        "price": 12.34,
        "weight": "string",
        "calories": "string",
        "proteins": "string",
        "fats": "string",
        "carbohydrates": "string",
        "vitamins": "string",
        "imageUrl": "string",
        "state": "string"
      },
      "quantity": 2
    }
  ],
  "status": "Created"
}
```

### `POST` /orders
Request body:
```json
{
  "reservationId": "string",
  "orderItems": [
    {
      "dishId": "string",
      "quantity": 2
    }
  ]
}
```

Response: `201 Created`
```json
{
  "orderId": "string",
  "reservationId": "string",
  "reservationAddress": "string",
  "reservationTime": "string",
  "orderItems": [
    {
      "dish": {
        "id": "string",
        "name": "string",
        "description": "string",
        "dishType": "string",
        "price": 12.34,
        "weight": "string",
        "calories": "string",
        "proteins": "string",
        "fats": "string",
        "carbohydrates": "string",
        "vitamins": "string",
        "imageUrl": "string",
        "state": "string"
      },
      "quantity": 2
    }
  ],
  "status": "Created"
}
```

### `PUT` /orders/{reservationId}
Request body:
```json
{
  "orderItems": [
    {
      "dishId": "string",
      "quantity": 3
    }
  ]
}
```

Response: `200 OK`
```json
"Pre-order updated successfully."
```

### `DELETE` /orders/{reservationId}
Response: `200 OK`
```json
"Pre-order canceled successfully."
```

### `POST` /orders/{reservationId}/submit
Response: `200 OK`
```json
"Pre-order submitted successfully."
```

### `POST` /orders/{reservationId}/unsubmit
Response: `200 OK`
```json
"Pre-order unsubmitted successfully."
```

## Reports

### `GET` /reports/download
Query parameters:
- `format` (string, optional: Excel, Pdf, Csv)
- `type` (string, optional: Service, Sales)
- `startDate` (YYYY-MM-DD, optional)
- `endDate` (YYYY-MM-DD, optional)
- `locationId` (string, required)

Response: `200 OK` with file download content

### `GET` /reports/generate
Query parameters:
- `type` (string, optional: Service, Sales)
- `startDate` (YYYY-MM-DD, required)
- `endDate` (YYYY-MM-DD, required)
- `locationId` (string, required)

Response: `200 OK`
```json
[
  {
    "reportType": "Service",
    "startDate": "2026-08-01",
    "endDate": "2026-08-07",
    "userId": "string",
    "locationId": "string",
    "locationName": "string",
    "waiterId": "string",
    "waiterName": "string",
    "waiterEmail": "string",
    "workingHours": 72.5,
    "totalOrders": 28,
    "avgServiceFeedback": 4.7,
    "minServiceFeedback": 3.8,
    "deltaAvgServiceFeedback": 1.2,
    "deltaTotalOrders": 5.0
  }
]
```

For `type=Sales`, the response is an array of sales report history objects:
```json
[
  {
    "reportType": "Sales",
    "startDate": "2026-08-01",
    "endDate": "2026-08-07",
    "userId": "string",
    "locationId": "string",
    "locationName": "string",
    "totalOrders": 147,
    "avgCuisineFeedback": 4.3,
    "minCuisineFeedback": 2.5,
    "revenue": 32145.60,
    "deltaAvgCuisineFeedback": 2.1,
    "deltaRevenue": 8.4
  }
]
```

### `GET` /reports
Response: `200 OK`
```json
[
  {
    "id": "string",
    "reportType": "Service",
    "startDate": "2026-08-01",
    "endDate": "2026-08-07",
    "locationId": "string",
    "createdAt": "2026-08-07T12:00:00Z"
  }
]
```

## Notifications

### `GET` /notifications
Response: `200 OK`
```json
[
  {
    "id": "string",
    "message": "string",
    "orderId": "string",
    "reservationId": "string",
    "createdAt": "2026-08-07T12:00:00Z",
    "isRead": false
  }
]
```

### `PATCH` /notifications/{id}/read
Response: `200 OK`
```json
"Notification marked as read"
```

## Group & Guest Reservations

### `GET` /guest/invites/{token}
Public. Returns the group details **in any group state** — it is not a join check. Joining is guarded by
`POST /guest/sessions` (`400` with `errorCode` `GroupReservationSubmitted` / `GroupReservationExpired` /
`GuestSessionExpired` / `GuestLimitExceeded`). `Status` and `CanJoin` let the guest UI show "group is full / closed" before joining.

Response: `200 OK` (PascalCase, as serialized today)
```json
{
  "GroupReservationId": "string",
  "LocationId": "string",
  "RestaurantName": "Green & Tasty",
  "LocationAddress": "string",
  "StartTime": "2026-08-07T19:00:00Z",
  "EndTime": "2026-08-07T20:30:00Z",
  "MaxGuestCount": 4,
  "TableNumber": 3,
  "InviterName": "string",
  "Status": "Open | Submitted | Closed",
  "CanJoin": true
}
```
- `CanJoin` = group `Open` **and** reservation `Reserved` **and** joined guests + 1 (owner) < `MaxGuestCount` — the same rules as `POST /guest/sessions`.
- Errors: `404` unknown token · `400` associated reservation not found.
- Before F2/F3 this endpoint returned `400` for a full group (`GuestLimitExceeded`) and for a non-open group (`GroupReservationExpired`).

### `POST` /guest/sessions
Request body:
```json
{
  "groupReservationId": "string",
  "guestName": "string"
}
```
Response: 201 Created
```json
{
  "sessionToken": "string"
}
```

### `GET` /guest/sessions/{guestSessionId}
Response: `200 OK`
```json
{
  "id": "string",
  "groupReservationId": "string",
  "guestName": "string",
  "status": "string"
}
```
### `POST` /guest-sessions/{guestSessionId}/dishes
Request body:
```json
{
  "dishId": "string",
  "quantity": 1
}
```
Response: 200 OK
```json
{
  "message": "Dish added to guest session successfully."
}
```

### `DELETE` /guest-sessions/{guestSessionId}/dishes
Request body:
```json
{
  "dishId": "string"
}
```
Response: 200 OK
```json
{
  "message": "Dish removed from guest session successfully."
}
```

### `GET` /guest/sessions/{guestSessionId}/menu
Response: 200 OK
```json
[
  {
    "id": "string",
    "name": "string",
    "imageUrl": "string",
    "price": 12.34,
    "state": "Available",
    "weight": "string",
    "dishType": "string",
    "description": "string",
    "calories": "string",
    "proteins": "string",
    "fats": "string",
    "carbohydrates": "string",
    "vitamins": "string"
  }
]
```

### `POST` /guest/sessions/{guestSessionId}/submit
Response: 200 OK
```json
{
  "message": "Guest cart submitted successfully."
}
```

### `GET` /group-reservations/{groupReservationId}/status
Response: 200 OK
```json
{
  "status": "string",
  "totalGuests": 4,
  "submittedGuests": 3
}
```

## Group pre-order (Owner)

All endpoints below are behind `cognito_authorizer` (`Authorization: Bearer <idToken>`).
**Owner** = the customer whose reservation the group reservation belongs to (`reservation.customerId == sub`).

Error bodies:
- `401` / `403` / `404` — `{ "error": "<message>" }` (`ResponseBuilder.CreateErrorResponse`);
- `400` from business rules — `{ "message": "<message>", "errorCode": "<code>" }` (`ResponseBuilder.CreateErrorCodeResponse`);
  `errorCode` is `GroupReservationSubmitted` for a repeated submit, `GroupReservationExpired` for a closed group, `Unknown` otherwise;
- `400` from request validation in the handler (missing path parameter / body without `reservationId` / invalid JSON) — `{ "error": "<message>" }`.

| Method and path | Success | Errors |
|---|---|---|
| `GET /guest/groups/{groupReservationId}/sessions` | `200` snapshot | `401` no token · `403` not the Owner · `404` group reservation not found · `404` its reservation not found |
| `GET /reservations/{reservationId}/guest-session-id` | `200 { "guestSessionId": "…" }` | `401` · `403` not the Owner (also for reservations without a customer) · `404` reservation not found · `404` group reservation not found · `404` owner session not found (only when the group is no longer `Open`, see below) |
| `POST /group-reservations/{groupReservationId}/submit`, body `{ "reservationId": "…" }` | `200 [ { "dishId": "…", "quantity": 3 } ]` | `401` · `403` not the Owner · `404` reservation not found · `404` group reservation not found · `400` body without `reservationId` / invalid JSON · `400` group belongs to another reservation · `400` repeated submit: `"Group pre-order has already been submitted."` · `400` group is `Closed`: `"Group reservation is closed."` · `400` no submitted guest sessions and the Owner has no dishes in the order · `400` submitted guests have no dishes and the Owner has none · `400` no order for the reservation (or the order is `Cancelled`) |
| `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` | `200` updated snapshot | `401` · `403` not the Owner · `404` group reservation not found · `404` guest session not found · `400` session belongs to another group · `400` attempt to remove the owner session |

### Snapshot (`GET …/sessions`, `DELETE …/sessions/{guestSessionId}`)
```json
{
  "participants": [
    {
      "id": "owner-<groupReservationId>",
      "type": "Owner",
      "name": "Andrii Shevchenko",
      "items": [],
      "total": 0,
      "isSubmitted": false
    },
    {
      "id": "session1",
      "type": "Guest",
      "name": "Andrii",
      "items": [
        { "dish": { "id": "dish1", "name": "Caesar Salad", "price": 180, "...": "other DishResponse fields" }, "quantity": 2 }
      ],
      "total": 360,
      "isSubmitted": true
    },
    {
      "id": "session2",
      "type": "Guest",
      "name": "Olena",
      "items": [ { "dish": { "id": "dish2", "name": "Tom Yum", "price": 220 }, "quantity": 1 } ],
      "total": 220,
      "isSubmitted": false
    }
  ],
  "total": 580
}
```
- `type` = `Owner` for the session with `isOwnerSession = true`, otherwise `Guest`.
- **Owner participant = the reservation order** ("My Pre-Order" = what the restaurant gets), not the owner session's cart:
  `items` = lines of the order of the reservation (not `Cancelled`), without `Canceled` lines and lines with quantity ≤ 0,
  as `{ dish, quantity }`; `total` = Σ of those lines; `isSubmitted` = the order exists and has such lines (otherwise `false`, `items: []`).
  This applies in **any** group state. After a group submit the order also contains the guests' dishes, so for a non-`Open` group
  the snapshot subtracts the group's `submittedGuestDishes` (stored atomically with the claim) from the order lines by `dishId`;
  lines that drop to ≤ 0 are removed. Result: "My Pre-Order" = the Owner's own dishes, and the consolidated submitted participants =
  the order (nothing counted twice). For an `Open` group, or groups submitted before the field existed (`null`), nothing is subtracted.
  Deleting a guest after submit does not change the Owner's items.
  If the owner session does not exist yet (the frontend calls `GET …/sessions` in parallel with `GET guest-session-id`, which creates it)
  but the order has such lines, the Owner participant is still returned — with id `owner-<groupReservationId>` (the id
  `guest-session-id` returns) and the Owner's Cognito name (or `"Owner"`); nothing is written. No session and no dishes → no Owner.
  The order is read once per snapshot.
- `participant.total` = Σ `price × quantity`; `total` = Σ `participant.total` of **all** participants (submitted or not).
- An empty group → `{ "participants": [], "total": 0 }`.
- `dish` is the full `DishResponse` (`id`, `name`, `price`, `description`, `dishType`, `weight`, `imageUrl`, `state`, …).

### Submit — order of actions
1. Find the reservation from the body (`404`), the group reservation from the path (`404`), check the caller is the Owner (`403`),
   check the group belongs to that reservation (`400`), check the group status: `Submitted` → `400` "Group pre-order has already been submitted.", `Closed` → `400` "Group reservation is closed.".
2. Take only **guest** sessions with `isSubmitted = true` — the owner session is skipped (the Owner's dishes are already in the
   order; merging its cart would add them twice). Concatenate their dishes and merge by `dishId` (sum of quantities, order of first
   appearance). The response contains only these guests' dishes.
3. Load the order of the reservation (consistent read). **The Owner with dishes in the order** (order not `Cancelled`, at least one line
   not `Canceled` with quantity > 0) **counts as a submitted participant**:
   - no submitted guests **and** the Owner has no dishes → `400` "There are no submitted guest sessions in this group.";
   - the merged guest list is empty **and** the Owner has no dishes → `400` "Submitted guest sessions do not contain any dishes.";
   - no order (or `Cancelled`) → `400` "Pre-order for reservation … was not found.";
   - no guest dishes but the Owner has an order → submit succeeds: order → `SubmittedByCustomer`, group → `Submitted`, response `[]`.
4. In memory: for each merged guest dish, if it is already in the order its quantity is increased, otherwise a new line is added;
   order → `SubmittedByCustomer`.
5. **Claim the group**: re-read it by key (consistent), it must still be `Open` (otherwise the same `400`s as in step 1), then a
   version-checked write `Open → Submitted` that **in the same write** stores `submittedGuestDishes` (the merged guest list from
   step 2) on the group. A version conflict → `400` "Group pre-order has already been submitted.". The compensation (reopen) clears it.
6. Version-checked order write. Version conflict (a *different* write to the same order — owner edit/cancel, waiter confirm —
   because the claim lets only one submit through) → the group is written back to `Open` (version-checked compensation) and
   `400 { "message": "The pre-order was changed while submitting. Please try again.", "errorCode": "Unknown" }`.
   Any other failure → the same compensation and the error is returned (`500`).
7. Response — the merged list from step 3 (not the whole order).

### Decisions on behaviour not defined by the homework
- **Repeated submit** → `400` `{ "message": "Group pre-order has already been submitted.", "errorCode": "GroupReservationSubmitted" }`; there is no "unsubmit"/re-merge of a group.
- **Closed group** (reservation cancelled/finished) → `400` "Group reservation is closed." (`errorCode: GroupReservationExpired`) instead of the "already submitted" message.
- **Check order on submit**: `404` reservation → `404` group → `403` owner → `400` mismatch → `400` status. A non-owner never learns whether the group belongs to the reservation.
- **Order lines changed by submit** get `state = Pending` (also an existing line whose quantity was increased): the waiter has to confirm the new quantity.
- **Cancelled order** is treated as "no order" (`400`).
- **Concurrent submits** (optimistic locking on both items, no DynamoDB transaction).
  - `GroupReservation` and `Order` have a nullable `version` attribute (`[DynamoDBVersion]`, `[JsonIgnore]`): old records load with
    `null`, the first save sets it; every save of these entities is a conditional write.
  - The group transition `Open → Submitted` (step 5) is single-winner: only one submit can make it, so the guests' dishes are added
    to the order once however the requests interleave — including "B validated the group before A started, A finished, B continues"
    (B's consistent re-read sees `Submitted`; if B re-read just before A's claim, B's version-checked claim fails).
  - The order version check (step 6) protects the order against a concurrent edit between read and write. Such a conflict is not
    another submit (the claim is single-winner), so the group is reopened and the Owner retries: no "Submitted group without the
    guests' dishes" state, and no false "already submitted" message.
  - The reopen is version-checked too: the SDK bumps `Version` on the claimed object after the claim, so the reopen succeeds only
    if nobody changed the group since; otherwise it is rejected and ignored, and nothing is overwritten.
  - Why not `TransactWriteItems`: the SDK supports it (`CreateTransactWrite`), but it would bypass `IOrderRepository.SaveOrderAsync` /
    `IGroupReservationRepository.SaveGroupReservationAsync`, which all order flows and their tests rely on; the claim + compensation
    gives the same single-submit guarantee with the existing repositories.
  - Save paths checked for the versions: new items always get a new id (`CreateGroupReservationAsync`, `AddOrderAsync`); every update
    loads first. Reads from eventually consistent indexes are re-read by key with `ConsistentRead` before a save
    (`OrderRepository.GetOrderByReservationIdAsync`, `CloseGroupReservationAsync`; the latter also retries up to 3 times on a version conflict).
  - Remaining non-atomic window: if the order write fails for a reason other than a version conflict *and* the compensation write
    also fails, the group stays `Submitted` without the guests' dishes in the order.
- **Submit deliberately does not re-check dish availability (`DishState`) or the pre-order edit window (`EnsurePreOrderEditable`,
  30 minutes before the start)** — the homework does not require it; guests' carts were validated (`DishState`) when each guest
  submitted the cart, and a closed/finished reservation closes the group (`400 "Group reservation is closed."`).
- **Owner session is created lazily.** The backend never created owner sessions on reservation creation, so `GET /reservations/{reservationId}/guest-session-id`
  creates it on the first call while the group is `Open` (id `owner-<groupReservationId>`, `isOwnerSession = true`, empty cart,
  `guestName` = the owner's Cognito name or `"Owner"`). Repeated/concurrent calls resolve to the same session. When the group is already
  `Submitted`/`Closed` and there is no owner session, the endpoint returns `404`.
- **The owner session is not counted as a joined guest**: guest capacity checks (`POST /guest/sessions`, `CanJoin` of `GET /guest/invites/{token}`) and
  `currentParticipants` in `GET /guest/groups/{id}/status` count guests only, the owner is always `+1`.
- **Owner's own dishes**: there is no API that adds dishes to the owner session (guest dish endpoints require a guest session token).
  The Owner's own pre-order is the regular order (`POST /orders`, `PUT /orders/{reservationId}`); group submit adds only the guests'
  dishes to it (the owner session is skipped even if it has dishes), and the snapshot shows the order as the Owner's items.
- **Snapshot order**: the owner session first, then guests in storage order. Dish lines whose dish no longer exists are skipped (cannot be priced).
- **DELETE** is allowed in any group status (the frontend is read-only after submit); removing the owner session → `400`.
- Snapshot responses are camelCase (`participants`, `total`, `id`, `type`, …) as in the homework; before the fix `DELETE` returned PascalCase.
- **Eventual consistency of the `GroupReservationId` index.** The snapshot after `DELETE` never contains the deleted session even if the
  index still returns it. If the index does not return an owner session yet, the snapshot (and `guest-session-id`) also look it up by its
  deterministic id `owner-<groupReservationId>`, so a just-created owner session appears in the first `GET …/sessions` and is not re-created.
- **Owner screen in any group state** (full / `Submitted` / `Closed`): `GET /reservations/{id}/invite-token` and `GET /guest/invites/{token}`
  no longer reject non-open or full groups; "full / not Open" checks protect only guest join (`POST /guest/sessions`).

### Known limitations
- `DELETE` of a guest after a group submit: the guest's dishes stay in the order and in the group's `submittedGuestDishes`, but disappear
  from the snapshot (the UI does not show "Remove" after submit).
- A version conflict in the regular order flows (customer edit/cancel/submit/unsubmit, waiter confirm/cancel dish) — i.e. a truly
  concurrent write to the same order — returns `409 { "error": "The resource was changed concurrently. Please retry." }`
  (`ExceptionHandler`, for exceptions a handler does not catch itself); retrying the request succeeds.
  Group submit handles its conflicts itself and keeps its `400`s.
- `CloseGroupReservationAsync` (reservation cancelled / status changed) retries a group version conflict 3 times and then throws:
  `PATCH /reservations/{id}/status` returns `409`, `DELETE /reservations` returns `503` (that handler maps every `AmazonDynamoDBException` to `503`).

### Authorization (group pre-order)
Full table and audit notes: `docs/features/group-preorder.md` → "Authorization". In short:

| Endpoint | Who has access | How it is checked |
|---|---|---|
| `GET /reservations/{reservationId}/invite-token` | Owner (customer of the reservation) | `cognito_authorizer` (no/invalid token → 401 from API Gateway) + handler: no `sub` → 401; service `GetOwnerInviteTokenAsync`: `reservation.customerId == sub`, else 403 (also for reservations without a customer); 404 reservation / group |
| `GET /reservations/{reservationId}/guest-session-id` | Owner | `cognito_authorizer` + handler `sub` (401) + `EnsureOwner` in `GetOwnerGuestSessionIdAsync` (403) |
| `GET /guest/groups/{groupReservationId}/sessions` | Owner | `cognito_authorizer` + handler `sub` (401) + `ValidateReservationOwnerAsync` (group → reservation → `customerId == sub`, 403) |
| `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` | Owner | `cognito_authorizer` + handler `sub` + `ValidateReservationOwnerAsync` (403); session must belong to the group (400), owner session cannot be removed (400) |
| `POST /group-reservations/{groupReservationId}/submit` | Owner | `cognito_authorizer` + handler `sub` + `EnsureOwner` on the reservation from the body (403); the group must belong to that reservation (400) |
| `GET /guest/invites/{token}` | Anyone with the invite token (anonymous) | `NONE` (public); the 32-hex invite token is the secret; unknown token → 404 |
| `POST /guest/sessions` | Anyone who knows the `groupReservationId` (anonymous guest) | `NONE`; join rules in `CreateGuestSessionAsync` (group Open, reservation Reserved, free seat); returns a random 32-byte session token, only its SHA-256 is stored |
| `GET /guest/sessions/{guestSessionId}`, `POST/DELETE …/dishes`, `GET …/menu`, `POST …/submit` | The guest holding **that** session's token | `NONE` + `GuestAuthorizationService`: `Authorization: Bearer <session token>` (header name case-insensitive), SHA-256 of the token is looked up and must belong to the session in the path — another guest's token → 401; missing/malformed header or token (incl. a Cognito JWT) → 401. The owner session has no token, so it cannot be modified through these endpoints |
| `GET /guest/groups/{groupReservationId}/status` | Anyone (anonymous) | `NONE`, no check — returns only status, participant count and capacity |
