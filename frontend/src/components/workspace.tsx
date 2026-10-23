"use client";

import { useState } from "react";
import {
  Alert, Box, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Grid, MenuItem, Paper, Stack,
  Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import PlayArrowIcon from "@mui/icons-material/PlayArrow";
import { api, Closeout, Decision, Estimate, MergeRequest, ProjectDocument, Repository, Requirement, TestRun, thb } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, StatusChip, useErrorMessage, useLoad } from "./common";
import { Rise } from "./cute";

const pre = { m: 0, p: 1.5, borderRadius: "10px", bgcolor: "action.hover", whiteSpace: "pre-wrap", wordBreak: "break-word", fontFamily: "monospace", fontSize: 13, maxHeight: 420, overflow: "auto" } as const;

// ---------------- Requirements ----------------

export function RequirementsPanel({ projectId }: { projectId: string }) {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<Requirement[]>(`/api/projects/${projectId}/requirements`), projectId);
  useRealtime("agentRunChanged", () => void reload());
  const [form, setForm] = useState<{ kind: Requirement["kind"]; title: string; description: string; acceptanceCriteria: string; priority: Requirement["priority"] } | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  if (!data) return <Loading error={error} />;
  const act = async (fn: () => Promise<unknown>) => {
    setMessage(null);
    try { await fn(); } catch (e) { setMessage(errorMessage(e)); }
    await reload();
  };

  return (
    <>
      {message && <Alert severity="warning" sx={{ mb: 2 }}>{message}</Alert>}
      {can("project.manage") && (
        <Button startIcon={<AddIcon />} sx={{ mb: 2 }} onClick={() => setForm({ kind: "Functional", title: "", description: "", acceptanceCriteria: "", priority: "Should" })}>
          {t.newRequirement}
        </Button>
      )}
      {data.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
        <TableContainer component={Paper} variant="outlined">
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{t.code}</TableCell>
                <TableCell>{t.title}</TableCell>
                <TableCell>{t.kindLabel}</TableCell>
                <TableCell>{t.priority}</TableCell>
                <TableCell>{t.status}</TableCell>
                <TableCell />
              </TableRow>
            </TableHead>
            <TableBody>
              {data.map((r) => (
                <TableRow key={r.id}>
                  <TableCell sx={{ fontFamily: "monospace" }}>{r.code}</TableCell>
                  <TableCell>
                    <Typography variant="body2" fontWeight={600}>{r.title}</Typography>
                    {r.acceptanceCriteria && <Typography variant="caption" color="text.secondary">✓ {r.acceptanceCriteria}</Typography>}
                    {r.authorName && <Typography variant="caption" color="text.secondary" display="block">— {r.authorName}</Typography>}
                  </TableCell>
                  <TableCell>{label(r.kind)}</TableCell>
                  <TableCell><StatusChip value={r.priority} /></TableCell>
                  <TableCell><StatusChip value={r.status} /></TableCell>
                  <TableCell align="right" sx={{ whiteSpace: "nowrap" }}>
                    {can("project.manage") && r.status === "Draft" && (
                      <>
                        <Button size="small" onClick={() => act(() => api.post(`/api/requirements/${r.id}/status`, { status: "Approved" }))}>{t.approve}</Button>
                        <Button size="small" color="error" onClick={() => act(() => api.post(`/api/requirements/${r.id}/status`, { status: "Rejected" }))}>{t.reject}</Button>
                      </>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.newRequirement}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <Stack direction="row" spacing={2}>
                <TextField select fullWidth label={t.kindLabel} value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value as Requirement["kind"] })}>
                  {["Functional", "NonFunctional", "Constraint", "Assumption", "Risk"].map((k) => <MenuItem key={k} value={k}>{label(k)}</MenuItem>)}
                </TextField>
                <TextField select fullWidth label={t.priority} value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value as Requirement["priority"] })}>
                  {["Must", "Should", "Could", "Wont"].map((k) => <MenuItem key={k} value={k}>{k}</MenuItem>)}
                </TextField>
              </Stack>
              <TextField label={t.title} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} required />
              <TextField label={t.description} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} multiline minRows={2} />
              <TextField label={t.acceptanceCriteria} value={form.acceptanceCriteria} onChange={(e) => setForm({ ...form, acceptanceCriteria: e.target.value })} multiline minRows={2} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!form?.title} onClick={() => act(async () => { await api.post(`/api/projects/${projectId}/requirements`, form); setForm(null); })}>{t.create}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

