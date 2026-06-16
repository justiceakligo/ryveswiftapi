# RyvePool Order Delivery Frontend and Admin Integration Guide

This guide explains how the frontend should integrate the RyvePool order delivery feature in RyveSend, including the customer-facing delivery flow and the admin management flow.

This integration is for order delivery only. The food delivery side is intentionally not enabled.

## 1. Scope

Implemented backend scope:

- Get RyvePool delivery quotes.
- Create RyvePool dispatches.
- Store local dispatch records.
- Read local dispatch records.
- Read live courier status from RyvePool.
- Update recipient details before pickup.
- Cancel dispatches when allowed.
- Receive RyvePool webhooks.
- Let admins configure test/production credentials and active environment.
- Let admins run test quote/webhook checks.
- Let admins view reports, webhook logs, and local delivery records.

Explicitly out of scope:

- Food delivery UI.
- Sending `packageType: "food"`.
- Exposing RyvePool API secrets to the browser.
- Direct frontend calls to `https://api.ryvepool.com/v1`.

All RyvePool calls must go through the RyveSend backend.

## 2. Base URLs

Production RyveSend API:

```text
https://swift.ryvepos.com
```

RyvePool upstream API configured in backend:

```text
https://api.ryvepool.com/v1
```

Frontend must call only RyveSend backend endpoints:

```text
https://swift.ryvepos.com/api/order-deliveries/...
https://swift.ryvepos.com/api/admin/ryvepool/...
```

## 3. Authentication

Customer/user endpoints:

| Endpoint | Auth |
|---|---|
| `POST /api/order-deliveries/quotes` | Public |
| All other `/api/order-deliveries/*` endpoints | User bearer token required |

Admin endpoints:

```text
Authorization: Bearer <admin_access_token>
```

The user must have role `Admin`.

RyvePool API credentials:

- Stored only in backend `AppConfigs`.
- Managed through admin endpoints.
- Never returned in full by config responses.
- Never sent to the frontend.

## 4. Shared Error Envelope

All local backend errors use this shape:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "externalOrderId",
        "message": "External order ID is required."
      }
    ]
  }
}
```

`details` is an empty array when there are no field-level errors.

Auth failures may return only HTTP `401` or `403` without this JSON body, depending on where the request is rejected.

## 5. Important Frontend Rules

Use these package types only:

```text
parcel
document
fragile
```

Do not send:

```text
food
```

If the frontend sends `food`, the backend returns:

```json
{
  "error": {
    "code": "unsupported_package_type",
    "message": "Food delivery is not enabled for this integration.",
    "details": []
  }
}
```

Use minor units for money values returned by RyvePool:

```text
682 = CAD $6.82
```

Use `externalOrderId` as the idempotency key. It should be the RyveSend/RyveSwift order ID or another stable order identifier.

## 6. Dispatch Lifecycle

Expected RyvePool statuses:

```text
created
searching_driver
assigned
picked_up
en_route
delivered
cancelled
failed
```

Recommended frontend status labels:

| Backend status | UI label |
|---|---|
| `created` | Delivery created |
| `searching_driver` | Finding courier |
| `assigned` | Courier assigned |
| `picked_up` | Picked up |
| `en_route` | On the way |
| `delivered` | Delivered |
| `cancelled` | Cancelled |
| `failed` | Delivery failed |

Cancellation is normally allowed only before pickup and only while `canCancel` is true.

## 7. Customer Flow

Recommended customer flow:

1. User enters pickup/dropoff details.
2. Frontend gets a quote with `POST /api/order-deliveries/quotes`.
3. User confirms delivery.
4. Frontend creates a dispatch with `POST /api/order-deliveries/dispatches`.
5. Frontend stores the returned local `id`.
6. Frontend shows status from local dispatch response.
7. Frontend can poll `GET /api/order-deliveries/dispatches/{id}/live` every 15-30 seconds if live courier tracking is shown.
8. Backend updates local status via webhook events.

## 8. Customer Endpoint: Get Quote

```text
POST /api/order-deliveries/quotes
Auth: public
```

Use this before creating a dispatch.

### Request

Coordinates are preferred. City fallback is allowed when coordinates are not available.

```json
{
  "pickupLat": 45.4215,
  "pickupLng": -75.6972,
  "dropoffLat": 45.4291,
  "dropoffLng": -75.6878,
  "pickupCity": "Ottawa",
  "dropoffCity": "Ottawa",
  "packageType": "parcel",
  "weightKg": 2.5,
  "regionCode": "CA-ON",
  "vehicleCategoryId": null
}
```

### Minimal Request With City Fallback

```json
{
  "pickupCity": "Ottawa",
  "dropoffCity": "Ottawa",
  "packageType": "parcel",
  "weightKg": 2.5,
  "regionCode": "CA-ON"
}
```

### Success Response

The backend returns the RyvePool quote response as-is.

```json
{
  "estimatedKm": 1.82,
  "pricingMode": "calculated",
  "currency": "CAD",
  "breakdown": {
    "baseMinor": 500,
    "kmChargeMinor": 182,
    "weightChargeMinor": 0,
    "distanceSurchargeMinor": 0,
    "packageSurchargeMinor": 0,
    "totalMinor": 682
  },
  "vehicleCategory": {
    "id": "cat_bicycle",
    "slug": "bicycle"
  }
}
```

### Quote Validation Errors

Pickup missing:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some quote details are invalid.",
    "details": [
      {
        "field": "pickup",
        "message": "Pickup coordinates or pickup city are required."
      }
    ]
  }
}
```

