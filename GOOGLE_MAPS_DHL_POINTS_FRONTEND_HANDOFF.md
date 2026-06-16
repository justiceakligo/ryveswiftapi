# Google Maps Address Search and DHL Point Picker Handoff

This handoff covers the backend support for simplifying address entry and helping users find nearby DHL Service Points for RyvePool delivery to DHL dropoff.

Frontend domain:

```text
https://ryvesend.com
```

Backend API base:

```text
https://swift.ryvepos.com
```

## 1. Goal

Improve the shipping form so users can:

- Search addresses with Google Places Autocomplete.
- Use current location to find nearby DHL Service Points.
- Drag a map pin to refine pickup or DHL point location.
- Select a DHL point with a clean address card.
- See the optional pickup cost clearly before payment.

## 2. Backend Configuration

Google Maps configuration is stored in `AppConfigs`.

New config keys:

| Key | Purpose |
|---|---|
| `GOOGLE_MAPS_ENABLED` | Enables frontend maps and backend Places search |
| `GOOGLE_MAPS_BROWSER_KEY` | Browser key for Google Maps JavaScript and Places Autocomplete |
| `GOOGLE_MAPS_SERVER_KEY` | Server key for backend Google Places calls |
| `GOOGLE_MAPS_PLACES_BASE_URL` | Google Places API base URL |
| `GOOGLE_MAPS_MAP_ID` | Optional Google Cloud map ID |
| `GOOGLE_MAPS_COUNTRY_RESTRICTIONS` | Comma-separated countries for autocomplete |
| `GOOGLE_MAPS_DEFAULT_RADIUS_METERS` | Default DHL point search radius |
| `GOOGLE_MAPS_MAX_DHL_POINTS` | Max DHL points returned |
| `GOOGLE_MAPS_DEFAULT_DHL_POINT_PHONE` | Fallback phone for DHL point dropoff |

Do not hardcode the Google key in frontend source. Fetch it from the backend.

## 3. Admin Config Endpoint

```http
GET /api/admin/maps/config
Authorization: Bearer <admin_access_token>
```

Sample response:

```json
{
  "enabled": true,
  "placesBaseUrl": "https://maps.googleapis.com/maps/api/place",
  "browserKeyConfigured": true,
  "browserKey": "AIzaSy...abcd",
  "serverKeyConfigured": true,
  "serverKey": "AIzaSy...abcd",
  "mapId": null,
  "countryRestrictions": ["CA", "US", "GH", "NG", "KE", "ZA", "ET"],
  "defaultRadiusMeters": 10000,
  "maxDhlPoints": 10,
  "defaultDhlPointPhone": "+18002255345"
}
```

Update config:

```http
PUT /api/admin/maps/config
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

Sample request:

```json
{
  "enabled": true,
  "browserKey": "<google_maps_browser_key>",
  "serverKey": "<google_maps_server_key_or_same_key_if_allowed>",
  "placesBaseUrl": "https://maps.googleapis.com/maps/api/place",
  "mapId": null,
  "countryRestrictions": "CA,US,GH,NG,KE,ZA,ET",
  "defaultRadiusMeters": 10000,
  "maxDhlPoints": 10,
  "defaultDhlPointPhone": "+18002255345"
}
```

Notes:

- Admin GET returns masked keys.
- Browser key is exposed only by public maps config when enabled.
- Server key is never exposed publicly.
- If the browser key is HTTP-referrer restricted, use a separate server key for backend DHL point search.

## 4. Public Maps Config

```http
GET /api/maps/config
```

Sample response:

```json
{
  "enabled": true,
  "browserKeyConfigured": true,
  "googleMapsBrowserKey": "<google_maps_browser_key>",
  "mapId": null,
  "countryRestrictions": ["CA", "US", "GH", "NG", "KE", "ZA", "ET"],
  "defaultRadiusMeters": 10000,
  "maxDhlPoints": 10,
  "defaultDhlPointPhone": "+18002255345",
  "placesAutocompleteEnabled": true,
  "mapDragSelectionEnabled": true
}
```

Frontend should load Google Maps JS only when:

```text
enabled = true
googleMapsBrowserKey is not null
```

## 5. DHL Point Search Endpoint

```http
GET /api/locations/dhl-points?lat=45.4248&lng=-75.6996&radiusMeters=10000&limit=10
```

Auth: public

Use this for “Use my location”.

Sample response:

```json
{
  "status": "ok",
  "provider": "google_places",
  "points": [
    {
      "id": "ChIJabc123",
      "googlePlaceId": "ChIJabc123",
      "name": "DHL Service Point",
      "address": "275 Slater St, Ottawa, ON K1P 5H9",
      "phone": "+18002255345",
      "latitude": 45.4207,
      "longitude": -75.7021,
      "distanceKm": 0.52,
      "openNow": true,
      "rating": 4.2,
      "userRatingsTotal": 118,
      "source": "google_nearbysearch"
    }
  ],
  "message": null,
  "warnings": null
}
```

Search by typed address:

```http
GET /api/locations/dhl-points?query=DHL%20Service%20Point%20275%20Slater%20Ottawa&country=CA&limit=8
```

Search by user's city:

```http
GET /api/locations/dhl-points?city=Ottawa&country=CA&limit=10
```

Search with both typed address and current location:

```http
GET /api/locations/dhl-points?lat=45.4248&lng=-75.6996&query=Slater%20Ottawa&country=CA&radiusMeters=15000
```

## 6. No Match Response

```json
{
  "status": "no_match",
  "provider": "google_places",
  "points": [],
  "message": "No DHL point match found nearby. Try a wider radius or type the public DHL Service Point address.",
  "warnings": null
}
```

Frontend should not dead-end here. Offer:

- Increase radius.
- Type DHL point address.
- Drag pin on map.
- Continue without optional pickup.

## 7. Unavailable Response

When Google Maps is disabled or not configured:

```json
{
  "status": "disabled",
  "provider": "google_places",
  "points": [],
  "message": "Google Maps support is not enabled.",
  "warnings": null
}
```

or:

```json
{
  "status": "unavailable",
  "provider": "google_places",
  "points": [],
  "message": "Google Maps server key is not configured.",
  "warnings": null
}
```

Frontend should hide the map/DHL point finder and fall back to manual DHL point address entry.

## 8. Recommended Frontend UX

Address entry:

- Use Google Places Autocomplete for pickup address.
- Show suggestion rows with primary text, secondary text, country, and a map preview when selected.
- Keep manual entry available.
- Store selected lat/lng with the address.

Use my location:

- Ask browser geolocation permission.
- Center map on user location.
- Call `/api/locations/dhl-points`.
- Show DHL points as map markers and list cards.
- Sort cards by `distanceKm`.
- If no points are returned, retry with the user's selected city: `/api/locations/dhl-points?city={city}&country={country}`.

Map drag:

- Let user drag the pickup pin.
- Reverse-geocode with Google Maps JS if frontend wants a human readable address.
- Re-run DHL point search when the user confirms the new pin.
- If browser location is unavailable, initialize the map from the selected city and call the city DHL point search.

DHL point cards:

- Name
- Address
- Distance
- Open now if available
- Rating if available
- “Select this DHL point” button

## 9. Feeding Quote Delivery Option

When the user selects a DHL point, use it as the `dropoff` in:

```http
POST /api/quotes/{quoteId}/delivery-option
```

Example dropoff shape:

```json
{
  "dropoff": {
    "name": "DHL Service Point",
    "phone": "+18002255345",
    "address": "275 Slater St, Ottawa, ON K1P 5H9",
    "landmark": "Selected DHL Service Point",
    "lat": 45.4207,
    "lng": -75.7021,
    "email": null
  },
  "dhlPointId": "ChIJabc123",
  "dhlPointName": "DHL Service Point"
}
```

Use the returned quote response as the source of truth.

## 10. Pickup Cost Visibility

The pickup cost can be overlooked if it is hidden inside the total. Make it hard to miss.

Recommended checkout display:

```text
DHL shipping: CAD 78.45
RyveSend fee: CAD 5.00
Pickup to DHL point: CAD 6.82
Total due today: CAD 90.27
```

Also show a compact badge near the optional toggle:

```text
Optional pickup selected +CAD 6.82
```

Before payment, require the final quote response to include:

```json
{
  "deliveryOption": {
    "enabled": true,
    "feeAmount": 6.82
  },
  "breakdown": {
    "deliveryFee": 6.82,
    "total": 90.27
  },
  "amount": 90.27
}
```

Disable the pay button if the optional pickup toggle is on but `deliveryOption` is missing.

## 11. Security Notes

- Restrict the browser key in Google Cloud to `https://ryvesend.com/*`.
- Restrict the server key by API and keep it backend-only.
- Enable Maps JavaScript API and Places API for the browser key.
- Enable Places API for the server key.
- Do not put Google keys in frontend `.env` files committed to git.
