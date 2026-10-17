"use client";

import { useState } from "react";
import {
  Alert, Button, Card, CardContent, Grid, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from "@mui/material";
import { api, CompanySettings, RoleRate } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, useErrorMessage, useLoad } from "@/components/common";

export default function CompanySettingsPage() {
  const { t } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const canManage = can("project.manage");
  const { data, error, reload } = useLoad(async () => {
    const [settings, rates] = await Promise.all([api.get<CompanySettings>("/api/company-settings"), api.get<RoleRate[]>("/api/rate-card")]);
    return { settings, rates };
  });
  const [draft, setDraft] = useState<CompanySettings | null>(null);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  if (!data) return <Loading error={error} />;
  const s = draft ?? data.settings;

  async function act(fn: () => Promise<unknown>) {
    try {
      await fn();
      setMessage({ ok: true, text: "✓" });
    } catch (e) {
      setMessage({ ok: false, text: errorMessage(e) });
    }
    setDraft(null);
    await reload();
  }

  return (
    <>
      <PageHeader title={t.companySettings} />
      {message && <Alert severity={message.ok ? "success" : "error"} sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message.text}</Alert>}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Grid container spacing={2}>
            {([["usdToThb", t.usdToThb], ["defaultContingencyPercent", t.defaultContingency], ["targetMarginPercent", t.targetMargin]] as const).map(([k, label]) => (
              <Grid key={k} size={{ xs: 12, sm: 4 }}>
                <TextField label={label} type="number" fullWidth value={s[k]} disabled={!canManage}
                  onChange={(e) => setDraft({ ...s, [k]: Number(e.target.value) })} />
              </Grid>
            ))}
          </Grid>
          {canManage && (
            <Stack direction="row" sx={{ justifyContent: "flex-end", mt: 2 }}>
              <Button variant="contained" disabled={!draft} onClick={() => act(() => api.put("/api/company-settings", s))}>{t.save}</Button>
            </Stack>
          )}
        </CardContent>
      </Card>

      <Typography variant="h6" gutterBottom>{t.rateCard}</Typography>
      <TableContainer component={Paper} variant="outlined">
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t.jobTitle}</TableCell>
              <TableCell>{t.code}</TableCell>
              <TableCell align="right">{t.rate}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data.rates.map((r) => (
              <TableRow key={r.roleDefinitionId}>
                <TableCell>{r.title}</TableCell>
                <TableCell sx={{ fontFamily: "monospace" }}>{r.code}</TableCell>
                <TableCell align="right">
                  <TextField size="small" type="number" defaultValue={r.hourlyRateThb} disabled={!canManage}
                    slotProps={{ htmlInput: { min: 0, step: 50, style: { textAlign: "right", width: 100 } } }}
                    onBlur={(e) => {
                      const v = Number(e.target.value);
                      if (v !== r.hourlyRateThb) void act(() => api.put(`/api/rate-card/${r.roleDefinitionId}`, { hourlyRateThb: v }));
                    }} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </>
  );
}