Dropoff missing:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some quote details are invalid.",
    "details": [
      {
        "field": "dropoff",
        "message": "Dropoff coordinates or dropoff city are required."
      }
    ]
  }
}
```

Invalid weight:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some quote details are invalid.",
    "details": [
      {
        "field": "weightKg",
        "message": "Weight must be greater than 0."
      }
    ]
  }
}
```

Food package blocked:

```json
{
  "error": {
    "code": "unsupported_package_type",
    "message": "Food delivery is not enabled for this integration.",
    "details": []
  }
}
```

Integration disabled:

```json
{
  "error": {
    "code": "ryvepool_disabled",
    "message": "RyvePool order delivery is not enabled.",
    "details": []
  }
}
```

## 9. Customer Endpoint: Create Dispatch

```text
POST /api/order-deliveries/dispatches
Auth: user
```

Creates a RyvePool dispatch and stores a local delivery record.

### Request

```json
{
  "externalOrderId": "ryvesend-order-10001",
  "merchantReference": "RS-10001",
  "externalBranchId": "ottawa-main-branch",
  "dispatchMode": "ryvepool_marketplace",
  "pickup": {
    "name": "RyveSend Warehouse",
    "phone": "+16135551234",
    "address": "145 Bank St, Ottawa ON K1A 0A6",
    "landmark": "Loading door at rear",
    "lat": 45.4215,
    "lng": -75.6972
  },
  "dropoff": {
    "name": "Alice Customer",
    "phone": "+16135559876",
    "email": "alice@example.com",
    "address": "200 Rideau St, Ottawa ON K1N 5Y1",
    "landmark": "Apt 4B, buzzer 412",
    "lat": 45.4291,
    "lng": -75.6878
  },
  "paymentType": "prepaid",
  "codAmountMinor": 0,
  "packageType": "parcel",
  "parcelWeightKg": 2.5,
  "driverInstructions": "Call recipient on arrival.",
  "metadata": {
    "platform": "ryvesend",
    "orderType": "parcel_delivery"
  }
}
```

### Valid `dispatchMode`

```text
own_fleet
ryvepool_marketplace
overflow
```

If omitted, backend uses admin default.

### Valid `paymentType`

```text
prepaid
cod
```

If `paymentType` is `cod`, send `codAmountMinor > 0`.

### Success Response

The response is the local RyveSend delivery record, not the full upstream RyvePool object.

```json
{
  "id": "df2bb97f-7be2-46bf-bf72-14f2b652dd7b",
  "environment": "test",
  "externalOrderId": "ryvesend-order-10001",
  "ryvePoolDispatchId": "disp_01jzf9k3m7n8p2q4r5s6t7u8v9",
  "status": "created",
  "trackingUrl": "https://api.ryvepool.com/v1/track/abc123token",
  "regionCode": "CA-ON",
  "externalBranchId": "ottawa-main-branch",
  "dispatchModeUsed": "ryvepool_marketplace",
  "paymentType": "prepaid",
  "codAmountMinor": 0,
  "packageType": "parcel",
  "currency": "CAD",
  "deliveryFeeMinor": 682,
  "platformFeeMinor": 50,
  "ryvePoolCommissionMinor": 50,
  "driverPayoutMinor": 632,
  "canCancel": true,
  "cancellableUntil": "2026-06-15T14:32:00Z",
  "createdAt": "2026-06-15T14:22:00Z",
  "updatedAt": "2026-06-15T14:22:00Z"
}
```

### Idempotency Behavior

Calling create again with the same `externalOrderId` in the same active environment returns the existing local delivery if it belongs to the same user.

If the same `externalOrderId` belongs to another user:

```json
{
  "error": {
    "code": "duplicate_external_order_id",
    "message": "This external order ID already has a RyvePool dispatch.",
    "details": []
  }
}
```

### Create Dispatch Validation Errors

Missing external order ID:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "externalOrderId",
        "message": "External order ID is required."
      }
    ]
  }
}
```

Missing pickup object:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "pickup",
        "message": "pickup is required."
      }
    ]
  }
}
```

Missing pickup name:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "pickup.name",
        "message": "Name is required."
      }
    ]
  }
}
```

Missing pickup phone:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "pickup.phone",
        "message": "Phone is required."
      }
    ]
  }
}
```

