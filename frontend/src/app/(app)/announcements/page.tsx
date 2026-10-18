"use client";

import { useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import {
  Alert, Button, Card, CardContent, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, Tab, Tabs,
  TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import PushPinIcon from "@mui/icons-material/PushPin";
import DoneIcon from "@mui/icons-material/DoneAll";
import { Announcement, AnnouncementPriority, AnnouncementStatus, api, Department } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { emitLocal, useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";
import { Mascot } from "@/components/cute";

interface Form {
  id?: string;
  version?: number;
  title: string;
  body: string;
  priority: AnnouncementPriority;
  departmentId: string;
  isPinned: boolean;
  publish: boolean;
}

const EMPTY: Form = { title: "", body: "", priority: "Normal", departmentId: "", isPinned: false, publish: true };

export default function AnnouncementsPage() {
  const { t, label, lang } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const reduce = useReducedMotion();
  const canManage = can("comms.manage");
  const [status, setStatus] = useState<AnnouncementStatus>("Published");
  const { data, error, reload } = useLoad(() => api.get<Announcement[]>(`/api/announcements?status=${status}`), status);
  const { data: departments } = useLoad(() => (canManage ? api.get<Department[]>("/api/departments") : Promise.resolve([])), String(canManage));
  const [form, setForm] = useState<Form | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  useRealtime("announcementsChanged", () => void reload());

  const fmt = new Intl.DateTimeFormat(lang === "th" ? "th-TH" : "en-GB", { dateStyle: "medium", timeStyle: "short" });

  async function act(fn: () => Promise<unknown>) {
    setMessage(null);
    try { await fn(); } catch (e) { setMessage(errorMessage(e)); }
    await reload();
  }

  async function save() {
    const f = form!;
    const body = { title: f.title, body: f.body, priority: f.priority, departmentId: f.departmentId || null, isPinned: f.isPinned, publish: f.publish, version: f.version };
    await act(async () => {
      if (f.id) await api.put(`/api/announcements/${f.id}`, body);
      else await api.post("/api/announcements", body);
      setForm(null);
    });
  }

  return (
    <>
      <PageHeader title={t.navAnnouncements}
        actions={canManage && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setForm({ ...EMPTY })}>{t.newAnnouncement}</Button>} />
      {canManage && (
        <Tabs value={status} onChange={(_, v) => setStatus(v)} sx={{ mb: 2 }}>
          {(["Published", "Draft", "Archived"] as const).map((s) => <Tab key={s} value={s} label={label(s)} />)}
        </Tabs>
      )}
      {message && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}
      {!data ? <Loading error={error} /> : data.length === 0 ? (
        <Stack sx={{ alignItems: "center", py: 6 }}><Mascot size={96} /><Typography color="text.secondary">{t.empty}</Typography></Stack>
      ) : (
        <Stack spacing={1.5}>
          <AnimatePresence initial={false}>
            {data.map((a) => (
              <motion.div key={a.id} layout initial={reduce ? false : { opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} exit={reduce ? undefined : { opacity: 0 }}>
                <Card sx={a.status === "Published" && !a.isRead ? { borderLeft: 4, borderLeftColor: a.priority === "Important" ? "error.main" : "secondary.main" } : undefined}>
                  <CardContent>
                    <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1, mb: 1 }}>
                      {a.isPinned && <PushPinIcon fontSize="small" color="secondary" titleAccess={t.pinned} />}
                      <Typography fontWeight={700} sx={{ flex: 1, minWidth: 160 }}>{a.title}</Typography>
                      {a.priority === "Important" && <StatusChip value="Important" />}
                      <Chip size="small" variant="outlined" label={a.departmentName ?? t.wholeCompany} />
                      {a.status !== "Published" && <StatusChip value={a.status} />}
                    </Stack>
                    {a.status === "Draft" && a.authorName && a.isSimulated && <Alert severity="warning" sx={{ mb: 1 }}>{t.simulatedDraft}</Alert>}
                    <Typography variant="body2" sx={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{a.body}</Typography>
                    <Stack direction="row" spacing={1} sx={{ mt: 1.5, alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
                      <Typography variant="caption" color="text.secondary" sx={{ flex: 1 }}>
                        {a.authorName ? `${a.authorName} · ` : ""}{fmt.format(new Date(a.publishedAt ?? a.createdAt))}
                        {canManage && a.status === "Published" ? ` · ${t.readBy} ${a.readCount}` : ""}
                      </Typography>
                      {a.status === "Published" && (a.isRead
                        ? <Chip size="small" icon={<DoneIcon />} label={t.readBy} color="success" variant="outlined" />
                        : <Button size="small" variant="contained" color="secondary" onClick={() => act(async () => {
                          await api.post(`/api/announcements/${a.id}/read`);
                          emitLocal("announcementRead");
                        })}>{t.markRead}</Button>)}
                      {canManage && a.status !== "Archived" && (
                        <>
                          <Button size="small" onClick={() => setForm({ id: a.id, version: a.version, title: a.title, body: a.body, priority: a.priority,
                            departmentId: a.departmentId ?? "", isPinned: a.isPinned, publish: false })}>{t.edit}</Button>
                          {a.status === "Draft" && (
                            <Button size="small" variant="contained" disabled={a.isSimulated} onClick={() => act(() => api.post(`/api/announcements/${a.id}/publish`))}>{t.publish}</Button>
                          )}
                          <Button size="small" onClick={() => act(() => api.post(`/api/announcements/${a.id}/archive`))}>{t.archive}</Button>
                        </>
                      )}
                    </Stack>
                  </CardContent>
                </Card>
              </motion.div>
            ))}
          </AnimatePresence>
        </Stack>
      )}
      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="md">
        <DialogTitle>{form?.id ? t.edit : t.newAnnouncement}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField label={t.title} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} slotProps={{ htmlInput: { maxLength: 200 } }} />
              <TextField label={t.description} value={form.body} onChange={(e) => setForm({ ...form, body: e.target.value })} multiline minRows={6} />
              <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                <TextField select fullWidth label={t.audience} value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value })}>
                  <MenuItem value="">{t.wholeCompany}</MenuItem>
                  {(departments ?? []).map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
                </TextField>
                <TextField select fullWidth label={t.priority} value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value as AnnouncementPriority })}>
                  {(["Normal", "Important"] as const).map((p) => <MenuItem key={p} value={p}>{label(p)}</MenuItem>)}
                </TextField>
              </Stack>
              <Stack direction="row" spacing={2}>
                <FormControlLabel control={<Checkbox checked={form.isPinned} onChange={(e) => setForm({ ...form, isPinned: e.target.checked })} />} label={t.pinned} />
                <FormControlLabel control={<Checkbox checked={form.publish} onChange={(e) => setForm({ ...form, publish: e.target.checked })} />} label={t.publishNow} />
              </Stack>
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!form?.title.trim() || !form?.body.trim()} onClick={() => void save()}>{t.save}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
