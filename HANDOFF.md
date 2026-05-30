# RyveSwift API — Handoff Document

**Base URL:** `https://swift.ryvepos.com`  
**API Docs (Scalar):** `https://swift.ryvepos.com/scalar/v1`  
**OpenAPI spec:** `https://swift.ryvepos.com/openapi/v1.json`

---

## Authentication

All protected endpoints require a Bearer token in the `Authorization` header.

```
Authorization: Bearer <access_token>
```

Tokens are obtained from `POST /api/auth/login`. Access tokens expire in 60 minutes. Use `POST /api/auth/refresh` to rotate.

---

## Error Envelope

All errors use this shape:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some shipment details are invalid.",
    "details": [
      { "field": "weightKg", "message": "Must be between 0.1 and 70 kg." }
    ]
  }
}
```

`details` is omitted when there are no field-level errors.

---

## Status Codes

| Code | Meaning |
|------|---------|
| 200 | OK |
| 201 | Created |
| 400 | Validation error |
| 401 | Missing or invalid token |
| 403 | Forbidden (wrong role) |
| 404 | Resource not found |
| 409 | Conflict (e.g. quote expired) |
| 422 | Unprocessable (e.g. payment not complete, DHL hard error) |
| 429 | Rate limit exceeded |
| 502 | Upstream transient error (DHL 5xx) — retry safe |

---

## Shipment Status Values

| API value | Meaning |
|-----------|---------|
| `pending_payment` | Quote issued, awaiting payment |
| `paid` | Payment captured, DHL booking in progress |
| `label_created` | DHL label generated, ready for drop-off |
| `dropped_off` | Handed to DHL |
| `in_transit` | Package en route |
| `out_for_delivery` | With last-mile courier |
| `delivered` | Delivered |
| `exception` | DHL exception (damaged, held, etc.) |
| `cancelled` | Cancelled before booking |
| `refunded` | Payment refunded |

---

## Endpoints

### Health

#### `GET /health` — Public

```
GET /health
```

**Response 200**
```json
{
  "status": "ok",
  "timestamp": "2026-05-30T00:46:35.670Z"
}
```

---

### Auth

#### `POST /api/auth/register`

Rate-limited: 10 req/min.

**Request**
```json
{
  "email": "amina@example.com",
  "password": "SecurePass1",
  "fullName": "Amina Owusu",
  "phone": "+1-416-555-0199"
}
```

**Response 200**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "dGhpc2lzYXJlZnJlc2h0b2tlbg==",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

---

#### `POST /api/auth/login`

Rate-limited: 10 req/min.

**Request**
```json
{
  "email": "amina@example.com",
  "password": "SecurePass1"
}
```

**Response 200**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "dGhpc2lzYXJlZnJlc2h0b2tlbg==",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

---

#### `POST /api/auth/refresh`

**Request**
```json
{
  "refreshToken": "dGhpc2lzYXJlZnJlc2h0b2tlbg=="
}
```

**Response 200** — same shape as login response.

---

### Users

#### `GET /api/users/profile` — Auth required

**Response 200**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "amina@example.com",
  "phone": "+1-416-555-0199",
  "fullName": "Amina Owusu",
  "role": "Customer",
  "createdAt": "2026-05-20T14:00:00Z"
}
```

---

#### `PUT /api/users/profile` — Auth required

**Request** (all fields optional — only supplied fields are updated)
```json
{
  "fullName": "Amina Owusu-Mensah",
  "phone": "+1-416-555-0200"
}
```

**Response 200** — same shape as `GET /api/users/profile`.

---

### Addresses

#### `GET /api/addresses` — Auth required

**Response 200**
```json
[
  {
    "id": "a1b2c3d4-0000-0000-0000-000000000001",
    "contactName": "Amina Owusu",
    "companyName": null,
    "email": "amina@example.com",
    "phone": "+1-416-555-0199",
    "countryCode": "CA",
    "cityName": "Toronto",
    "postalCode": "M5V 3A1",
    "addressLine1": "200 Front Street West",
    "addressLine2": "Suite 900",
    "addressLine3": null,
    "isDefaultSender": true,
    "createdAt": "2026-05-20T14:00:00Z"
  }
]
```