Missing pickup address when no coordinates:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "pickup.address",
        "message": "Address is required when coordinates are not provided."
      }
    ]
  }
}
```

Missing dropoff object:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "dropoff",
        "message": "dropoff is required."
      }
    ]
  }
}
```

Missing dropoff name:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "dropoff.name",
        "message": "Name is required."
      }
    ]
  }
}
```

Missing dropoff phone:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "dropoff.phone",
        "message": "Phone is required."
      }
    ]
  }
}
```

Missing dropoff address when no coordinates:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "dropoff.address",
        "message": "Address is required when coordinates are not provided."
      }
    ]
  }
}
```

COD amount missing:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "codAmountMinor",
        "message": "COD amount is required when payment type is cod."
      }
    ]
  }
}
```

Invalid parcel weight:

```json
{
  "error": {
    "code": "validation_failed",
    "message": "Some dispatch details are invalid.",
    "details": [
      {
        "field": "parcelWeightKg",
        "message": "Parcel weight must be greater than 0."
      }
    ]
  }
}
```

Unsupported package type:

```json
{
  "error": {
    "code": "unsupported_package_type",
    "message": "Package type must be parcel, document, or fragile.",
    "details": []
  }
}
```

Unsupported dispatch mode:

```json
{
  "error": {
    "code": "unsupported_dispatch_mode",
    "message": "Dispatch mode must be own_fleet, ryvepool_marketplace, or overflow.",
    "details": []
  }
}
```

Unsupported payment type:

```json
{
  "error": {
    "code": "unsupported_payment_type",
    "message": "Payment type must be prepaid or cod.",
    "details": []
  }
}
```

Missing active environment credentials:

```json
{
  "error": {
    "code": "ryvepool_credentials_missing",
    "message": "RyvePool credentials are not configured for the active environment.",
    "details": []
  }
}
```

## 10. Customer Endpoint: Get Local Dispatch

```text
GET /api/order-deliveries/dispatches/{id}
Auth: user
```

### Success Response

```json
{
  "id": "df2bb97f-7be2-46bf-bf72-14f2b652dd7b",
  "environment": "test",
  "externalOrderId": "ryvesend-order-10001",
  "ryvePoolDispatchId": "disp_01jzf9k3m7n8p2q4r5s6t7u8v9",
  "status": "assigned",
  "trackingUrl": "https://api.ryvepool.com/v1/track/abc123token",
  "regionCode": "CA-ON",
  "externalBranchId": "ottawa-main-branch",
  "dispatchModeUsed": "ryvepool_marketplace",
  "paymentType": "prepaid",
  "codAmountMinor": 0,
  "packageType": "parcel",
  "currency": "CAD",
  "deliveryFeeMinor": 682,
  "platformFeeMinor": 50,
  "ryvePoolCommissionMinor": 50,
  "driverPayoutMinor": 632,
  "canCancel": true,
  "cancellableUntil": "2026-06-15T14:32:00Z",
  "createdAt": "2026-06-15T14:22:00Z",
  "updatedAt": "2026-06-15T14:24:00Z"
}
```

### Not Found

```json
{
  "error": {
    "code": "not_found",
    "message": "Order delivery dispatch not found.",
    "details": []
  }
}
```

## 11. Customer Endpoint: Lookup by External Order ID

```text
GET /api/order-deliveries/dispatches/by-external-order/{externalOrderId}
Auth: user
```

Use this when the frontend has an order ID but not the local RyvePool delivery record ID.

### Example

```text
GET /api/order-deliveries/dispatches/by-external-order/ryvesend-order-10001
```

### Success Response

Same as `GET /dispatches/{id}`.

### Not Found

```json
{
  "error": {
    "code": "not_found",
    "message": "Order delivery dispatch not found.",
    "details": []
  }
}
```

## 12. Customer Endpoint: Live Status

```text
GET /api/order-deliveries/dispatches/{id}/live
Auth: user
```

This calls RyvePool live status and returns the upstream response as-is.

Poll every 15-30 seconds only when the user is actively viewing the live tracking screen.

### Success Response

```json
{
  "dispatchId": "disp_01jzf9k3m7n8p2q4r5s6t7u8v9",
  "externalOrderId": "ryvesend-order-10001",
  "status": "en_route",
  "trackingUrl": "https://api.ryvepool.com/v1/track/abc123token",
  "orgId": "org_01abc",
  "branchId": "ven_01xyz",
  "regionCode": "CA-ON",
  "timezone": "America/Toronto",
  "dispatchMode": "ryvepool_marketplace",
  "rider": {
    "name": "Kwame Osei",
    "phone": "+16135550001",
    "lat": 45.426,
    "lng": -75.69,
    "locationUpdatedAt": "2026-06-15T14:28:15Z",
    "estimatedArrival": "4 min"
  },
  "pickedUpAt": "2026-06-15T14:26:00Z",
  "deliveredAt": null,
  "updatedAt": "2026-06-15T14:28:15Z"
}
```

### Missing Dispatch ID

```json
{
  "error": {
    "code": "missing_dispatch_id",
    "message": "RyvePool dispatch ID is not available yet.",
    "details": []
  }
}
```

