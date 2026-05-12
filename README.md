# Learning-Repo
README — DBLB POS (Mobile QR Ordering)
Hey 👋 — here's the quick tour of what we've been hacking on. Think of this less as "official docs" and more as "what I'd tell a new teammate at chai".

What is this project, really?
It's a multi-tenant Blazor Server POS app — full-stack .NET 8 + SQL Server, with a sales invoice + kiosk side of things. The interesting bits we've been working on lately are the customer-facing flows: scan-a-QR-and-do-stuff without ever logging in.

There are basically two QR flows:

Sales invoice QR — staff prints an invoice, customer scans the little QR in the corner, lands on a clean public "your bill" page (no app chrome, no auth required).
Table QR (kiosk) — staff sticks a printed QR on each table, customer scans it, lands directly in the menu, picks their food, taps PLACE ORDER, gets a token to show at the counter. No app install, no login.
Both flows have to work for somebody who has never heard of our app. That's the whole game.

The big secret: URL tokens, not IDs
Originally the QR would just encode something like /app/public/bill/123. Problem? Anyone with one valid QR could change 123 → 124 and view someone else's bill. Classic IDOR.

So we built BillUrlProtector and TableQrProtector — tiny utilities that:

Take an integer ID (the invoice ID, or tenant+warehouse+table)
Encrypt it with AES-GCM using a 32-byte secret from appsettings.json ("BillUrlSecret" / "TableQrSecret")
Spit out an opaque ~43-char URL-safe Base64 token
Decrypt it on the way back in — and fail closed if anyone tampers with even one character
No database changes. No new table. The encrypted token IS the lookup key. Tamper-proof because AES-GCM has an auth tag built in. Beautiful. The only downside: if you rotate the secret, every printed QR poster becomes invalid — so pick once and keep it secret like a password.

The "anonymous user" trap (this bit us a lot)
The app was built assuming everyone is logged in. Tenant is read from the auth context. So when a logged-out customer hits a page, a bunch of services see tenantId = null and all their queries return zero rows.

Fix pattern we settled on: add a parallel *Public(string tenantId) overload to every service the public/kiosk pages touch. The customer's tenant is decoded from the QR token; we pass it explicitly. Staff flow keeps using the original methods — completely unaffected.

So now there's a bunch of paired methods like:

_company.GetbyId() (staff) vs _company.GetbyIdPublic(tenantId) (customer)
_product.POSProductWithStock(...) vs _product.POSProductWithStockPublic(warehouseId, tenantId)
_category.GetAll() vs _category.GetAllPublic(tenantId)
_ledger.LedgerById(26) vs _ledger.LedgerByIdPublic(26, tenantId)
_invoiceSetting.VoucherTypeView(...) vs _invoiceSetting.VoucherTypeViewPublicTenant(...)
Plus variant + add-on lookups for the kiosk flow
Plus SalesInvoiceMasterViewPublic and SalesInvoiceDetailsViewPublic (extended to include sales person, type, non-inventory, etc. so the public bill view can render the same columns as the in-app one)
It's whack-a-mole, but predictable whack-a-mole. Every time we plug a new injected service into a public page, we check: does its constructor read auth claims unsafely? Does any of its methods filter by tenantId? If yes → add a public overload.

The ?.Value problem
Sister problem to the tenant one. Tons of services have a constructor that does:


UserId    = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;  // null-safe
UserEmail = user.FindFirst(ClaimTypes.Name).Value;             // ❌ NRE for anonymous
When an anonymous visitor injects one of these services, FindFirst(ClaimTypes.Name) returns null, and .Value throws. The page crashes before it even renders. Symptom: ASP.NET Core's ugly "An unhandled exception occurred" page.

Fix: change .Value to ?.Value. One character. Behavior is identical for logged-in users (the claim exists, both return the same string); for anonymous users, the value is just null instead of an NRE.

We've only patched the services that are actually reached by the public flows (PaymentInService, PurchaseOrderServiceASN, TaxCalculateMasterService, WarehouseService so far). Rest of the codebase is untouched — the pattern is there in 27+ files, but we don't fix what isn't broken for our pages.

The kiosk SalesMaster TenantId issue
One specific gotcha that took a while: when a kiosk customer places an order, the order saves into SalesMaster. The Save() method used to unconditionally do model.TenantId = tenantId; — overwriting whatever was on the model with the service-constructor's tenant. For an anonymous QR customer, that's null. So orders saved with TenantId = NULL, and staff (filtered by their tenant) never saw them. Looked like "PLACE ORDER does nothing".

