# RyvePool Optional DHL-Point Delivery Frontend Handoff

This note covers the new integrated RyvePool delivery add-on inside the RyveSend DHL shipment flow.

Frontend domain:

```text
https://ryvesend.com
```

Backend API base:

```text
https://swift.ryvepos.com
```

## 1. What Changed

The optional RyvePool pickup/delivery cost is now a backend-persisted quote add-on.

Frontend must not add the RyvePool fee only in browser state. The backend now owns the final payable quote amount so Stripe, booking, and dispatch all agree.

Implemented backend behavior:

- User can create the normal DHL quote.
- User can optionally attach RyvePool pickup/delivery to the quote.
- Backend calls RyvePool quote, extracts the delivery fee, and adds it to `quote.amount`.
- Backend returns a clear quote breakdown with `deliveryFee`.
- Once payment starts, the quote is locked and the delivery option cannot be changed.
- On successful payment and DHL label creation:
  - `dispatchTiming: "immediate"` creates the RyvePool dispatch right away.
  - `dispatchTiming: "scheduled"` stores the local delivery and the background worker dispatches it later.
- Booking and shipment detail responses include `orderDelivery` when a RyvePool delivery add-on exists.

## 2. Important UI Rule

The UI should show three separate money lines:

```text
DHL shipping
RyveSend fee
Optional pickup to DHL point
Final total
```

Use `quote.amount` as the final payable amount.

Do not calculate Stripe amount on the frontend.

## 3. DHL Point Selection Requirement

The backend now includes a Google Places-backed DHL point finder:

```text
GET /api/locations/dhl-points
```

See:

```text
GOOGLE_MAPS_DHL_POINTS_FRONTEND_HANDOFF.md
```

Frontend must provide the selected DHL point details when attaching the delivery option:

- DHL point ID or name
- DHL point display address
- latitude
- longitude
- contact phone if available

Coordinates are required because the backend must price the RyvePool courier leg.

## 4. Customer Flow

1. User enters normal DHL shipment details.
2. Frontend calls `POST /api/quotes`.
3. Frontend shows DHL quote without delivery.
4. User toggles optional RyvePool pickup/delivery.
5. User selects nearest DHL point.
6. Frontend calls `POST /api/quotes/{quoteId}/delivery-option`.
7. Frontend replaces the displayed quote with the returned quote.
8. User pays using `POST /api/payments/create-intent`.
9. User confirms booking using `POST /api/bookings/confirm`.
10. Frontend reads `orderDelivery` from the booking response.

## 5. Create Normal DHL Quote

```http
POST /api/quotes
Content-Type: application/json
```

Sample request:

```json
{
  "origin": {
    "country": "CA",
    "postalCode": "K1A 0B1",
    "city": "Ottawa"
  },
  "destination": {
    "country": "GH",
    "postalCode": "GA-184-8164",
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
    "category": "General Goods",
    "declaredValue": 120,
    "currency": "CAD",
    "reason": "sale"
  },
  "incoterm": "DAP"
}
```

Sample response without RyvePool delivery:

```json
{
  "quoteId": "4dcebe26-29b3-4c32-b0a7-661be4212b37",
  "service": "DHL Express Worldwide",
  "currency": "CAD",
  "amount": 83.45,
  "etaBusinessDays": {
    "min": 3,
    "max": 5
  },
  "expiresAt": "2026-06-16T21:30:00Z",
  "breakdown": {
    "base": 78.45,
    "fuelSurcharge": 0,
    "ryveFee": 5,
    "deliveryFee": 0,
    "total": 83.45
  },
  "deliveryOption": null,
  "expired": false
}
```

## 6. Attach Optional RyvePool Delivery

```http
POST /api/quotes/{quoteId}/delivery-option
Authorization: Bearer <access_token>
Content-Type: application/json
```

Use this only before creating the payment intent.

Sample request for immediate dispatch after payment:

```json
{
  "enabled": true,
  "pickup": {
    "name": "Jane Sender",
    "phone": "+16135550111",
    "address": "111 Wellington St, Ottawa, ON",
    "landmark": "Main entrance",
    "lat": 45.4248,
    "lng": -75.6996,
    "email": "jane@example.com"
  },
  "dropoff": {
    "name": "DHL Service Point - Ottawa",
    "phone": "+18002255345",
    "address": "275 Slater St, Ottawa, ON",
    "landmark": "DHL counter",
    "lat": 45.4207,
    "lng": -75.7021,
    "email": null
  },
  "dispatchTiming": "immediate",
  "scheduledFor": null,
  "dhlPointId": "DHL-OTTAWA-SLATER",
  "dhlPointName": "DHL Service Point - Ottawa Slater",
  "externalBranchId": null,
  "dispatchMode": "ryvepool_marketplace",
  "packageType": "parcel",
  "weightKg": 2.5,
  "regionCode": "CA-ON",
  "vehicleCategoryId": null,
  "driverInstructions": "Pick up the sealed parcel and deliver it to the DHL counter."
}
```

Sample response:

```json
{
  "quoteId": "4dcebe26-29b3-4c32-b0a7-661be4212b37",
  "service": "DHL Express Worldwide",
  "currency": "CAD",
  "amount": 90.27,
  "etaBusinessDays": {
    "min": 3,
    "max": 5
  },
  "expiresAt": "2026-06-16T21:30:00Z",
  "breakdown": {
    "base": 78.45,
    "fuelSurcharge": 0,
    "ryveFee": 5,
    "deliveryFee": 6.82,
    "total": 90.27
  },
  "deliveryOption": {
    "enabled": true,
    "status": "quoted",
    "dispatchTiming": "immediate",
    "scheduledFor": null,
    "currency": "CAD",
    "feeMinor": 682,
    "feeAmount": 6.82,
    "packageType": "parcel",
    "regionCode": "CA-ON",
    "dispatchMode": "ryvepool_marketplace",
    "pickup": {
      "id": null,
      "name": "Jane Sender",
      "address": "111 Wellington St, Ottawa, ON",
      "lat": 45.4248,
      "lng": -75.6996
    },
    "dropoff": {
      "id": "DHL-OTTAWA-SLATER",
      "name": "DHL Service Point - Ottawa Slater",
      "address": "275 Slater St, Ottawa, ON",
      "lat": 45.4207,
      "lng": -75.7021
    }
  },
  "expired": false
}
```

## 7. Schedule RyvePool Delivery For Later

Use the same endpoint, but set:

```json
{
  "enabled": true,
  "dispatchTiming": "scheduled",
  "scheduledFor": "2026-06-17T14:00:00Z"
}
```

The returned quote still includes the delivery fee before payment.

After payment and DHL booking, backend creates a local delivery with:

```json
{
  "status": "scheduled",
  "dispatchTiming": "scheduled",
  "scheduledForUtc": "2026-06-17T14:00:00Z",
  "ryvePoolDispatchId": null
}
```

The background worker dispatches due scheduled deliveries automatically when:

- `RYVEPOOL_SCHEDULED_DISPATCH_ENABLED=true`
- the delivery `scheduledForUtc` time has passed
- the delivery belongs to the currently active RyvePool environment

## 8. Remove Optional Delivery Before Payment

```http
POST /api/quotes/{quoteId}/delivery-option
Authorization: Bearer <access_token>
Content-Type: application/json
```

Request:

```json
{
  "enabled": false,
  "pickup": null,
  "dropoff": null,
  "dispatchTiming": null,
  "scheduledFor": null,
  "dhlPointId": null,
  "dhlPointName": null,
  "externalBranchId": null,
  "dispatchMode": null,
  "packageType": null,
  "weightKg": null,
  "regionCode": null,
  "vehicleCategoryId": null,
  "driverInstructions": null
}
```

Response returns the quote with:

```json
{
  "breakdown": {
    "deliveryFee": 0
  },
  "deliveryOption": null
}
```

## 9. Payment Intent

