"use client";

import { useState } from "react";
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Grid, Paper, Stack, TextField, Typography } from "@mui/material";
import { api, Retention, RetentionCounts } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { Loading, useErrorMessage, useLoad } from "./common";

type Field = "runTranscriptDays" | "testOutputDays" | "notificationDays" | "integrationDeliveryDays" | "chatMessageDays";
const FIELDS: Field[] = ["runTranscriptDays", "testOutputDays", "notificationDays", "integrationDeliveryDays", "chatMessageDays"];
const COUNTS: (keyof RetentionCounts)[] = ["runTranscripts", "runSteps", "toolOutputs", "testOutputs", "notifications", "integrationDeliveries", "chatMessages"];

/** Data retention policy (security.manage). Empty keeps data forever; clean-up shows what it will remove and asks first. */
export function RetentionPanel() {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<Retention>("/api/security/retention"));
  const [form, setForm] = useState<Record<Field, string> | null>(null);
  const [preview, setPreview] = useState<RetentionCounts | null>(null);
  const [message, setMessage] = useState<{ severity: "success" | "warning"; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  if (!data) return <Loading error={error} />;
  const values = form ?? (Object.fromEntries(FIELDS.map((f) => [f, data[f]?.toString() ?? ""])) as Record<Field, string>);
  const summary = (c: RetentionCounts) =>
    COUNTS.filter((k) => c[k] > 0).map((k) => `${t[`rc_${k}`]} ${c[k].toLocaleString()}`).join(" · ") || t.retentionNothing;

  async function run(fn: () => Promise<void>) {
    setBusy(true);
    setMessage(null);
    try { await fn(); } catch (e) { setMessage({ severity: "warning", text: errorMessage(e) }); }
    setBusy(false);
  }

  const save = () => run(async () => {
    const body = Object.fromEntries(FIELDS.map((f) => [f, values[f].trim() === "" ? null : Number(values[f])]));
    await api.put("/api/security/retention", { ...body, version: data.version });
    setForm(null);
    await reload();
    setMessage({ severity: "success", text: t.retentionSaved });
  });

  return (
    <Paper variant="outlined" sx={{ p: 2, mt: 3 }}>
      <Typography variant="h6" gutterBottom>{t.retentionTitle}</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>{t.retentionNote}</Typography>
      {message && <Alert severity={message.severity} sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message.text}</Alert>}
      <Grid container spacing={2}>
        {FIELDS.map((f) => (
          <Grid key={f} size={{ xs: 12, sm: 6, md: 4 }}>
            <TextField fullWidth size="small" type="number" label={t[`rf_${f}`]} value={values[f]} placeholder={t.keepForever}
              helperText={t[`rh_${f}`]} slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: 30, max: 3650 } }}
              onChange={(e) => setForm({ ...values, [f]: e.target.value })} />
          </Grid>
        ))}
      </Grid>
      <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5} sx={{ mt: 2, alignItems: { sm: "center" } }}>
        <Button variant="contained" disabled={busy || !form} onClick={save}>{t.save}</Button>
        <Button color="warning" disabled={busy || !!form}
          onClick={() => run(async () => setPreview(await api.get<RetentionCounts>("/api/security/retention/preview")))}>
          {t.retentionRunNow}
        </Button>
        <Typography variant="body2" color="text.secondary">
          {data.lastRunAt
            ? `${t.retentionLastRun}: ${new Date(data.lastRunAt).toLocaleString()}${data.lastResult ? ` · ${summary(data.lastResult)}` : ""}`
            : t.retentionNeverRun}
        </Typography>
      </Stack>

      <Dialog open={!!preview} onClose={() => setPreview(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t.retentionConfirmTitle}</DialogTitle>
        <DialogContent>
          <Typography sx={{ mb: 1 }}>{preview && summary(preview)}</Typography>
          <Typography variant="body2" color="text.secondary">{t.retentionConfirmNote}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setPreview(null)}>{t.cancel}</Button>
          <Button color="error" variant="contained" disabled={busy || !preview || COUNTS.every((k) => preview[k] === 0)}
            onClick={() => run(async () => {
              const done = await api.post<RetentionCounts>("/api/security/retention/apply");
              setPreview(null);
              await reload();
              setMessage({ severity: "success", text: `${t.retentionDone}: ${summary(done)}` });
            })}>
            {t.retentionApply}
          </Button>
        </DialogActions>
      </Dialog>
    </Paper>
  );
}
