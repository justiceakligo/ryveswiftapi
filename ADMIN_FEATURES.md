# RyveSwift Admin Features and API Reference

This guide documents every admin feature currently exposed by the RyveSwift API, including endpoint behavior, query parameters, request payloads, response payloads, and suggested dashboard uses.

Base URL:

```text
https://swift.ryvepos.com
```

Local development URL:

```text
http://localhost:5191
```

All admin endpoints require a valid admin JWT.

```http
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

The JWT must contain the `Admin` role because admin routes use the `AdminOnly` authorization policy.

## Admin Endpoint Index

| Feature | Method | Endpoint |
|---|---:|---|
| Dashboard analytics overview | GET | `/api/admin/analytics/overview` |
| Revenue report alias | GET | `/api/admin/reports/revenue` |
| Shipment administration list | GET | `/api/admin/shipments` |
| User administration list | GET | `/api/admin/users` |
| User detail | GET | `/api/admin/users/{id}` |
| Create user | POST | `/api/admin/users` |
| Update user | PUT | `/api/admin/users/{id}` |
| Suspend user | POST | `/api/admin/users/{id}/suspend` |
| Reactivate user | POST | `/api/admin/users/{id}/reactivate` |
| Soft-delete user | DELETE | `/api/admin/users/{id}` |
| Reset user password | POST | `/api/admin/users/{id}/reset-password` |
| Markup rules list | GET | `/api/admin/markup-rules` |
| Create markup rule | POST | `/api/admin/markup-rules` |
| Update markup rule | PUT | `/api/admin/markup-rules/{id}` |
| Deactivate markup rule | DELETE | `/api/admin/markup-rules/{id}` |
| DHL booking failures | GET | `/api/admin/dhl-failures` |
| Email configuration status | GET | `/api/admin/emails/config` |
| Update email configuration | PUT | `/api/admin/emails/config` |
| Send test email | POST | `/api/admin/emails/test` |
| Send custom email | POST | `/api/admin/emails/send` |
| Manual tracking sync | POST | `/api/admin/sync-tracking` |

## Shared Behavior

### Date Ranges

Analytics endpoints accept optional `from` and `to` query parameters.

```http
GET /api/admin/analytics/overview?from=2026-05-01&to=2026-05-31
```

The date range aliases `fromDate` / `toDate` and `startDate` / `endDate` are also supported.

```http
GET /api/admin/analytics/overview?fromDate=2026-05-01&toDate=2026-05-31
GET /api/admin/analytics/overview?startDate=2026-05-01&endDate=2026-05-31
```

If `to` is supplied as a date without a time, the API treats it as the end of that day. For example, `to=2026-05-31` becomes `2026-05-31T23:59:59.9999999Z`.

If no range is provided, analytics default to the last month ending at the current UTC time.

### Error Envelope

Admin errors use the normal API error envelope.

```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "'to' must be greater than or equal to 'from'.",
    "details": []
  }
}
```

### Revenue Definitions

Revenue-bearing shipment statuses:

```json
[
  "PaymentAuthorized",
  "Booked",
  "LabelGenerated",
  "DroppedOff",
  "InTransit",
  "OutForDelivery",
  "Delivered",
  "Exception"
]
```

Financial definitions:

| Field | Meaning |
|---|---|
| `customerRevenue` / `totalRevenue` | Amount paid by customers for revenue-bearing shipments. |
| `dhlActuallyCharged` / `dhlBaseCost` | DHL base rate stored on each shipment. This is the DHL cost basis. |
| `markupRevenue` | Percent markup portion only: customer revenue minus DHL charge minus platform fee. |
| `platformFees` | Flat platform fees collected across shipments. |
| `grossProfit` / `markupEarned` | Customer revenue minus DHL charge. This includes markup revenue plus platform fees. |
| `grossMarginPercent` | Gross profit divided by customer revenue. |
| `shipmentsMissingDhlCharge` | Count of revenue shipments where `DhlBaseRate` is missing. |

## Dashboard Analytics Overview

Use this endpoint as the primary source for admin dashboard cards, charts, route performance tables, product mix, funnel analysis, and customer analytics.

```http
GET /api/admin/analytics/overview?from=2026-05-01&to=2026-05-31
```

Alias:

```http
GET /api/admin/reports/revenue?from=2026-05-01&to=2026-05-31
```

### Request

No JSON body.

Query parameters:

| Parameter | Type | Required | Notes |
|---|---|---:|---|
| `from` | Date/time | No | UTC date or date-time. Defaults to one month before now. |
| `to` | Date/time | No | UTC date or date-time. Date-only values include the full day. Defaults to now. |

Accepted aliases:

| Canonical | Aliases |
|---|---|
| `from` | `fromDate`, `startDate` |
| `to` | `toDate`, `endDate` |

Example:

```http
GET /api/admin/analytics/overview?from=2026-05-01&to=2026-05-31
Authorization: Bearer <admin_access_token>
```

### Response 200

```json
{
  "from": "2026-05-01T00:00:00Z",
  "to": "2026-05-31T23:59:59.9999999Z",
  "currency": "CAD",
  "totalRevenue": 1240.00,
  "dhlBaseCost": 980.00,
  "markupEarned": 260.00,
  "totalShipments": 12,
  "paidShipments": 12,
  "revenueSplit": {
    "customerRevenue": 1240.00,
    "dhlActuallyCharged": 980.00,
    "markupRevenue": 200.00,
    "platformFees": 60.00,
    "grossProfit": 260.00,
    "grossMarginPercent": 20.97,
    "averageOrderValue": 103.33,
    "averageDhlCharge": 81.67,
    "averageMarkupRevenue": 16.67,
    "averagePlatformFee": 5.00,
    "averageGrossProfit": 21.67,
    "averageMarkupPercentApplied": 20.00,
    "shipmentsMissingDhlCharge": 0
  },
  "operations": {
    "shipmentsCreated": 15,
    "revenueShipments": 12,
    "labelsGenerated": 8,
    "inTransit": 3,
    "delivered": 1,
    "pendingPayment": 2,
    "refunded": 1,
    "cancelled": 0,
    "exceptions": 0,
    "dhlBookingFailures": 1,
    "dhlBookingFailureRatePercent": 6.67,
    "totalWeightKg": 36.50,
    "averageWeightKg": 2.43,
    "averageMinutesToLabel": 4.75
  },
  "funnel": {
    "quotesCreated": 40,
    "guestQuotes": 12,
    "registeredQuotes": 28,
    "expiredQuotes": 10,
    "paymentIntentsCreated": 18,
    "succeededPayments": 12,
    "failedPayments": 2,
    "pendingPayments": 3,
    "refundedPayments": 1,
    "quoteToShipmentRatePercent": 37.50,
    "quoteToPaidShipmentRatePercent": 30.00,
    "paymentSuccessRatePercent": 66.67
  },
  "customers": {
    "totalCustomers": 86,
    "newCustomers": 9,
    "activeCustomers": 11,
    "repeatCustomers": 2,
    "averageRevenuePerActiveCustomer": 112.73,
    "averageShipmentsPerActiveCustomer": 1.36
  },
  "timeSeries": [
    {
      "periodStart": "2026-05-01T00:00:00Z",
      "period": "2026-05-01",
      "quotes": 4,
      "shipments": 2,
      "paidShipments": 2,
      "newCustomers": 1,
      "revenue": 210.00,
      "dhlActuallyCharged": 166.00,
      "markupRevenue": 34.00,
      "platformFees": 10.00,
      "grossProfit": 44.00
    },
    {
      "periodStart": "2026-05-02T00:00:00Z",
      "period": "2026-05-02",
      "quotes": 6,
      "shipments": 1,
      "paidShipments": 1,
      "newCustomers": 0,
      "revenue": 95.00,
      "dhlActuallyCharged": 75.00,
      "markupRevenue": 15.00,
      "platformFees": 5.00,
      "grossProfit": 20.00
    }
  ],
  "topRoutes": [
    {
      "route": "CA-GH",
      "originCountry": "CA",
      "destinationCountry": "GH",
      "shipments": 7,
      "revenue": 760.00,
      "dhlActuallyCharged": 600.00,
      "markupRevenue": 125.00,
      "platformFees": 35.00,
      "grossProfit": 160.00,
      "grossMarginPercent": 21.05,
      "averageRevenue": 108.57,
      "averageWeightKg": 2.90
    },
    {
      "route": "US-NG",
      "originCountry": "US",
      "destinationCountry": "NG",
      "shipments": 5,
      "revenue": 480.00,
      "dhlActuallyCharged": 380.00,
      "markupRevenue": 75.00,
      "platformFees": 25.00,
      "grossProfit": 100.00,
      "grossMarginPercent": 20.83,
      "averageRevenue": 96.00,
      "averageWeightKg": 2.20
    }
  ],
  "statusBreakdown": [
    {
      "status": "LabelGenerated",
      "count": 8,
      "revenue": 820.00
    },
    {
      "status": "InTransit",
      "count": 3,
      "revenue": 325.00
    },
    {
      "status": "Refunded",
      "count": 1,
      "revenue": 0.00
    }
  ],
  "productMix": [
    {
      "productCode": "P",
      "service": "DHL Express Worldwide",
      "shipments": 10,
      "revenue": 1090.00,
      "grossProfit": 230.00,
      "averageRevenue": 109.00
    },
    {
      "productCode": "D",
      "service": "DHL Express Documents",
      "shipments": 2,
      "revenue": 150.00,
      "grossProfit": 30.00,
      "averageRevenue": 75.00
    }
  ],
  "topCustomers": [
    {
      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "email": "amina@example.com",
      "shipments": 3,
      "revenue": 310.00,
      "grossProfit": 64.00,
      "lastShipmentAt": "2026-05-29T18:30:00Z"
    }
  ]
}
```

### Response 400

```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "'to' must be greater than or equal to 'from'.",
    "details": []
  }
}
```

### Dashboard Usage

Recommended dashboard cards:

| Card | Source |
|---|---|
| Total Revenue | `totalRevenue` |
| DHL Actually Charged | `revenueSplit.dhlActuallyCharged` |
| Markup Revenue | `revenueSplit.markupRevenue` |
| Platform Fees | `revenueSplit.platformFees` |
| Gross Profit | `revenueSplit.grossProfit` |
| Gross Margin | `revenueSplit.grossMarginPercent` |
| Paid Shipments | `paidShipments` |
| Average Order Value | `revenueSplit.averageOrderValue` |
| DHL Booking Failure Rate | `operations.dhlBookingFailureRatePercent` |
| Quote to Paid Shipment Rate | `funnel.quoteToPaidShipmentRatePercent` |

Recommended charts:

| Chart | Source |
|---|---|
| Revenue over time | `timeSeries[].revenue` |
| DHL cost vs markup vs platform fees | `timeSeries[].dhlActuallyCharged`, `timeSeries[].markupRevenue`, `timeSeries[].platformFees` |
| Route profitability | `topRoutes` |
| Shipment status distribution | `statusBreakdown` |
| Product mix | `productMix` |
| Customer leaderboard | `topCustomers` |

## Revenue Report

This endpoint returns the same payload as dashboard analytics. It is kept for compatibility with earlier admin integrations.

```http
GET /api/admin/reports/revenue?from=2026-05-01&to=2026-05-31
```

### Request

No JSON body.

Query parameters are the same as `/api/admin/analytics/overview`.

### Response 200

Same shape as `/api/admin/analytics/overview`.

## Shipment Administration

Use this endpoint for the admin shipment table. It includes user context and per-shipment financial split fields.

```http
GET /api/admin/shipments?page=1&pageSize=50&status=LabelGenerated
```

### Request

No JSON body.

Query parameters:

| Parameter | Type | Required | Default | Notes |
|---|---|---:|---|---|
| `page` | Integer | No | `1` | Minimum `1`. |
| `pageSize` | Integer | No | `50` | Clamped from `1` to `200`. |
| `status` | String | No | none | Filters by internal shipment status, for example `LabelGenerated`, `InTransit`, `Delivered`, `Refunded`. |

Example:

```http
GET /api/admin/shipments?page=1&pageSize=25&status=LabelGenerated
Authorization: Bearer <admin_access_token>
```

### Response 200

```json
{
  "items": [
    {
      "id": "b1c2d3e4-0000-0000-0000-000000000001",
      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "userEmail": "amina@example.com",
      "trackingNumber": "1234567890",
      "status": "LabelGenerated",
      "originCountry": "CA",
      "destinationCountry": "GH",
      "productCode": "P",
      "totalAmount": 98.50,
      "dhlBaseRate": 77.92,
      "markupPercent": 20.00,
      "platformFee": 5.00,
      "markupRevenue": 15.58,
      "grossProfit": 20.58,
      "currency": "CAD",
      "createdAt": "2026-05-29T18:30:00Z"
    },
    {
      "id": "c2d3e4f5-0000-0000-0000-000000000002",
      "userId": "4aa85f64-5717-4562-b3fc-2c963f66afa7",
      "userEmail": "kofi@example.com",
      "trackingNumber": "9988776655",
      "status": "InTransit",
      "originCountry": "US",
      "destinationCountry": "NG",
      "productCode": "D",
      "totalAmount": 75.00,
      "dhlBaseRate": 58.33,
      "markupPercent": 20.00,
      "platformFee": 5.00,
      "markupRevenue": 11.67,
      "grossProfit": 16.67,
      "currency": "CAD",
      "createdAt": "2026-05-28T15:14:12Z"
    }
  ],
  "total": 2,
  "page": 1,
  "pageSize": 25
}
```

### Field Notes

| Field | Notes |
|---|---|
| `productCode` | `P` means parcel/worldwide, `D` means documents. |
| `dhlBaseRate` | Stored DHL cost. Can be `null` for legacy or incomplete shipments. |
| `markupRevenue` | `totalAmount - dhlBaseRate - platformFee`. |
| `grossProfit` | `totalAmount - dhlBaseRate`. |

## User Administration

Use this endpoint for customer/admin account browsing.

```http
GET /api/admin/users?page=1&pageSize=50&includeDeleted=false
```

### Request

No JSON body.

Query parameters:

| Parameter | Type | Required | Default | Notes |
|---|---|---:|---|---|
| `page` | Integer | No | `1` | Minimum `1`. |
| `pageSize` | Integer | No | `50` | Clamped from `1` to `200`. |
| `includeDeleted` | Boolean | No | `false` | When `true`, soft-deleted users are included. |

### Response 200

```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "email": "amina@example.com",
      "fullName": "Amina Owusu",
      "phone": "+1-416-555-0199",
      "role": "Customer",
      "isSuspended": false,
      "suspendedAt": null,
      "suspendedReason": null,
      "deletedAt": null,
      "passwordResetRequired": false,
      "createdAt": "2026-05-20T14:00:00Z",
      "lastLogin": "2026-05-30T22:10:15Z"
    },
    {
      "id": "11111111-2222-3333-4444-555555555555",
      "email": "admin@ryveswift.com",
      "fullName": "RyveSwift Admin",
      "phone": null,
      "role": "Admin",
      "isSuspended": false,
      "suspendedAt": null,
      "suspendedReason": null,
      "deletedAt": null,
      "passwordResetRequired": false,
      "createdAt": "2026-05-01T09:00:00Z",
      "lastLogin": "2026-05-31T11:04:20Z"
    }
  ],
  "total": 2,
  "page": 1,
  "pageSize": 50
}
```

## User Detail

```http
GET /api/admin/users/{id}
```

### Response 200

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "amina@example.com",
  "fullName": "Amina Owusu",
  "phone": "+1-416-555-0199",
  "role": "Customer",
  "isSuspended": false,
  "suspendedAt": null,
  "suspendedReason": null,
  "deletedAt": null,
  "passwordResetRequired": false,
  "passwordChangedAt": "2026-05-20T14:00:00Z",
  "createdAt": "2026-05-20T14:00:00Z",
  "lastLogin": "2026-05-30T22:10:15Z",
  "shipmentCount": 3,
  "lifetimeRevenue": 310.00,
  "lastShipmentAt": "2026-05-29T18:30:00Z"
}
```