// ---------------- ADRs ----------------

export function DecisionsPanel({ projectId }: { projectId: string }) {
  const { t } = useI18n();
  const { can } = useAuth();
  const { data, error, reload } = useLoad(() => api.get<Decision[]>(`/api/projects/${projectId}/decisions`), projectId);
  useRealtime("agentRunChanged", () => void reload());
  if (!data) return <Loading error={error} />;
  if (data.length === 0) return <Typography color="text.secondary">{t.empty}</Typography>;
  return (
    <Stack spacing={2}>
      {data.map((a, i) => (
        <Rise key={a.id} index={i}>
          <Card>
            <CardContent>
              <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
                <Chip size="small" label={`ADR-${String(a.number).padStart(3, "0")}`} />
                <Typography fontWeight={600} sx={{ flex: 1 }}>{a.title}</Typography>
                <StatusChip value={a.status} />
              </Stack>
              <Typography variant="subtitle2" color="text.secondary">{t.context}</Typography>
              <Typography variant="body2" sx={{ whiteSpace: "pre-wrap", mb: 1 }}>{a.context}</Typography>
              <Typography variant="subtitle2" color="text.secondary">{t.decisionText}</Typography>
              <Typography variant="body2" sx={{ whiteSpace: "pre-wrap", mb: 1 }}>{a.decision}</Typography>
              {a.consequences && (
                <>
                  <Typography variant="subtitle2" color="text.secondary">{t.consequences}</Typography>
                  <Typography variant="body2" sx={{ whiteSpace: "pre-wrap" }}>{a.consequences}</Typography>
                </>
              )}
              {can("project.manage") && a.status === "Proposed" && (
                <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                  <Button size="small" variant="contained" onClick={async () => { await api.post(`/api/decisions/${a.id}/status`, { status: "Accepted" }); await reload(); }}>{t.accept}</Button>
                  <Button size="small" color="error" onClick={async () => { await api.post(`/api/decisions/${a.id}/status`, { status: "Rejected" }); await reload(); }}>{t.reject}</Button>
                </Stack>
              )}
            </CardContent>
          </Card>
        </Rise>
      ))}
    </Stack>
  );
}

// ---------------- Estimates ----------------

