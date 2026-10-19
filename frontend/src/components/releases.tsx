"use client";

import { useState } from "react";
import {
  Alert, Box, Button, Card, CardContent, Chip, Collapse, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import CheckCircleIcon from "@mui/icons-material/CheckCircle";
import CancelIcon from "@mui/icons-material/Cancel";
import WarningIcon from "@mui/icons-material/WarningAmber";
import Link from "next/link";
import { api, Release, ReleaseReadiness, ReleaseStatus } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, useErrorMessage, useLoad } from "./common";

const pre = { m: 0, p: 1.25, borderRadius: "10px", bgcolor: "action.hover", whiteSpace: "pre-wrap", wordBreak: "break-word", fontSize: 13, maxHeight: 280, overflow: "auto" } as const;
const STATUS_COLOR: Record<ReleaseStatus, "default" | "info" | "warning" | "success" | "error"> = {
  Draft: "default", PendingApproval: "warning", Approved: "info", Deployed: "success", RolledBack: "error", Cancelled: "default",
};

type Form = { id?: string; version?: number; tag: string; title: string; notes: string; deploymentPlan: string; rollbackPlan: string; knownIssues: string };
type Outcome = { release: Release; action: "deployed" | "rolled-back" | "cancel"; note: string };

/** Notes, deployment instructions and rollback plan as read-only blocks (also used in the approval inbox). */
export function ReleasePlans({ notes, deploymentPlan, rollbackPlan, knownIssues }: Pick<Release, "notes" | "deploymentPlan" | "rollbackPlan" | "knownIssues">) {
  const { t } = useI18n();
  const blocks: [string, string | null][] = [[t.releaseNotes, notes], [t.deploymentPlan, deploymentPlan], [t.rollbackPlan, rollbackPlan], [t.knownIssues, knownIssues]];
  return (
    <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "repeat(2, 1fr)" }, gap: 1.5 }}>
      {blocks.filter(([, v]) => v).map(([k, v]) => (
        <Box key={k} sx={{ minWidth: 0 }}>
          <Typography variant="caption" color="text.secondary" fontWeight={600}>{k}</Typography>
          <Typography component="pre" sx={pre}>{v}</Typography>
        </Box>
      ))}
    </Box>
  );
}

export function IssueList({ issues }: { issues: string[] }) {
  const { t } = useI18n();
  if (issues.length === 0) return null;
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" fontWeight={600}>{t.unresolvedIssues} ({issues.length})</Typography>
      {issues.map((i) => <Typography key={i} variant="body2" sx={{ pl: 1 }}>• {i}</Typography>)}
    </Box>
  );
}

function Readiness({ id }: { id: string }) {
  const { t } = useI18n();
  const text = (key: string) => (t as unknown as Record<string, string | undefined>)[key];
  const { data, error } = useLoad(() => api.get<ReleaseReadiness>(`/api/releases/${id}/readiness`), id);
  if (!data) return <Loading error={error} />;
  return (
    <Stack spacing={1}>
      {data.repositoryName && (
        <Typography variant="caption" color="text.secondary">
          {data.repositoryName} · {data.branch} {data.commitSha ? `@ ${data.commitSha.slice(0, 8)}` : `· ${t.noCommits}`}
        </Typography>
      )}
      {data.checks.map((c) => (
        <Stack key={c.code} direction="row" spacing={0.75} sx={{ alignItems: "center" }}>
          {c.met ? <CheckCircleIcon sx={{ fontSize: 18, color: "success.main" }} />
            : c.blocking ? <CancelIcon sx={{ fontSize: 18, color: "error.main" }} /> : <WarningIcon sx={{ fontSize: 18, color: "warning.main" }} />}
          <Typography variant="body2">
            {text(`rc_${c.code}`) ?? c.code}
            {c.detail && <Box component="span" sx={{ color: "text.secondary" }}> · {c.detail}</Box>}
            {!c.met && <Box component="span" sx={{ color: c.blocking ? "error.main" : "warning.main" }}> — {c.blocking ? t.readinessBlocking : t.readinessWarning}</Box>}
          </Typography>
        </Stack>
      ))}
      <IssueList issues={data.unresolvedIssues} />
    </Stack>
  );
}