```http
POST /api/payments/create-intent
Authorization: Bearer <access_token>
Content-Type: application/json
Idempotency-Key: checkout-4dcebe26-29b3
```

Request:

```json
{
  "quoteId": "4dcebe26-29b3-4c32-b0a7-661be4212b37"
}
```

Response:

```json
{
  "clientSecret": "pi_123_secret_abc",
  "paymentIntentId": "pi_123",
  "amount": 9027,
  "currency": "cad",
  "status": "requires_payment_method"
}
```

The `amount` is in minor units and must match `quote.amount * 100`.

After this point, the quote is locked.

## 10. Booking Confirm

```http
POST /api/bookings/confirm
Authorization: Bearer <access_token>
Content-Type: application/json
```

Request:

```json
{
  "paymentIntentId": "pi_123",
  "quoteId": "4dcebe26-29b3-4c32-b0a7-661be4212b37",
  "senderAddressId": "7f0c26aa-eed4-4faa-8856-e097b9699642",
  "receiverAddressId": "0e31cfe1-364c-4f3a-87c2-49ec74c9198d",
  "customsItems": [
    {
      "description": "Cotton shirts",
      "quantity": 2,
      "unitOfMeasurement": "PCS",
      "unitPrice": 60,
      "currency": "CAD",
      "hsCode": "610910",
      "manufacturerCountry": "CA",
      "netWeightKg": 1.2,
      "grossWeightKg": 1.25
    }
  ],
  "exportReason": "sale",
  "invoiceNumber": "INV-1001",
  "invoiceDate": "2026-06-15T00:00:00Z",
  "incoterm": "DAP"
}
```

Response when immediate dispatch succeeds:

```json
{
  "shipmentId": "64736141-b7f0-47ee-86da-df316cb35c16",
  "trackingNumber": "1234567890",
  "status": "label_created",
  "documents": [
    {
      "type": "label",
      "url": "/api/shipments/64736141-b7f0-47ee-86da-df316cb35c16/documents/label",
      "ready": true
    },
    {
      "type": "invoice",
      "url": "/api/shipments/64736141-b7f0-47ee-86da-df316cb35c16/documents/invoice",
      "ready": true
    },
    {
      "type": "waybill",
      "url": "/api/shipments/64736141-b7f0-47ee-86da-df316cb35c16/documents/waybill",
      "ready": true
    }
  ],
  "orderDelivery": {
    "id": "493ae63d-c4f1-4058-b778-b3487ed9fe19",
    "quoteId": "4dcebe26-29b3-4c32-b0a7-661be4212b37",
    "shipmentId": "64736141-b7f0-47ee-86da-df316cb35c16",
    "environment": "production",
    "externalOrderId": "ryvesend-shipment-64736141b7f047ee86dadf316cb35c16",
    "ryvePoolDispatchId": "rpd_01jzabcd1234",
    "status": "created",
    "trackingUrl": "https://track.ryvepool.com/rpd_01jzabcd1234",
    "regionCode": "CA-ON",
    "externalBranchId": null,
    "dispatchModeUsed": "ryvepool_marketplace",
    "paymentType": "prepaid",
    "codAmountMinor": 0,
    "packageType": "parcel",
    "currency": "CAD",
    "deliveryFeeMinor": 682,
    "platformFeeMinor": 0,
    "ryvePoolCommissionMinor": 0,
    "driverPayoutMinor": 0,
    "canCancel": true,
    "cancellableUntil": "2026-06-15T22:00:00Z",
    "dispatchTiming": "immediate",
    "scheduledForUtc": null,
    "dispatchAttemptCount": 1,
    "lastDispatchAttemptAt": "2026-06-15T21:41:12Z",
    "lastDispatchError": null,
    "dhlPointId": "DHL-OTTAWA-SLATER",
    "dhlPointName": "DHL Service Point - Ottawa Slater",
    "createdAt": "2026-06-15T21:41:12Z",
    "updatedAt": "2026-06-15T21:41:13Z"
  },
  "refundId": null
}
```

