"use client";

import { useState } from "react";
import {
  Alert, Box, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, Grid, LinearProgress, MenuItem, Paper, Stack, Tab,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, TextField, Typography,
} from "@mui/material";
import { api, Budget, BudgetScope, Department, FinanceReport, PerformanceRow, Project, thb, usd } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";
import { Avatar, Rise } from "@/components/cute";

const thisMonth = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
};

export default function FinancePage() {
  const { t } = useI18n();
  const [tab, setTab] = useState<"finance" | "performance">("finance");
  const [month, setMonth] = useState(thisMonth());
  return (
    <>
      <PageHeader
        title={t.navFinance}
        actions={<TextField type="month" size="small" label={t.month} value={month} onChange={(e) => setMonth(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />}
      />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
        <Tab value="finance" label={t.finance} />
        <Tab value="performance" label={t.performance} />
      </Tabs>
      {tab === "finance" ? <Finance month={month} /> : <Performance month={month} />}
    </>
  );
}

function Finance({ month }: { month: string }) {
  const { t } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<FinanceReport>(`/api/reports/finance?month=${month}-01`), month);
  useRealtime("agentRunChanged", () => void reload());
  const [form, setForm] = useState<{ scope: BudgetScope; scopeId: string; amountUsd: number } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const { data: lookups } = useLoad(async () => {
    const [departments, projects] = await Promise.all([api.get<Department[]>("/api/departments"), api.get<Project[]>("/api/projects")]);
    return { departments, projects };
  });

  if (!data) return <Loading error={error} />;
  const stats = [
    { label: `${t.aiCostMonth} · ${t.realOnly}`, value: `${usd(data.totalUsd)} · ${thb(data.totalThb)}` },
    { label: t.usdToThb, value: String(data.usdToThb) },
    { label: `🧪 ${t.simulated}`, value: usd(data.simulatedUsd) },
  ];

  return (
    <Stack spacing={3}>
      <Grid container spacing={2}>
        {stats.map((s, i) => (
          <Grid key={s.label} size={{ xs: 12, md: 4 }}>
            <Rise index={i}>
              <Card sx={{ height: "100%" }}>
                <CardContent>
                  <Typography variant="body2" color="text.secondary">{s.label}</Typography>
                  <Typography variant="h5">{s.value}</Typography>
                </CardContent>
              </Card>
            </Rise>
          </Grid>
        ))}
      </Grid>

      <Box>
        <Stack direction="row" sx={{ alignItems: "center", mb: 1 }}>
          <Typography variant="h6" sx={{ flex: 1 }}>{t.budgets}</Typography>
          {can("finance.manage") && <Button size="small" variant="outlined" onClick={() => setForm({ scope: "Company", scopeId: "", amountUsd: 50 })}>{t.setBudget}</Button>}
        </Stack>
        {message && <Alert severity="warning" sx={{ mb: 1 }}>{message}</Alert>}
        {data.budgets.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
          <Stack spacing={1}>
            {data.budgets.map((b: Budget) => {
              const pct = b.amountUsd > 0 ? Math.min(100, (b.spentUsd / b.amountUsd) * 100) : 100;
              return (
                <Paper key={b.id} variant="outlined" sx={{ p: 1.5 }}>
                  <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                    <Typography fontWeight={600} sx={{ flex: 1 }}>{b.scope === "Company" ? t.company : b.scopeName}</Typography>
                    <Typography variant="body2">{usd(b.spentUsd)} / {usd(b.amountUsd)}</Typography>
                    <StatusChip value={b.level} />
                  </Stack>
                  <LinearProgress variant="determinate" value={pct} color={b.level === "Exceeded" ? "error" : b.level === "Warning" ? "warning" : "primary"} sx={{ mt: 1, height: 8 }} />
                </Paper>
              );
            })}
          </Stack>
        )}
      </Box>

      <Box>
        <Typography variant="h6" gutterBottom>{t.projects}: {t.planned} vs {t.actual}</Typography>
        <TableContainer component={Paper} variant="outlined">
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{t.projects}</TableCell>
                <TableCell align="right">{t.estimateLabel}</TableCell>
                <TableCell align="right">{t.planned} AI</TableCell>
                <TableCell align="right">{t.actual} AI</TableCell>
                <TableCell align="right">{t.variance}</TableCell>
                <TableCell align="right">{t.month}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.projects.map((p) => (
                <TableRow key={p.projectId}>
                  <TableCell><b>{p.code}</b> {p.name}</TableCell>
                  <TableCell align="right">{p.priceThb != null ? `r${p.estimateRevision} · ${thb(p.priceThb)}` : "—"}</TableCell>
                  <TableCell align="right">{p.plannedAiCostThb != null ? thb(p.plannedAiCostThb) : "—"}</TableCell>
                  <TableCell align="right">{thb(p.actualAiThbAllTime)}</TableCell>
                  <TableCell align="right" sx={{ color: (p.aiVarianceThb ?? 0) > 0 ? "error.main" : "success.main" }}>
                    {p.aiVarianceThb != null ? `${p.aiVarianceThb > 0 ? "+" : ""}${thb(p.aiVarianceThb)}` : "—"}
                  </TableCell>
                  <TableCell align="right">{usd(p.actualAiUsdMonth)}</TableCell>
                </TableRow>
              ))}
              {data.projects.length === 0 && <TableRow><TableCell colSpan={6}>{t.empty}</TableCell></TableRow>}
            </TableBody>
          </Table>
        </TableContainer>
      </Box>

      <Grid container spacing={2}>
        {([[t.departmentsSpend, data.departments], [t.employeesSpend, data.employees]] as const).map(([title, rows]) => (
          <Grid key={title} size={{ xs: 12, md: 6 }}>
            <Typography variant="h6" gutterBottom>{title}</Typography>
            <Paper variant="outlined">
              {rows.length === 0 && <Typography sx={{ p: 2 }} color="text.secondary">{t.empty}</Typography>}
              {rows.map((r) => (
                <Stack key={r.id} direction="row" spacing={1} sx={{ px: 2, py: 1, alignItems: "center", borderBottom: 1, borderColor: "divider" }}>
                  <Typography sx={{ flex: 1 }} noWrap>{r.name}</Typography>
                  <Typography variant="caption" color="text.secondary">{r.runs} {t.runs} · {r.tokens.toLocaleString()} tok</Typography>
                  <Typography fontWeight={600}>{usd(r.costUsd)}</Typography>
                </Stack>
              ))}
            </Paper>
          </Grid>
        ))}
      </Grid>

      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t.setBudget} · {month}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField select label={t.scope} value={form.scope} onChange={(e) => setForm({ ...form, scope: e.target.value as BudgetScope, scopeId: "" })}>
                <MenuItem value="Company">{t.company}</MenuItem>
                <MenuItem value="Department">{t.department}</MenuItem>
                <MenuItem value="Project">{t.projects}</MenuItem>
              </TextField>
              {form.scope !== "Company" && (
                <TextField select label={form.scope === "Department" ? t.department : t.projects} value={form.scopeId} onChange={(e) => setForm({ ...form, scopeId: e.target.value })}>
                  {(form.scope === "Department" ? lookups?.departments ?? [] : lookups?.projects ?? []).map((x) => <MenuItem key={x.id} value={x.id}>{x.name}</MenuItem>)}
                </TextField>
              )}
              <TextField label={t.amountUsd} type="number" value={form.amountUsd} onChange={(e) => setForm({ ...form, amountUsd: Number(e.target.value) })} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!form || (form.scope !== "Company" && !form.scopeId)} onClick={async () => {
            try {
              await api.put("/api/budgets", { scope: form!.scope, scopeId: form!.scope === "Company" ? null : form!.scopeId, month: `${month}-01`, amountUsd: form!.amountUsd });
              setForm(null);
              setMessage(null);
            } catch (e) { setMessage(errorMessage(e)); }
            await reload();
          }}>{t.save}</Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}

