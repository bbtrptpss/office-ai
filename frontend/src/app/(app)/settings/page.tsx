"use client";

import { useState } from "react";
import {
  Alert, Button, Card, CardContent, FormControlLabel, Stack, Switch, TextField, Typography,
} from "@mui/material";
import MailIcon from "@mui/icons-material/MailOutlined";
import EventIcon from "@mui/icons-material/EventOutlined";
import ContentCopyIcon from "@mui/icons-material/ContentCopy";
import { api, CalendarFeed, DeliverySettings } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";

/** A person's own email notifications and private calendar link. */
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