## 13. Customer Endpoint: Update Recipient

```text
PATCH /api/order-deliveries/dispatches/{id}/recipient
Auth: user
```

Allowed before pickup only. RyvePool may reject the update if the dispatch has already passed the allowed status.

### Request

```json
{
  "recipientName": "Jane Customer",
  "recipientPhone": "+16135557777",
  "recipientEmail": "jane@example.com"
}
```

All fields are optional, but at least one must be sent.

### Success Response

Same local delivery response as `GET /dispatches/{id}`.

### No Fields Sent

```json
{
  "error": {
    "code": "validation_failed",
    "message": "At least one recipient field is required.",
    "details": []
  }
}
```

### Upstream Cannot Update

RyvePool may return a forwarded error:

```json
{
  "error": {
    "code": "cannot_update",
    "message": "Attempted to update recipient after pickup",
    "details": []
  }
}
```

## 14. Customer Endpoint: Cancel Dispatch

```text
POST /api/order-deliveries/dispatches/{id}/cancel
Auth: user
```

### Request

```json
{
  "reason": "Customer changed their mind"
}
```

### Success Response

Same local delivery response, with `status` normally set to `cancelled`.

```json
{
  "id": "df2bb97f-7be2-46bf-bf72-14f2b652dd7b",
  "environment": "test",
  "externalOrderId": "ryvesend-order-10001",
  "ryvePoolDispatchId": "disp_01jzf9k3m7n8p2q4r5s6t7u8v9",
  "status": "cancelled",
  "trackingUrl": "https://api.ryvepool.com/v1/track/abc123token",
  "regionCode": "CA-ON",
  "externalBranchId": "ottawa-main-branch",
  "dispatchModeUsed": "ryvepool_marketplace",
  "paymentType": "prepaid",
  "codAmountMinor": 0,
  "packageType": "parcel",
  "currency": "CAD",
  "deliveryFeeMinor": 682,
  "platformFeeMinor": 50,
  "ryvePoolCommissionMinor": 50,
  "driverPayoutMinor": 632,
  "canCancel": false,
  "cancellableUntil": "2026-06-15T14:32:00Z",
  "createdAt": "2026-06-15T14:22:00Z",
  "updatedAt": "2026-06-15T14:25:00Z"
}
```

### Known Cancel Errors From RyvePool

Cannot cancel after pickup:

```json
{
  "error": {
    "code": "cannot_cancel_after_pickup",
    "message": "Courier already picked up",
    "details": []
  }
}
```

Already delivered:

```json
{
  "error": {
    "code": "already_delivered",
    "message": "Dispatch was already delivered",
    "details": []
  }
}
```

Cancellation window expired:

```json
{
  "error": {
    "code": "cancellation_window_expired",
    "message": "The 10-minute free cancellation window has passed. Contact support to cancel this dispatch.",
    "details": []
  }
}
```

## 15. Webhook Endpoint

```text
POST /api/public/webhooks/ryvepool
Auth: none
```

This is for RyvePool, not the frontend. Configure this URL in RyvePool:

```text
https://swift.ryvepos.com/api/public/webhooks/ryvepool
```

Expected headers:

```text
X-RyvePool-Signature: sha256=<hex-digest>
X-RyvePool-Event: dispatch.assigned
X-RyvePool-Delivery-Id: whe_01jzf9abc
```

### Webhook Payload Example

```json
{
  "id": "whe_01jzf9abc",
  "event": "dispatch.assigned",
  "partnerId": "ryveserve",
  "environment": "test",
  "createdAt": "2026-06-15T14:23:45Z",
  "data": {
    "dispatchId": "disp_01jzf9k3m7n8p2q4r5s6t7u8v9",
    "externalOrderId": "ryvesend-order-10001",
    "previousStatus": "searching_driver",
    "status": "assigned",
    "orgId": "org_01abc",
    "branchId": "ven_01xyz",
    "regionCode": "CA-ON",
    "currency": "CAD",
    "dispatchMode": "ryvepool_marketplace",
    "driverPool": "ryvepool_marketplace",
    "driverSource": "ryvepool_marketplace"
  }
}
```

### Webhook Success Response

```json
{
  "received": true
}
```

Duplicate event:

```json
{
  "received": true,
  "duplicate": true
}
```

### Webhook Errors

Invalid signature:

```text
HTTP 401
```

Invalid JSON:

```json
{
  "error": {
    "code": "invalid_payload",
    "message": "Webhook body is not valid JSON.",
    "details": []
  }
}
```

## 16. Admin Flow

Recommended admin setup flow:

1. Admin opens RyvePool settings.
2. Frontend calls `GET /api/admin/ryvepool/config`.
3. Admin enters test credentials and webhook secret.
4. Frontend calls `PUT /api/admin/ryvepool/config`.
5. Admin enables integration in test mode.
6. Admin runs `POST /api/admin/ryvepool/test-quote`.
7. Admin runs `POST /api/admin/ryvepool/webhooks/test`.
8. Admin verifies webhook logs.
9. Admin switches `environment` to `production` only after production keys are configured.

