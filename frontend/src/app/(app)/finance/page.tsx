"use client";

import { useState } from "react";
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Grid, LinearProgress,
  MenuItem, Paper, Stack, Tab, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tabs, TextField, Typography,
} from "@mui/material";
import {
  AiProvider, api, Budget, BudgetScope, Department, FinanceReport, OperatingAllocation, OperatingCost, PerformanceRow, ProfitabilityReport, Project, Scenario,
  ScenarioResult, thb, usd,
} from "@/lib/api";
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
  const [tab, setTab] = useState<"finance" | "profitability" | "performance">("finance");
  const [month, setMonth] = useState(thisMonth());
  return (
    <>
      <PageHeader
        title={t.navFinance}
        actions={tab !== "profitability" && <TextField type="month" size="small" label={t.month} value={month} onChange={(e) => setMonth(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />}
      />
      <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ mb: 2 }}>
        <Tab value="finance" label={t.finance} />
        <Tab value="profitability" label={t.profitability} />
        <Tab value="performance" label={t.performance} />
      </Tabs>
      {tab === "finance" ? <Finance month={month} /> : tab === "profitability" ? <Profitability /> : <Performance month={month} />}
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
    { label: t.operatingMonth, value: thb(data.operatingThb), hint: data.operatingEstimatedThb > 0 ? `${t.ofWhichEstimated} ${thb(data.operatingEstimatedThb)}` : undefined },
    { label: t.totalCostMonth, value: thb(data.totalWithOperatingThb) },
    { label: t.usdToThb, value: String(data.usdToThb) },
    { label: `🧪 ${t.simulated}`, value: usd(data.simulatedUsd) },
  ];

  return (
    <Stack spacing={3}>
      <Grid container spacing={2}>
        {stats.map((s, i) => (
          <Grid key={s.label} size={{ xs: 12, sm: 6, md: i < 3 ? 4 : 6 }}>
            <Rise index={i}>
              <Card sx={{ height: "100%" }}>
                <CardContent>
                  <Typography variant="body2" color="text.secondary">{s.label}</Typography>
                  <Typography variant="h5">{s.value}</Typography>
                  {"hint" in s && s.hint && <Typography variant="caption" color="text.secondary">{s.hint}</Typography>}
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
                <TableCell align="right">{t.operatingShort}</TableCell>
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
                  <TableCell align="right">{p.operatingThbMonth > 0 ? thb(p.operatingThbMonth) : "—"}</TableCell>
                </TableRow>
              ))}
              {data.projects.length === 0 && <TableRow><TableCell colSpan={7}>{t.empty}</TableCell></TableRow>}
            </TableBody>
          </Table>
        </TableContainer>
      </Box>

      <OperatingCosts report={data} projects={lookups?.projects ?? []} canManage={can("finance.manage")} onChanged={() => void reload()} />

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

const CATEGORIES = ["electricity", "ai_subscription", "hosting", "hardware", "software", "other"] as const;
const ALLOCATIONS: OperatingAllocation[] = ["AiUsage", "Project", "Overhead"];
type CostForm = {
  id?: string; version?: number; name: string; category: string; monthlyAmount: string; currency: "Thb" | "Usd"; isEstimate: boolean;
  startMonth: string; endMonth: string; allocation: OperatingAllocation; projectId: string; provider: string; notes: string;
};

/** Operating costs: this month's figures and where they went, plus the list people manage (finance.manage). */
function OperatingCosts({ report, projects, canManage, onChanged }: { report: FinanceReport; projects: Project[]; canManage: boolean; onChanged: () => void }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const { data: costs, reload } = useLoad(() => api.get<OperatingCost[]>("/api/operating-costs"));
  const { data: providers } = useLoad(() => api.get<AiProvider[]>("/api/ai/providers").catch(() => [] as AiProvider[]));
  const [form, setForm] = useState<CostForm | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const category = (c: string) => t[`cost_${c}` as keyof typeof t] as string ?? c;
  const allocation = (a: OperatingAllocation, provider: string | null) =>
    a === "AiUsage" ? `${t.allocAiUsage}${provider ? ` · ${provider}` : ""}` : a === "Project" ? t.allocProject : t.allocOverhead;
  const changed = async () => { await reload(); onChanged(); };

  async function save() {
    if (!form) return;
    const body = {
      name: form.name, category: form.category, monthlyAmount: Number(form.monthlyAmount), currency: form.currency, isEstimate: form.isEstimate,
      startMonth: `${form.startMonth}-01`, endMonth: form.endMonth ? `${form.endMonth}-01` : null, allocation: form.allocation,
      projectId: form.allocation === "Project" ? form.projectId || null : null, provider: form.allocation === "AiUsage" ? form.provider || null : null,
      notes: form.notes || null, version: form.version,
    };
    try {
      if (form.id) await api.put(`/api/operating-costs/${form.id}`, body);
      else await api.post("/api/operating-costs", body);
      setForm(null);
      setProblem(null);
    } catch (e) { setProblem(errorMessage(e)); }
    await changed();
  }

  return (
    <Box>
      <Stack direction="row" sx={{ alignItems: "center", mb: 0.5 }}>
        <Typography variant="h6" sx={{ flex: 1 }}>{t.operatingCosts}</Typography>
        {canManage && <Button size="small" variant="outlined" onClick={() => setForm({
          name: "", category: "electricity", monthlyAmount: "", currency: "Thb", isEstimate: true, startMonth: report.month.slice(0, 7), endMonth: "",
          allocation: "AiUsage", projectId: "", provider: "ollama", notes: "",
        })}>{t.addOperatingCost}</Button>}
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{t.operatingHint}</Typography>
      {report.operating.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
        <Stack spacing={1}>
          {report.operating.map((o) => (
            <Paper key={o.id} variant="outlined" sx={{ p: 1.5 }}>
              <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap" }}>
                <Typography fontWeight={600} sx={{ flex: 1, minWidth: 160 }}>{o.name}</Typography>
                <Typography variant="caption" color="text.secondary">{category(o.category)} · {allocation(o.allocation, o.provider)}{o.isEstimate ? ` · ${t.estimated}` : ""}</Typography>
                <Typography fontWeight={600}>{thb(o.thb)}</Typography>
              </Stack>
              <Typography variant="caption" color="text.secondary" component="div">
                {[...o.projects.map((p) => `${p.name} ${thb(p.thb)}`), ...(o.unallocatedThb > 0 ? [`${o.allocation === "Overhead" ? t.allocOverhead : t.unallocated} ${thb(o.unallocatedThb)}`] : [])].join(" · ")}
              </Typography>
            </Paper>
          ))}
        </Stack>
      )}
      {canManage && (costs?.length ?? 0) > 0 && (
        <TableContainer component={Paper} variant="outlined" sx={{ mt: 1.5 }}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{t.displayName}</TableCell>
                <TableCell align="right">{t.perMonth}</TableCell>
                <TableCell>{t.period}</TableCell>
                <TableCell>{t.allocation}</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {costs!.map((c) => (
                <TableRow key={c.id}>
                  <TableCell>{c.name}{c.notes && <Typography variant="caption" color="text.secondary" component="div">{c.notes}</Typography>}</TableCell>
                  <TableCell align="right">{c.currency === "Usd" ? `${usd(c.monthlyAmount)} · ` : ""}{thb(c.monthlyThb)}{c.isEstimate ? ` (${t.estimated})` : ""}</TableCell>
                  <TableCell>{c.startMonth.slice(0, 7)} → {c.endMonth ? c.endMonth.slice(0, 7) : t.ongoing}</TableCell>
                  <TableCell>{c.allocation === "Project" ? c.projectName : allocation(c.allocation, c.provider)}</TableCell>
                  <TableCell align="right" sx={{ whiteSpace: "nowrap" }}>
                    <Button size="small" onClick={() => setForm({
                      id: c.id, version: c.version, name: c.name, category: c.category, monthlyAmount: String(c.monthlyAmount), currency: c.currency, isEstimate: c.isEstimate,
                      startMonth: c.startMonth.slice(0, 7), endMonth: c.endMonth?.slice(0, 7) ?? "", allocation: c.allocation, projectId: c.projectId ?? "",
                      provider: c.provider ?? "", notes: c.notes ?? "",
                    })}>{t.edit}</Button>
                    <Button size="small" color="error" onClick={async () => {
                      try { await api.del(`/api/operating-costs/${c.id}`); } catch (e) { setProblem(errorMessage(e)); }
                      await changed();
                    }}>{t.delete}</Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {problem && !form && <Alert severity="warning" sx={{ mt: 1 }} onClose={() => setProblem(null)}>{problem}</Alert>}

      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{form?.id ? t.edit : t.addOperatingCost}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              {problem && <Alert severity="warning">{problem}</Alert>}
              <TextField label={t.displayName} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
              <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                <TextField select fullWidth label={t.category} value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })}>
                  {CATEGORIES.map((c) => <MenuItem key={c} value={c}>{category(c)}</MenuItem>)}
                </TextField>
                <TextField fullWidth type="number" label={t.perMonth} value={form.monthlyAmount} onChange={(e) => setForm({ ...form, monthlyAmount: e.target.value })} />
                <TextField select label={t.currency} value={form.currency} onChange={(e) => setForm({ ...form, currency: e.target.value as "Thb" | "Usd" })} sx={{ minWidth: 100 }}>
                  <MenuItem value="Thb">THB</MenuItem>
                  <MenuItem value="Usd">USD</MenuItem>
                </TextField>
              </Stack>
              <FormControlLabel control={<Checkbox checked={form.isEstimate} onChange={(e) => setForm({ ...form, isEstimate: e.target.checked })} />} label={t.isEstimateLabel} />
              <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                <TextField fullWidth type="month" label={t.startMonth} value={form.startMonth} onChange={(e) => setForm({ ...form, startMonth: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
                <TextField fullWidth type="month" label={t.endMonthOptional} value={form.endMonth} onChange={(e) => setForm({ ...form, endMonth: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
              </Stack>
              <TextField select label={t.allocation} value={form.allocation} onChange={(e) => setForm({ ...form, allocation: e.target.value as OperatingAllocation })}
                helperText={form.allocation === "AiUsage" ? t.allocAiUsageHint : form.allocation === "Overhead" ? t.allocOverheadHint : undefined}>
                {ALLOCATIONS.map((a) => <MenuItem key={a} value={a}>{allocation(a, null)}</MenuItem>)}
              </TextField>
              {form.allocation === "Project" && (
                <TextField select label={t.projects} value={form.projectId} onChange={(e) => setForm({ ...form, projectId: e.target.value })}>
                  {projects.map((p) => <MenuItem key={p.id} value={p.id}>{p.code} {p.name}</MenuItem>)}
                </TextField>
              )}
              {form.allocation === "AiUsage" && (
                <TextField select label={t.providerFilter} value={form.provider} onChange={(e) => setForm({ ...form, provider: e.target.value })}>
                  <MenuItem value="">{t.allProviders}</MenuItem>
                  {(providers ?? []).filter((p) => !p.isSimulated).map((p) => <MenuItem key={p.name} value={p.name}>{p.name}</MenuItem>)}
                  {form.provider && !(providers ?? []).some((p) => p.name === form.provider) && <MenuItem value={form.provider}>{form.provider}</MenuItem>}
                </TextField>
              )}
              <TextField label={t.notes} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} multiline minRows={2} helperText={t.operatingNotesHint} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => { setForm(null); setProblem(null); }}>{t.cancel}</Button>
          <Button variant="contained" disabled={!form?.name.trim() || form.monthlyAmount === "" || !form.startMonth || (form.allocation === "Project" && !form.projectId)} onClick={() => void save()}>{t.save}</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}

function Profitability() {
  const { t } = useI18n();
  const { data, error, reload } = useLoad(() => api.get<ProfitabilityReport>("/api/reports/profitability"));
  useRealtime("agentRunChanged", () => void reload());
  if (!data) return <Loading error={error} />;
  if (data.projects.length === 0) return <Typography color="text.secondary">{t.noApprovedEstimate}</Typography>;
  return (
    <Stack spacing={3}>
      <Box>
        <Typography variant="h6">{t.profitTitle}</Typography>
        <Typography variant="body2" color="text.secondary" gutterBottom>{t.profitNote}</Typography>
        <TableContainer component={Paper} variant="outlined">
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{t.projects}</TableCell>
                <TableCell align="right">{t.priceLabel}</TableCell>
                <TableCell align="right">{t.plannedMargin}</TableCell>
                <TableCell align="right">{t.planned} AI</TableCell>
                <TableCell align="right">{t.actual} AI</TableCell>
                <TableCell align="right">{t.aiOverrun}</TableCell>
                <TableCell align="right">{t.operatingShort}</TableCell>
                <TableCell align="right">{t.forecastMargin}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.projects.map((p) => (
                <TableRow key={p.projectId}>
                  <TableCell>
                    <b>{p.code}</b> {p.name} <Typography component="span" variant="caption" color="text.secondary">r{p.estimateRevision}</Typography>
                    {p.atRisk && <Typography component="span" variant="caption" color="error.main" sx={{ ml: 1, fontWeight: 700 }}>⚠ {t.atRisk}</Typography>}
                  </TableCell>
                  <TableCell align="right">{thb(p.priceThb)}</TableCell>
                  <TableCell align="right">{thb(p.plannedMarginThb)} · {p.plannedMarginPercent}%</TableCell>
                  <TableCell align="right">{thb(p.plannedAiThb)}</TableCell>
                  <TableCell align="right">{thb(p.actualAiThb)}</TableCell>
                  <TableCell align="right" sx={{ color: p.aiOverrunThb > 0 ? "error.main" : undefined }}>{p.aiOverrunThb > 0 ? `+${thb(p.aiOverrunThb)}` : "—"}</TableCell>
                  <TableCell align="right">{p.operatingThb > 0 ? `+${thb(p.operatingThb)}` : "—"}</TableCell>
                  <TableCell align="right" sx={{ fontWeight: 700, color: p.forecastMarginThb < 0 ? "error.main" : p.atRisk ? "warning.main" : "success.main" }}>
                    {thb(p.forecastMarginThb)} · {p.forecastMarginPercent}%
                  </TableCell>
                </TableRow>
              ))}
              <TableRow>
                <TableCell><b>{t.totalLabel}</b></TableCell>
                <TableCell align="right"><b>{thb(data.totalPriceThb)}</b></TableCell>
                <TableCell align="right"><b>{thb(data.totalPlannedMarginThb)}</b></TableCell>
                <TableCell colSpan={4} />
                <TableCell align="right"><b>{thb(data.totalForecastMarginThb)}</b></TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </TableContainer>
      </Box>
      <ScenarioPanel projects={data.projects.map((p) => ({ id: p.projectId, label: `${p.code} ${p.name}` }))} />
    </Stack>
  );
}

function ScenarioPanel({ projects }: { projects: { id: string; label: string }[] }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [projectId, setProjectId] = useState(projects[0]?.id ?? "");
  const [form, setForm] = useState({ hours: "0", rate: "0", aiUsd: "", contingency: "", price: "", reprice: false });
  const [result, setResult] = useState<Scenario | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const num = (v: string) => (v.trim() === "" ? null : Number(v));

  const calculate = async () => {
    try {
      setResult(await api.post<Scenario>("/api/reports/scenario", {
        projectId, hoursChangePercent: num(form.hours) ?? 0, rateChangePercent: num(form.rate) ?? 0, aiCostUsd: num(form.aiUsd),
        contingencyPercent: num(form.contingency), priceThb: form.reprice ? null : num(form.price), repriceAtTargetMargin: form.reprice,
      }));
      setMessage(null);
    } catch (e) {
      setMessage(errorMessage(e));
      setResult(null);
    }
  };

  const rows: [string, (r: ScenarioResult) => string][] = [
    [t.hoursLabel, (r) => r.totalHours.toLocaleString()],
    [t.laborCost, (r) => thb(r.laborCost)],
    ["AI", (r) => thb(r.aiCost)],
    [t.contingencyLabel, (r) => thb(r.contingency)],
    [t.totalCost, (r) => thb(r.totalCost)],
    [t.priceLabel, (r) => thb(r.price)],
    [t.marginLabel, (r) => `${thb(r.grossMargin)} · ${r.grossMarginPercent}%`],
  ];
  const field = (key: "hours" | "rate" | "aiUsd" | "contingency", label: string) => (
    <TextField fullWidth size="small" type="number" label={label} value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} />
  );

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="h6">{t.scenarioTitle}</Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>{t.scenarioNote}</Typography>
      <Grid container spacing={2} sx={{ mt: 0.5, alignItems: "center" }}>
        <Grid size={{ xs: 12, md: 4 }}>
          <TextField select fullWidth size="small" label={t.projects} value={projectId} onChange={(e) => { setProjectId(e.target.value); setResult(null); }}>
            {projects.map((p) => <MenuItem key={p.id} value={p.id}>{p.label}</MenuItem>)}
          </TextField>
        </Grid>
        <Grid size={{ xs: 6, md: 2 }}>{field("hours", t.hoursChange)}</Grid>
        <Grid size={{ xs: 6, md: 2 }}>{field("rate", t.rateChange)}</Grid>
        <Grid size={{ xs: 6, md: 2 }}>{field("aiUsd", t.aiCostUsdLabel)}</Grid>
        <Grid size={{ xs: 6, md: 2 }}>{field("contingency", t.contingencyLabel)}</Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <TextField fullWidth size="small" type="number" label={t.priceOverride} value={form.price} disabled={form.reprice} onChange={(e) => setForm({ ...form, price: e.target.value })} />
        </Grid>
        <Grid size={{ xs: 12, md: 5 }}>
          <FormControlLabel control={<Checkbox checked={form.reprice} onChange={(e) => setForm({ ...form, reprice: e.target.checked })} />} label={t.repriceAtTarget} />
        </Grid>
        <Grid size={{ xs: 12, md: 3 }}>
          <Button fullWidth variant="contained" disabled={!projectId} onClick={() => void calculate()}>{t.runScenario}</Button>
        </Grid>
      </Grid>
      {message && <Alert severity="warning" sx={{ mt: 2 }}>{message}</Alert>}
      {result && (
        <TableContainer sx={{ mt: 2 }}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell />
                <TableCell align="right">{t.baseline} (r{result.estimateRevision})</TableCell>
                <TableCell align="right">{t.scenario}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map(([label, f]) => (
                <TableRow key={label}>
                  <TableCell>{label}</TableCell>
                  <TableCell align="right">{f(result.baseline)}</TableCell>
                  <TableCell align="right">{f(result.scenario)}</TableCell>
                </TableRow>
              ))}
              <TableRow>
                <TableCell><b>{t.marginChange}</b></TableCell>
                <TableCell />
                <TableCell align="right" sx={{ fontWeight: 700, color: result.marginChangeThb < 0 ? "error.main" : "success.main" }}>
                  {result.marginChangeThb > 0 ? "+" : ""}{thb(result.marginChangeThb)}
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </TableContainer>
      )}
    </Paper>
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