Response when scheduled:

```json
{
  "shipmentId": "64736141-b7f0-47ee-86da-df316cb35c16",
  "trackingNumber": "1234567890",
  "status": "label_created",
  "documents": [],
  "orderDelivery": {
    "id": "493ae63d-c4f1-4058-b778-b3487ed9fe19",
    "status": "scheduled",
    "ryvePoolDispatchId": null,
    "dispatchTiming": "scheduled",
    "scheduledForUtc": "2026-06-17T14:00:00Z",
    "deliveryFeeMinor": 682,
    "lastDispatchError": null
  },
  "refundId": null
}
```

## 11. Refresh Shipment Detail

```http
GET /api/shipments/{shipmentId}
Authorization: Bearer <access_token>
```

Shipment detail now includes:

```json
{
  "id": "64736141-b7f0-47ee-86da-df316cb35c16",
  "status": "label_created",
  "trackingNumber": "1234567890",
  "totalAmount": 90.27,
  "currency": "CAD",
  "orderDelivery": {
    "id": "493ae63d-c4f1-4058-b778-b3487ed9fe19",
    "status": "scheduled",
    "dispatchTiming": "scheduled",
    "scheduledForUtc": "2026-06-17T14:00:00Z"
  }
}
```

## 12. Manual Dispatch Or Retry

Use this when:

- the delivery was scheduled and the user/admin wants to send it now
- dispatch failed after booking and support wants to retry

```http
POST /api/order-deliveries/dispatches/{orderDeliveryId}/dispatch
Authorization: Bearer <access_token>
```

Success response is the local `orderDelivery` object.

Failure response:

```json
{
  "error": {
    "code": "ryvepool_dispatch_failed",
    "message": "RyvePool request failed with HTTP 422.",
    "details": []
  }
}
```

Then call:

```http
GET /api/order-deliveries/dispatches/{orderDeliveryId}
Authorization: Bearer <access_token>
```

to display `lastDispatchError`.

## 13. Status Labels

Recommended labels:

```text
quoted -> Delivery price added
scheduled -> Pickup scheduled
dispatch_pending -> Preparing courier dispatch
dispatching -> Dispatching courier
created -> Delivery requested
searching_driver -> Finding courier
assigned -> Courier assigned
picked_up -> Picked up
en_route -> On the way to DHL point
delivered -> Delivered to DHL point
dispatch_failed -> Courier dispatch failed
cancelled -> Cancelled
failed -> Failed
```

## 14. Error Handling

