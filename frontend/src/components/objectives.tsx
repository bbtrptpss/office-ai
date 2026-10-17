"use client";

import { useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import {
  Alert, Box, Button, Card, CardContent, Collapse, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, LinearProgress,
  MenuItem, Stack, TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import { api, Employee, usd, Workflow, WorkflowEvent } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { StatusChip, useErrorMessage, useLoad } from "./common";
import { Avatar } from "./cute";

const EVENT_EMOJI: Record<string, string> = {
  started: "🚀", plan_submitted: "🗺️", plan_approved: "✅", plan_rejected: "↩️", assigned: "📌", reviewer: "🔍", run_started: "💜",
  review: "📝", approval_requested: "🙋", task_done: "🎀", task_rejected: "↩️", escalated: "🚨", delegated: "🤝", completed: "🎉",
  cancelled: "🛑", budget_reached: "💸", approval_expired: "⌛",
};

export function ObjectivesPanel({ projectId, employees }: { projectId: string; employees: Employee[] }) {
  const { t } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const reduce = useReducedMotion();
  const { data, reload } = useLoad(() => api.get<Workflow[]>(`/api/workflows?projectId=${projectId}`), projectId);
  useRealtime<{ projectId: string }>("taskChanged", (e) => e.projectId === projectId && void reload());
  useRealtime("approvalsChanged", () => void reload());
  useRealtime<{ projectId: string }>("agentRunChanged", (e) => e.projectId === projectId && void reload());

  const planners = employees.filter((e) => e.kind === "AI" && e.status !== "Inactive");
  const [form, setForm] = useState<{ objective: string; plannerEmployeeId: string; budgetUsd: number; maxParallelRuns: number } | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function start() {
    if (!form) return;
    try {
      await api.post(`/api/projects/${projectId}/objectives`, { ...form, plannerEmployeeId: form.plannerEmployeeId || null });
      setForm(null);
      await reload();
    } catch (e) {
      setMessage(errorMessage(e));
    }
  }

  if (!data) return null;
  return (
    <Box sx={{ mb: 3 }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
        <Typography variant="h6" sx={{ flex: 1 }}>🗺️ {t.objectives}</Typography>
        {can("project.manage") && (
          <Button size="small" variant="outlined" startIcon={<AddIcon />}
            onClick={() => { setMessage(null); setForm({ objective: "", plannerEmployeeId: planners.find((p) => p.jobTitle === "COO")?.id ?? "", budgetUsd: 20, maxParallelRuns: 3 }); }}>
            {t.newObjective}
          </Button>
        )}
      </Stack>
      <Stack spacing={1}>
        <AnimatePresence initial={false}>
          {data.map((w) => (
            <motion.div key={w.id} layout initial={reduce ? false : { opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }}>
              <WorkflowCard workflow={w} onChanged={reload} />
            </motion.div>
          ))}
        </AnimatePresence>
      </Stack>

      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.newObjective}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              {message && <Alert severity="error">{message}</Alert>}
              <TextField label={t.objective} value={form.objective} multiline minRows={3} autoFocus
                onChange={(e) => setForm({ ...form, objective: e.target.value })} />
              <TextField select label={t.planner} value={form.plannerEmployeeId} onChange={(e) => setForm({ ...form, plannerEmployeeId: e.target.value })}>
                {planners.map((p) => <MenuItem key={p.id} value={p.id}>🤖 {p.displayName} — {p.jobTitle}</MenuItem>)}
              </TextField>
              <Stack direction="row" spacing={2}>
                <TextField label={t.budgetUsd} type="number" value={form.budgetUsd} fullWidth
                  onChange={(e) => setForm({ ...form, budgetUsd: Number(e.target.value) })} slotProps={{ htmlInput: { min: 0 } }} />
                <TextField label={t.maxParallel} type="number" value={form.maxParallelRuns} fullWidth
                  onChange={(e) => setForm({ ...form, maxParallelRuns: Number(e.target.value) })} slotProps={{ htmlInput: { min: 1, max: 20 } }} />
              </Stack>
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!form?.objective.trim()} onClick={start}>{t.start}</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}

function WorkflowCard({ workflow: w, onChanged }: { workflow: Workflow; onChanged: () => void }) {
  const { t, lang } = useI18n();
  const { can } = useAuth();
  const [open, setOpen] = useState(false);
  const pct = w.taskCount ? Math.round((w.doneCount / w.taskCount) * 100) : 0;
  const spentPct = w.budgetUsd > 0 ? Math.min(100, (w.spentUsd / w.budgetUsd) * 100) : 0;
  const isOpen = ["Planning", "AwaitingPlanApproval", "Executing"].includes(w.status);

  return (
    <Card>
      <CardContent sx={{ pb: "12px !important" }}>
        <Stack direction="row" spacing={1.5} sx={{ alignItems: "center" }}>
          <Avatar kind="AI" seed={w.plannerName} size={36} />
          <Box sx={{ flex: 1, minWidth: 0 }}>
            <Typography fontWeight={600} noWrap>{w.objective}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t.planner}: {w.plannerName} · {w.doneCount}/{w.taskCount} · {t.spent} {usd(w.spentUsd)} / {usd(w.budgetUsd)}
            </Typography>
          </Box>
          <StatusChip value={w.status} />
          <IconButton size="small" onClick={() => setOpen(!open)} aria-label={t.timeline}
            sx={{ transform: open ? "rotate(180deg)" : "none", transition: "transform .2s" }}>
            <ExpandMoreIcon />
          </IconButton>
        </Stack>
        <LinearProgress variant="determinate" value={pct} sx={{ mt: 1.5, height: 6 }} />
        <LinearProgress variant="determinate" value={spentPct} color={spentPct >= 80 ? "warning" : "secondary"} sx={{ mt: 0.5, height: 3, opacity: 0.6 }} />
        {w.error && <Alert severity="warning" sx={{ mt: 1 }}>{w.error}</Alert>}
        <Collapse in={open} unmountOnExit>
          <Timeline workflowId={w.id} lang={lang} />
          {isOpen && can("project.manage") && (
            <Button color="error" size="small" sx={{ mt: 1 }}
              onClick={async () => { await api.post(`/api/workflows/${w.id}/cancel`); onChanged(); }}>
              {t.cancelWorkflow}
            </Button>
          )}
        </Collapse>
      </CardContent>
    </Card>
  );
}

function Timeline({ workflowId, lang }: { workflowId: string; lang: string }) {
  const { data, reload } = useLoad(() => api.get<WorkflowEvent[]>(`/api/workflows/${workflowId}/events`), workflowId);
  useRealtime("approvalsChanged", () => void reload());
  useRealtime("agentRunChanged", () => void reload());
  if (!data) return null;
  return (
    <Stack spacing={0.75} sx={{ mt: 2, maxHeight: 280, overflow: "auto", pr: 1 }}>
      {data.map((e) => (
        <Stack key={e.id} direction="row" spacing={1} sx={{ alignItems: "flex-start" }}>
          <Box sx={{ width: 22, textAlign: "center" }} aria-hidden>{EVENT_EMOJI[e.kind] ?? "•"}</Box>
          <Typography variant="body2" sx={{ flex: 1 }}>{e.message}</Typography>
          <Typography variant="caption" color="text.secondary" sx={{ whiteSpace: "nowrap" }}>
            {new Date(e.createdAt).toLocaleTimeString(lang === "th" ? "th-TH" : "en-GB")}
          </Typography>
        </Stack>
      ))}
    </Stack>
  );
}
