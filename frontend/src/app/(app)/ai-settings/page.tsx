"use client";

import { useState } from "react";
import {
  Alert, Card, CardContent, Chip, Grid, Paper, Stack, Switch, Table, TableBody, TableCell, TableContainer, TableHead,
  TableRow, TextField, Typography,
} from "@mui/material";
import { api, AiModel, AiProvider } from "@/lib/api";
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
      <PageHeader title={t.aiSettings} subtitle="API keys are read from server configuration (.env), never stored in the database." />
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
                </CardContent>
              </Card>
            </Rise>
          </Grid>
        ))}
      </Grid>

      {message && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}
      <Typography variant="h6" gutterBottom>{t.prices}</Typography>
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
    </>
  );
}