## 17. Admin Endpoint: Get Config

```text
GET /api/admin/ryvepool/config
Auth: admin
```

### Success Response

```json
{
  "enabled": false,
  "environment": "test",
  "baseUrl": "https://api.ryvepool.com/v1",
  "timeoutSeconds": 20,
  "defaultRegionCode": "CA-ON",
  "defaultExternalBranchId": null,
  "defaultDispatchMode": "ryvepool_marketplace",
  "defaultPackageType": "parcel",
  "webhookSignatureRequired": true,
  "test": {
    "publicKey": "pk_test...4abc",
    "secretConfigured": true,
    "webhookSecretConfigured": true
  },
  "production": {
    "publicKey": null,
    "secretConfigured": false,
    "webhookSecretConfigured": false
  }
}
```

Secrets are never returned.

## 18. Admin Endpoint: Update Config

```text
PUT /api/admin/ryvepool/config
Auth: admin
```

All fields are optional. Send only changed fields.

### Configure Test Mode

```json
{
  "enabled": true,
  "environment": "test",
  "baseUrl": "https://api.ryvepool.com/v1",
  "timeoutSeconds": 20,
  "defaultRegionCode": "CA-ON",
  "defaultExternalBranchId": "ottawa-main-branch",
  "defaultDispatchMode": "ryvepool_marketplace",
  "defaultPackageType": "parcel",
  "webhookSignatureRequired": true,
  "testPublicKey": "pk_test_01jz4abc",
  "testSecretKey": "sk_test_secret_value_here",
  "testWebhookSecret": "test_webhook_secret_value_here"
}
```

### Configure Production Credentials

```json
{
  "productionPublicKey": "pk_live_01jz4abc",
  "productionSecretKey": "sk_live_secret_value_here",
  "productionWebhookSecret": "live_webhook_secret_value_here"
}
```

### Switch to Production

```json
{
  "environment": "production",
  "enabled": true
}
```

### Disable Integration

```json
{
  "enabled": false
}
```

### Success Response

Same as `GET /api/admin/ryvepool/config`.

### Config Errors

Food package default blocked:

```json
{
  "error": {
    "code": "unsupported_package_type",
    "message": "Food delivery is not enabled for this integration.",
    "details": []
  }
}
```

Unsupported default dispatch mode:

```json
{
  "error": {
    "code": "unsupported_dispatch_mode",
    "message": "Dispatch mode must be own_fleet, ryvepool_marketplace, or overflow.",
    "details": []
  }
}
```

## 19. Admin Endpoint: Test Quote

```text
POST /api/admin/ryvepool/test-quote
Auth: admin
```

This sends a quote request to the active RyvePool environment.

### Request

```json
{
  "pickupLat": 45.4215,
  "pickupLng": -75.6972,
  "dropoffLat": 45.4291,
  "dropoffLng": -75.6878,
  "packageType": "parcel",
  "weightKg": 2.5,
  "regionCode": "CA-ON"
}
```

### Success Response

Same as customer quote response.

## 20. Admin Endpoint: Send Webhook Test

```text
POST /api/admin/ryvepool/webhooks/test
Auth: admin
```

This asks RyvePool to queue a `test.ping` event.

### Success Response

```json
{
  "message": "Test webhook queued for delivery",
  "subscriptionCount": 1,
  "subscriptions": [
    {
      "id": "whsub_abc123",
      "webhookUrl": "https://swift.ryvepos.com/api/public/webhooks/ryvepool",
      "events": ["dispatch.*"]
    }
  ]
}
```

## 21. Admin Endpoint: Remote Webhook Logs

```text
GET /api/admin/ryvepool/webhook-events?status=failed&limit=50
Auth: admin
```

Query params:

| Name | Notes |
|---|---|
| `eventType` | Example: `dispatch.delivered` |
| `status` | `pending`, `delivered`, `failed`, `cancelled` |
| `limit` | 1-200 |

### Success Response

```json
{
  "count": 2,
  "items": [
    {
      "id": "whe_01jzf9abc",
      "eventType": "dispatch.delivered",
      "resourceId": "disp_01jzf9k3",
      "status": "delivered",
      "attemptCount": 1,
      "maxAttempts": 5,
      "lastHttpStatus": 200,
      "createdAt": "2026-06-15T14:31:00Z",
      "deliveredAt": "2026-06-15T14:31:01Z"
    },
    {
      "id": "whe_01jzf8xyz",
      "eventType": "dispatch.assigned",
      "resourceId": "disp_01jzf9k3",
      "status": "failed",
      "attemptCount": 5,
      "maxAttempts": 5,
      "lastHttpStatus": 503,
      "lastErrorMessage": "Service Unavailable",
      "createdAt": "2026-06-15T14:23:45Z",
      "failedAt": "2026-06-15T14:33:50Z",
      "nextRetryAt": null
    }
  ]
}
```