---

#### `POST /api/addresses` — Auth required

**Request**
```json
{
  "contactName": "Kofi Boateng",
  "companyName": "Boateng Imports Ltd",
  "email": "kofi@boatengimports.com",
  "phone": "+233-24-555-0188",
  "countryCode": "GH",
  "cityName": "Accra",
  "postalCode": null,
  "addressLine1": "14 Independence Avenue",
  "addressLine2": null,
  "addressLine3": null,
  "isDefaultSender": false
}
```

**Response 201** — same shape as address object above.

---

#### `PUT /api/addresses/{id}` — Auth required

Same request shape as `POST /api/addresses`. **Response 200** — address object.

---

#### `DELETE /api/addresses/{id}` — Auth required

**Response 200**
```json
{ "message": "Address deleted." }
```

---

### Quotes

#### `POST /api/quotes` — Public (rate-limited: 20 req/min)

Supported routes: `CA → GH`, `CA → NG`, `US → GH`, `US → NG`.  
`shipmentType`: `"parcel"` or `"documents"`.

**Request — parcel**
```json
{
  "origin": {
    "country": "CA",
    "postalCode": "M5V 3A1",
    "city": "Toronto"
  },
  "destination": {
    "country": "GH",
    "city": "Accra"
  },
  "shipmentType": "parcel",
  "pieces": 1,
  "weightKg": 2.5,
  "dimensionsCm": {
    "length": 30,
    "width": 20,
    "height": 15
  },
  "customs": {
    "category": "Electronics",
    "declaredValue": 250.00,
    "currency": "CAD",
    "reason": "SOLD"
  }
}
```

**Response 200**
```json
{
  "quoteId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "service": "DHL Express Worldwide",
  "currency": "CAD",
  "amount": 98.50,
  "etaBusinessDays": {
    "min": 3,
    "max": 5
  },
  "expiresAt": "2026-05-31T00:46:00Z",
  "breakdown": {
    "base": 93.50,
    "fuelSurcharge": 0.00,
    "ryveFee": 5.00
  },
  "expired": false
}
```

**Request — documents**
```json
{
  "origin": { "country": "CA", "postalCode": "M5V 3A1" },
  "destination": { "country": "NG", "city": "Lagos" },
  "shipmentType": "documents",
  "pieces": 1,
  "weightKg": 0.3,
  "dimensionsCm": { "length": 32, "width": 24, "height": 2 }
}
```

**Response 200**
```json
{
  "quoteId": "9d3a1234-0000-0000-0000-abcdef012345",
  "service": "DHL Express Documents",
  "currency": "CAD",
  "amount": 52.00,
  "etaBusinessDays": { "min": 2, "max": 3 },
  "expiresAt": "2026-05-31T00:46:00Z",
  "breakdown": { "base": 47.00, "fuelSurcharge": 0.00, "ryveFee": 5.00 },
  "expired": false
}
```

---

#### `GET /api/quotes/{id}` — Auth required

**Response 200** — same shape as quote response above.

---

### Payments

#### `POST /api/payments/create-intent` — Auth required

Called after the user selects a quote. Pass the `Idempotency-Key` header to prevent duplicate intents if the request is retried.

**Headers**
```
Idempotency-Key: <uuid or any string unique to this checkout attempt>
```

