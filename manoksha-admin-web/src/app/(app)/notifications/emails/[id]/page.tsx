import Link from "next/link";
import { Alert, Card, Forbidden, PageHeader } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { EmailPreview } from "@/lib/types";

/** Exactly what the recipient receives. The HTML is shown in a sandboxed frame (no scripts, no same-origin access). */
export default async function EmailPreviewPage({ params }: { params: Promise<{ id: string }> }) {
  const me = (await getMe())!;
  if (!can(me, P.exceptionsManage)) return <Forbidden what="emails" />;
  const { id } = await params;
  const email = await backendFetch<EmailPreview>(`admin/notifications/emails/${id}`);
  if (!email.ok) return <Alert>{email.problem.title}</Alert>;
  return (
    <>
      <PageHeader title={email.data.subject} description={`To ${email.data.toAddress}`}
        actions={<Link href="/notifications?tab=emails" className="text-sm text-brand-700 hover:underline">← Email log</Link>} />
      <div className="grid gap-6 lg:grid-cols-3">
        <Card title="Email" className="lg:col-span-2">
          <iframe title="Email preview" sandbox="" srcDoc={email.data.htmlBody} className="h-[640px] w-full rounded border border-slate-200 bg-white" />
        </Card>
        <Card title="Plain-text version">
          <pre className="whitespace-pre-wrap text-xs text-slate-700">{email.data.textBody}</pre>
        </Card>
      </div>
    </>
  );
}
