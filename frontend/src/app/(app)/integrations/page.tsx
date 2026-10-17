"use client";

import { useState } from "react";
import {
  Alert, Button, Card, CardContent, Checkbox, Chip, Collapse, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Stack, Switch,
  TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import { api, Delivery, Integration, INTEGRATION_EVENTS } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";

export default function IntegrationsPage() {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<Integration[]>("/api/integrations"));
  const [form, setForm] = useState<{ name: string; secretEnvVar: string; events: string[] } | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function act(fn: () => Promise<unknown>) {
    setMessage(null);
    try { await fn(); } catch (e) { setMessage(errorMessage(e)); }
    await reload();
  }

  return (
    <>
      <PageHeader title={t.integrations} subtitle="Webhook URLs live in server environment variables named INTEGRATION_…, never in the database."
        actions={<Button variant="contained" startIcon={<AddIcon />} onClick={() => setForm({ name: "", secretEnvVar: "INTEGRATION_", events: ["approval.requested"] })}>{t.newIntegration}</Button>} />
      {message && <Alert severity="warning" sx={{ mb: 2 }}>{message}</Alert>}
      {!data ? <Loading error={error} /> : data.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
        <Stack spacing={2}>
          {data.map((i) => <IntegrationCard key={i.id} item={i} onToggle={(on) => act(() => api.put(`/api/integrations/${i.id}`, { ...i, isEnabled: on }))} />)}
        </Stack>
      )}
      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.newIntegration}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField label={t.name} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
              <TextField label={t.envVar} value={form.secretEnvVar} onChange={(e) => setForm({ ...form, secretEnvVar: e.target.value.toUpperCase() })}
                slotProps={{ htmlInput: { style: { fontFamily: "monospace" } } }} />
              <Typography variant="subtitle2">{t.events}</Typography>
              {INTEGRATION_EVENTS.map((ev) => (
                <FormControlLabel key={ev} label={<code>{ev}</code>} control={<Checkbox checked={form.events.includes(ev)}
                  onChange={(e) => setForm({ ...form, events: e.target.checked ? [...form.events, ev] : form.events.filter((x) => x !== ev) })} />} />
              ))}
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!form?.name || form.events.length === 0} onClick={() => act(async () => { await api.post("/api/integrations", form); setForm(null); })}>{t.create}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

function IntegrationCard({ item: i, onToggle }: { item: Integration; onToggle: (on: boolean) => void }) {
  const { t, lang } = useI18n();
  const [open, setOpen] = useState(false);
  const { data } = useLoad(async () => (open ? api.get<Delivery[]>(`/api/integrations/${i.id}/deliveries`) : []), `${i.id}:${open}`);
  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
          <Typography variant="h6" sx={{ flex: 1 }}>🔔 {i.name}</Typography>
          <Chip size="small" sx={{ fontFamily: "monospace" }} label={i.secretEnvVar} color={i.secretPresent ? "default" : "warning"} />
          <FormControlLabel control={<Switch checked={i.isEnabled} onChange={(e) => onToggle(e.target.checked)} />} label={t.enabled} />
        </Stack>
        {!i.secretPresent && <Alert severity="warning" sx={{ mt: 1 }}>{t.secretMissing}</Alert>}
        <Stack direction="row" spacing={0.5} sx={{ mt: 1, flexWrap: "wrap", rowGap: 0.5 }}>
          {i.events.map((e) => <Chip key={e} size="small" variant="outlined" label={e} />)}
        </Stack>
        <Button size="small" sx={{ mt: 1 }} onClick={() => setOpen(!open)}>{t.deliveries}</Button>
        <Collapse in={open}>
          <Stack spacing={0.5} sx={{ mt: 1 }}>
            {(data ?? []).map((d) => (
              <Stack key={d.id} direction="row" spacing={1} sx={{ alignItems: "center" }}>
                <StatusChip value={d.status} />
                <Typography variant="body2" sx={{ flex: 1 }} noWrap>{d.title}</Typography>
                <Typography variant="caption" color="text.secondary">{d.statusCode ?? d.error} · {new Date(d.createdAt).toLocaleString(lang === "th" ? "th-TH" : "en-GB")}</Typography>
              </Stack>
            ))}
            {data?.length === 0 && <Typography variant="body2" color="text.secondary">{t.empty}</Typography>}
          </Stack>
        </Collapse>
      </CardContent>
    </Card>
  );
}
