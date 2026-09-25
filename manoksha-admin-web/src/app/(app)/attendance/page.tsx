import { Alert, Badge, Card, Forbidden, PageHeader, Table, formatDateTime } from "@/components/ui";
import { P, can } from "@/lib/access";
import { backendFetch, getMe } from "@/lib/backend";
import type { Attendance, MyAttendance } from "@/lib/types";
import { ClockButtons, CorrectButton } from "./attendance-actions";

export default async function AttendancePage() {
  const me = (await getMe())!;
  const canSelf = can(me, P.attendanceSelf);
  const canTeam = can(me, P.attendanceView);
  if (!canSelf && !canTeam) return <Forbidden what="attendance" />;
  const [mine, team] = await Promise.all([
    canSelf ? backendFetch<MyAttendance>("admin/me/attendance") : Promise.resolve(null),
    canTeam ? backendFetch<Attendance[]>("admin/attendance") : Promise.resolve(null),
  ]);

  return (
    <>
      <PageHeader title="Attendance" description="Clock-in is recorded at your assigned branch." />
      <div className="space-y-6">
        {mine?.ok && (
          <Card title="My attendance">
            {!mine.data.isEmployee ? (
              <p className="text-sm text-slate-600">Your account has no employee profile.</p>
            ) : (
              <div className="space-y-4">
                <div className="flex flex-wrap items-center gap-4">
                  <Badge tone={mine.data.isClockedIn ? "green" : "slate"}>{mine.data.isClockedIn ? "Clocked in" : "Not clocked in"}</Badge>
                  <span className="text-sm text-slate-600">Branch: <strong>{mine.data.assignedBranchName}</strong></span>
                  {mine.data.open && <span className="text-sm text-slate-600">since {formatDateTime(mine.data.open.clockInAt)}</span>}
                  <ClockButtons clockedIn={mine.data.isClockedIn} />
                </div>
                <AttendanceTable rows={mine.data.recent.slice(0, 10)} />
              </div>
            )}
          </Card>
        )}
        {team && (team.ok ? (
          <Card title="Team attendance (last 7 days)">
            <AttendanceTable rows={team.data} showEmployee correctable={can(me, P.attendanceCorrect)} selfUserId={me.userId} />
          </Card>
        ) : <Alert>{team.problem.title}</Alert>)}
      </div>
    </>
  );
}

function AttendanceTable({ rows, showEmployee, correctable }: { rows: Attendance[]; showEmployee?: boolean; correctable?: boolean; selfUserId?: string }) {
  return (
    <Table head={[...(showEmployee ? ["Employee"] : []), "Branch", "In", "Out", "Hours", "Note", ...(correctable ? [""] : [])]} empty={rows.length === 0}>
      {rows.map((a) => (
        <tr key={a.id}>
          {showEmployee && <td className="px-4 py-2 text-sm">{a.employeeName} <span className="text-xs text-slate-400">{a.employeeCode}</span></td>}
          <td className="px-4 py-2 text-sm">{a.branchName}</td>
          <td className="px-4 py-2 text-sm">{formatDateTime(a.clockInAt)}</td>
          <td className="px-4 py-2 text-sm">{a.clockOutAt ? formatDateTime(a.clockOutAt) : <Badge tone="green">Open</Badge>}</td>
          <td className="px-4 py-2 text-sm">{a.hoursWorked ?? "—"}</td>
          <td className="px-4 py-2 text-xs text-slate-500">{a.correctionReason ? `Corrected: ${a.correctionReason}` : ""}</td>
          {correctable && <td className="px-4 py-2 text-right"><CorrectButton record={a} /></td>}
        </tr>
      ))}
    </Table>
  );
}
