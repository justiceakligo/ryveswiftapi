# Frontend DHL Certification Handoff

This handoff covers the public customer UI and admin UI changes required after DHL Express Canada certification for RyveSwift.

## Backend Status

- API commit deployed: `6efe3e3` (`Enforce DHL certification constraints`)
- DHL-certified product: `P` only, shown to users as `DHL Express Worldwide`
- Forbidden DHL products: `D`, `N`, `W`, `X`, `C`, plus any DHL response product named Domestic Express, Economy Select, Express Easy, or Medical Express
- Documents shipments are no longer supported in the certified flow
- Domestic shipments are no longer supported in the certified flow

## Public Customer UI

### Quote Form

- Remove any DHL service selector or dropdown.
- Display only `DHL Express Worldwide` as the available service.
- Remove or disable the `documents` shipment type.
- Send quote requests with `shipmentType: "parcel"` only.
- Block domestic quotes before calling the API when origin and destination countries match.
- If the backend returns `UNSUPPORTED_DHL_SERVICE`, show the returned message and direct the user to use an eligible international parcel shipment.

### Customs Form

For every international parcel booking, collect line-item customs data before confirmation.

Required per line item:

- `description`
- `quantity`
- `unitOfMeasurement`
- `unitPrice`
- `currency`
- `hsCode`
- `manufacturerCountry`
- `netWeightKg`
- `grossWeightKg`

HS code rules:

- Required on every line item.
- Must be a real 6- to 10-digit HS code.
- Prefer 10-digit codes for Canada exports when the user has them.
- Do not allow placeholder codes like `000000` or `999999`.

Description rules:

- Require detailed, accurate product descriptions.
- Reject vague descriptions such as `Gift`, `Clothes`, `Sample`, `Goods`, `General Goods`, `Misc`, `Package`, or `Fashion item`.
- Good examples:
  - `Women's woven dress, 100% cotton, finished garment`
  - `Men's leather belt, full-grain cowhide, 38mm width`
  - `Bluetooth headphones, over-ear, rechargeable lithium battery`

### Address Forms

- Do not auto-fill or send `00000` postal codes.
- Ask for a real postal code where the destination country uses postal codes.
- For countries without postal codes, allow the postal code field to be blank or let the user enter a DHL-recognized service-area code.
- Keep using the existing `addressLine3` DTO field if needed, but label it in the UI as `County / Suburb / District`.
- Do not label `addressLine3` as a normal street address line for DHL shipments. The backend maps it to DHL `countyName`.

### Booking Confirmation

The frontend can keep using:

- `POST /api/bookings/confirm`
- `GET /api/shipments/{id}`
- `GET /api/shipments/{id}/documents/{type}`

Document links returned by booking and shipment responses are relative API paths:

- `/api/shipments/{shipmentId}/documents/label`
- `/api/shipments/{shipmentId}/documents/invoice`
- `/api/shipments/{shipmentId}/documents/waybill`

Use the actual configured API base URL for the environment rather than assuming `https://swift.ryvepos.com`.

## Admin UI

### Markup Rules

- Product code should be blank/global or `P`.
- Do not allow admin users to create or update markup rules with `D`, `N`, `W`, `X`, or `C`.
- Remove service choices for:
  - DHL Express Documents
  - DHL Domestic Express
  - DHL Economy Select
  - DHL Express Easy
  - DHL Medical Express

### Shipment Views

- New shipments should display service as `DHL Express Worldwide`.
- If a legacy shipment has a forbidden DHL product code, display it as `DHL service unavailable` or a clear legacy/unavailable state.
- Retry/create-label actions may return `UNSUPPORTED_DHL_SERVICE` or `INVALID_CUSTOMS_DATA`. Show the backend message and do not retry automatically.

### Address Admin

- Label `addressLine3` as `County / Suburb / District`.
- Do not encourage users to put street-unit data in that field if it is intended for DHL routing.
- Do not insert `00000` as a postal-code fallback.

## API Error Messages To Handle

Common validation responses:

- `UNSUPPORTED_DHL_SERVICE`
- `INVALID_CUSTOMS_DATA`
- `validation_failed`
- `invalid_customs_data`

The backend returns field-level errors where possible. Use those details to highlight the exact customs or quote fields that need correction.

## Environment Note

Existing repository docs and deployment scripts reference `https://swift.ryvepos.com`, but the frontend should treat the API base URL as an environment variable. Do not hardcode that domain until ownership/routing is confirmed.