## 22. Admin Endpoint: Reports Summary

```text
GET /api/admin/ryvepool/reports/summary?from=2026-06-01T00:00:00Z&to=2026-06-30T23:59:59Z
Auth: admin
```

Optional query params:

| Name | Notes |
|---|---|
| `from` | ISO date/time |
| `to` | ISO date/time |
| `branchId` | RyvePool branch ID |
| `regionCode` | Example: `CA-ON` |

### Success Response

```json
{
  "partnerId": "ryveserve",
  "orgId": "org_01abc",
  "from": "2026-06-01T00:00:00Z",
  "to": "2026-06-30T23:59:59Z",
  "totalDispatches": 142,
  "byCurrency": [
    {
      "currency": "CAD",
      "totalDispatches": 142,
      "completedDispatches": 138,
      "cancelledDispatches": 3,
      "failedDispatches": 1,
      "ownFleetDispatches": 110,
      "ryvepoolMarketplaceDispatches": 32,
      "deliveryFeeMinor": 96940,
      "platformFeeMinor": 7100,
      "commissionMinor": 7100,
      "driverPayoutMinor": 89840,
      "taxMinor": 0,
      "codAmountMinor": 0
    }
  ],
  "byDispatchMode": [
    { "dispatchMode": "own_fleet", "count": 110 },
    { "dispatchMode": "ryvepool_marketplace", "count": 32 }
  ],
  "byDriverSource": [
    { "driverSource": "own_fleet", "count": 110 },
    { "driverSource": "ryvepool_marketplace", "count": 32 }
  ]
}
```

## 23. Admin Endpoint: Branch Reports

```text
GET /api/admin/ryvepool/reports/branches?from=2026-06-01T00:00:00Z
Auth: admin
```

Returns RyvePool branch-level report response as-is.

## 24. Admin Endpoint: Local Deliveries

```text
GET /api/admin/ryvepool/deliveries?page=1&pageSize=50&environment=test&status=delivered
Auth: admin
```

### Success Response

```json
{
  "items": [
    {
      "id": "df2bb97f-7be2-46bf-bf72-14f2b652dd7b",
      "environment": "test",
      "externalOrderId": "ryvesend-order-10001",
      "ryvePoolDispatchId": "disp_01jzf9k3m7n8p2q4r5s6t7u8v9",
      "status": "delivered",
      "trackingUrl": "https://api.ryvepool.com/v1/track/abc123token",
      "regionCode": "CA-ON",
      "externalBranchId": "ottawa-main-branch",
      "dispatchModeUsed": "ryvepool_marketplace",
      "paymentType": "prepaid",
      "codAmountMinor": 0,
      "packageType": "parcel",
      "currency": "CAD",
      "deliveryFeeMinor": 682,
      "platformFeeMinor": 50,
      "ryvePoolCommissionMinor": 50,
      "driverPayoutMinor": 632,
      "canCancel": false,
      "cancellableUntil": "2026-06-15T14:32:00Z",
      "createdAt": "2026-06-15T14:22:00Z",
      "updatedAt": "2026-06-15T14:31:00Z",
      "userEmail": "customer@example.com"
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50,
  "totalPages": 1
}
```

## 25. Admin Endpoint: Local Delivery Detail

```text
GET /api/admin/ryvepool/deliveries/{id}
Auth: admin
```

### Success Response