Validation error shape:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some delivery option details are invalid.",
    "details": [
      {
        "field": "dropoff.coordinates",
        "message": "Latitude and longitude are required to price the RyvePool delivery option."
      }
    ]
  }
}
```

Known local errors:

| HTTP | Code | Message |
|---|---|---|
| 400 | `validation_failed` | `Some delivery option details are invalid.` |
| 404 | `not_found` | `Quote not found.` |
| 409 | `quote_expired` | `This quote has expired. Please request a new one.` |
| 409 | `quote_locked` | `This quote already has a payment in progress and can no longer be changed.` |
| 422 | `unsupported_package_type` | `Food delivery is not enabled for this integration.` |
| 422 | `unsupported_package_type` | `Package type must be parcel, document, or fragile.` |
| 422 | `unsupported_dispatch_mode` | `Dispatch mode must be own_fleet, ryvepool_marketplace, or overflow.` |
| 422 | `unsupported_dispatch_timing` | `Dispatch timing must be immediate or scheduled.` |
| 422 | `delivery_currency_mismatch` | `RyvePool returned <currency>, but the quote currency is <currency>.` |
| 502 | `ryvepool_quote_unpriced` | `RyvePool quote did not include a payable delivery total.` |
| 502 | `ryvepool_dispatch_failed` | Upstream RyvePool dispatch error message |
| 503 | `ryvepool_disabled` | `RyvePool order delivery is not enabled.` |
| 503 | `ryvepool_credentials_missing` | `RyvePool credentials are not configured for the active environment.` |

Field-level messages:

| Field | Message |
|---|---|
| `pickup` | `pickup is required.` |
| `pickup.name` | `Name is required.` |
| `pickup.phone` | `Phone is required.` |
| `pickup.coordinates` | `Latitude and longitude are required to price the RyvePool delivery option.` |
| `dropoff` | `dropoff is required.` |
| `dropoff.name` | `Name is required.` |
| `dropoff.phone` | `Phone is required.` |
| `dropoff.coordinates` | `Latitude and longitude are required to price the RyvePool delivery option.` |
| `dhlPoint` | `DHL point ID or name is required for RyvePool dropoff.` |
| `scheduledFor` | `Scheduled dispatch time is required when dispatchTiming is scheduled.` |
| `scheduledFor` | `Scheduled dispatch time must be in the future.` |
| `weightKg` | `Delivery weight must be greater than 0.` |

Payment/booking errors that matter for this flow:

| HTTP | Code | Message |
|---|---|---|
| 409 | `quote_payment_amount_locked` | `This quote already has a payment intent with a different amount. Please request a new quote.` |
| 422 | `payment_not_found` | `PaymentIntent not found.` |
| 422 | `payment_not_complete` | `PaymentIntent status is '<status>', expected 'succeeded'.` |
| 422 | `amount_mismatch` | `PaymentIntent amount does not match quote amount.` |

## 15. Frontend Implementation Checklist

- Add an optional “Pickup and deliver to DHL point” toggle after DHL quote.
- Do not show this option until a quote exists.
- Require the user to select a DHL point before enabling the option.
- Require pickup and DHL point coordinates.
- Call `/api/quotes/{quoteId}/delivery-option` when the toggle or schedule changes.
- Replace the local quote state with the endpoint response.
- Disable edits to delivery option after payment intent creation.
- Show `breakdown.deliveryFee` and `deliveryOption.feeAmount`.
- Use `quote.amount` for payment display.
- Never send `food` as package type.
- Use `dispatchTiming: "immediate"` for “send courier after payment”.
- Use `dispatchTiming: "scheduled"` plus `scheduledFor` for later pickup.
- On booking success, store `shipmentId` and `orderDelivery.id`.
- On refresh, call `/api/shipments/{shipmentId}` and read `orderDelivery`.
- If `orderDelivery.status` is `dispatch_failed`, show support/retry messaging.

## 16. Admin Notes

Admin config response now includes:

```json
{
  "enabled": true,
  "environment": "production",
  "baseUrl": "https://api.ryvepool.com/v1",
  "timeoutSeconds": 20,
  "defaultRegionCode": "CA-ON",
  "defaultExternalBranchId": null,
  "defaultDispatchMode": "ryvepool_marketplace",
  "defaultPackageType": "parcel",
  "webhookSignatureRequired": true,
  "scheduledDispatchEnabled": true,
  "scheduledDispatchIntervalSeconds": 60,
  "test": {
    "publicKey": "pk_test...abcd",
    "secretConfigured": true,
    "webhookSecretConfigured": true
  },
  "production": {
    "publicKey": "pk_live...wxyz",
    "secretConfigured": true,
    "webhookSecretConfigured": true
  }
}
```

Admin can list deliveries:

```http
GET /api/admin/ryvepool/deliveries?status=scheduled
Authorization: Bearer <admin_access_token>
```

Delivery records include:

- `quoteId`
- `shipmentId`
- `dispatchTiming`
- `scheduledForUtc`
- `dispatchAttemptCount`
- `lastDispatchAttemptAt`
- `lastDispatchError`
- `dhlPointId`
- `dhlPointName`

## 17. Do Not Do This

Do not:

- Add delivery cost only in frontend state.
- Create Stripe payment before attaching the selected delivery option.
- Let users edit the delivery option after `/api/payments/create-intent`.
- Dispatch RyvePool from the browser directly.
- Send RyvePool API credentials to the frontend.
- Send package type `food`.