### Response 404

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "User not found.",
    "details": []
  }
}
```

## Create User

Admins can create customer or admin accounts. If `password` is omitted, the API generates a temporary password and returns it once.

```http
POST /api/admin/users
```

### Request Body

```json
{
  "email": "newcustomer@example.com",
  "password": null,
  "fullName": "New Customer",
  "phone": "+1-416-555-0101",
  "role": "Customer"
}
```

Admin account example:

```json
{
  "email": "ops-admin@example.com",
  "password": "StrongTempPass1!",
  "fullName": "Ops Admin",
  "phone": null,
  "role": "Admin"
}
```

### Response 201

```json
{
  "user": {
    "id": "8a3b8796-0000-0000-0000-000000000001",
    "email": "newcustomer@example.com",
    "fullName": "New Customer",
    "phone": "+1-416-555-0101",
    "role": "Customer",
    "isSuspended": false,
    "suspendedAt": null,
    "suspendedReason": null,
    "deletedAt": null,
    "passwordResetRequired": true,
    "createdAt": "2026-05-31T18:00:00Z",
    "lastLogin": null
  },
  "temporaryPassword": "G7za!mQp2V#xK9Ls"
}
```

When an admin supplies `password`, `temporaryPassword` is `null` and `passwordResetRequired` is `false`.

### Response 409

```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "An account with this email already exists.",
    "details": []
  }
}
```

## Update User

Admins can update email, name, phone, and role. The role must be `Customer` or `Admin`.

```http
PUT /api/admin/users/{id}
```

### Request Body

```json
{
  "email": "amina.new@example.com",
  "fullName": "Amina Owusu-Mensah",
  "phone": "+1-416-555-0200",
  "role": "Customer"
}
```

Partial updates are accepted.

```json
{
  "phone": "+1-416-555-0200"
}
```

### Response 200

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "amina.new@example.com",
  "fullName": "Amina Owusu-Mensah",
  "phone": "+1-416-555-0200",
  "role": "Customer",
  "isSuspended": false,
  "suspendedAt": null,
  "suspendedReason": null,
  "deletedAt": null,
  "passwordResetRequired": false,
  "createdAt": "2026-05-20T14:00:00Z",
  "lastLogin": "2026-05-30T22:10:15Z"
}
```

