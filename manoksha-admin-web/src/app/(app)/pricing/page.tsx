import { Alert, Badge, Button, Forbidden, Input, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can, inr } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { SkuPrice } from "@/lib/types";
import { PriceEditor, ProductDiscountEditor } from "./price-editors";

type Search = Record<string, string | string[] | undefined>;

export default async function PricingPage({ searchParams }: { searchParams: Promise<Search> }) {
  const me = (await getMe())!;
  if (!can(me, P.pricingView)) return <Forbidden what="pricing" />;
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q : "";
  const productId = typeof sp.productId === "string" ? sp.productId : "";
  const rows = q || productId
    ? await backendFetch<SkuPrice[]>(`admin/pricing/skus?${new URLSearchParams({ ...(q && { q }), ...(productId && { productId }) })}`)
    : null;
  const manage = can(me, P.pricingManage);
  const products = rows?.ok ? [...new Map(rows.data.map((r) => [r.productId, r])).values()] : [];

  return (
    <>
      <PageHeader title="Pricing" description="Owner-controlled retail prices (history kept) and product reseller discounts, which override each reseller's normal discount — never stacked." />
      <form method="get" className="mb-4 flex gap-2">
        <Input name="q" placeholder="Product name, SKU code or barcode" defaultValue={q} className="max-w-md" />
        <Button type="submit" variant="secondary">Find</Button>
      </form>
      {!rows && <p className="text-sm text-slate-500">Search for a product to view or change its prices.</p>}
      {rows && !rows.ok && <Alert>{rows.problem.title}</Alert>}
      {rows?.ok && (
        <div className="space-y-6">
          <Table head={["SKU", "Status", "Channels", "Retail price", "Since", ...(manage ? [""] : [])]} empty={rows.data.length === 0}>
            {rows.data.map((r) => (
              <tr key={r.skuId} className="align-top">
                <td className="px-4 py-2.5">{r.productName} · {r.variantName}<div className="font-mono text-xs text-slate-400">{r.skuCode}</div></td>
                <td className="px-4 py-2.5"><Badge tone={r.productStatus === "Active" ? "green" : "amber"}>{r.productStatus}</Badge></td>
                <td className="px-4 py-2.5 space-x-1">{r.availableForRetail && <Badge>Retail</Badge>}{r.availableForReseller && <Badge tone="brand">Reseller</Badge>}</td>
                <td className="px-4 py-2.5 font-semibold">{r.retailPrice == null ? <Badge tone="red">Not priced</Badge> : inr(r.retailPrice)}</td>
                <td className="px-4 py-2.5 text-xs text-slate-500">{formatDateTime(r.priceSince)}</td>
                {manage && <td className="px-4 py-2.5 text-right"><PriceEditor sku={r} /></td>}
              </tr>
            ))}
          </Table>
          {products.map((p) => (
            <ProductDiscountEditor key={p.productId} productId={p.productId} productName={p.productName} current={p.productResellerDiscountPct} editable={manage} />
          ))}
        </div>
      )}
    </>
  );
}