**Request**
```json
{
  "quoteId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

**Response 200**
```json
{
  "clientSecret": "pi_3QoHbk...._secret_...",
  "paymentIntentId": "pi_3QoHbkAKjmn0TXYZ",
  "amount": 9850,
  "currency": "cad",
  "status": "requires_payment_method"
}
```

> `amount` is in the smallest currency unit (cents). Divide by 100 for display.  
> Pass `clientSecret` to `stripe.confirmPayment()` on the frontend.

**Error 409** — quote expired
```json
{
  "error": {
    "code": "quote_expired",
    "message": "This quote has expired. Please request a new one.",
    "details": []
  }
}
```

---

#### `GET /api/payments/{paymentIntentId}/status` — Auth required

Poll this after `stripe.confirmPayment()` resolves. The frontend should poll until `bookingStatus` is no longer `"paid"` (i.e. `"label_created"` or `"failed"`).

**Response 200**
```json
{
  "paymentIntentId": "pi_3QoHbkAKjmn0TXYZ",
  "paymentStatus": "succeeded",
  "bookingStatus": "label_created",
  "rejectionReason": null,
  "shipmentId": "b1c2d3e4-0000-0000-0000-000000000001",
  "trackingNumber": "1234567890"
}
```

`bookingStatus` mirrors the shipment status values table above.  
`rejectionReason` is populated only when `bookingStatus` is `"failed"`.

---

#### `POST /api/public/webhooks/stripe` — Stripe webhook (no auth)

Configure in Stripe dashboard: `https://swift.ryvepos.com/api/public/webhooks/stripe`

Handled events:
- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `charge.refunded`
- `charge.dispute.created`

**Response 200** `{}` (always — Stripe requires 2xx to stop retries)

---

### Bookings

#### `POST /api/bookings/confirm` — Auth required

The main booking endpoint. Call this after `stripe.confirmPayment()` succeeds. Idempotent — if a shipment already exists for the `quoteId`, the existing shipment is returned immediately.

**Request — parcel with customs**
```json
{
  "paymentIntentId": "pi_3QoHbkAKjmn0TXYZ",
  "quoteId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "senderAddressId": null,
  "receiverAddressId": "a1b2c3d4-0000-0000-0000-000000000002",
  "customsItems": [
    {
      "description": "Bluetooth Headphones",
      "quantity": 1,
      "unitOfMeasurement": "PCS",
      "unitPrice": 250.00,
      "currency": "CAD",
      "hsCode": "851830",
      "manufacturerCountry": "CN",
      "netWeightKg": 0.35,
      "grossWeightKg": 0.50
    }
  ],
  "exportReason": "SOLD",
  "invoiceNumber": "INV-2026-001",
  "invoiceDate": "2026-05-29T00:00:00Z"
}
```

> `senderAddressId`: omit or set to `null` to use the user's default sender address.  
> `customsItems`: omit for documents shipments or when customs fields were supplied in the quote.

**Response 200**
```json
{
  "shipmentId": "b1c2d3e4-0000-0000-0000-000000000001",
  "trackingNumber": "1234567890",
  "status": "label_created",
  "documents": [
    { "type": "label",   "url": "/api/shipments/b1c2d3e4-.../documents/label",   "ready": true },
    { "type": "invoice", "url": "/api/shipments/b1c2d3e4-.../documents/invoice", "ready": true },
    { "type": "waybill", "url": "/api/shipments/b1c2d3e4-.../documents/waybill", "ready": false }
  ],
  "refundId": null
}
```

**Response 422 — DHL hard error (auto-refunded)**
```json
{
  "shipmentId": "b1c2d3e4-...",
  "trackingNumber": null,
  "status": "failed",
  "documents": [],
  "refundId": "re_3QoHbkAKjmn0WXYZ"
}
```

**Response 502** — DHL transient error. Payment is preserved; the frontend should retry `POST /api/bookings/confirm` with the same body.

---

### Shipments

#### `GET /api/shipments` — Auth required

**Response 200**
```json
{
  "shipments": [
    {
      "id": "b1c2d3e4-0000-0000-0000-000000000001",
      "createdAt": "2026-05-29T18:30:00Z",
      "service": "DHL Express Worldwide",
      "route": "CA → GH",
      "weightKg": 2.5,
      "amount": 98.50,
      "currency": "CAD",
      "status": "label_created",
      "trackingNumber": "1234567890"
    }
  ]
}
```