Safety rule: the API will not allow the last active admin to be demoted.

## Suspend User

Suspension blocks future login, blocks existing access-token requests through the active-user middleware, and revokes active refresh tokens.

```http
POST /api/admin/users/{id}/suspend
```

### Request Body

```json
{
  "reason": "Chargeback review in progress."
}
```

### Response 200

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "amina@example.com",
  "fullName": "Amina Owusu",
  "phone": "+1-416-555-0199",
  "role": "Customer",
  "isSuspended": true,
  "suspendedAt": "2026-05-31T18:05:00Z",
  "suspendedReason": "Chargeback review in progress.",
  "deletedAt": null,
  "passwordResetRequired": false,
  "createdAt": "2026-05-20T14:00:00Z",
  "lastLogin": "2026-05-30T22:10:15Z"
}
```

Safety rules:

- Admins cannot suspend their own account.
- The API will not allow the last active admin to be suspended.

## Reactivate User

Reactivation clears suspension fields and clears `deletedAt` for soft-deleted users.

```http
POST /api/admin/users/{id}/reactivate
```

### Request

No JSON body.

### Response 200

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "amina@example.com",
  "fullName": "Amina Owusu",
  "phone": "+1-416-555-0199",
  "role": "Customer",
  "isSuspended": false,
  "suspendedAt": null,
  "suspendedReason": null,
  "deletedAt": null,
  "passwordResetRequired": false,
  "createdAt": "2026-05-20T14:00:00Z",
  "lastLogin": "2026-05-30T22:10:15Z"
}
```

