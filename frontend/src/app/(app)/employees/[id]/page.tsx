"use client";

import { use, useState } from "react";
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, FormControlLabel, Grid, LinearProgress, MenuItem, Stack, Switch,
  TextField, Typography,
} from "@mui/material";
import { api, AgentConfig, AgentRun, AgentTool, AiModel, AiProvider, Effort, Employee, usd } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, StatusChip, useErrorMessage, useLoad } from "@/components/common";
import { Avatar, Rise } from "@/components/cute";
import { RunList } from "@/components/agents";

const EFFORTS: Effort[] = ["Low", "Medium", "High", "XHigh", "Max"];

export default function EmployeeProfilePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const canManage = can("agent.manage");

  const { data, error, reload } = useLoad(async () => {
    const employee = await api.get<Employee>(`/api/employees/${id}`);
    if (employee.kind !== "AI") return { employee, ai: null };
    const [config, runs, models, tools, providers] = await Promise.all([
      api.get<AgentConfig>(`/api/agents/${id}/config`),
      api.get<AgentRun[]>(`/api/agent-runs?employeeId=${id}&take=20`),
      api.get<AiModel[]>("/api/ai/models"),
      api.get<AgentTool[]>("/api/ai/tools"),
      api.get<AiProvider[]>("/api/ai/providers"),
    ]);
    return { employee, ai: { config, runs, models, tools, providers } };
  }, id);
  useRealtime<{ runId: string }>("agentRunChanged", () => void reload());

  const [draft, setDraft] = useState<AgentConfig | null>(null);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  if (!data) return <Loading error={error} />;
  const { employee, ai } = data;
  const config = draft ?? ai?.config ?? null;

  async function save() {
    if (!config) return;
    try {
      await api.put(`/api/agents/${id}/config`, config);
      await api.put(`/api/agents/${id}/tools`, { tools: config.tools });
      setDraft(null);
      setMessage({ ok: true, text: "✓" });
    } catch (e) {
      setMessage({ ok: false, text: errorMessage(e) });
      setDraft(null);
    }
    await reload();
  }

  const edit = (patch: Partial<AgentConfig>) => config && setDraft({ ...config, ...patch });
  const spentPct = config && config.monthlyBudgetUsd > 0 ? Math.min(100, (config.spentThisMonthUsd / config.monthlyBudgetUsd) * 100) : 0;
  const provider = ai?.providers.find((p) => p.name === config?.provider);

  return (
    <>
      <Rise>
        <Card sx={{ mb: 3 }}>
          <CardContent>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { sm: "center" } }}>
              <Avatar kind={employee.kind} seed={employee.displayName} size={88} />
              <Box sx={{ flex: 1 }}>
                <Typography variant="h5">{employee.displayName}</Typography>
                <Typography color="text.secondary">{employee.jobTitle} · {employee.departmentName}</Typography>
                <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                  <StatusChip value={employee.status} />
                  <Chip size="small" label={employee.kind === "AI" ? t.ai : t.human} />
                </Stack>
              </Box>
            </Stack>
          </CardContent>
        </Card>
      </Rise>

      {ai && config && (
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, md: 6 }}>
            <Rise index={1}>
              <Card>
                <CardContent>
                  <Typography variant="h6" gutterBottom>{t.aiSettings}</Typography>
                  {message && <Alert severity={message.ok ? "success" : "error"} sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message.text}</Alert>}
                  {provider && !provider.isAvailable && <Alert severity="info" sx={{ mb: 2 }}>{config.provider}: {t.notConfigured}</Alert>}
                  <Stack spacing={2}>
                    <Stack direction="row" spacing={2}>
                      <TextField select label={t.provider} value={config.provider} disabled={!canManage} fullWidth
                        onChange={(e) => edit({ provider: e.target.value, modelId: ai.models.find((m) => m.provider === e.target.value && m.isEnabled)?.modelId ?? "" })}>
                        {ai.providers.map((p) => (
                          <MenuItem key={p.name} value={p.name}>
                            {p.name}{p.isSimulated ? ` (${t.simulated})` : ""}{p.isAvailable ? "" : " ✕"}
                          </MenuItem>
                        ))}
                      </TextField>
                      <TextField select label={t.model} value={config.modelId} disabled={!canManage} fullWidth onChange={(e) => edit({ modelId: e.target.value })}>
                        {ai.models.filter((m) => m.provider === config.provider && m.isEnabled).map((m) => (
                          <MenuItem key={m.id} value={m.modelId}>{m.displayName} · {label(m.tier)}</MenuItem>
                        ))}
                      </TextField>
                    </Stack>
                    <TextField select label={t.effort} value={config.effort} disabled={!canManage} onChange={(e) => edit({ effort: e.target.value as Effort })}>
                      {EFFORTS.map((x) => <MenuItem key={x} value={x}>{label(x)}</MenuItem>)}
                    </TextField>
                    <Box>
                      <TextField label={t.budgetMonthly} type="number" value={config.monthlyBudgetUsd} disabled={!canManage} fullWidth
                        onChange={(e) => edit({ monthlyBudgetUsd: Number(e.target.value) })} slotProps={{ htmlInput: { min: 0, step: 0.5 } }} />
                      <LinearProgress variant="determinate" value={spentPct} color={spentPct >= 80 ? "warning" : "primary"} sx={{ height: 8, mt: 1 }} />
                      <Typography variant="caption" color="text.secondary">
                        {t.spentThisMonth}: {usd(config.spentThisMonthUsd)} / {usd(config.monthlyBudgetUsd)}
                      </Typography>
                    </Box>
                    <Typography variant="subtitle2" color="text.secondary">{t.limits}</Typography>
                    <Grid container spacing={2}>
                      {([
                        ["maxTurnsPerRun", t.maxTurns], ["maxToolCallsPerRun", t.maxToolCalls], ["maxTokensPerRun", t.maxTokensRun],
                        ["maxTokensPerCall", t.maxTokensCall], ["timeoutSeconds", t.timeout],
                      ] as const).map(([key, text]) => (
                        <Grid key={key} size={6}>
                          <TextField label={text} type="number" size="small" fullWidth value={config[key]} disabled={!canManage}
                            onChange={(e) => edit({ [key]: Number(e.target.value) } as Partial<AgentConfig>)} />
                        </Grid>
                      ))}
                    </Grid>
                    <FormControlLabel control={<Switch checked={config.isEnabled} disabled={!canManage} onChange={(e) => edit({ isEnabled: e.target.checked })} />} label={t.enabled} />
                  </Stack>
                </CardContent>
              </Card>
            </Rise>
          </Grid>

          <Grid size={{ xs: 12, md: 6 }}>
            <Rise index={2}>
              <Card sx={{ mb: 2 }}>
                <CardContent>
                  <Typography variant="h6" gutterBottom>{t.tools}</Typography>
                  <Stack spacing={0.5}>
                    {ai.tools.map((tool) => (
                      <FormControlLabel
                        key={tool.id}
                        disabled={!canManage}
                        control={<Checkbox checked={config.tools.includes(tool.id)}
                          onChange={(e) => edit({ tools: e.target.checked ? [...config.tools, tool.id] : config.tools.filter((x) => x !== tool.id) })} />}
                        label={
                          <Box>
                            <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                              <Typography variant="body2" fontFamily="monospace">{tool.id}</Typography>
                              {tool.hasSideEffects && <Chip size="small" variant="outlined" label={t.sideEffects} />}
                              {tool.requiresApproval && <Chip size="small" color="warning" variant="outlined" label={t.needsApproval} />}
                            </Stack>
                            <Typography variant="caption" color="text.secondary">{tool.description}</Typography>
                          </Box>
                        }
                      />
                    ))}
                  </Stack>
                </CardContent>
              </Card>
              {canManage && (
                <Stack direction="row" spacing={1} sx={{ justifyContent: "flex-end", mb: 2 }}>
                  <Button disabled={!draft} onClick={() => setDraft(null)}>{t.cancel}</Button>
                  <Button variant="contained" disabled={!draft} onClick={save}>{t.save}</Button>
                </Stack>
              )}
            </Rise>
          </Grid>

          <Grid size={12}>
            <Typography variant="h6" gutterBottom>{t.recentRuns}</Typography>
            <RunList runs={ai.runs} />
          </Grid>
        </Grid>
      )}
    </>
  );
}
