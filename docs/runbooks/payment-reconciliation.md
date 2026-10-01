# Payment reconciliation (SPEC §14.2, §36)

A reconciliation case is created when money arrived but could not be applied: the payment succeeded after the reservation
expired and the stock is gone, the amount differs, or a paid order was cancelled. The Owner gets a CRITICAL email and a red banner.

1. Admin → **Exception Center → Late payment / inventory gone** → open the case. It shows the provider reference, amount, the
   order/deposit it belonged to and the customer.
2. Decide with the customer: ship a replacement (place a new order for them) or refund.
3. Refunds are made **in the payment provider's dashboard** (V1 has no automatic refund). Then record on the case:
   *Refund initiated* (with the provider's refund reference) → *Refund completed*. Each step is audited.
4. Close the case as *Resolved* with a note. It leaves the Exception Center; history stays.

Never edit an order or wallet to "make the numbers match" — every money movement must stay traceable.
