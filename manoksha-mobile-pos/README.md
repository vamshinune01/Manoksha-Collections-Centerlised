# manoksha-mobile-pos

Flutter app for store operations: POS with barcode scanning, authorized bargaining, split payments, stock counts,
goods receiving, transfers and attendance. Internet connectivity is mandatory to finalize a sale (no offline POS in V1);
dynamic POS UPI QR is deferred.

Scaffolded in **Phase 8**. It signs in with the internal-user endpoint using `client: "pos"` (token audience `pos`) and
calls only the central backend.