## Delete User

Delete is a soft delete. The API sets `deletedAt`, marks the user suspended, and revokes refresh tokens. Shipment, payment, quote, and analytics history remain intact.

```http
DELETE /api/admin/users/{id}
```

### Request

No JSON body.

### Response 200

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "amina@example.com",
  "fullName": "Amina Owusu",
  "phone": "+1-416-555-0199",
  "role": "Customer",
  "isSuspended": true,
  "suspendedAt": "2026-05-31T18:10:00Z",
  "suspendedReason": "Account deleted by admin.",
  "deletedAt": "2026-05-31T18:10:00Z",
  "passwordResetRequired": false,
  "createdAt": "2026-05-20T14:00:00Z",
  "lastLogin": "2026-05-30T22:10:15Z"
}
```

Safety rules:

- Admins cannot delete their own account.
- The API will not allow the last active admin to be deleted.

## Reset User Password

Admins can set a specific password or ask the API to generate a temporary password. Resetting a password revokes active refresh tokens.

```http
POST /api/admin/users/{id}/reset-password
```

### Request Body

Generate a temporary password:

```json
{
  "newPassword": null,
  "requirePasswordChange": true
}
```

Set a specific password:

```json
{
  "newPassword": "StrongTempPass1!",
  "requirePasswordChange": true
}
```

### Response 200

Generated temporary password response:

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "amina@example.com",
  "passwordResetRequired": true,
  "temporaryPassword": "G7za!mQp2V#xK9Ls"
}
```

