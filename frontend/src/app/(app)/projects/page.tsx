"use client";

import { useState } from "react";
import Link from "next/link";
import {
  Alert, Button, Card, CardActionArea, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, Grid,
  LinearProgress, MenuItem, Stack, TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import { api, Project, ProjectStatus } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";
import { Mascot, Rise } from "@/components/cute";

const STATUSES: ProjectStatus[] = ["Draft", "Active", "OnHold", "Completed", "Cancelled"];

export default function ProjectsPage() {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<Project[]>("/api/projects"));
  useRealtime("taskChanged", () => void reload());

  const [form, setForm] = useState<{ code: string; name: string; description: string; status: ProjectStatus; estimatedBudget: string; startDate: string; dueDate: string } | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  async function create() {
    if (!form) return;
    try {
      await api.post("/api/projects", {
        ...form,
        estimatedBudget: form.estimatedBudget ? Number(form.estimatedBudget) : null,
        startDate: form.startDate || null,
        dueDate: form.dueDate || null,
      });
      setForm(null);
      await reload();
    } catch (e) {
      setFormError(errorMessage(e));
    }
  }

  if (!data) return <Loading error={error} />;

  return (
    <>
      <PageHeader
        title={t.projects}
        actions={can("project.manage") && (
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setFormError(null); setForm({ code: "", name: "", description: "", status: "Active", estimatedBudget: "", startDate: "", dueDate: "" }); }}>
            {t.newProject}
          </Button>
        )}
      />
      {data.length === 0 && (
        <Stack sx={{ alignItems: "center", py: 6 }}>
          <Mascot size={110} />
          <Typography color="text.secondary" sx={{ mt: 1 }}>{t.empty}</Typography>
        </Stack>
      )}
      <Grid container spacing={2}>
        {data.map((p, i) => {
          const pct = p.taskCount ? Math.round((p.doneCount / p.taskCount) * 100) : 0;
          return (
            <Grid key={p.id} size={{ xs: 12, sm: 6, lg: 4 }}>
              <Rise index={i}>
              <Card sx={{ height: "100%", "&:hover": { transform: "translateY(-4px)" } }}>
                <CardActionArea component={Link} href={`/projects/${p.id}`} sx={{ height: "100%" }}>
                  <CardContent>
                    <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
                      <Typography variant="caption" color="text.secondary">{p.code}</Typography>
                      <StatusChip value={p.status} />
                    </Stack>
                    <Typography variant="h6" gutterBottom noWrap>{p.name}</Typography>
                    <LinearProgress variant="determinate" value={pct} sx={{ height: 6, borderRadius: 3 }} />
                    <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
                      {p.doneCount}/{p.taskCount} · {pct}%
                      {p.dueDate && ` · ${t.dueDate} ${p.dueDate}`}
                    </Typography>
                    {p.estimatedBudget != null && (
                      <Typography variant="body2" color="text.secondary">
                        {t.budget}: {p.estimatedBudget.toLocaleString("th-TH")} บาท
                      </Typography>
                    )}
                  </CardContent>
                </CardActionArea>
              </Card>
              </Rise>
            </Grid>
          );
        })}
      </Grid>

      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.newProject}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              {formError && <Alert severity="error">{formError}</Alert>}
              <TextField label={t.code} value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} required />
              <TextField label={t.name} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
              <TextField label={t.description} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} multiline minRows={2} />
              <TextField select label={t.status} value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as ProjectStatus })}>
                {STATUSES.map((s) => <MenuItem key={s} value={s}>{label(s)}</MenuItem>)}
              </TextField>
              <TextField label={t.budget} type="number" value={form.estimatedBudget} onChange={(e) => setForm({ ...form, estimatedBudget: e.target.value })} slotProps={{ htmlInput: { min: 0 } }} />
              <Stack direction="row" spacing={2}>
                <TextField label={t.startDate} type="date" value={form.startDate} onChange={(e) => setForm({ ...form, startDate: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} fullWidth />
                <TextField label={t.dueDate} type="date" value={form.dueDate} onChange={(e) => setForm({ ...form, dueDate: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} fullWidth />
              </Stack>
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" onClick={create} disabled={!form?.code || !form?.name}>{t.create}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
