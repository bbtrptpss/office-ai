"use client";

import { useState } from "react";
import { Card, CardContent, Chip, Grid, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography } from "@mui/material";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, StatusChip, useLoad } from "@/components/common";
import { Rise } from "@/components/cute";

interface Report {
  days: number;
  testRuns: number;
  testsPassed: number;
  testPassRate: number;
  recentTestFailures: { id: string; repositoryId: string; branch: string; status: string; startedAt: string }[];
  openMergeRequests: number;
  conflictedMergeRequests: number;
  mergedMergeRequests: number;
  failedRuns: number;
  limitReachedRuns: number;
  toolDenials: number;
  pendingApprovals: { key: string; count: number }[];
  securityEventCounts: { key: string; count: number }[];
  recentSecurityEvents: { occurredAt: string; action: string; actorName: string | null; entityId: string | null }[];
}

export default function QualityPage() {
  const { t, lang } = useI18n();
  const [days, setDays] = useState(30);
  const { data, error, reload } = useLoad(() => api.get<Report>(`/api/reports/quality-security?days=${days}`), String(days));
  useRealtime("agentRunChanged", () => void reload());
  useRealtime("approvalsChanged", () => void reload());
  if (!data) return <Loading error={error} />;

  const tiles = [
    { label: `${t.testRuns} · ${t.successRate}`, value: data.testRuns ? `${data.testPassRate}%` : "—", sub: `${data.testsPassed}/${data.testRuns}`, emoji: "🧪" },
    { label: t.mergeRequests, value: String(data.openMergeRequests), sub: `${data.mergedMergeRequests} merged · ${data.conflictedMergeRequests} conflict`, emoji: "🔀" },
    { label: "AI runs failed / limit", value: `${data.failedRuns} / ${data.limitReachedRuns}`, sub: `${data.toolDenials} ${t.denied}`, emoji: "🚧" },
    { label: t.approvals, value: String(data.pendingApprovals.reduce((s, x) => s + x.count, 0)), sub: data.pendingApprovals.map((x) => `${x.key} ${x.count}`).join(" · ") || "—", emoji: "📝" },
  ];
  const when = (s: string) => new Date(s).toLocaleString(lang === "th" ? "th-TH" : "en-GB");

  return (
    <>
      <PageHeader title={t.navQuality}
        actions={<TextField select size="small" value={days} onChange={(e) => setDays(Number(e.target.value))}>
          {[7, 30, 90].map((d) => <MenuItem key={d} value={d}>{d} days</MenuItem>)}
        </TextField>} />
      <Grid container spacing={2} sx={{ mb: 3 }}>
        {tiles.map((x, i) => (
          <Grid key={x.label} size={{ xs: 12, sm: 6, md: 3 }}>
            <Rise index={i}>
              <Card sx={{ height: "100%" }}>
                <CardContent>
                  <Stack direction="row" sx={{ justifyContent: "space-between" }}>
                    <Typography variant="body2" color="text.secondary">{x.label}</Typography>
                    <span aria-hidden>{x.emoji}</span>
                  </Stack>
                  <Typography variant="h5">{x.value}</Typography>
                  <Typography variant="caption" color="text.secondary">{x.sub}</Typography>
                </CardContent>
              </Card>
            </Rise>
          </Grid>
        ))}
      </Grid>

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 6 }}>
          <Typography variant="h6" gutterBottom>🔐 Security events</Typography>
          <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: "wrap", rowGap: 1 }}>
            {data.securityEventCounts.length === 0 && <Typography color="text.secondary">{t.empty}</Typography>}
            {data.securityEventCounts.map((c) => (
              <Chip key={c.key} label={`${c.key}: ${c.count}`} color={c.key === "PermissionChangeDenied" || c.key === "LockedOut" ? "error" : "warning"} variant="outlined" />
            ))}
          </Stack>
          <TableContainer component={Paper} variant="outlined">
            <Table size="small">
              <TableHead><TableRow><TableCell>{t.when}</TableCell><TableCell>{t.action}</TableCell><TableCell>{t.actor}</TableCell><TableCell>{t.entity}</TableCell></TableRow></TableHead>
              <TableBody>
                {data.recentSecurityEvents.map((e, i) => (
                  <TableRow key={i}>
                    <TableCell sx={{ whiteSpace: "nowrap" }}>{when(e.occurredAt)}</TableCell>
                    <TableCell>{e.action}</TableCell>
                    <TableCell>{e.actorName ?? "—"}</TableCell>
                    <TableCell sx={{ wordBreak: "break-all" }}>{e.entityId ?? "—"}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </Grid>
        <Grid size={{ xs: 12, md: 6 }}>
          <Typography variant="h6" gutterBottom>🧪 {t.testRuns} — failures</Typography>
          <Stack spacing={1}>
            {data.recentTestFailures.length === 0 && <Typography color="text.secondary">{t.empty}</Typography>}
            {data.recentTestFailures.map((f) => (
              <Paper key={f.id} variant="outlined" sx={{ p: 1.5 }}>
                <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                  <StatusChip value={f.status} />
                  <Typography variant="body2" sx={{ fontFamily: "monospace", flex: 1 }}>{f.branch}</Typography>
                  <Typography variant="caption" color="text.secondary">{when(f.startedAt)}</Typography>
                </Stack>
              </Paper>
            ))}
          </Stack>
        </Grid>
      </Grid>
    </>
  );
}