Specific password response:

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "amina@example.com",
  "passwordResetRequired": true,
  "temporaryPassword": null
}
```

## User Change Password

Users can change their own password after login. This is useful after an admin reset with `passwordResetRequired = true`.

```http
POST /api/users/change-password
```

### Request Body

```json
{
  "currentPassword": "G7za!mQp2V#xK9Ls",
  "newPassword": "MyNewSecurePass1!"
}
```

### Response 200

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "amina@example.com",
  "phone": "+1-416-555-0199",
  "fullName": "Amina Owusu",
  "role": "Customer",
  "passwordResetRequired": false,
  "createdAt": "2026-05-20T14:00:00Z"
}
```

## Public Password Reset

Users can request an email reset link without being logged in. The response is intentionally generic so the API does not reveal whether an email address exists.

```http
POST /api/auth/forgot-password
```

### Request Body

```json
{
  "email": "amina@example.com"
}
```

### Response 200

```json
{
  "message": "If an active account exists for that email, a password reset link has been sent."
}
```

The reset link points to:

```text
{App:FrontendBaseUrl}/reset-password?token=<reset_token>
```

Frontend should collect the new password and submit the token to the reset endpoint.

```http
POST /api/auth/reset-password
```

### Request Body

```json
{
  "token": "reset_token_from_email",
  "newPassword": "MyNewSecurePass1!"
}
```

### Response 200

```json
{
  "message": "Password has been reset."
}
```

Behavior:

- Reset links expire after `Email:PasswordResetExpiryMinutes` minutes.
- Requesting a new reset link invalidates earlier unused reset links for that user.
- Resetting a password revokes active refresh tokens.
- Reset links are stored server-side as SHA-256 hashes, not raw tokens.

## Transactional Email

Email is sent through the `IEmailService` dispatcher. The current provider is Resend. Business flows do not fail if email sending is disabled or Resend is unavailable; the send result is logged or returned by admin email endpoints.

### Configuration Keys

| Key | Default | Notes |
|---|---|---|
| `email.provider` | `resend` | Supported values: `resend`, `disabled`, `none`, `noop`. |
| `Email:Resend:ApiKey` | `PLACEHOLDER_RESEND_API_KEY` | Secret Resend API key. Email sends are skipped until this is set. |
| `Email:Resend:From` | `no-reply@ryverental.info` | Must be a verified sender/domain in Resend. |
| `Email:Resend:FromName` | `RyveSwift` | Sender display name. |
| `Email:ReplyTo` | `support@ryvepool.com` | Reply-to address. |
| `Email:AdminRecipients` | empty | Comma-separated admin alert list. Falls back to active admin user emails when blank. |
| `Email:PasswordResetExpiryMinutes` | `30` | User password reset link lifetime. |
| `Email:SubjectPrefix` | `RyveSwift` | Prefix applied to all outbound subject lines unless already present. |
| `App:PublicBaseUrl` | `https://swift.ryvepos.com` | Public API base URL used for unsubscribe/resubscribe links. |
| `App:FrontendBaseUrl` | `https://swift.ryvepos.com` | Base URL used for password reset links. |

