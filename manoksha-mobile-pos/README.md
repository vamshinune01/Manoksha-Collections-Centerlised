# manoksha-mobile-pos

Flutter app (Android) for store staff: store sales with barcode scanning, authorized bargaining, UPI and split payments,
Bluetooth thermal receipts, item lookup and attendance. It signs in with `client: "pos"` (token audience `pos`) and
calls only the central backend. A sale needs an internet connection (no offline POS in V1).

## Sale flow

1. Scan the piece label (keyboard-wedge scanner, or the camera button), or search by name/SKU for non-serialized items.
2. Tap a line to change the price. A reason is always required. Limits come from the backend settings:
   staff up to `pos.staff_max_discount_pct` (5%), managers up to `pos.manager_max_discount_pct` (15%), above that only the Owner.
3. **Pay**: payments must add up to the total; methods come from `pos.payment_methods` (currently `UPI`, add `CASH`,
   `CARD`, `OTHER` later). Every non-cash payment needs the transaction reference.
4. If the discount is above the seller's limit, the approver chooses their name and enters **their own PIN** on the
   device (they cannot approve their own sale). PINs are set in *Settings → Set approval PIN*.
5. The server checks prices, limits, the PIN and stock, then completes the sale (`MC-POS-…`). One idempotency key per
   basket means a retry after a dropped connection never sells twice. The receipt prints automatically if a printer is set.

## Printer

Pair the 58 mm or 80 mm ESC/POS Bluetooth printer in Android settings, then *Settings → Choose*, select the paper width
and *Print test receipt*.

## Run and build

```bash
flutter run --dart-define=API_BASE_URL=https://<api-host>
flutter build apk --release --dart-define=API_BASE_URL=https://<api-host>
```

Without `API_BASE_URL` the app uses the staging API. Minimum Android version: 6.0 (API 23).