```json
{
  "id": "df2bb97f-7be2-46bf-bf72-14f2b652dd7b",
  "environment": "test",
  "externalOrderId": "ryvesend-order-10001",
  "merchantReference": "RS-10001",
  "externalBranchId": "ottawa-main-branch",
  "ryvePoolDispatchId": "disp_01jzf9k3m7n8p2q4r5s6t7u8v9",
  "status": "delivered",
  "trackingUrl": "https://api.ryvepool.com/v1/track/abc123token",
  "regionCode": "CA-ON",
  "timezone": "America/Toronto",
  "dispatchModeRequested": "ryvepool_marketplace",
  "dispatchModeUsed": "ryvepool_marketplace",
  "driverPool": "ryvepool_marketplace",
  "paymentType": "prepaid",
  "codAmountMinor": 0,
  "packageType": "parcel",
  "parcelWeightKg": 2.5,
  "driverInstructions": "Call recipient on arrival.",
  "currency": "CAD",
  "deliveryFeeMinor": 682,
  "platformFeeMinor": 50,
  "ryvePoolCommissionMinor": 50,
  "driverPayoutMinor": 632,
  "notificationFeeMinor": 0,
  "taxMinor": 0,
  "paymentProcessingFeeMinor": 0,
  "refundAdjustmentMinor": 0,
  "settlementStatus": "unsettled",
  "cancellationWindowMinutes": 10,
  "cancellableUntil": "2026-06-15T14:32:00Z",
  "canCancel": false,
  "shortCode": "AB4X9",
  "pickup": {
    "pickupName": "RyveSend Warehouse",
    "pickupPhone": "+16135551234",
    "pickupAddress": "145 Bank St, Ottawa ON K1A 0A6",
    "pickupLandmark": "Loading door at rear",
    "pickupLat": 45.4215,
    "pickupLng": -75.6972
  },
  "dropoff": {
    "dropoffName": "Alice Customer",
    "dropoffPhone": "+16135559876",
    "dropoffEmail": "alice@example.com",
    "dropoffAddress": "200 Rideau St, Ottawa ON K1N 5Y1",
    "dropoffLandmark": "Apt 4B, buzzer 412",
    "dropoffLat": 45.4291,
    "dropoffLng": -75.6878
  },
  "metadataJson": "{\"platform\":\"ryvesend\",\"orderType\":\"parcel_delivery\"}",
  "createdAt": "2026-06-15T14:22:00Z",
  "updatedAt": "2026-06-15T14:31:00Z",
  "pickedUpAt": "2026-06-15T14:26:00Z",
  "deliveredAt": "2026-06-15T14:31:00Z",
  "cancelledAt": null,
  "failedAt": null,
  "user": {
    "id": "f6cb6c6a-a774-4716-ad5d-e0166a408b6e",
    "email": "customer@example.com",
    "fullName": "Alice Customer"
  },
  "webhookEvents": [
    {
      "id": "e2e4e5f7-74d9-4c5d-8ec8-3916510edbed",
      "ryvePoolEventId": "whe_01jzf9abc",
      "event": "dispatch.delivered",
      "environment": "test",
      "dispatchId": "disp_01jzf9k3m7n8p2q4r5s6t7u8v9",
      "externalOrderId": "ryvesend-order-10001",
      "previousStatus": "en_route",
      "status": "delivered",
      "isSignatureValid": true,
      "receivedAt": "2026-06-15T14:31:01Z"
    }
  ]
}
```

### Not Found

```json
{
  "error": {
    "code": "not_found",
    "message": "RyvePool order delivery not found.",
    "details": []
  }
}
```

## 26. Upstream RyvePool Errors Forwarded by Backend

When RyvePool returns an error body with `error` and `message`, the backend forwards it inside the standard RyveSend error envelope.

Known upstream errors:

| HTTP | Code | Meaning |
|---|---|---|
| 400 | `missing_external_order_id` | Required field not provided |
| 400 | `missing_pickup` | Pickup object missing |
| 400 | `missing_dropoff` | Dropoff object missing |
| 400 | `partner_scope_not_resolved` | Branch/org/region could not be resolved from API key |
| 400 | `cannot_update` | Attempted to update recipient after pickup |
| 401 | upstream auth error | API key missing, invalid, or expired |
| 403 | upstream authz error | Key not authorized for branch/region |
| 404 | `dispatch_not_found` | Dispatch ID not found or belongs to another partner |
| 422 | `cannot_cancel_after_pickup` | Dispatch status is picked up or later |
| 422 | `already_delivered` | Dispatch is already delivered |
| 422 | `cancellation_window_expired` | Free-cancel window has passed |
| 429 | upstream rate limit | RyvePool rate limit exceeded |
| 500 | upstream server error | RyvePool internal error |

Generic upstream error if RyvePool does not return a parseable error:

```json
{
  "error": {
    "code": "ryvepool_request_failed",
    "message": "RyvePool request failed with HTTP 500.",
    "details": []
  }
}
```

## 27. Scenario: Test Setup End-to-End

1. Admin saves test credentials:

```http
PUT /api/admin/ryvepool/config
Authorization: Bearer <admin_token>
Content-Type: application/json
```

```json
{
  "enabled": true,
  "environment": "test",
  "testPublicKey": "pk_test_01jz4abc",
  "testSecretKey": "sk_test_secret_value_here",
  "testWebhookSecret": "test_webhook_secret_value_here",
  "defaultRegionCode": "CA-ON",
  "defaultExternalBranchId": "ottawa-main-branch",
  "defaultDispatchMode": "ryvepool_marketplace",
  "defaultPackageType": "parcel"
}
```

2. Admin sends a test quote.
3. Admin sends webhook test.
4. Customer creates a quote.
5. Customer creates a dispatch.
6. Test simulator advances status over about 95 seconds.
7. Frontend shows status changes from local dispatch or live endpoint.

## 28. Scenario: Production Switch

1. Admin configures production credentials:

```json
{
  "productionPublicKey": "pk_live_01jz4abc",
  "productionSecretKey": "sk_live_secret_value_here",
  "productionWebhookSecret": "live_webhook_secret_value_here"
}
```

2. Admin verifies config response shows:

```json
{
  "production": {
    "publicKey": "pk_live_...4abc",
    "secretConfigured": true,
    "webhookSecretConfigured": true
  }
}
```

3. Admin switches active environment:

```json
{
  "environment": "production",
  "enabled": true
}
```

4. Admin runs a production test quote.
5. Admin confirms webhook URL in RyvePool is:

```text
https://swift.ryvepos.com/api/public/webhooks/ryvepool
```

## 29. Scenario: COD Delivery

Use `paymentType: "cod"` and set `codAmountMinor`.

```json
{
  "externalOrderId": "ryvesend-cod-10001",
  "pickup": {
    "name": "RyveSend Warehouse",
    "phone": "+16135551234",
    "address": "145 Bank St, Ottawa ON",
    "lat": 45.4215,
    "lng": -75.6972
  },
  "dropoff": {
    "name": "Cash Customer",
    "phone": "+16135559876",
    "address": "200 Rideau St, Ottawa ON",
    "lat": 45.4291,
    "lng": -75.6878
  },
  "paymentType": "cod",
  "codAmountMinor": 4500,
  "packageType": "parcel",
  "parcelWeightKg": 1.2
}
```

## 30. Scenario: Recipient Edit

If customer typed the wrong phone number and courier has not picked up:

```http
PATCH /api/order-deliveries/dispatches/df2bb97f-7be2-46bf-bf72-14f2b652dd7b/recipient
Authorization: Bearer <user_token>
Content-Type: application/json
```

```json
{
  "recipientPhone": "+16135557777"
}
```

If RyvePool returns `cannot_update`, show:

```text
Recipient details can no longer be changed because the courier has already picked up the package.
```

## 31. Scenario: Cancellation Window Expired

If cancel returns `cancellation_window_expired`, show:

```text
The free cancellation window has passed. Please contact support to cancel this delivery.
```

Do not keep retrying cancellation automatically.

## 32. Frontend Implementation Checklist

- Use `/api/order-deliveries/quotes` for price preview.
- Use `/api/order-deliveries/dispatches` only after user confirms.
- Store returned local `id`.
- Store `externalOrderId` from the order.
- Do not store or request RyvePool secrets in frontend.
- Never call RyvePool API directly from frontend.
- Do not send `packageType: "food"`.
- Use `parcel`, `document`, or `fragile`.
- Format minor-unit money as dollars/cents.
- Respect `canCancel` and `cancellableUntil`.
- Poll `/live` only on active tracking screens.
- Handle `failed`, `cancelled`, and `delivered` as terminal states.
- Show friendly messages for `cannot_cancel_after_pickup`, `already_delivered`, and `cancellation_window_expired`.

## 33. Admin UI Checklist

- Config screen can read and update RyvePool settings.
- Config screen masks public keys and never expects secret values back.
- Admin can set test credentials.
- Admin can set production credentials.
- Admin can switch active environment.
- Admin can enable/disable the integration.
- Admin can run test quote.
- Admin can trigger webhook test.
- Admin can view remote webhook delivery logs.
- Admin can view reports summary.
- Admin can view branch reports.
- Admin can view local deliveries and delivery detail.
- Admin UI must make it visually clear whether active environment is `test` or `production`.

## 34. Local Backend Error Catalog

| Code | Message |
|---|---|
| `validation_failed` | `Some quote details are invalid.` |
| `validation_failed` | `Some dispatch details are invalid.` |
| `validation_failed` | `At least one recipient field is required.` |
| `duplicate_external_order_id` | `This external order ID already has a RyvePool dispatch.` |
| `not_found` | `Order delivery dispatch not found.` |
| `not_found` | `RyvePool order delivery not found.` |
| `missing_dispatch_id` | `RyvePool dispatch ID is not available yet.` |
| `invalid_payload` | `Webhook body is not valid JSON.` |
| `unsupported_package_type` | `Food delivery is not enabled for this integration.` |
| `unsupported_package_type` | `Package type must be parcel, document, or fragile.` |
| `unsupported_dispatch_mode` | `Dispatch mode must be own_fleet, ryvepool_marketplace, or overflow.` |
| `unsupported_payment_type` | `Payment type must be prepaid or cod.` |
| `ryvepool_disabled` | `RyvePool order delivery is not enabled.` |
| `ryvepool_credentials_missing` | `RyvePool credentials are not configured for the active environment.` |
| `ryvepool_request_failed` | `RyvePool request failed with HTTP <status>.` |

Field-level validation messages:

| Field | Message |
|---|---|
| `pickup` | `Pickup coordinates or pickup city are required.` |
| `dropoff` | `Dropoff coordinates or dropoff city are required.` |
| `weightKg` | `Weight must be greater than 0.` |
| `externalOrderId` | `External order ID is required.` |
| `pickup` | `pickup is required.` |
| `dropoff` | `dropoff is required.` |
| `pickup.name` | `Name is required.` |
| `dropoff.name` | `Name is required.` |
| `pickup.phone` | `Phone is required.` |
| `dropoff.phone` | `Phone is required.` |
| `pickup.address` | `Address is required when coordinates are not provided.` |
| `dropoff.address` | `Address is required when coordinates are not provided.` |
| `codAmountMinor` | `COD amount is required when payment type is cod.` |
| `parcelWeightKg` | `Parcel weight must be greater than 0.` |