Environment variable overrides are supported for email values, including `RESEND_API_KEY`, `EMAIL_PROVIDER`, `EMAIL_FROM`, `EMAIL_FROM_NAME`, `EMAIL_REPLY_TO`, `EMAIL_ADMIN_RECIPIENTS`, `EMAIL_SUBJECT_PREFIX`, and `APP_PUBLIC_BASE_URL`.

## Get Email Configuration

Use this for the admin panel email settings screen and health/status indicator. The Resend API key is never returned.

```http
GET /api/admin/emails/config
```

### Response 200

```json
{
  "provider": "resend",
  "fromEmail": "no-reply@ryverental.info",
  "fromName": "RyveSwift",
  "replyTo": "support@ryvepool.com",
  "adminRecipients": "ops@example.com, support@example.com",
  "subjectPrefix": "RyveSwift",
  "publicBaseUrl": "https://swift.ryvepos.com",
  "resendApiKeyConfigured": true
}
```

## Update Email Configuration

```http
PUT /api/admin/emails/config
```

### Request Body

All fields are optional. Send only the values being changed.

```json
{
  "provider": "resend",
  "resendApiKey": "re_live_xxxxxxxxxxxxxxxxx",
  "fromEmail": "no-reply@ryverental.info",
  "fromName": "RyveSwift",
  "replyTo": "support@ryvepool.com",
  "adminRecipients": "ops@example.com, support@example.com",
  "subjectPrefix": "RyveSwift",
  "publicBaseUrl": "https://swift.ryvepos.com"
}
```

### Response 200

```json
{
  "provider": "resend",
  "fromEmail": "no-reply@ryverental.info",
  "fromName": "RyveSwift",
  "replyTo": "support@ryvepool.com",
  "adminRecipients": "ops@example.com, support@example.com",
  "subjectPrefix": "RyveSwift",
  "publicBaseUrl": "https://swift.ryvepos.com",
  "resendApiKeyConfigured": true
}
```

## Send Test Email

```http
POST /api/admin/emails/test
```

### Request Body

```json
{
  "toEmail": "admin@example.com"
}
```

### Response 200

```json
{
  "status": "sent",
  "messageId": "email_abc123",
  "error": null
}
```

If Resend is not configured:

```json
{
  "status": "skipped",
  "messageId": null,
  "error": "missing_resend_api_key"
}
```

## Send Custom Email

```http
POST /api/admin/emails/send
```

### Request Body

```json
{
  "toEmail": "customer@example.com",
  "subject": "Your shipment update",
  "textBody": "Your shipment is ready.",
  "htmlBody": "<p>Your shipment is ready.</p>"
}
```

### Response 200

```json
{
  "status": "sent",
  "messageId": "email_abc123",
  "error": null
}
```

## Email Unsubscribe

Automatic user emails include a signed unsubscribe link in the footer.

```http
GET /api/email/unsubscribe?token=<signed_token>
```

### Response 200

Returns a small HTML confirmation page.

Frontend or account settings screens can also use JSON:

```http
POST /api/email/unsubscribe
```

```json
{
  "token": "signed_unsubscribe_token"
}
```

```json
{
  "emailUnsubscribed": true,
  "emailUnsubscribedAt": "2026-05-31T18:45:00Z",
  "message": "You have been unsubscribed from non-essential RyveSwift email notifications."
}
```

Resubscribe:

```http
POST /api/email/resubscribe
```

```json
{
  "token": "signed_unsubscribe_token"
}
```

```json
{
  "emailUnsubscribed": false,
  "emailUnsubscribedAt": null,
  "message": "You have been resubscribed to RyveSwift email notifications."
}
```

Unsubscribed users are skipped for non-essential user notifications. Security and account-access emails, such as password reset, password changed, account suspension, reactivation, and deletion, may still be sent.

## Automatic Email Events

User emails are sent for these events:

- Registration welcome email.
- Admin-created account with temporary password when generated.
- Admin password reset with temporary password when generated.
- Public password reset link.
- Password changed confirmation.
- Account suspended, reactivated, or deleted.
- DHL label generated.
- DHL booking failure with refund reference when available.
- Shipment delivered or shipment exception.
- Payment failed.
- Refund processed.
- Shipment cancelled.

Admin emails are sent for these events:

- New user registration.
- New shipment label generated.
- DHL booking failure.
- DHL transient booking error that requires retry.
- Shipment exception.
- Payment failed.
- Refund processed.
- Stripe dispute opened.
- Shipment cancelled.