---

#### `GET /api/shipments/{id}` — Auth required

**Response 200**
```json
{
  "id": "b1c2d3e4-0000-0000-0000-000000000001",
  "status": "label_created",
  "trackingNumber": "1234567890",
  "documents": [
    { "type": "label",   "url": "/api/shipments/b1c2d3e4-.../documents/label",   "ready": true },
    { "type": "invoice", "url": "/api/shipments/b1c2d3e4-.../documents/invoice", "ready": true },
    { "type": "waybill", "url": "/api/shipments/b1c2d3e4-.../documents/waybill", "ready": false }
  ],
  "sender": {
    "id": "a1b2c3d4-0000-0000-0000-000000000001",
    "contactName": "Amina Owusu",
    "phone": "+1-416-555-0199",
    "countryCode": "CA",
    "cityName": "Toronto",
    "postalCode": "M5V 3A1",
    "addressLine1": "200 Front Street West",
    "isDefaultSender": true,
    "createdAt": "2026-05-20T14:00:00Z"
  },
  "receiver": {
    "id": "a1b2c3d4-0000-0000-0000-000000000002",
    "contactName": "Kofi Boateng",
    "phone": "+233-24-555-0188",
    "countryCode": "GH",
    "cityName": "Accra",
    "addressLine1": "14 Independence Avenue",
    "isDefaultSender": false,
    "createdAt": "2026-05-20T14:00:00Z"
  },
  "customs": [
    {
      "id": "c1d2e3f4-0000-0000-0000-000000000001",
      "description": "Bluetooth Headphones",
      "quantity": 1,
      "unitOfMeasurement": "PCS",
      "unitPrice": 250.00,
      "currency": "CAD",
      "hsCode": "851830",
      "manufacturerCountry": "CN",
      "netWeightKg": 0.35,
      "grossWeightKg": 0.50
    }
  ],
  "payment": {
    "status": "succeeded",
    "paymentIntentId": "pi_3QoHbkAKjmn0TXYZ"
  },
  "totalAmount": 98.50,
  "currency": "CAD",
  "createdAt": "2026-05-29T18:30:00Z"
}
```

---

#### `GET /api/shipments/{id}/documents/{type}` — Auth required

`type` must be one of: `label`, `invoice`, `waybill`.

Returns the PDF file directly with `Content-Type: application/pdf`.

**Response 200** — PDF binary stream  
**Response 404** — document not yet generated

---

### Tracking

#### `GET /api/track/{trackingNumber}` — Public

**Response 200**
```json
{
  "trackingNumber": "1234567890",
  "status": "TransitEvent",
  "estimatedDelivery": "2026-06-03T18:00:00Z",
  "events": [
    {
      "timestamp": "2026-05-30T08:15:00Z",
      "location": "Toronto",
      "description": "Shipment picked up"
    },
    {
      "timestamp": "2026-05-30T22:40:00Z",
      "location": "Frankfurt",
      "description": "Arrived at DHL hub"
    }
  ]
}
```

---

### Admin

All admin endpoints require `Role: Admin` on the JWT.

#### `GET /api/admin/shipments?page=1&pageSize=50&status=label_created`