function Performance({ month }: { month: string }) {
  const { t } = useI18n();
  const { data, error } = useLoad(() => api.get<{ rows: PerformanceRow[] }>(`/api/reports/performance?month=${month}-01`), month);
  if (!data) return <Loading error={error} />;
  if (data.rows.length === 0) return <Typography color="text.secondary">{t.empty}</Typography>;
  return (
    <TableContainer component={Paper} variant="outlined">
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>{t.displayName}</TableCell>
            <TableCell align="right">{t.runs}</TableCell>
            <TableCell align="right">{t.successRate}</TableCell>
            <TableCell align="right">{t.costPerRun}</TableCell>
            <TableCell align="right">{t.turns}</TableCell>
            <TableCell align="right">{t.denied}</TableCell>
            <TableCell align="right">{t.tasksDoneShort}</TableCell>
            <TableCell align="right">{t.firstPass}</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {data.rows.map((r) => (
            <TableRow key={r.employeeId}>
              <TableCell>
                <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                  <Avatar kind="AI" seed={r.name} size={28} />
                  <Box>
                    <Typography variant="body2" fontWeight={600}>{r.name}</Typography>
                    <Typography variant="caption" color="text.secondary">{r.jobTitle} · {r.department}</Typography>
                  </Box>
                </Stack>
              </TableCell>
              <TableCell align="right">{r.runs}</TableCell>
              <TableCell align="right">{r.successRate}%</TableCell>
              <TableCell align="right">{usd(r.costPerRunUsd)}</TableCell>
              <TableCell align="right">{r.avgTurns}</TableCell>
              <TableCell align="right" sx={{ color: r.toolDenied ? "warning.main" : undefined }}>{r.toolDenied}</TableCell>
              <TableCell align="right">{r.tasksDone}</TableCell>
              <TableCell align="right">{r.reviewsApproved + r.changesRequested > 0 ? `${r.firstPassApprovalRate}%` : "—"}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
