"use client";

import { useEffect, useState } from "react";
import QRCode from "qrcode";
import {
  Alert, Box, Button, Card, CardContent, Chip, FormControlLabel, Stack, Switch, TextField, Typography,
} from "@mui/material";
import MailIcon from "@mui/icons-material/MailOutlined";
import EventIcon from "@mui/icons-material/EventOutlined";
import ContentCopyIcon from "@mui/icons-material/ContentCopy";
import LockIcon from "@mui/icons-material/LockOutlined";
import { api, CalendarFeed, DeliverySettings } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";

/** A person's own two-factor sign-in, email notifications and private calendar link. */
export default function MySettingsPage() {
  const { t, lang } = useI18n();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(() => api.get<DeliverySettings>("/api/me/delivery-settings"));
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const [feed, setFeed] = useState<CalendarFeed | null>(null);
  const [copied, setCopied] = useState(false);
  const [busy, setBusy] = useState(false);
  const when = (s: string) => new Date(s).toLocaleString(lang === "th" ? "th-TH" : "en-GB");

  async function act(fn: () => Promise<unknown>, ok?: string) {
    setBusy(true);
    setMessage(null);
    try {
      await fn();
      if (ok) setMessage({ ok: true, text: ok });
    } catch (e) {
      setMessage({ ok: false, text: errorMessage(e) });
    } finally {
      setBusy(false);
    }
    await reload();
  }

  if (!data) return <Loading error={error} />;
  return (
    <>
      <PageHeader title={t.navMySettings} subtitle={t.mySettingsHint} />
      {message && <Alert severity={message.ok ? "success" : "warning"} sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message.text}</Alert>}
      <Stack spacing={2}>
        <TwoFactorCard />
        <Card>
          <CardContent>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
              <MailIcon color="secondary" />
              <Typography variant="h6" sx={{ flex: 1 }}>{t.emailNotifications}</Typography>
              <FormControlLabel label={t.emailOn} control={<Switch checked={data.emailNotifications} disabled={busy}
                onChange={(e) => act(() => api.put("/api/me/email-notifications", { enabled: e.target.checked }))} />} />
            </Stack>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>{t.emailNotificationsHint}</Typography>
            {!data.emailConfigured
              ? <Alert severity="info">{t.emailNotConfigured}</Alert>
              : (
                <Stack direction={{ xs: "column", sm: "row" }} spacing={1} sx={{ alignItems: { sm: "center" } }}>
                  <Typography variant="body2" sx={{ flex: 1 }}>
                    {t.emailTo}: <b>{data.myEmail ?? "—"}</b> · {t.emailConfiguredFrom}: {data.fromAddress}
                  </Typography>
                  <Button variant="outlined" size="small" disabled={busy} onClick={() => act(() => api.post("/api/me/email-test", {}), t.emailTestSent)}>{t.emailTest}</Button>
                </Stack>
              )}
            {data.recentEmails.length > 0 && (
              <>
                <Typography variant="subtitle2" color="text.secondary" sx={{ mt: 2, mb: 0.5 }}>{t.recentEmails}</Typography>
                <Stack spacing={0.5}>
                  {data.recentEmails.map((m) => (
                    <Stack key={m.id} direction="row" spacing={1} sx={{ alignItems: "center" }}>
                      <StatusChip value={m.status} />
                      <Typography variant="body2" sx={{ flex: 1, minWidth: 0 }} noWrap>{m.subject}</Typography>
                      <Typography variant="caption" color="text.secondary" noWrap>
                        {m.status === "Failed" ? `${m.error ?? ""} · ${m.attempts} ${t.attempts}` : when(m.sentAt ?? m.createdAt)}
                      </Typography>
                    </Stack>
                  ))}
                </Stack>
              </>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardContent>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
              <EventIcon color="secondary" />
              <Typography variant="h6" sx={{ flex: 1 }}>{t.calendarFeed}</Typography>
            </Stack>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>{t.calendarFeedHint}</Typography>
            {feed && (
              <Alert severity="success" sx={{ mb: 1.5 }}>
                <Typography variant="body2" sx={{ mb: 1 }}>{t.calendarFeedOnce}</Typography>
                <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                  <TextField size="small" fullWidth value={feed.url} slotProps={{ htmlInput: { readOnly: true, style: { fontFamily: "monospace", fontSize: 12 } } }}
                    onFocus={(e) => e.target.select()} />
                  <Button size="small" startIcon={<ContentCopyIcon />} sx={{ flexShrink: 0, whiteSpace: "nowrap" }} onClick={async () => {
                    try { await navigator.clipboard.writeText(feed.url); setCopied(true); } catch { /* the field is selectable */ }
                  }}>{copied ? t.copied : t.copy}</Button>
                </Stack>
                {/^https?:\/\/(localhost|127\.0\.0\.1)/.test(feed.url) && <Typography variant="caption" sx={{ display: "block", mt: 1 }}>{t.calendarFeedLocal}</Typography>}
              </Alert>
            )}
            <Stack direction={{ xs: "column", sm: "row" }} spacing={1} sx={{ alignItems: { sm: "center" } }}>
              {data.calendarFeedActive && data.calendarFeedCreatedAt && (
                <Typography variant="body2" sx={{ flex: 1 }}>{t.calendarFeedActive} {when(data.calendarFeedCreatedAt)}</Typography>
              )}
              <Button variant={data.calendarFeedActive ? "outlined" : "contained"} size="small" disabled={busy}
                onClick={() => act(async () => { setCopied(false); setFeed(await api.post<CalendarFeed>("/api/me/calendar-feed", {})); })}>
                {data.calendarFeedActive ? t.calendarFeedRotate : t.calendarFeedCreate}
              </Button>
              {data.calendarFeedActive && (
                <Button color="error" size="small" disabled={busy} onClick={() => act(async () => { await api.del("/api/me/calendar-feed"); setFeed(null); })}>
                  {t.calendarFeedRevoke}
                </Button>
              )}
            </Stack>
          </CardContent>
        </Card>
      </Stack>
    </>
  );
}

/** Two-factor sign-in with an authenticator app: set up with a QR code, recovery codes shown once, off with password + code. */
function TwoFactorCard() {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const { data, reload } = useLoad(() => api.get<{ enabled: boolean; recoveryCodesLeft: number }>("/api/auth/2fa"));
  const [setup, setSetup] = useState<{ sharedKey: string; otpauthUri: string } | null>(null);
  const [qr, setQr] = useState<string | null>(null);
  const [codes, setCodes] = useState<string[] | null>(null);
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const [disabling, setDisabling] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!setup) return;
    let live = true;
    QRCode.toDataURL(setup.otpauthUri, { margin: 1, width: 180 }).then((url) => { if (live) setQr(url); }, () => setQr(null));
    return () => { live = false; };
  }, [setup]);

  async function act(fn: () => Promise<void>) {
    setBusy(true);
    setProblem(null);
    try { await fn(); setCode(""); setPassword(""); } catch (e) { setProblem(errorMessage(e)); }
    setBusy(false);
    await reload();
  }

  if (!data) return null;
  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
          <LockIcon color="secondary" />
          <Typography variant="h6" sx={{ flex: 1 }}>{t.twoFactor}</Typography>
          <Chip size="small" color={data.enabled ? "success" : "default"} label={data.enabled ? t.twoFactorOn : t.twoFactorOff} />
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>{t.twoFactorHint}</Typography>
        {problem && <Alert severity="warning" sx={{ mb: 1.5 }} onClose={() => setProblem(null)}>{problem}</Alert>}
        {codes && (
          <Alert severity="success" sx={{ mb: 1.5 }} onClose={() => setCodes(null)}>
            <Typography variant="body2" sx={{ mb: 1 }}>{t.recoveryCodesSave}</Typography>
            <Box component="pre" sx={{ m: 0, fontFamily: "monospace", fontSize: 14, columns: 2 }}>{codes.join("\n")}</Box>
          </Alert>
        )}

        {!data.enabled && !setup && (
          <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
            <TextField size="small" type="password" label={t.password} value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />
            <Button variant="contained" size="small" disabled={busy || !password} onClick={() => act(async () => { setQr(null); setSetup(await api.post("/api/auth/2fa/setup", { password })); })}>
              {t.twoFactorStart}
            </Button>
          </Stack>
        )}
        {!data.enabled && setup && (
          <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ alignItems: { sm: "flex-start" } }}>
            {qr && <Box component="img" src={qr} alt="QR" sx={{ width: 180, height: 180, borderRadius: 2, bgcolor: "#fff", flexShrink: 0 }} />}
            <Stack spacing={1.5} sx={{ flex: 1, minWidth: 0 }}>
              <Typography variant="body2">{t.twoFactorScan}</Typography>
              <TextField size="small" label={t.twoFactorKey} value={setup.sharedKey} onFocus={(e) => e.target.select()}
                slotProps={{ htmlInput: { readOnly: true, style: { fontFamily: "monospace" } } }} />
              <Stack direction="row" spacing={1}>
                <TextField size="small" label={t.authenticatorCode} value={code} onChange={(e) => setCode(e.target.value)} autoComplete="one-time-code"
                  slotProps={{ htmlInput: { inputMode: "numeric", maxLength: 7 } }} />
                <Button variant="contained" disabled={busy || code.trim().length < 6} onClick={() => act(async () => {
                  const res = await api.post<{ codes: string[] }>("/api/auth/2fa/enable", { code: code.trim() });
                  setCodes(res.codes);
                  setSetup(null);
                })}>{t.twoFactorEnable}</Button>
              </Stack>
            </Stack>
          </Stack>
        )}

        {data.enabled && (
          <Stack spacing={1.5}>
            <Typography variant="body2">{t.recoveryCodesLeft}: <b>{data.recoveryCodesLeft}</b></Typography>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={1}>
              <TextField size="small" label={t.authenticatorCode} value={code} onChange={(e) => setCode(e.target.value)} autoComplete="one-time-code" />
              {disabling && <TextField size="small" type="password" label={t.password} value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" />}
              {!disabling && (
                <Button variant="outlined" size="small" disabled={busy || code.trim().length < 6} onClick={() => act(async () => {
                  setCodes((await api.post<{ codes: string[] }>("/api/auth/2fa/recovery-codes", { code: code.trim() })).codes);
                })}>{t.recoveryCodesNew}</Button>
              )}
              <Button color="error" size="small" disabled={busy || (disabling && (!password || code.trim().length < 6))} onClick={() => {
                if (!disabling) { setDisabling(true); setProblem(null); return; }
                void act(async () => {
                  await api.post("/api/auth/2fa/disable", { password, code: code.trim() });
                  setDisabling(false);
                });
              }}>{t.twoFactorDisable}</Button>
            </Stack>
            {disabling && <Typography variant="caption" color="text.secondary">{t.twoFactorDisableHint}</Typography>}
          </Stack>
        )}
      </CardContent>
    </Card>
  );
}
