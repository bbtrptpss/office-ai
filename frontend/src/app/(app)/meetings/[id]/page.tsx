"use client";

import { use, useState } from "react";
import Link from "next/link";
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, Grid, IconButton, MenuItem, Stack, TextField, Tooltip, Typography,
} from "@mui/material";
import ArrowBackIcon from "@mui/icons-material/ArrowBack";
import AddTaskIcon from "@mui/icons-material/AddTaskOutlined";
import { api, Attendance, Employee, Meeting } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";
import { Avatar } from "@/components/cute";
import { MeetingDialog } from "@/components/meetings";

export default function MeetingPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const { t, label, lang } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const { data: m, error, reload } = useLoad(() => api.get<Meeting>(`/api/meetings/${id}`), id);
  const { data: employees } = useLoad(() => api.get<Employee[]>("/api/employees"));
  const [editing, setEditing] = useState(false);
  const [minutes, setMinutes] = useState<string | null>(null);
  const [item, setItem] = useState({ title: "", ownerEmployeeId: "", dueDate: "" });
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  useRealtime<{ meetingId: string }>("meetingChanged", (p) => { if (p.meetingId === id) void reload(); });

  const canManage = can("comms.manage");
  const canPost = can("comms.post");
  const fmt = new Intl.DateTimeFormat(lang === "th" ? "th-TH" : "en-GB", { dateStyle: "full", timeStyle: "short" });

  async function act(fn: () => Promise<unknown>, ok?: string) {
    setMessage(null);
    try {
      await fn();
      if (ok) setMessage({ ok: true, text: ok });
    } catch (e) { setMessage({ ok: false, text: errorMessage(e) }); }
    await reload();
  }

  if (!m) return <Loading error={error} />;
  const cancelled = m.status === "Cancelled";
  const editingMinutes = minutes !== null;

  return (
    <>
      <Button component={Link} href="/meetings" startIcon={<ArrowBackIcon />} size="small" sx={{ mb: 1 }}>{t.navMeetings}</Button>
      <PageHeader title={m.title}
        subtitle={<>{fmt.format(new Date(m.scheduledAt))} · {m.durationMinutes} min{m.location ? ` · ${m.location}` : ""}{m.organizerName ? ` · ${t.organizer}: ${m.organizerName}` : ""}</>}
        actions={canManage && !cancelled && (
          <>
            <Button onClick={() => setEditing(true)}>{t.edit}</Button>
            {m.status === "Scheduled" && <Button onClick={() => act(() => api.post(`/api/meetings/${id}/status`, { status: "Held" }))}>{t.markHeld}</Button>}
            {m.minutesStatus !== "Approved" && <Button color="error" onClick={() => act(() => api.post(`/api/meetings/${id}/status`, { status: "Cancelled" }))}>{t.cancelMeeting}</Button>}
          </>
        )} />
      <Stack direction="row" spacing={1} sx={{ mb: 2, flexWrap: "wrap", rowGap: 1 }}>
        <StatusChip value={m.status} />
        {m.projectId && <Chip size="small" variant="outlined" component={Link} href={`/projects/${m.projectId}`} clickable label={`📁 ${m.projectName}`} />}
        {m.departmentName && <Chip size="small" variant="outlined" label={m.departmentName} />}
      </Stack>
      {message && <Alert severity={message.ok ? "success" : "warning"} sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message.text}</Alert>}
      {m.purpose && <Typography sx={{ mb: 2, whiteSpace: "pre-wrap" }}>{m.purpose}</Typography>}

      <Grid container spacing={2}>
        <Grid size={{ xs: 12, md: 7 }}>
          <Stack spacing={2}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 1 }}>{t.agenda}</Typography>
                {m.agenda.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
                  <Stack component="ol" spacing={1} sx={{ pl: 3, m: 0 }}>
                    {m.agenda.map((a) => (
                      <li key={a.id}>
                        <Typography fontWeight={600} component="span">{a.title}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {a.ownerName ? ` · ${a.ownerName}` : ""}{a.durationMinutes ? ` · ${a.durationMinutes} min` : ""}
                        </Typography>
                        {a.notes && <Typography variant="body2" color="text.secondary">{a.notes}</Typography>}
                      </li>
                    ))}
                  </Stack>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardContent>
                <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
                  <Typography variant="h6" sx={{ flex: 1 }}>{t.minutes}</Typography>
                  {m.minutesStatus !== "None" && <StatusChip value={m.minutesStatus} />}
                  {m.minutesIsSimulated && <Chip size="small" color="warning" variant="outlined" label={t.simulatedBadge} />}
                </Stack>
                {editingMinutes ? (
                  <>
                    <TextField fullWidth multiline minRows={8} value={minutes} onChange={(e) => setMinutes(e.target.value)} helperText={t.minutesHint} />
                    <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                      <Button variant="contained" disabled={!minutes?.trim()} onClick={() => act(async () => {
                        await api.put(`/api/meetings/${id}/minutes`, { minutes, version: m.version });
                        setMinutes(null);
                      })}>{t.save}</Button>
                      <Button onClick={() => setMinutes(null)}>{t.cancel}</Button>
                    </Stack>
                  </>
                ) : (
                  <>
                    {m.minutes
                      ? <Typography variant="body2" sx={{ whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>{m.minutes}</Typography>
                      : <Typography color="text.secondary">{t.empty}</Typography>}
                    {m.minutesAuthorName && (
                      <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1 }}>{t.minutesBy} {m.minutesAuthorName}</Typography>
                    )}
                    {m.minutesStatus === "Approved" && m.minutesDocumentId && m.projectId && (
                      <Alert severity="success" sx={{ mt: 1 }}>{t.minutesFiled}</Alert>
                    )}
                    <Stack direction="row" spacing={1} sx={{ mt: 1.5 }}>
                      {canPost && !cancelled && <Button onClick={() => setMinutes(m.minutes ?? "")}>{t.edit}</Button>}
                      {canManage && m.minutesStatus === "Draft" && (
                        <Tooltip title={m.minutesIsSimulated ? t.simulatedDraft : ""}>
                          <span>
                            <Button variant="contained" disabled={m.minutesIsSimulated} onClick={() => act(() => api.post(`/api/meetings/${id}/minutes/approve`))}>
                              {t.approveMinutes}
                            </Button>
                          </span>
                        </Tooltip>
                      )}
                    </Stack>
                  </>
                )}
              </CardContent>
            </Card>
          </Stack>
        </Grid>

        <Grid size={{ xs: 12, md: 5 }}>
          <Stack spacing={2}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 1 }}>{t.participants}</Typography>
                {m.participants.length === 0 ? <Typography color="text.secondary">{t.empty}</Typography> : (
                  <Stack spacing={1}>
                    {m.participants.map((p) => (
                      <Stack key={p.employeeId} direction="row" spacing={1.5} sx={{ alignItems: "center" }}>
                        <Avatar kind={p.kind} seed={p.displayName} size={30} />
                        <Typography variant="body2" sx={{ flex: 1 }} noWrap>{p.displayName}</Typography>
                        {canManage && !cancelled ? (
                          <TextField select size="small" value={p.attendance} sx={{ minWidth: 140 }} aria-label={t.attendance}
                            onChange={(e) => act(() => api.post(`/api/meetings/${id}/attendance`, { employeeId: p.employeeId, attendance: e.target.value as Attendance }))}>
                            {(["Invited", "Attended", "Absent"] as const).map((a) => <MenuItem key={a} value={a}>{label(a)}</MenuItem>)}
                          </TextField>
                        ) : <StatusChip value={p.attendance} />}
                      </Stack>
                    ))}
                  </Stack>
                )}
              </CardContent>
            </Card>

            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 1 }}>{t.actionItems}</Typography>
                <Stack spacing={0.5}>
                  {m.actionItems.length === 0 && <Typography color="text.secondary">{t.empty}</Typography>}
                  {m.actionItems.map((a) => (
                    <Stack key={a.id} direction="row" spacing={1} sx={{ alignItems: "center" }}>
                      <Checkbox checked={a.isDone} disabled={!canPost} size="small"
                        onChange={(e) => act(() => api.put(`/api/meetings/${id}/action-items/${a.id}`, { isDone: e.target.checked, version: a.version }))} />
                      <Box sx={{ flex: 1, minWidth: 0 }}>
                        <Typography variant="body2" sx={{ textDecoration: a.isDone ? "line-through" : undefined }}>{a.title}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {[a.ownerName, a.dueDate && `${t.dueDate} ${a.dueDate}`].filter(Boolean).join(" · ")}
                        </Typography>
                      </Box>
                      {a.taskId ? (
                        <Chip size="small" color="success" variant="outlined" label={t.taskCreated} component={Link} href={`/projects/${m.projectId}`} clickable />
                      ) : m.projectId && can("task.manage") && (
                        <Tooltip title={t.createTask}>
                          <IconButton size="small" onClick={() => act(() => api.post(`/api/meetings/${id}/action-items/${a.id}/task`), t.taskCreated)}>
                            <AddTaskIcon fontSize="small" />
                          </IconButton>
                        </Tooltip>
                      )}
                    </Stack>
                  ))}
                </Stack>
                {canPost && !cancelled && (
                  <Stack spacing={1} sx={{ mt: 2 }}>
                    <TextField size="small" label={t.addActionItem} value={item.title} onChange={(e) => setItem({ ...item, title: e.target.value })} />
                    <Stack direction={{ xs: "column", sm: "row" }} spacing={1}>
                      <TextField size="small" select fullWidth sx={{ minWidth: 0 }} label={t.owner} value={item.ownerEmployeeId} onChange={(e) => setItem({ ...item, ownerEmployeeId: e.target.value })}>
                        <MenuItem value="">{t.none}</MenuItem>
                        {(employees ?? []).map((e) => <MenuItem key={e.id} value={e.id}>{e.displayName}</MenuItem>)}
                      </TextField>
                      <TextField size="small" type="date" label={t.dueDate} value={item.dueDate} sx={{ flexShrink: 0, minWidth: 160 }} onChange={(e) => setItem({ ...item, dueDate: e.target.value })}
                        slotProps={{ inputLabel: { shrink: true } }} />
                    </Stack>
                    <Button variant="outlined" disabled={!item.title.trim()} sx={{ alignSelf: "flex-start" }} onClick={() => act(async () => {
                      await api.post(`/api/meetings/${id}/action-items`, { title: item.title, ownerEmployeeId: item.ownerEmployeeId || null, dueDate: item.dueDate || null });
                      setItem({ title: "", ownerEmployeeId: "", dueDate: "" });
                    })}>{t.add}</Button>
                  </Stack>
                )}
              </CardContent>
            </Card>
          </Stack>
        </Grid>
      </Grid>
      {editing && <MeetingDialog open meeting={m} onClose={() => setEditing(false)} onSaved={() => { setEditing(false); void reload(); }} />}
    </>
  );
}
