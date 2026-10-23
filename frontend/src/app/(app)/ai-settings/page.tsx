"use client";

import { useState } from "react";
import {
  Alert, Autocomplete, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Grid, IconButton, MenuItem, Paper, Stack, Switch, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, TextField, Tooltip, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import ArrowDownwardIcon from "@mui/icons-material/ArrowDownward";
import ArrowUpwardIcon from "@mui/icons-material/ArrowUpward";
import CloseIcon from "@mui/icons-material/Close";
import DownloadIcon from "@mui/icons-material/Download";
import { api, AiModel, AiProvider, downloadFile, TrainingExportSummary } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, useErrorMessage, useLoad } from "@/components/common";
import { Rise } from "@/components/cute";

type PriceKey = "inputUsdPerMTok" | "outputUsdPerMTok" | "cacheReadUsdPerMTok" | "cacheWriteUsdPerMTok";
const PRICE_KEYS: PriceKey[] = ["inputUsdPerMTok", "outputUsdPerMTok", "cacheReadUsdPerMTok", "cacheWriteUsdPerMTok"];

export default function AiSettingsPage() {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const canManage = can("agent.manage");
  const [message, setMessage] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const { data, error, reload } = useLoad(async () => {
    const [providers, models] = await Promise.all([api.get<AiProvider[]>("/api/ai/providers"), api.get<AiModel[]>("/api/ai/models")]);
    return { providers, models };
  });

  if (!data) return <Loading error={error} />;

  async function update(model: AiModel, patch: Partial<AiModel>) {
    setMessage(null);
    try {
      await api.put(`/api/ai/models/${model.id}`, { ...model, ...patch });
    } catch (e) {
      setMessage(errorMessage(e));
    }
    await reload();
  }

  const headers: Record<PriceKey, string> = {
    inputUsdPerMTok: t.input, outputUsdPerMTok: t.output, cacheReadUsdPerMTok: t.cacheRead, cacheWriteUsdPerMTok: t.cacheWrite,
  };

  return (
    <>
      <PageHeader title={t.aiSettings} subtitle={t.aiKeysSubtitle} />
      <Grid container spacing={2} sx={{ mb: 3 }}>
        {data.providers.map((p, i) => (
          <Grid key={p.name} size={{ xs: 12, sm: 6 }}>
            <Rise index={i}>
              <Card>
                <CardContent>
                  <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                    <Typography variant="h6" sx={{ flex: 1 }}>{p.name}</Typography>
                    {p.isSimulated && <Chip size="small" label={`🧪 ${t.simulated}`} />}
                    <Chip size="small" color={p.isAvailable ? "success" : "default"} label={p.isAvailable ? t.available : t.notConfigured} />
                  </Stack>
                  {p.isSimulated && <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>{t.simulatedHint}</Typography>}
                  {!p.isSimulated && <ProviderKey provider={p} canManage={canManage} onChanged={() => void reload()} />}
                </CardContent>
              </Card>
            </Rise>
          </Grid>
        ))}
      </Grid>

      {message && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}
      <FallbackOrder models={data.models} providers={data.providers} canManage={canManage} onError={setMessage} onChanged={() => void reload()} />
      <Stack direction="row" sx={{ alignItems: "center", mb: 1 }}>
        <Typography variant="h6" sx={{ flex: 1 }}>{t.prices}</Typography>
        {canManage && <Button startIcon={<AddIcon />} onClick={() => setAdding(true)}>{t.addModel}</Button>}
      </Stack>
      <TableContainer component={Paper} variant="outlined">
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t.model}</TableCell>
              {PRICE_KEYS.map((k) => <TableCell key={k} align="right">{headers[k]}</TableCell>)}
              <TableCell align="center">{t.enabled}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data.models.map((m) => (
              <TableRow key={m.id}>
                <TableCell>
                  <Typography variant="body2" fontWeight={600}>{m.displayName}</Typography>
                  <Typography variant="caption" color="text.secondary">{m.provider}/{m.modelId} · {label(m.tier)}</Typography>
                </TableCell>
                {PRICE_KEYS.map((k) => (
                  <TableCell key={k} align="right">
                    <TextField
                      size="small"
                      type="number"
                      defaultValue={m[k]}
                      disabled={!canManage}
                      slotProps={{ htmlInput: { min: 0, step: 0.01, style: { textAlign: "right", width: 80 } } }}
                      onBlur={(e) => {
                        const v = Number(e.target.value);
                        if (v !== m[k]) void update(m, { [k]: v } as Partial<AiModel>);
                      }}
                    />
                  </TableCell>
                ))}
                <TableCell align="center">
                  <Switch checked={m.isEnabled} disabled={!canManage} onChange={(e) => update(m, { isEnabled: e.target.checked })} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {canManage && <TrainingExport />}
      {adding && <AddModelDialog providers={data.providers.filter((x) => !x.isSimulated).map((x) => x.name)} onClose={() => setAdding(false)} onSaved={() => { setAdding(false); void reload(); }} />}
    </>
  );
}

/** Real work people approved, downloaded as fine-tuning data for the company's own model (D:\FreeAi\train format). */
function TrainingExport() {
  const { t, lang } = useI18n();
  const errorMessage = useErrorMessage();
  const [since, setSince] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const { data } = useLoad(() => api.get<TrainingExportSummary>(`/api/training/summary${since ? `?since=${since}` : ""}`), since);
  const when = (s: string | null) => (s ? new Date(s).toLocaleDateString(lang === "th" ? "th-TH" : "en-GB") : "—");
  return (
    <Card variant="outlined" sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h6">{t.trainingExport}</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>{t.trainingExportHint}</Typography>
        <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5} sx={{ alignItems: { sm: "center" } }}>
          <TextField size="small" type="date" label={t.since} value={since} onChange={(e) => setSince(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
          <Typography variant="body2" sx={{ flex: 1 }}>
            {data ? `${t.trainingWorkRuns} ${data.workRuns} · ${t.trainingPlanRuns} ${data.planRuns} · ${when(data.oldest)} – ${when(data.newest)}` : "…"}
          </Typography>
          <Button variant="outlined" startIcon={<DownloadIcon />} disabled={!data || data.workRuns + data.planRuns === 0}
            onClick={() => downloadFile(`/api/training/export${since ? `?since=${since}` : ""}`, "officeai-real.jsonl").catch((e) => setProblem(errorMessage(e)))}>
            {t.download}
          </Button>
        </Stack>
        {problem && <Alert severity="warning" sx={{ mt: 1 }} onClose={() => setProblem(null)}>{problem}</Alert>}
      </CardContent>
    </Card>
  );
}

/** Company-wide order of models that take over when an employee's own model cannot answer. */
function FallbackOrder({ models, providers, canManage, onError, onChanged }: {
  models: AiModel[]; providers: AiProvider[]; canManage: boolean; onError: (m: string) => void; onChanged: () => void;
}) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [busy, setBusy] = useState(false);
  const [adding, setAdding] = useState("");
  const order = models.filter((m) => m.fallbackRank != null).sort((a, b) => a.fallbackRank! - b.fallbackRank!);
  const ready = (m: AiModel) => providers.some((p) => p.name === m.provider && p.isAvailable);
  const simulated = new Set(providers.filter((p) => p.isSimulated).map((p) => p.name));
  const addable = models.filter((m) => m.isEnabled && m.fallbackRank == null && !simulated.has(m.provider));

  async function save(ids: string[]) {
    setBusy(true);
    try {
      await api.put("/api/ai/fallback", { modelIds: ids });
    } catch (e) {
      onError(errorMessage(e));
    }
    setBusy(false);
    onChanged();
  }
  const ids = order.map((m) => m.id);
  const move = (i: number, by: number) => {
    const next = [...ids];
    [next[i], next[i + by]] = [next[i + by], next[i]];
    void save(next);
  };

  return (
    <Card variant="outlined" sx={{ mb: 3 }}>
      <CardContent>
        <Typography variant="h6">{t.fallbackOrder}</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>{t.fallbackHint}</Typography>
        {order.length === 0 && <Typography variant="body2" sx={{ mb: 1 }}>{t.fallbackEmpty}</Typography>}
        <Stack spacing={0.5} sx={{ mb: canManage ? 1.5 : 0 }}>
          {order.map((m, i) => (
            <Stack key={m.id} direction="row" spacing={1} sx={{ alignItems: "center" }}>
              <Chip size="small" label={i + 1} />
              <Typography variant="body2" sx={{ flex: 1, minWidth: 0 }} noWrap>
                <b>{m.displayName}</b> <Typography component="span" variant="caption" color="text.secondary">{m.provider}/{m.modelId}</Typography>
              </Typography>
              {!ready(m) && <Chip size="small" color="warning" variant="outlined" label={t.providerNotReady} />}
              {canManage && (
                <>
                  <Tooltip title={t.moveUp}><span><IconButton size="small" disabled={busy || i === 0} onClick={() => move(i, -1)}><ArrowUpwardIcon fontSize="small" /></IconButton></span></Tooltip>
                  <Tooltip title={t.moveDown}><span><IconButton size="small" disabled={busy || i === order.length - 1} onClick={() => move(i, 1)}><ArrowDownwardIcon fontSize="small" /></IconButton></span></Tooltip>
                  <Tooltip title={t.remove}><span><IconButton size="small" disabled={busy} onClick={() => void save(ids.filter((x) => x !== m.id))}><CloseIcon fontSize="small" /></IconButton></span></Tooltip>
                </>
              )}
            </Stack>
          ))}
        </Stack>
        {canManage && addable.length > 0 && (
          <Stack direction="row" spacing={1}>
            <TextField select size="small" sx={{ minWidth: 260, maxWidth: "100%" }} label={t.addFallback} value={adding} onChange={(e) => setAdding(e.target.value)}>
              {addable.map((m) => <MenuItem key={m.id} value={m.id}>{m.displayName} · {m.provider}</MenuItem>)}
            </TextField>
            <Button disabled={busy || !adding} startIcon={<AddIcon />} onClick={() => { void save([...ids, adding]); setAdding(""); }}>{t.addFallback}</Button>
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}

/** Key for one provider: write-only. Shows where the key comes from and its last 4 characters, never the key. */
function ProviderKey({ provider, canManage, onChanged }: { provider: AiProvider; canManage: boolean; onChanged: () => void }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [value, setValue] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null);

  async function run(fn: () => Promise<void>) {
    setBusy(true);
    setResult(null);
    try { await fn(); } catch (e) { setResult({ ok: false, text: errorMessage(e) }); }
    setBusy(false);
    onChanged();
  }

  const source = provider.isFlatRate ? (provider.isAvailable ? t.noKeyNeeded : t.notConnected)
    : provider.keySource === "settings" ? `${t.keyFromSettings} ••••${provider.keyLast4}`
    : provider.keySource === "environment" ? t.keyFromEnv : t.notConfigured;
  const works = provider.isFlatRate ? t.connectionWorks : t.keyWorks;

  return (
    <Stack spacing={1} sx={{ mt: 1.5 }}>
      <Typography variant="body2" color="text.secondary">{provider.isFlatRate ? "🖥️" : "🔑"} {source}</Typography>
      {canManage && (
        <>
          {!provider.isFlatRate && <Stack direction="row" spacing={1}>
            <TextField size="small" fullWidth type="password" autoComplete="off" label={provider.keySource === "settings" ? t.replaceKey : t.apiKey}
              value={value} onChange={(e) => setValue(e.target.value)} slotProps={{ htmlInput: { "data-lpignore": "true", spellCheck: false } }} />
            <Button variant="contained" disabled={busy || value.trim().length < 10} onClick={() => run(async () => {
              await api.put(`/api/ai/providers/${provider.name}/key`, { apiKey: value.trim() });
              setValue("");
              const check = await api.post<{ ok: boolean; problem: string | null }>(`/api/ai/providers/${provider.name}/verify`);
              setResult(check.ok ? { ok: true, text: t.keyWorks } : { ok: false, text: `${t.keySavedButFailed}: ${check.problem}` });
            })}>{t.save}</Button>
          </Stack>}
          <Stack direction="row" spacing={1}>
            <Button size="small" disabled={busy || !provider.isAvailable} onClick={() => run(async () => {
              const check = await api.post<{ ok: boolean; problem: string | null }>(`/api/ai/providers/${provider.name}/verify`);
              setResult(check.ok ? { ok: true, text: works } : { ok: false, text: check.problem ?? "" });
            })}>{t.testKey}</Button>
            {provider.keySource === "settings" && (
              <Button size="small" color="error" disabled={busy} onClick={() => run(async () => { await api.del(`/api/ai/providers/${provider.name}/key`); })}>
                {t.removeKey}
              </Button>
            )}
          </Stack>
        </>
      )}
      {result && <Alert severity={result.ok ? "success" : "warning"} sx={{ py: 0 }}>{result.text}</Alert>}
    </Stack>
  );
}

const TIERS = ["Routine", "Standard", "Advanced"] as const;

/** Add a model with its list prices (USD per million tokens). */
function AddModelDialog({ providers, onClose, onSaved }: { providers: string[]; onClose: () => void; onSaved: () => void }) {
  const { t, label } = useI18n();
  const errorMessage = useErrorMessage();
  const [form, setForm] = useState({ provider: providers[0] ?? "", modelId: "", displayName: "", tier: "Standard", input: "", output: "", cacheRead: "", cacheWrite: "" });
  const [problem, setProblem] = useState<string | null>(null);
  // What the provider offers this key (free listing call); empty when its key is not set.
  const { data: remote } = useLoad(() => (form.provider ? api.get<string[]>(`/api/ai/providers/${form.provider}/models`).catch(() => []) : Promise.resolve([])), form.provider);
  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, [k]: e.target.value });
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t.addModel}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {problem && <Alert severity="warning">{problem}</Alert>}
          <Stack direction="row" spacing={2}>
            <TextField select fullWidth label="Provider" value={form.provider} onChange={set("provider")}>
              {providers.map((p) => <MenuItem key={p} value={p}>{p}</MenuItem>)}
            </TextField>
            <TextField select fullWidth label={t.tier} value={form.tier} onChange={set("tier")}>
              {TIERS.map((x) => <MenuItem key={x} value={x}>{label(x)}</MenuItem>)}
            </TextField>
          </Stack>
          <Autocomplete freeSolo options={remote ?? []} inputValue={form.modelId}
            onInputChange={(_, v) => setForm((f) => ({ ...f, modelId: v, displayName: f.displayName || "" }))}
            renderInput={(params) => <TextField {...params} label="Model ID" helperText={remote?.length ? `${remote.length} ${t.modelsAvailable}` : undefined} />} />
          <TextField label={t.displayName} value={form.displayName} onChange={set("displayName")} />
          <Typography variant="caption" color="text.secondary">{t.pricesHint}</Typography>
          <Stack direction="row" spacing={1}>
            <TextField type="number" label={t.input} value={form.input} onChange={set("input")} />
            <TextField type="number" label={t.output} value={form.output} onChange={set("output")} />
            <TextField type="number" label={t.cacheRead} value={form.cacheRead} onChange={set("cacheRead")} />
            <TextField type="number" label={t.cacheWrite} value={form.cacheWrite} onChange={set("cacheWrite")} />
          </Stack>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t.cancel}</Button>
        <Button variant="contained" disabled={!form.modelId.trim() || !form.displayName.trim() || !form.input || !form.output} onClick={async () => {
          try {
            await api.post("/api/ai/models", {
              provider: form.provider, modelId: form.modelId.trim(), displayName: form.displayName.trim(), tier: form.tier,
              inputUsdPerMTok: Number(form.input), outputUsdPerMTok: Number(form.output),
              cacheReadUsdPerMTok: Number(form.cacheRead || 0), cacheWriteUsdPerMTok: Number(form.cacheWrite || 0),
            });
            onSaved();
          } catch (e) { setProblem(errorMessage(e)); }
        }}>{t.create}</Button>
      </DialogActions>
    </Dialog>
  );
}