export function EstimatesPanel({ projectId }: { projectId: string }) {
  const { t } = useI18n();
  const { data, error, reload } = useLoad(() => api.get<Estimate[]>(`/api/projects/${projectId}/estimates`), projectId);
  useRealtime("approvalsChanged", () => void reload());
  useRealtime("agentRunChanged", () => void reload());
  if (!data) return <Loading error={error} />;
  if (data.length === 0) return <Typography color="text.secondary">{t.empty}</Typography>;
  return (
    <Stack spacing={2}>
      {data.map((e) => (
        <Card key={e.id}>
          <CardContent>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 2 }}>
              <Typography variant="h6" sx={{ flex: 1 }}>r{e.revision} · {thb(e.price)}</Typography>
              <StatusChip value={e.status} />
            </Stack>
            <Grid container spacing={2} sx={{ mb: 2 }}>
              {([[t.hours, `${e.totalHours}`], [t.labor, thb(e.laborCost)], [t.aiCost, `${thb(e.aiCost)} ($${e.aiCostUsd})`], [`${t.contingency} ${e.contingencyPercent}%`, thb(e.contingency)],
                 [t.totalCost, thb(e.totalCost)], [t.price, thb(e.price)], [t.grossMargin, `${thb(e.grossMargin)} (${e.grossMarginPercent}%)`]] as const).map(([k, v]) => (
                <Grid key={k} size={{ xs: 6, md: 3 }}>
                  <Typography variant="caption" color="text.secondary">{k}</Typography>
                  <Typography fontWeight={600}>{v}</Typography>
                </Grid>
              ))}
            </Grid>
            {e.warnings.length > 0 && <Alert severity="warning" sx={{ mb: 2 }}>{e.warnings.join(" ")}</Alert>}
            {e.assumptions && <Typography variant="body2" sx={{ mb: 2, whiteSpace: "pre-wrap" }}><b>{t.assumptions}:</b> {e.assumptions}</Typography>}
            <TableContainer component={Paper} variant="outlined">
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>{t.description}</TableCell>
                    <TableCell>{t.jobTitle}</TableCell>
                    <TableCell align="right">{t.hours}</TableCell>
                    <TableCell align="right">{t.rate}</TableCell>
                    <TableCell align="right">{t.amount}</TableCell>
                    <TableCell>{t.evidence}</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {e.items.map((i, n) => (
                    <TableRow key={n}>
                      <TableCell>{i.description}</TableCell>
                      <TableCell>{i.roleTitle}</TableCell>
                      <TableCell align="right">{i.hours}</TableCell>
                      <TableCell align="right">{thb(i.rateThb)}</TableCell>
                      <TableCell align="right">{thb(i.amount)}</TableCell>
                      <TableCell sx={{ color: i.evidence ? undefined : "warning.main" }}>{i.evidence ?? "—"}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </CardContent>
        </Card>
      ))}
    </Stack>
  );
}

// ---------------- Repository ----------------

export function RepositoryPanel({ projectId }: { projectId: string }) {
  const { t } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(async () => {
    const [repos, sandbox, mrs] = await Promise.all([
      api.get<Repository[]>(`/api/projects/${projectId}/repositories`),
      api.get<{ isAvailable: boolean; allowedImages: string[] }>("/api/sandbox"),
      api.get<MergeRequest[]>(`/api/projects/${projectId}/merge-requests`),
    ]);
    return { repos, sandbox, mrs };
  }, projectId);
  useRealtime("approvalsChanged", () => void reload());
  useRealtime("agentRunChanged", () => void reload());
  const [form, setForm] = useState<{ name: string; sandboxImage: string; testCommand: string; lintCommand: string; importUrl: string; remoteTokenEnvVar: string } | null>(null);
  const [checks, setChecks] = useState<{ sandboxImage: string; testCommand: string; lintCommand: string; version: number } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [remoteForm, setRemoteForm] = useState<{ remoteUrl: string; remoteTokenEnvVar: string } | null>(null);
  const [pushing, setPushing] = useState(false);

  if (!data) return <Loading error={error} />;
  const repo = data.repos[0];

  return (
    <>
      {message && <Alert severity="warning" sx={{ mb: 2 }}>{message}</Alert>}
      {!data.sandbox.isAvailable && <Alert severity="info" sx={{ mb: 2 }}>{t.sandboxOff}</Alert>}
      {!repo ? (
        <Stack spacing={1} sx={{ alignItems: "flex-start" }}>
          <Typography color="text.secondary">{t.noRepository}</Typography>
          {can("project.manage") && (
            <Button variant="contained" startIcon={<AddIcon />}
              onClick={() => setForm({ name: "app", sandboxImage: data.sandbox.allowedImages[0] ?? "", testCommand: "dotnet test", lintCommand: "", importUrl: "", remoteTokenEnvVar: "" })}>
              {t.newRepository}
            </Button>
          )}
        </Stack>
      ) : (
        <Stack spacing={3}>
          <Card>
            <CardContent>
              <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
                <Typography variant="h6" sx={{ flex: 1 }}>📦 {repo.name}</Typography>
                <Chip size="small" label={`${t.image}: ${repo.sandboxImage}`} />
                <Chip size="small" label={`${t.testCommand}: ${repo.testCommand}`} sx={{ fontFamily: "monospace" }} />
                <Chip size="small" variant={repo.lintCommand ? "filled" : "outlined"} label={`${t.lintCommand}: ${repo.lintCommand ?? t.lintNone}`} sx={{ fontFamily: "monospace" }} />
                {can("project.manage") && (
                  <Button size="small" onClick={() => setChecks({ sandboxImage: repo.sandboxImage, testCommand: repo.testCommand, lintCommand: repo.lintCommand ?? "", version: repo.version })}>{t.editChecks}</Button>
                )}
              </Stack>
              <Stack direction={{ xs: "column", sm: "row" }} spacing={1} sx={{ mt: 1.5, alignItems: { sm: "center" } }}>
                <Typography variant="body2" sx={{ flex: 1, minWidth: 0, overflowWrap: "anywhere" }}>
                  🌐 {t.remote}: {repo.remoteUrl ?? t.remoteNone}
                  {repo.remoteUrl && (
                    <Typography component="span" variant="caption" color="text.secondary">
                      {" · "}{repo.remotePushedAt ? `${t.lastPushed} ${new Date(repo.remotePushedAt).toLocaleString()} (${repo.remotePushedSha?.slice(0, 7)})` : t.notPushedYet}
                    </Typography>
                  )}
                </Typography>
                {repo.remoteUrl && can("project.manage") && (
                  <Button size="small" variant="outlined" disabled={pushing} onClick={async () => {
                    setPushing(true);
                    try { await api.post(`/api/repositories/${repo.id}/push`); } catch (e) { setMessage(errorMessage(e)); }
                    setPushing(false);
                    await reload();
                  }}>{t.pushNow}</Button>
                )}
                {can("security.manage") && (
                  <Button size="small" onClick={() => setRemoteForm({ remoteUrl: repo.remoteUrl ?? "", remoteTokenEnvVar: repo.remoteTokenEnvVar ?? "" })}>{t.edit}</Button>
                )}
              </Stack>
              {repo.remoteUrl && repo.remoteTokenEnvVar && !repo.remoteTokenPresent && (
                <Alert severity="warning" sx={{ mt: 1 }}>{repo.remoteTokenEnvVar}: {t.secretMissing}</Alert>
              )}
              {repo.remoteError && <Alert severity="error" sx={{ mt: 1, whiteSpace: "pre-wrap" }}>{t.pushFailed}: {repo.remoteError}</Alert>}
            </CardContent>
          </Card>
          <FileBrowser repo={repo} />
          <Box>
            <Typography variant="h6" gutterBottom>{t.mergeRequests}</Typography>
            <MergeRequestList items={data.mrs} />
          </Box>
          <TestRunsList repo={repo} canRun={can("project.manage") && data.sandbox.isAvailable} onError={(e) => setMessage(errorMessage(e))} />
        </Stack>
      )}
      <Dialog open={!!checks} onClose={() => setChecks(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.editChecks}</DialogTitle>
        {checks && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField select label={t.image} value={checks.sandboxImage} onChange={(e) => setChecks({ ...checks, sandboxImage: e.target.value })}>
                {data.sandbox.allowedImages.map((i) => <MenuItem key={i} value={i}>{i}</MenuItem>)}
              </TextField>
              <TextField label={t.testCommand} value={checks.testCommand} onChange={(e) => setChecks({ ...checks, testCommand: e.target.value })}
                slotProps={{ htmlInput: { style: { fontFamily: "monospace" } } }} />
              <TextField label={t.lintCommand} value={checks.lintCommand} onChange={(e) => setChecks({ ...checks, lintCommand: e.target.value })} helperText={t.lintHint}
                slotProps={{ htmlInput: { style: { fontFamily: "monospace" } } }} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setChecks(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!checks?.testCommand.trim()} onClick={async () => {
            try {
              await api.put(`/api/repositories/${repo!.id}`, { ...checks, lintCommand: checks!.lintCommand || null });
              setChecks(null);
            } catch (e) { setMessage(errorMessage(e)); }
            await reload();
          }}>{t.save}</Button>
        </DialogActions>
      </Dialog>
      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.newRepository}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField label={t.repoName} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
              <TextField select label={t.image} value={form.sandboxImage} onChange={(e) => setForm({ ...form, sandboxImage: e.target.value })}>
                {data.sandbox.allowedImages.map((i) => <MenuItem key={i} value={i}>{i}</MenuItem>)}
              </TextField>
              <TextField label={t.testCommand} value={form.testCommand} onChange={(e) => setForm({ ...form, testCommand: e.target.value })}
                slotProps={{ htmlInput: { style: { fontFamily: "monospace" } } }} />
              <TextField label={t.lintCommand} value={form.lintCommand} onChange={(e) => setForm({ ...form, lintCommand: e.target.value })} helperText={t.lintHint}
                slotProps={{ htmlInput: { style: { fontFamily: "monospace" } } }} />
              {can("security.manage") && (
                <>
                  <TextField label={t.importFrom} placeholder="https://github.com/acme/app.git" value={form.importUrl}
                    onChange={(e) => setForm({ ...form, importUrl: e.target.value })} helperText={t.importHint} />
                  {form.importUrl && (
                    <TextField label={t.tokenEnvVar} placeholder="GIT_REMOTE_GITHUB" value={form.remoteTokenEnvVar}
                      onChange={(e) => setForm({ ...form, remoteTokenEnvVar: e.target.value.toUpperCase() })} helperText={t.tokenEnvHint} />
                  )}
                </>
              )}
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" onClick={async () => {
            const { importUrl, remoteTokenEnvVar, ...rest } = form!;
            try {
              await api.post(`/api/projects/${projectId}/repositories`, { ...rest, lintCommand: rest.lintCommand || null, importUrl: importUrl || null, remoteTokenEnvVar: importUrl && remoteTokenEnvVar ? remoteTokenEnvVar : null });
              setForm(null);
            } catch (e) { setMessage(errorMessage(e)); }
            await reload();
          }}>{t.create}</Button>
        </DialogActions>
      </Dialog>
      <Dialog open={!!remoteForm} onClose={() => setRemoteForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.remote}</DialogTitle>
        {remoteForm && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <Alert severity="info">{t.remoteHint}</Alert>
              <TextField label="URL" placeholder="https://github.com/acme/app.git" value={remoteForm.remoteUrl}
                onChange={(e) => setRemoteForm({ ...remoteForm, remoteUrl: e.target.value })} />
              <TextField label={t.tokenEnvVar} placeholder="GIT_REMOTE_GITHUB" value={remoteForm.remoteTokenEnvVar}
                onChange={(e) => setRemoteForm({ ...remoteForm, remoteTokenEnvVar: e.target.value.toUpperCase() })} helperText={t.tokenEnvHint} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setRemoteForm(null)}>{t.cancel}</Button>
          <Button variant="contained" onClick={async () => {
            try {
              await api.put(`/api/repositories/${repo!.id}/remote`, {
                remoteUrl: remoteForm!.remoteUrl.trim() || null, remoteTokenEnvVar: remoteForm!.remoteTokenEnvVar.trim() || null, version: repo!.version,
              });
              setRemoteForm(null);
            } catch (e) { setMessage(errorMessage(e)); }
            await reload();
          }}>{t.save}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

function FileBrowser({ repo }: { repo: Repository }) {
  const { t } = useI18n();
  const [branch, setBranch] = useState(repo.defaultBranch);
  const [file, setFile] = useState<{ path: string; content: string } | null>(null);
  const { data } = useLoad(async () => {
    const [branches, files] = await Promise.all([
      api.get<string[]>(`/api/repositories/${repo.id}/branches`),
      api.get<string[]>(`/api/repositories/${repo.id}/files?branch=${encodeURIComponent(branch)}`),
    ]);
    return { branches, files };
  }, `${repo.id}:${branch}`);

  async function open(path: string) {
    const res = await fetch(`${process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000"}/api/repositories/${repo.id}/file?branch=${encodeURIComponent(branch)}&path=${encodeURIComponent(path)}`, { credentials: "include" });
    setFile({ path, content: await res.text() });
  }

  return (
    <Grid container spacing={2}>
      <Grid size={{ xs: 12, md: 4 }}>
        <TextField select size="small" fullWidth label={t.branch} value={branch} onChange={(e) => { setBranch(e.target.value); setFile(null); }} sx={{ mb: 1 }}>
          {(data?.branches ?? [branch]).map((b) => <MenuItem key={b} value={b} sx={{ fontFamily: "monospace" }}>{b}</MenuItem>)}
        </TextField>
        <Paper variant="outlined" sx={{ maxHeight: 360, overflow: "auto" }}>
          {(data?.files ?? []).map((f) => (
            <Box key={f} onClick={() => open(f)}
              sx={{ px: 1.5, py: 0.5, cursor: "pointer", fontFamily: "monospace", fontSize: 13, bgcolor: file?.path === f ? "action.selected" : undefined, "&:hover": { bgcolor: "action.hover" } }}>
              📄 {f}
            </Box>
          ))}
        </Paper>
      </Grid>
      <Grid size={{ xs: 12, md: 8 }}>
        {file ? (
          <>
            <Typography variant="subtitle2" sx={{ fontFamily: "monospace", mb: 1 }}>{file.path}</Typography>
            <Box component="pre" sx={pre}>{file.content}</Box>
          </>
        ) : <Typography color="text.secondary">{t.files}: {data?.files.length ?? 0}</Typography>}
      </Grid>
    </Grid>
  );
}

function MergeRequestList({ items }: { items: MergeRequest[] }) {
  const { t } = useI18n();
  const [open, setOpen] = useState<string | null>(null);
  const { data } = useLoad(async () => (open ? (await api.get<{ diff: string }>(`/api/merge-requests/${open}`)).diff : null), open ?? "");
  if (items.length === 0) return <Typography color="text.secondary">{t.empty}</Typography>;
  return (
    <Stack spacing={1}>
      {items.map((m) => (
        <Card key={m.id}>
          <CardContent sx={{ pb: "12px !important" }}>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center", cursor: "pointer", flexWrap: "wrap", rowGap: 1 }} onClick={() => setOpen(open === m.id ? null : m.id)}>
              <Chip size="small" label={`!${m.number}`} />
              <Typography fontWeight={600} sx={{ flex: 1 }}>{m.title}</Typography>
              <Typography variant="caption" color="text.secondary" sx={{ fontFamily: "monospace" }}>{m.sourceBranch} → {m.targetBranch}</Typography>
              {m.latestTestStatus && <StatusChip value={m.latestTestStatus} />}
              <StatusChip value={m.status} />
            </Stack>
            {m.authorName && <Typography variant="caption" color="text.secondary">{m.authorName} · {m.headSha.slice(0, 8)}</Typography>}
            {open === m.id && <Box component="pre" sx={{ ...pre, mt: 1 }}>{data ?? t.loading}</Box>}
          </CardContent>
        </Card>
      ))}
    </Stack>
  );
}

function TestRunsList({ repo, canRun, onError }: { repo: Repository; canRun: boolean; onError: (e: unknown) => void }) {
  const { t, lang } = useI18n();
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState<string | null>(null);
  const { data, reload } = useLoad(() => api.get<TestRun[]>(`/api/repositories/${repo.id}/test-runs`), repo.id);
  useRealtime("agentRunChanged", () => void reload());
  return (
    <Box>
      <Stack direction="row" sx={{ alignItems: "center", mb: 1 }}>
        <Typography variant="h6" sx={{ flex: 1 }}>{t.testRuns}</Typography>
        {canRun && (["Test", "Lint"] as const).filter((k) => k === "Test" || repo.lintCommand).map((kind) => (
          <Button key={kind} size="small" variant="outlined" startIcon={<PlayArrowIcon />} disabled={busy} sx={{ ml: 1 }}
            onClick={async () => {
              setBusy(true);
              try { await api.post(`/api/repositories/${repo.id}/test-runs`, { branch: repo.defaultBranch, kind }); } catch (e) { onError(e); }
              setBusy(false);
              await reload();
            }}>
            {kind === "Lint" ? t.runLint : t.runTests} ({repo.defaultBranch})
          </Button>
        ))}
      </Stack>
      <Stack spacing={1}>
        {(data ?? []).map((r) => (
          <Paper key={r.id} variant="outlined" sx={{ p: 1.5 }}>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center", cursor: "pointer" }} onClick={() => setOpen(open === r.id ? null : r.id)}>
              <StatusChip value={r.status} />
              <Chip size="small" variant="outlined" label={r.kind === "Lint" ? t.checkLint : t.checkTest} />
              <Typography variant="body2" sx={{ fontFamily: "monospace", flex: 1 }}>{r.branch}{r.commitSha ? ` @ ${r.commitSha.slice(0, 8)}` : " (uncommitted)"}</Typography>
              <Typography variant="caption" color="text.secondary">{(r.durationMs / 1000).toFixed(1)}s · {new Date(r.startedAt).toLocaleString(lang === "th" ? "th-TH" : "en-GB")}</Typography>
            </Stack>
            {open === r.id && <Box component="pre" sx={{ ...pre, mt: 1 }}>{r.output ?? ""}</Box>}
          </Paper>
        ))}
        {data?.length === 0 && <Typography color="text.secondary">{t.empty}</Typography>}
      </Stack>
    </Box>
  );
}

// ---------------- Documents ----------------

export function DocumentsPanel({ projectId }: { projectId: string }) {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<ProjectDocument[]>(`/api/projects/${projectId}/documents`), projectId);
  useRealtime("agentRunChanged", () => void reload());
  const [open, setOpen] = useState<ProjectDocument | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  if (!data) return <Loading error={error} />;
  if (data.length === 0) return <Typography color="text.secondary">{t.empty}</Typography>;
  const base = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

  return (
    <Grid container spacing={2}>
      <Grid size={{ xs: 12, md: 4 }}>
        <Stack spacing={1}>
          {data.map((d) => (
            <Paper key={d.id} variant="outlined" sx={{ p: 1.5, cursor: "pointer", bgcolor: open?.id === d.id ? "action.selected" : undefined }}
              onClick={async () => setOpen(await api.get<ProjectDocument>(`/api/documents/${d.id}`))}>
              <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                <Typography fontWeight={600} sx={{ flex: 1 }} noWrap>📄 {d.title}</Typography>
                <Chip size="small" label={`v${d.version}`} />
                <StatusChip value={d.status} />
              </Stack>
              <Typography variant="caption" color="text.secondary">{label(d.kind)}{d.authorName ? ` · ${d.authorName}` : ""}{d.isSimulated ? " · 🧪" : ""}</Typography>
            </Paper>
          ))}
        </Stack>
      </Grid>
      <Grid size={{ xs: 12, md: 8 }}>
        {message && <Alert severity="warning" sx={{ mb: 1 }}>{message}</Alert>}
        {open && (
          <>
            <Stack direction="row" spacing={1} sx={{ mb: 1, alignItems: "center" }}>
              <Typography variant="h6" sx={{ flex: 1 }}>{open.title} v{open.version}</Typography>
              <Button size="small" href={`${base}/api/documents/${open.id}/download`}>{t.download}</Button>
              {can("knowledge.manage") && open.status === "Draft" && !open.isSimulated && (
                <Button size="small" variant="contained" onClick={async () => {
                  try { setOpen(await api.post<ProjectDocument>(`/api/documents/${open.id}/finalize`)); } catch (e) { setMessage(errorMessage(e)); }
                  await reload();
                }}>{t.finalize}</Button>
              )}
            </Stack>
            <Box component="pre" sx={{ ...pre, fontFamily: "inherit", fontSize: 14, maxHeight: 600 }}>{open.content}</Box>
          </>
        )}
      </Grid>
    </Grid>
  );
}

// ---------------- Closeout ----------------

/** Delivery stage 8: plan versus recorded figures, and a completion report draft written from them. */
export function CloseoutPanel({ projectId, onOpenDocuments }: { projectId: string; onOpenDocuments: () => void }) {
  const { t, lang, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<Closeout>(`/api/projects/${projectId}/closeout`), projectId);
  useRealtime("taskChanged", () => void reload());
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ severity: "success" | "warning"; text: string } | null>(null);
  if (!data) return <Loading error={error} />;
  const m = data.money;
  const n = (v: number) => v.toLocaleString(lang === "th" ? "th-TH" : "en-US", { maximumFractionDigits: 2 });
  const pct = (v: number) => `${n(v)}%`;

  const writeReport = async () => {
    setBusy(true);
    setMessage(null);
    try {
      const doc = await api.post<ProjectDocument>(`/api/projects/${projectId}/closeout/report`, { lang });
      setMessage({ severity: "success", text: `${t.completionReportDone} · ${doc.title} v${doc.version}` });
    } catch (e) { setMessage({ severity: "warning", text: errorMessage(e) }); }
    setBusy(false);
  };

  const stat = (label: string, value: React.ReactNode) => (
    <Stack direction="row" sx={{ justifyContent: "space-between", py: 0.5 }}>
      <Typography color="text.secondary">{label}</Typography>
      <Typography fontWeight={600}>{value}</Typography>
    </Stack>
  );

  return (
    <Stack spacing={2}>
      <Typography variant="body2" color="text.secondary">{t.closeoutNote}</Typography>
      {data.warnings.length > 0 && (
        <Alert severity="info">
          <Typography fontWeight={600}>{t.closeoutWarnings}</Typography>
          {data.warnings.map((w) => <Box key={w}>• {t[`cw_${w}`]}</Box>)}
        </Alert>
      )}
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 4 }}>
          <Card sx={{ height: "100%" }}><CardContent>
            <Typography variant="h6" sx={{ mb: 1 }}>{t.closeoutTasks}</Typography>
            {stat(label("Done"), `${data.tasks.done}/${data.tasks.total}`)}
            {stat(t.overdue, data.tasks.overdue)}
            {data.milestones.map((ms) => stat(`🏁 ${ms.name}${ms.dueDate ? ` · ${ms.dueDate}` : ""}`, `${ms.done}/${ms.tasks}`))}
          </CardContent></Card>
        </Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <Card sx={{ height: "100%" }}><CardContent>
            <Typography variant="h6" sx={{ mb: 1 }}>{t.closeoutQuality}</Typography>
            {stat(t.testsPassedLabel, `${data.quality.testsPassed}/${data.quality.testRuns}`)}
            {stat(t.mrMerged, data.quality.mergeRequestsMerged)}
            {stat(t.mrOpen, data.quality.mergeRequestsOpen)}
            {stat(t.reviewsApprovedLabel, data.quality.reviewsApproved)}
            {stat(t.changesRequestedLabel, data.quality.changesRequested)}
            {stat(t.deliverablesAcceptedLabel, data.quality.deliverablesAccepted)}
          </CardContent></Card>
        </Grid>
        <Grid size={{ xs: 12, md: 4 }}>
          <Card sx={{ height: "100%" }}><CardContent>
            <Typography variant="h6" sx={{ mb: 1 }}>{t.closeoutLessons}</Typography>
            {stat(t.lessonsValidated, data.knowledge.lessonsValidated)}
            {stat(t.lessonsProposed, data.knowledge.lessonsProposed)}
            <Button size="small" href="/knowledge" sx={{ mt: 1 }}>{t.proposeLesson}</Button>
          </CardContent></Card>
        </Grid>
      </Grid>

      <Card><CardContent>
        <Typography variant="h6" sx={{ mb: 1 }}>{t.closeoutEffortCost}</Typography>
        <TableContainer>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell />
                <TableCell align="right">{t.plannedCol}{m ? ` (rev ${m.estimateRevision})` : ""}</TableCell>
                <TableCell align="right">{t.actualCol}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              <TableRow>
                <TableCell>{t.hoursLabel}</TableCell>
                <TableCell align="right">{data.effort.plannedHours != null ? n(data.effort.plannedHours) : "—"}</TableCell>
                <TableCell align="right">{data.effort.actualHours != null
                  ? `${n(data.effort.actualHours)} (${data.effort.people} ${t.peopleUnit})`
                  : <Typography variant="body2" color="text.secondary">{t.notRecorded}</Typography>}</TableCell>
              </TableRow>
              {data.effort.actualLabourThb != null && (
                <TableRow>
                  <TableCell>{t.laborCost}</TableCell>
                  <TableCell align="right">{data.effort.plannedLabourThb != null ? thb(data.effort.plannedLabourThb) : "—"}</TableCell>
                  <TableCell align="right" sx={{ color: m && m.labourOverrunThb > 0 ? "error.main" : undefined }}>{thb(data.effort.actualLabourThb)}</TableCell>
                </TableRow>
              )}
              <TableRow>
                <TableCell>{t.aiCostLabel}</TableCell>
                <TableCell align="right">{m ? thb(m.plannedAiThb) : "—"}</TableCell>
                <TableCell align="right">{m ? thb(m.actualAiThb) : `$${n(data.actualAiUsd)}`}</TableCell>
              </TableRow>
              {m && <>
                <TableRow>
                  <TableCell>{t.totalCostLabel}</TableCell>
                  <TableCell align="right">{thb(m.plannedCostThb)}</TableCell>
                  <TableCell align="right">{thb(m.forecastCostThb)}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>{t.priceLabel}</TableCell>
                  <TableCell align="right">{thb(m.priceThb)}</TableCell>
                  <TableCell align="right">{thb(m.priceThb)}</TableCell>
                </TableRow>
                <TableRow>
                  <TableCell>{t.marginLabel}</TableCell>
                  <TableCell align="right">{thb(m.plannedMarginThb)} ({pct(m.plannedMarginPercent)})</TableCell>
                  <TableCell align="right" sx={{ color: m.atRisk ? "error.main" : undefined, fontWeight: 600 }}>
                    {m.atRisk ? "⚠ " : ""}{thb(m.forecastMarginThb)} ({pct(m.forecastMarginPercent)})
                  </TableCell>
                </TableRow>
              </>}
            </TableBody>
          </Table>
        </TableContainer>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>
          {t.aiRunsLabel}: {data.effort.aiRuns} · {t.aiRunHours}: {n(data.effort.aiRunHours)} · {n(data.effort.tokens)} tokens
          {data.effort.simulatedRuns > 0 && ` · 🧪 ${t.simulatedRunsLabel}: ${data.effort.simulatedRuns}`}
        </Typography>
      </CardContent></Card>

      {can("knowledge.manage") && (
        <Card><CardContent>
          {message && (
            <Alert severity={message.severity} sx={{ mb: 1.5 }}
              action={message.severity === "success" ? <Button color="inherit" size="small" onClick={onOpenDocuments}>{t.openDocuments}</Button> : undefined}>
              {message.text}
            </Alert>
          )}
          <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { sm: "center" } }}>
            <Button variant="contained" disabled={busy} onClick={writeReport}>{t.writeCompletionReport}</Button>
            <Typography variant="body2" color="text.secondary">{t.completionReportHint}</Typography>
          </Stack>
        </CardContent></Card>
      )}
    </Stack>
  );
}
