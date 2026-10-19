"use client";

import { useState } from "react";
import {
  Alert, Box, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, Switch,
  TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import { api, Defect, DefectSeverity, Task } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, StatusChip, useErrorMessage, useLoad } from "./common";

const SEVERITIES: DefectSeverity[] = ["Low", "Medium", "High", "Critical"];
const STATE_COLOR = { open: "warning", fixReady: "info", verified: "success", rejected: "default" } as const;
const pre = { m: 0, whiteSpace: "pre-wrap", wordBreak: "break-word", fontSize: 13 } as const;

type Form = { title: string; severity: DefectSeverity; stepsToReproduce: string; expectedResult: string; actualResult: string; environment: string; taskId: string };
type Decision = { defect: Defect; action: "verify" | "reject" | "reopen"; note: string };

/** QA defect log (Master Prompt §7 stage 6): each defect sends a fix task back to the developer and holds the affected task out of Done. */
export function DefectsPanel({ projectId, tasks, onOpenTask }: { projectId: string; tasks: Task[]; onOpenTask: (taskId: string) => void }) {
  const { t } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<Defect[]>(`/api/projects/${projectId}/defects`), projectId);
  useRealtime("taskChanged", () => void reload());
  const [form, setForm] = useState<Form | null>(null);
  const [decision, setDecision] = useState<Decision | null>(null);
  const [showClosed, setShowClosed] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  if (!data) return <Loading error={error} />;
  const act = async (fn: () => Promise<unknown>) => {
    setMessage(null);
    try { await fn(); } catch (e) { setMessage(errorMessage(e)); }
    await reload();
  };
  const open = data.filter((d) => d.status === "Open");
  const shown = showClosed ? data : open;
  const noteRequired = decision?.action !== "verify";

  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>{t.defectsNote}</Typography>
      {message && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}
      <Stack direction="row" spacing={2} sx={{ mb: 2, alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
        {can("task.manage") && (
          <Button startIcon={<AddIcon />} onClick={() => setForm({ title: "", severity: "Medium", stepsToReproduce: "", expectedResult: "", actualResult: "", environment: "", taskId: "" })}>
            {t.newDefect}
          </Button>
        )}
        <Typography variant="body2" color="text.secondary">{t.defectsOpenCount.replace("{n}", String(open.length))}</Typography>
        <FormControlLabel control={<Switch size="small" checked={showClosed} onChange={(e) => setShowClosed(e.target.checked)} />} label={t.defectsShowClosed} />
      </Stack>

      {shown.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
        <Stack spacing={1.5}>
          {shown.map((d) => (
            <Card key={d.id} variant="outlined">
              <CardContent>
                <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 0.5, mb: 1 }}>
                  <Typography sx={{ fontFamily: "monospace" }} color="text.secondary">{d.code}</Typography>
                  <Typography fontWeight={600} sx={{ flex: 1, minWidth: 160 }}>{d.title}</Typography>
                  <StatusChip value={d.severity} />
                  <Chip size="small" color={STATE_COLOR[d.state]} label={t[`defectState_${d.state}`]} />
                </Stack>
                <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "repeat(3, 1fr)" }, gap: 1.5, mb: 1 }}>
                  <Box>
                    <Typography variant="caption" color="text.secondary" fontWeight={600}>{t.defectSteps}</Typography>
                    <Typography component="pre" sx={pre}>{d.stepsToReproduce}</Typography>
                  </Box>
                  {d.expectedResult && <Box>
                    <Typography variant="caption" color="text.secondary" fontWeight={600}>{t.defectExpected}</Typography>
                    <Typography component="pre" sx={pre}>{d.expectedResult}</Typography>
                  </Box>}
                  {d.actualResult && <Box>
                    <Typography variant="caption" color="text.secondary" fontWeight={600}>{t.defectActual}</Typography>
                    <Typography component="pre" sx={pre}>{d.actualResult}</Typography>
                  </Box>}
                </Box>
                <Typography variant="caption" color="text.secondary" component="div">
                  {d.taskTitle && <>{t.defectFoundIn}: <Box component="span" sx={{ cursor: "pointer", textDecoration: "underline" }} onClick={() => onOpenTask(d.taskId!)}>{d.taskTitle}</Box> · </>}
                  {d.environment && <>{d.environment} · </>}
                  {t.defectReportedBy} {d.reporterName ?? "—"}{d.reportedByAi ? " (AI)" : ""} · {new Date(d.createdAt).toLocaleString()}
                  {d.reopenCount > 0 && <> · {t.defectReopened.replace("{n}", String(d.reopenCount))}</>}
                </Typography>
                {d.fixTaskId && (
                  <Typography variant="caption" color="text.secondary" component="div">
                    {t.defectFixTask}: <Box component="span" sx={{ cursor: "pointer", textDecoration: "underline" }} onClick={() => onOpenTask(d.fixTaskId!)}>{d.fixTaskTitle}</Box>
                    {d.fixTaskStatus && <> · <StatusChip value={d.fixTaskStatus} /></>}
                    {d.assigneeName && <> · {d.assigneeName}</>}
                  </Typography>
                )}
                {d.resolutionNote && <Typography variant="caption" component="div" sx={{ mt: 0.5 }}>📝 {d.resolutionNote}</Typography>}
                {can("task.approve") && (
                  <Stack direction="row" spacing={1} sx={{ mt: 1, justifyContent: "flex-end" }}>
                    {d.state === "fixReady" && <Button size="small" variant="contained" onClick={() => setDecision({ defect: d, action: "verify", note: "" })}>{t.defectVerify}</Button>}
                    {(d.state === "fixReady" || d.state === "verified") && <Button size="small" onClick={() => setDecision({ defect: d, action: "reopen", note: "" })}>{t.defectReopen}</Button>}
                    {d.status === "Open" && <Button size="small" color="error" onClick={() => setDecision({ defect: d, action: "reject", note: "" })}>{t.reject}</Button>}
                  </Stack>
                )}
              </CardContent>
            </Card>
          ))}
        </Stack>
      )}

      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.newDefect}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField label={t.title} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} required slotProps={{ htmlInput: { maxLength: 200 } }} />
              <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                <TextField select fullWidth label={t.defectSeverity} value={form.severity} onChange={(e) => setForm({ ...form, severity: e.target.value as DefectSeverity })}>
                  {SEVERITIES.map((s) => <MenuItem key={s} value={s}><StatusChip value={s} /></MenuItem>)}
                </TextField>
                <TextField select fullWidth label={t.defectFoundIn} value={form.taskId} onChange={(e) => setForm({ ...form, taskId: e.target.value })}>
                  <MenuItem value="">—</MenuItem>
                  {tasks.filter((x) => x.status !== "Cancelled").map((x) => <MenuItem key={x.id} value={x.id}>{x.title}</MenuItem>)}
                </TextField>
              </Stack>
              <TextField label={t.defectSteps} value={form.stepsToReproduce} onChange={(e) => setForm({ ...form, stepsToReproduce: e.target.value })} multiline minRows={3} required helperText={t.defectStepsHelp} />
              <TextField label={t.defectExpected} value={form.expectedResult} onChange={(e) => setForm({ ...form, expectedResult: e.target.value })} multiline minRows={2} />
              <TextField label={t.defectActual} value={form.actualResult} onChange={(e) => setForm({ ...form, actualResult: e.target.value })} multiline minRows={2} />
              <TextField label={t.defectEnvironment} value={form.environment} onChange={(e) => setForm({ ...form, environment: e.target.value })} slotProps={{ htmlInput: { maxLength: 500 } }} />
              <Alert severity="info">{t.defectFixInfo}</Alert>
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!form?.title.trim() || !form?.stepsToReproduce.trim()}
            onClick={() => act(async () => { await api.post(`/api/projects/${projectId}/defects`, { ...form, taskId: form!.taskId || null }); setForm(null); })}>
            {t.create}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={!!decision} onClose={() => setDecision(null)} fullWidth maxWidth="xs">
        {decision && <>
          <DialogTitle>{decision.defect.code} · {decision.action === "verify" ? t.defectVerify : decision.action === "reopen" ? t.defectReopen : t.reject}</DialogTitle>
          <DialogContent>
            <TextField fullWidth sx={{ mt: 1 }} label={noteRequired ? t.defectNoteRequired : t.note} value={decision.note} onChange={(e) => setDecision({ ...decision, note: e.target.value })} multiline minRows={2} required={noteRequired} />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setDecision(null)}>{t.cancel}</Button>
            <Button variant="contained" color={decision.action === "reject" ? "error" : "primary"} disabled={noteRequired && !decision.note.trim()}
              onClick={() => act(async () => {
                await api.post(`/api/defects/${decision.defect.id}/${decision.action}`, { note: decision.note || null, version: decision.defect.version });
                setDecision(null);
              })}>
              {t.defectConfirm}
            </Button>
          </DialogActions>
        </>}
      </Dialog>
    </>
  );
}