Fix: changed it to if (string.IsNullOrEmpty(model.TenantId)) model.TenantId = tenantId; — only fill in if the caller didn't already set it. The kiosk now explicitly stamps TenantId on the master from the decoded QR token. Staff flow is byte-for-byte identical because they never set TenantId themselves.

The public bill page (SalesbillPublic.razor)
This is what shows up when someone scans a sales-invoice QR. The layout mirrors the in-app SalesInvoiceDetailsPage — supplier info, company info, invoice info, the QR (smaller, ~120px, locked square with aspect-ratio: 1/1), the product table, the totals breakdown with GST-CGST / GST-SGST when applicable, amount in words, and notes.

On mobile (≤720px) the 3-column header collapses into stacked blocks, and the 9-column product table turns into stacked cards — one card per line item, with data-label attributes powering pseudo-element labels like "Qty:", "Price:", "Tax:". No horizontal scrollbar.

There's a Print button (uses the browser's native window.print()) and... that's it. We removed Download PDF because html2pdf's output looked bad. The page has noindex + Cache-Control: no-store headers so search engines don't crawl it and browsers don't cache it.

The kiosk page (KioskPage.razor)
This is the monster — 6000+ lines. It's the table-QR ordering experience. The flow:

Customer scans QR → /app/kiosk?t=<token> opens.
TryHandleQrScanParams decrypts the token, gets (tenantId, warehouseId, tableId).
Loads company/products/categories/ledger/invoiceSetting using the public service methods (with the decoded tenant).
Customer sees the menu, taps items, opens cart, taps PLACE ORDER.
We save a SalesMaster with Channel="QR", status "Unpaid", and a generated token like T101.
Customer sees the token on screen; staff settles the bill at the counter.
If multiple rounds happen on the same table before staff closes the tab, they all share the same Reference token — so it's one logical "tab" but multiple SalesMaster rows.

The kiosk page also has variant selection (e.g., "size: small/medium/large"), add-ons, and a customer info form — all skippable for QR flow.

Mobile responsiveness on the kiosk page is mostly already there (sidebar collapses, grid becomes 2-column, etc.). Our recent fix: making the bottom CART bar and the cart-page PLACE ORDER footer position: sticky; bottom: 0 on phones so those buttons don't scroll off-screen. Two tiny CSS additions, zero new components.

The "QR generation" pages
Three pages produce QRs that point at public routes:

Page	What QR points to
SalesInvoiceDetailsPage.razor	/app/public/bill/<token> (signed sales-invoice ID)
PosPrint.razor	same as above
PosAttributePrint.razor	same as above
QrCodeGenerator.razor (/app/table-qr-codes)	/app/kiosk?t=<token> (signed tenant+warehouse+table)
QR rendering uses QRCoder with GetGraphic(20) for high-resolution PNG bytes, so even when CSS scales them down to 100–180px they stay crisp.

Config you need to care about
appsettings.json has three secrets that need to be real 32-byte base64 keys:

BillUrlSecret — encrypts the sales-invoice ID for the public bill URL
TableQrSecret — encrypts (tenant, warehouse, table) for table QRs
QrOrder:SignatureSecret — older HMAC fallback for legacy multi-param URLs (we maintain backward compat)
Plus QrOrder:CustomerBaseUrl — if empty, QR URLs use whatever NavigationManager.BaseUri is (i.e., wherever the staff happens to be browsing from). For production this should be your public-facing hostname so QRs printed on a tablet don't accidentally encode localhost.

The DB connection string in dev tends to flip between 127.0.0.1 (one teammate's setup) and 20.232.17.82 (the shared staging DB) — keep an eye on it; a wrong host = Error Number 2, Class 20 SQL exception at startup.

Mental model for next time
Whenever we touch one of these public/anonymous flows, ask three questions:

Does this page's DI graph pull in any service whose constructor reads claims unsafely? → fix the .Value → ?.Value in that specific service.
Does any code path filter by tenantId that comes from the service constructor? → that path returns empty for anonymous; add a *Public(string tenantId) overload and use it.
Does any save/insert overwrite model.TenantId? → make it preserve a pre-set value with ??= or IsNullOrEmpty guard, and have the caller stamp the tenant explicitly.
Stick to those three rules and the public side stays consistent without touching the staff flows.

What "done" looks like
A customer on a fresh phone, with no app, no login, on cellular data, can:

Scan a sales-invoice QR and see their bill clearly (no horizontal scroll, sharp QR, Print works).
Scan a table QR, see the menu load, tap a product (variants open if applicable), see the items pile up in the cart, tap CART, tap PLACE ORDER, see their token.
Staff sees that order under the correct tenant immediately.
And nobody who's logged in notices any difference at all in their day-to-day flows.

That's the bar. Most things are there now; we keep grinding the edges.