/** Release preparation (Master Prompt §7 stage 7): plans, readiness against the exact commit, approval before production, deployment record. */
export function ReleasesPanel({ projectId }: { projectId: string }) {
  const { t } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<Release[]>(`/api/projects/${projectId}/releases`), projectId);
  useRealtime("approvalsChanged", () => void reload());
  useRealtime("agentRunChanged", () => void reload());
  const [form, setForm] = useState<Form | null>(null);
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const manage = can("project.manage");

  if (!data) return <Loading error={error} />;
  const act = async (fn: () => Promise<unknown>) => {
    setMessage(null);
    try { await fn(); } catch (e) { setMessage(errorMessage(e)); }
    await reload();
  };
  const edit = (r: Release) => setForm({
    id: r.id, version: r.version, tag: r.tag, title: r.title ?? "", notes: r.notes ?? "", deploymentPlan: r.deploymentPlan ?? "", rollbackPlan: r.rollbackPlan ?? "", knownIssues: r.knownIssues ?? "",
  });
  const noteRequired = outcome?.action === "rolled-back";

  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>{t.releasesNote}</Typography>
      {message && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}
      {manage && (
        <Button startIcon={<AddIcon />} sx={{ mb: 2 }} onClick={() => setForm({ tag: "", title: "", notes: "", deploymentPlan: "", rollbackPlan: "", knownIssues: "" })}>
          {t.newRelease}
        </Button>
      )}
      {data.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
        <Stack spacing={1.5}>
          {data.map((r) => {
            const open = expanded === r.id || r.status === "Draft";
            return (
              <Card key={r.id} variant="outlined" sx={{ opacity: r.status === "Cancelled" ? 0.6 : 1 }}>
                <CardContent>
                  <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 0.5 }}>
                    <Typography variant="h6" sx={{ fontFamily: "monospace" }}>{r.tag}</Typography>
                    <Typography sx={{ flex: 1, minWidth: 140 }}>{r.title}</Typography>
                    <Chip size="small" color={STATUS_COLOR[r.status]} label={t[`releaseStatus_${r.status}`]} />
                  </Stack>
                  <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 1 }}>
                    {r.authorName && <>{r.authorName} · </>}{new Date(r.createdAt).toLocaleString()}
                    {r.commitSha && <> · commit <code>{r.commitSha.slice(0, 8)}</code></>}
                    {r.approvedAt && <> · {t.releaseApprovedAt} {new Date(r.approvedAt).toLocaleString()}</>}
                    {r.deployedAt && <> · {t.releaseDeployedAt} {new Date(r.deployedAt).toLocaleString()}</>}
                    {r.rolledBackAt && <> · {t.releaseRolledBackAt} {new Date(r.rolledBackAt).toLocaleString()}</>}
                  </Typography>
                  {r.outcomeNote && <Typography variant="body2" sx={{ mb: 1 }}>📝 {r.outcomeNote}</Typography>}
                  {r.status !== "Draft" && (
                    <Button size="small" onClick={() => setExpanded(expanded === r.id ? null : r.id)}>{open ? t.hideDetails : t.showDetails}</Button>
                  )}
                  <Collapse in={open} unmountOnExit>
                    <Stack spacing={1.5} sx={{ mt: 1 }}>
                      <ReleasePlans {...r} />
                      {r.status === "Draft" ? <Readiness key={`${r.id}-${r.version}`} id={r.id} /> : <IssueList issues={r.unresolvedIssues} />}
                    </Stack>
                  </Collapse>
                  {manage && (
                    <Stack direction="row" spacing={1} sx={{ mt: 1.5, justifyContent: "flex-end", flexWrap: "wrap", rowGap: 1 }}>
                      {r.status === "Draft" && <>
                        <Button size="small" onClick={() => edit(r)}>{t.edit}</Button>
                        <Button size="small" variant="contained" onClick={() => act(() => api.post(`/api/releases/${r.id}/submit`, {}))}>{t.submitForProduction}</Button>
                      </>}
                      {r.status === "PendingApproval" && <Button size="small" component={Link} href="/approvals">{t.openApprovals}</Button>}
                      {r.status === "Approved" && <Button size="small" variant="contained" onClick={() => setOutcome({ release: r, action: "deployed", note: "" })}>{t.recordDeployed}</Button>}
                      {r.status === "Deployed" && <Button size="small" color="error" onClick={() => setOutcome({ release: r, action: "rolled-back", note: "" })}>{t.recordRolledBack}</Button>}
                      {(r.status === "Draft" || r.status === "PendingApproval" || r.status === "Approved") && (
                        <Button size="small" color="error" onClick={() => setOutcome({ release: r, action: "cancel", note: "" })}>{t.cancelRelease}</Button>
                      )}
                    </Stack>
                  )}
                </CardContent>
              </Card>
            );
          })}
        </Stack>
      )}

      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="md">
        <DialogTitle>{form?.id ? `${t.edit} ${form.tag}` : t.newRelease}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                <TextField label={t.releaseTag} placeholder="1.0.0" value={form.tag} onChange={(e) => setForm({ ...form, tag: e.target.value })} required slotProps={{ htmlInput: { maxLength: 40 } }} />
                <TextField fullWidth label={t.title} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} slotProps={{ htmlInput: { maxLength: 200 } }} />
              </Stack>
              <TextField label={t.releaseNotes} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} multiline minRows={3} />
              <TextField label={t.deploymentPlan} value={form.deploymentPlan} onChange={(e) => setForm({ ...form, deploymentPlan: e.target.value })} multiline minRows={3} helperText={t.deploymentPlanHelp} />
              <TextField label={t.rollbackPlan} value={form.rollbackPlan} onChange={(e) => setForm({ ...form, rollbackPlan: e.target.value })} multiline minRows={3} helperText={t.rollbackPlanHelp} />
              <TextField label={t.knownIssues} value={form.knownIssues} onChange={(e) => setForm({ ...form, knownIssues: e.target.value })} multiline minRows={2} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!form?.tag.trim()} onClick={() => act(async () => {
            const { id, ...body } = form!;
            if (id) await api.put(`/api/releases/${id}`, body); else await api.post(`/api/projects/${projectId}/releases`, body);
            setForm(null);
          })}>
            {form?.id ? t.save : t.create}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={!!outcome} onClose={() => setOutcome(null)} fullWidth maxWidth="xs">
        {outcome && <>
          <DialogTitle>
            {outcome.release.tag} · {outcome.action === "deployed" ? t.recordDeployed : outcome.action === "rolled-back" ? t.recordRolledBack : t.cancelRelease}
          </DialogTitle>
          <DialogContent>
            {outcome.action === "deployed" && <Alert severity="info" sx={{ mb: 1 }}>{t.recordDeployedInfo}</Alert>}
            <TextField fullWidth sx={{ mt: 1 }} label={noteRequired ? t.defectNoteRequired : t.note} value={outcome.note}
              onChange={(e) => setOutcome({ ...outcome, note: e.target.value })} multiline minRows={2} required={noteRequired} />
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setOutcome(null)}>{t.cancel}</Button>
            <Button variant="contained" color={outcome.action === "deployed" ? "primary" : "error"} disabled={noteRequired && !outcome.note.trim()}
              onClick={() => act(async () => { await api.post(`/api/releases/${outcome.release.id}/${outcome.action}`, { note: outcome.note || null }); setOutcome(null); })}>
              {t.defectConfirm}
            </Button>
          </DialogActions>
        </>}
      </Dialog>
    </>
  );
}
