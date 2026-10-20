"use client";

import { useState } from "react";
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField } from "@mui/material";
import { api, Project, ProjectStatus } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { useErrorMessage } from "@/components/common";

const STATUSES: ProjectStatus[] = ["Draft", "Active", "OnHold", "Completed", "Cancelled"];

interface Form { code: string; name: string; description: string; status: ProjectStatus; estimatedBudget: string; startDate: string; dueDate: string }

/**
 * Edits a project's details and status. Switching to Cancelled asks first: the server then cancels the project's
 * unfinished tasks, stops AI runs in it and withdraws pending approvals.
 */
export function ProjectEditDialog({ project, openTasks, onClose, onSaved }: {
  project: Project;
  /** Unfinished tasks that cancelling would cancel, for the warning. */
  openTasks: number;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t, label } = useI18n();
  const errorMessage = useErrorMessage();
  const [form, setForm] = useState<Form>({
    code: project.code, name: project.name, description: project.description ?? "", status: project.status,
    estimatedBudget: project.estimatedBudget?.toString() ?? "", startDate: project.startDate ?? "", dueDate: project.dueDate ?? "",
  });
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [saving, setSaving] = useState(false);
  const cancelling = form.status === "Cancelled" && project.status !== "Cancelled";

  async function save() {
    if (cancelling && !confirming) {
      setConfirming(true);
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await api.put(`/api/projects/${project.id}`, {
        ...form,
        ownerEmployeeId: project.ownerEmployeeId,
        estimatedBudget: form.estimatedBudget ? Number(form.estimatedBudget) : null,
        startDate: form.startDate || null,
        dueDate: form.dueDate || null,
        version: project.version,
      });
      onSaved();
    } catch (e) {
      setError(errorMessage(e));
      setConfirming(false);
    } finally {
      setSaving(false);
    }
  }

  if (confirming) {
    return (
      <Dialog open onClose={() => setConfirming(false)} fullWidth maxWidth="xs">
        <DialogTitle>{t.cancelProjectTitle}</DialogTitle>
        <DialogContent>
          <Alert severity="warning">{t.cancelProjectWarning.replace("{n}", String(openTasks))}</Alert>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirming(false)} disabled={saving}>{t.cancel}</Button>
          <Button variant="contained" color="error" onClick={save} disabled={saving}>{t.cancelProjectConfirm}</Button>
        </DialogActions>
      </Dialog>
    );
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t.editProject}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
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
      <DialogActions>
        <Button onClick={onClose}>{t.cancel}</Button>
        <Button variant="contained" color={cancelling ? "error" : "primary"} onClick={save} disabled={!form.code || !form.name || saving}>{t.save}</Button>
      </DialogActions>
    </Dialog>
  );
}