**Response 200**
```json
{
  "items": [
    {
      "id": "b1c2d3e4-...",
      "userId": "3fa85f64-...",
      "userEmail": "amina@example.com",
      "trackingNumber": "1234567890",
      "status": "label_created",
      "originCountry": "CA",
      "destinationCountry": "GH",
      "totalAmount": 98.50,
      "currency": "CAD",
      "createdAt": "2026-05-29T18:30:00Z"
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

---

#### `GET /api/admin/users?page=1&pageSize=50`

**Response 200**
```json
{
  "items": [
    {
      "id": "3fa85f64-...",
      "email": "amina@example.com",
      "fullName": "Amina Owusu",
      "phone": "+1-416-555-0199",
      "role": "Customer",
      "createdAt": "2026-05-20T14:00:00Z"
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

---

#### `GET /api/admin/reports/revenue?from=2026-05-01&to=2026-05-31`

**Response 200**
```json
{
  "from": "2026-05-01T00:00:00Z",
  "to": "2026-05-31T00:00:00Z",
  "totalRevenue": 1240.00,
  "totalShipments": 12,
  "currency": "CAD"
}
```

---

#### `GET /api/admin/markup-rules`

**Response 200**
```json
[
  {
    "id": "d1e2f3a4-...",
    "originCountry": null,
    "destinationCountry": null,
    "productCode": null,
    "minWeightKg": null,
    "maxWeightKg": null,
    "markupPercent": 20.0,
    "platformFee": 5.00,
    "priority": 0,
    "isActive": true,
    "createdAt": "2026-05-20T14:00:00Z"
  }
]
```

---

#### `POST /api/admin/markup-rules`

**Request**
```json
{
  "originCountry": "CA",
  "destinationCountry": "GH",
  "productCode": "P",
  "minWeightKg": null,
  "maxWeightKg": null,
  "markupPercent": 22.0,
  "platformFee": 5.00,
  "priority": 10,
  "isActive": true
}
```

**Response 201** — rule object (same shape as list item above).

---

#### `PUT /api/admin/markup-rules/{id}`

Same request shape as POST. **Response 200** — updated rule object.

---

#### `DELETE /api/admin/markup-rules/{id}`

**Response 200**
```json
{ "message": "Markup rule deactivated." }
```

---

#### `GET /api/admin/dhl-failures`

**Response 200**
```json
[
  {
    "shipmentId": "b1c2d3e4-...",
    "eventType": "DhlBookingFailed",
    "description": "DHL shipment creation failed: 422",
    "createdAt": "2026-05-29T18:35:00Z"
  }
]
```

---

#### `POST /api/admin/sync-tracking`

Triggers an immediate DHL tracking sync for all active shipments.

**Response 200**
```json
{ "updated": 3, "total": 8 }
```

---

## Booking Flow

```
1.  POST /api/quotes                       → quoteId, amount, expiresAt
2.  POST /api/payments/create-intent       → clientSecret, paymentIntentId
3.  stripe.confirmPayment(clientSecret)    → frontend SDK (Stripe.js)
4.  POST /api/bookings/confirm             → shipmentId, trackingNumber, documents
5.  GET  /api/payments/{id}/status         → poll for bookingStatus = "label_created"
6.  GET  /api/shipments/{id}/documents/label → download label PDF
```

---

## Rate Limits

| Scope | Limit |
|-------|-------|
| `/api/auth/*` | 10 req / min |
| `POST /api/quotes` | 20 req / min |
| All other endpoints | 100 req / min |

Exceeding returns **429 Too Many Requests**.

---

## Infrastructure

| Property | Value |
|----------|-------|
| Server | DigitalOcean `ryveserve-prod` (138.197.132.118) |
| Process | systemd `ryveswift.service` (user: `ryveswift`) |
| App dir | `/opt/ryveswift` |
| Env file | `/etc/ryveswift/env` (mode 600) |
| Port | `127.0.0.1:5191` |
| Public access | Cloudflare Tunnel `3bb0c7af-c9e3-473a-8740-eca8085166c7` |
| Database | PostgreSQL `ryveswift` @ `127.0.0.1:5432` |
| Runtime | .NET 10, `ASPNETCORE_ENVIRONMENT=Production` |

**Operational commands (on server):**
```bash
sudo systemctl restart ryveswift
sudo systemctl status ryveswift --no-pager
journalctl -u ryveswift -n 200 --no-pager
journalctl -u ryveswift -f
curl -fsS http://127.0.0.1:5191/health
```

---

## Test Accounts (Production)

| Role | Email | Password |
|------|-------|----------|
| Customer | `testuser@ryveswift.com` | `TestUser1!` |
| Admin | `testadmin@ryveswift.com` | `TestAdmin1!` |
