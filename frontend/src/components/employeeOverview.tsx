"use client";

import { Box, Card, CardContent, Chip, Grid, Link as MuiLink, Stack, Typography } from "@mui/material";
import Link from "next/link";
import { api, EmployeeOverview, usd } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, StatusChip, useLoad } from "./common";

/** Role, manager, skills, current assignments, deliverables and quality for one employee (Master Prompt §11). */
export function EmployeeOverviewCards({ employeeId, isAi }: { employeeId: string; isAi: boolean }) {
  const { t } = useI18n();
  const { data, error, reload } = useLoad(() => api.get<EmployeeOverview>(`/api/employees/${employeeId}/overview`), employeeId);
  useRealtime("taskChanged", () => void reload());
  if (!data) return <Loading error={error} />;
  const q = data.quality;

  const stat = (text: string, value: React.ReactNode) => (
    <Stack direction="row" sx={{ justifyContent: "space-between", py: 0.4 }}>
      <Typography variant="body2" color="text.secondary">{text}</Typography>
      <Typography variant="body2" fontWeight={600}>{value}</Typography>
    </Stack>
  );

  return (
    <Grid container spacing={2} sx={{ mb: 2 }}>
      <Grid size={{ xs: 12, md: 4 }}>
        <Card sx={{ height: "100%" }}><CardContent>
          <Typography variant="h6" gutterBottom>{t.roleAndSkills}</Typography>
          {stat(t.manager, data.managerId
            ? <MuiLink component={Link} href={`/employees/${data.managerId}`}>{data.managerName}</MuiLink>
            : "—")}
          {data.roleDescription && <Typography variant="body2" sx={{ my: 1 }}>{data.roleDescription}</Typography>}
          {data.responsibilities.length > 0 && (
            <Box component="ul" sx={{ pl: 2.5, my: 1 }}>
              {data.responsibilities.map((r) => <Typography key={r} component="li" variant="body2">{r}</Typography>)}
            </Box>
          )}
          <Stack direction="row" sx={{ flexWrap: "wrap", gap: 0.5, mt: 1 }}>
            {data.skills.map((s) => <Chip key={s} size="small" variant="outlined" label={s} />)}
          </Stack>
        </CardContent></Card>
      </Grid>

      <Grid size={{ xs: 12, md: 4 }}>
        <Card sx={{ height: "100%" }}><CardContent>
          <Typography variant="h6" gutterBottom>{t.currentAssignments} · {data.assignments.length}</Typography>
          {data.assignments.length === 0 && <Typography variant="body2" color="text.secondary">{t.empty}</Typography>}
          <Stack spacing={1} sx={{ maxHeight: 320, overflow: "auto" }}>
            {data.assignments.map((a) => (
              <Box key={a.id}>
                <MuiLink component={Link} href={`/projects/${a.projectId}?task=${a.id}`} underline="hover" sx={{ fontWeight: 500 }}>{a.title}</MuiLink>
                <Stack direction="row" spacing={0.5} sx={{ alignItems: "center", mt: 0.25, flexWrap: "wrap", rowGap: 0.5 }}>
                  <Typography variant="caption" color="text.secondary">{a.projectCode}</Typography>
                  <StatusChip value={a.status} />
                  <StatusChip value={a.priority} />
                  {a.dueDate && <Typography variant="caption" color="text.secondary">{a.dueDate}</Typography>}
                </Stack>
              </Box>
            ))}
          </Stack>
        </CardContent></Card>
      </Grid>

      <Grid size={{ xs: 12, md: 4 }}>
        <Card sx={{ height: "100%" }}><CardContent>
          <Typography variant="h6" gutterBottom>{t.qualityMetrics}</Typography>
          {isAi && stat(t.runsLabel, `${q.succeeded}/${q.runs} (${q.successRate}%)`)}
          {stat(t.tasksDoneLabel, q.tasksDone)}
          {stat(t.reviewsApprovedLabel, q.reviewsApproved)}
          {stat(t.changesRequestedLabel, q.changesRequested)}
          {stat(t.firstPass, `${q.firstPassApprovalRate}%`)}
          {stat(t.deliverablesAcceptedLabel, `${q.deliverablesAccepted} / ${q.deliverablesAccepted + q.deliverablesRejected}`)}
          {isAi && stat(t.usageAllTime, `${usd(q.costUsd)} · ${q.tokens.toLocaleString()} tokens`)}
        </CardContent></Card>
      </Grid>

      {data.deliverables.length > 0 && (
        <Grid size={12}>
          <Card><CardContent>
            <Typography variant="h6" gutterBottom>{t.recentDeliverables}</Typography>
            <Stack spacing={0.75}>
              {data.deliverables.map((d) => (
                <Stack key={d.id} direction="row" spacing={1} sx={{ alignItems: "center" }}>
                  <MuiLink component={Link} href={`/projects/${d.projectId}?task=${d.taskId}`} underline="hover" sx={{ flex: 1, minWidth: 0 }} noWrap>
                    📦 {d.title}
                  </MuiLink>
                  {d.isSimulated && <Chip size="small" label="🧪" />}
                  <StatusChip value={d.status} />
                  <Typography variant="caption" color="text.secondary">{new Date(d.createdAt).toLocaleDateString()}</Typography>
                </Stack>
              ))}
            </Stack>
          </CardContent></Card>
        </Grid>
      )}
    </Grid>
  );
}