## Markup Rules

Markup rules determine the percent markup and flat platform fee applied when a quote is created.

Matching fields are optional. More specific rules are evaluated by the markup service. A rule can apply globally, to a route, to a product code, or to a weight band.

### Markup Rule Object

```json
{
  "id": "d1e2f3a4-0000-0000-0000-000000000001",
  "originCountry": "CA",
  "destinationCountry": "GH",
  "minWeightKg": 0.10,
  "maxWeightKg": 10.00,
  "productCode": "P",
  "markupPercent": 22.00,
  "platformFee": 5.00,
  "isActive": true,
  "createdAt": "2026-05-20T14:00:00Z"
}
```

Fields:

| Field | Type | Notes |
|---|---|---|
| `originCountry` | String/null | Two-letter country code. Null means any origin. |
| `destinationCountry` | String/null | Two-letter country code. Null means any destination. |
| `minWeightKg` | Decimal/null | Minimum shipment weight. Null means no minimum. |
| `maxWeightKg` | Decimal/null | Maximum shipment weight. Null means no maximum. |
| `productCode` | String/null | `P` for parcel, `D` for documents. Null means any product. |
| `markupPercent` | Decimal | Percent added on top of DHL cost. Must be zero or greater. |
| `platformFee` | Decimal | Flat fee added after percent markup. |

## List Markup Rules

```http
GET /api/admin/markup-rules
```

### Request

No JSON body.

### Response 200

```json
[
  {
    "id": "d1e2f3a4-0000-0000-0000-000000000001",
    "originCountry": "CA",
    "destinationCountry": "GH",
    "minWeightKg": null,
    "maxWeightKg": null,
    "productCode": "P",
    "markupPercent": 20.00,
    "platformFee": 5.00,
    "isActive": true,
    "createdAt": "2026-05-20T14:00:00Z"
  },
  {
    "id": "d1e2f3a4-0000-0000-0000-000000000002",
    "originCountry": "US",
    "destinationCountry": "NG",
    "minWeightKg": 10.00,
    "maxWeightKg": 70.00,
    "productCode": "P",
    "markupPercent": 24.00,
    "platformFee": 8.00,
    "isActive": true,
    "createdAt": "2026-05-24T12:30:00Z"
  }
]
```

## Create Markup Rule

```http
POST /api/admin/markup-rules
```

### Request Body

```json
{
  "originCountry": "CA",
  "destinationCountry": "GH",
  "minWeightKg": 0.10,
  "maxWeightKg": 10.00,
  "productCode": "P",
  "markupPercent": 22.00,
  "platformFee": 5.00
}
```

Minimal global rule example:

```json
{
  "originCountry": null,
  "destinationCountry": null,
  "minWeightKg": null,
  "maxWeightKg": null,
  "productCode": null,
  "markupPercent": 20.00,
  "platformFee": 5.00
}
```

### Response 201

```json
{
  "id": "d1e2f3a4-0000-0000-0000-000000000010",
  "originCountry": "CA",
  "destinationCountry": "GH",
  "minWeightKg": 0.10,
  "maxWeightKg": 10.00,
  "productCode": "P",
  "markupPercent": 22.00,
  "platformFee": 5.00,
  "isActive": true,
  "createdAt": "2026-05-31T16:20:00Z"
}
```

### Response 400

```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "Markup percent cannot be negative.",
    "details": []
  }
}
```

## Update Markup Rule

```http
PUT /api/admin/markup-rules/{id}
```

Example:

```http
PUT /api/admin/markup-rules/d1e2f3a4-0000-0000-0000-000000000010
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

### Request Body

The update body uses the same shape as create.

```json
{
  "originCountry": "CA",
  "destinationCountry": "GH",
  "minWeightKg": 0.10,
  "maxWeightKg": 10.00,
  "productCode": "P",
  "markupPercent": 24.00,
  "platformFee": 6.00
}
```

### Response 200

```json
{
  "id": "d1e2f3a4-0000-0000-0000-000000000010",
  "originCountry": "CA",
  "destinationCountry": "GH",
  "minWeightKg": 0.10,
  "maxWeightKg": 10.00,
  "productCode": "P",
  "markupPercent": 24.00,
  "platformFee": 6.00,
  "isActive": true,
  "createdAt": "2026-05-31T16:20:00Z"
}
```

### Response 404

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Markup rule not found.",
    "details": []
  }
}
```

## Deactivate Markup Rule

The delete endpoint soft-deactivates a markup rule by setting `isActive` to `false`.

```http
DELETE /api/admin/markup-rules/{id}
```

Example:

```http
DELETE /api/admin/markup-rules/d1e2f3a4-0000-0000-0000-000000000010
Authorization: Bearer <admin_access_token>
```

### Request

No JSON body.

### Response 204

