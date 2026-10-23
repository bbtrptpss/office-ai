"use client";

import { useState } from "react";
import { Alert, Button, Card, CardContent, IconButton, MenuItem, Paper, Stack, TextField, Tooltip, Typography } from "@mui/material";
import DeleteIcon from "@mui/icons-material/DeleteOutline";
import { api, Project, Task, TimeEntry } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, useErrorMessage, useLoad } from "@/components/common";

const today = () => new Date().toISOString().slice(0, 10);
const daysAgo = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString().slice(0, 10);

/** A person's own working hours: log time per project (and task), see the last two weeks by day. */
export default function TimesheetPage() {
  const { t, lang } = useI18n();
  const errorMessage = useErrorMessage();
  const [from, setFrom] = useState(daysAgo(13));
  const { data: entries, error, reload } = useLoad(() => api.get<TimeEntry[]>(`/api/timesheet?from=${from}&to=${today()}`), from);
  const { data: projects } = useLoad(() => api.get<Project[]>("/api/projects"));
  const [form, setForm] = useState({ projectId: "", taskId: "", date: today(), hours: "", note: "" });
  const { data: tasks } = useLoad(() => (form.projectId ? api.get<Task[]>(`/api/projects/${form.projectId}/tasks`) : Promise.resolve([] as Task[])), form.projectId);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (!entries) return <Loading error={error} />;
  const open = (projects ?? []).filter((p) => p.status !== "Cancelled" && p.status !== "Completed");
  const byDay = [...new Set(entries.map((e) => e.date))].map((d) => ({ date: d, rows: entries.filter((e) => e.date === d) }));
  const total = entries.reduce((s, e) => s + e.hours, 0);
  const dayLabel = (d: string) => new Date(`${d}T00:00:00`).toLocaleDateString(lang === "th" ? "th-TH" : "en-GB", { weekday: "short", day: "numeric", month: "short" });

  async function run(fn: () => Promise<unknown>) {
    setBusy(true);
    setProblem(null);
    try { await fn(); } catch (e) { setProblem(errorMessage(e)); }
    setBusy(false);
    await reload();
  }

  return (
    <>
      <PageHeader title={t.navTimesheet} subtitle={t.timesheetHint} />
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Typography variant="h6" sx={{ mb: 1.5 }}>{t.logHours}</Typography>
          {problem && <Alert severity="warning" sx={{ mb: 1.5 }} onClose={() => setProblem(null)}>{problem}</Alert>}
          <Stack direction={{ xs: "column", md: "row" }} spacing={1.5}>
            <TextField size="small" type="date" label={t.date} value={form.date} onChange={(e) => setForm({ ...form, date: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
            <TextField select size="small" label={t.projects} value={form.projectId} sx={{ minWidth: 220 }} onChange={(e) => setForm({ ...form, projectId: e.target.value, taskId: "" })}>
              {open.map((p) => <MenuItem key={p.id} value={p.id}>{p.code} {p.name}</MenuItem>)}
            </TextField>
            <TextField select size="small" label={t.taskOptional} value={form.taskId} sx={{ minWidth: 200 }} disabled={!form.projectId} onChange={(e) => setForm({ ...form, taskId: e.target.value })}>
              <MenuItem value="">—</MenuItem>
              {(tasks ?? []).filter((x) => x.status !== "Cancelled").map((x) => <MenuItem key={x.id} value={x.id}>{x.title}</MenuItem>)}
            </TextField>
            <TextField size="small" type="number" label={t.hoursLabel} value={form.hours} sx={{ width: 110 }} onChange={(e) => setForm({ ...form, hours: e.target.value })}
              slotProps={{ htmlInput: { min: 0.25, max: 24, step: 0.25 } }} />
            <TextField size="small" label={t.notes} value={form.note} sx={{ flex: 1 }} onChange={(e) => setForm({ ...form, note: e.target.value })} />
            <Button variant="contained" disabled={busy || !form.projectId || !form.hours} onClick={() => run(async () => {
              await api.post("/api/timesheet", { projectId: form.projectId, taskId: form.taskId || null, date: form.date, hours: Number(form.hours), note: form.note || null });
              setForm({ ...form, hours: "", note: "" });
            })}>{t.save}</Button>
          </Stack>
        </CardContent>
      </Card>

      <Stack direction="row" spacing={2} sx={{ alignItems: "center", mb: 1 }}>
        <Typography variant="h6" sx={{ flex: 1 }}>{t.myHours}: {total} {t.hoursUnit}</Typography>
        <TextField size="small" type="date" label={t.since} value={from} onChange={(e) => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
      </Stack>
      {byDay.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
        <Stack spacing={1}>
          {byDay.map((d) => (
            <Paper key={d.date} variant="outlined" sx={{ p: 1.5 }}>
              <Stack direction="row" sx={{ mb: 0.5 }}>
                <Typography fontWeight={600} sx={{ flex: 1 }}>{dayLabel(d.date)}</Typography>
                <Typography fontWeight={600}>{d.rows.reduce((s, e) => s + e.hours, 0)} {t.hoursUnit}</Typography>
              </Stack>
              {d.rows.map((e) => (
                <Stack key={e.id} direction="row" spacing={1} sx={{ alignItems: "center" }}>
                  <Typography variant="body2" sx={{ flex: 1, minWidth: 0 }} noWrap>
                    <b>{e.projectCode}</b> {e.taskTitle ?? e.projectName}{e.note ? ` · ${e.note}` : ""}
                  </Typography>
                  <Typography variant="body2">{e.hours} {t.hoursUnit}</Typography>
                  <Tooltip title={t.delete}>
                    <IconButton size="small" disabled={busy} onClick={() => run(() => api.del(`/api/timesheet/${e.id}`))}><DeleteIcon fontSize="small" /></IconButton>
                  </Tooltip>
                </Stack>
              ))}
            </Paper>
          ))}
        </Stack>
      )}
    </>
  );
}