No response body.

### Response 404

```json
{
  "error": {
    "code": "NOT_FOUND",
    "message": "Markup rule not found.",
    "details": []
  }
}
```

## DHL Booking Failures

Use this endpoint for an operational exceptions panel. It lists recent shipment events where DHL booking failed.

```http
GET /api/admin/dhl-failures?limit=50
```

### Request

No JSON body.

Query parameters:

| Parameter | Type | Required | Default | Notes |
|---|---|---:|---|---|
| `limit` | Integer | No | `50` | Clamped from `1` to `200`. |

### Response 200

```json
[
  {
    "id": "e1e2e3e4-0000-0000-0000-000000000001",
    "shipmentId": "b1c2d3e4-0000-0000-0000-000000000001",
    "trackingNumber": null,
    "description": "DHL shipment creation failed: 422",
    "createdAt": "2026-05-29T18:35:00Z"
  },
  {
    "id": "e1e2e3e4-0000-0000-0000-000000000002",
    "shipmentId": "c2d3e4f5-0000-0000-0000-000000000002",
    "trackingNumber": "9988776655",
    "description": "DHL validation failed for customs data.",
    "createdAt": "2026-05-28T16:04:31Z"
  }
]
```

## Manual Tracking Sync

This endpoint manually triggers a DHL tracking sync for active shipments.

Active statuses checked:

```json
[
  "Booked",
  "LabelGenerated",
  "DroppedOff",
  "InTransit"
]
```

```http
POST /api/admin/sync-tracking
```

### Request

No JSON body.

Example:

```http
POST /api/admin/sync-tracking
Authorization: Bearer <admin_access_token>
```

### Response 200

```json
{
  "message": "Sync complete. 8 checked, 3 statuses updated."
}
```

### Behavior

The sync job:

1. Loads active shipments that have a tracking number.
2. Calls DHL tracking for each shipment.
3. Updates the internal shipment status when DHL returns a recognized status.
4. Inserts new tracking update events into `ShipmentEvents`.
5. Logs but skips individual DHL failures so one failed tracking lookup does not stop the whole sync.

DHL status mapping:

| DHL status | Internal status |
|---|---|
| `TRANSIT` | `InTransit` |
| `DELIVERED` | `Delivered` |
| `DELIVERY_FAILURE` | `Exception` |
| `DELIVERY_IMPOSSIBLE` | `Exception` |

## Authentication and Permission Errors

### Response 401

Returned when the bearer token is missing, expired, or invalid.

```json
{
  "error": {
    "code": "unauthorized",
    "message": "Missing or invalid token.",
    "details": []
  }
}
```

### Response 403

Returned when the user is authenticated but does not have the `Admin` role.

```json
{
  "error": {
    "code": "forbidden",
    "message": "Admin access is required.",
    "details": []
  }
}
```

Exact 401/403 response bodies can vary by ASP.NET authentication middleware, but dashboard clients should handle both statuses as auth failures.

## Recommended Admin Panel Layout

### Overview

Use `/api/admin/analytics/overview`.

Top cards:

- Total Revenue
- DHL Actually Charged
- Gross Profit
- Markup Revenue
- Platform Fees
- Gross Margin
- Paid Shipments
- Average Order Value

Charts and tables:

- Revenue / DHL cost / markup / platform fees over time from `timeSeries`.
- Top routes by revenue and gross profit from `topRoutes`.
- Shipment status distribution from `statusBreakdown`.
- Product mix from `productMix`.
- Top customers from `topCustomers`.

### Operations

Use `/api/admin/shipments`, `/api/admin/dhl-failures`, and `/api/admin/sync-tracking`.

Recommended controls:

- Status filter for shipments.
- Manual tracking sync button.
- DHL failure queue with shipment ID and tracking number.
- Per-shipment financial columns: customer amount, DHL cost, markup, platform fee, gross profit.

### Pricing

Use `/api/admin/markup-rules`.

Recommended controls:

- Route filter.
- Product code selector.
- Weight band inputs.
- Markup percent input.
- Platform fee input.
- Deactivate rule action.

### Customers

Use `/api/admin/users` and overview `customers` / `topCustomers`.

Recommended views:

- All users with role and last login.
- New customers in period.
- Active customers in period.
- Repeat customers in period.
- Top customers by revenue.

## Implementation Notes

- Analytics currency is currently reported as `CAD`.
- Time series returns daily buckets for ranges up to 92 days and monthly buckets for longer ranges.
- `grossProfit` includes both percent markup and platform fees.
- `markupRevenue` excludes platform fees.
- `totalShipments` in the analytics response is the count of revenue-bearing shipments for compatibility with the original revenue report.
- `operations.shipmentsCreated` is the count of all shipments created in the period, including pending, cancelled, and refunded shipments.
- `/api/admin/reports/revenue` and `/api/admin/analytics/overview` are backed by the same handler and return the same payload.
