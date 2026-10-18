"use client";

import { useState } from "react";
import {
  Alert, Autocomplete, Button, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, MenuItem, Stack, TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import DeleteIcon from "@mui/icons-material/DeleteOutline";
import { api, Employee, Meeting, Project } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { useErrorMessage, useLoad } from "./common";

interface AgendaRow { title: string; ownerEmployeeId: string; durationMinutes: string }

/** datetime-local wants "YYYY-MM-DDTHH:mm" in local time. */
function toLocalInput(iso: string) {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function nextHour() {
  const d = new Date();
  d.setMinutes(0, 0, 0);
  d.setHours(d.getHours() + 1);
  return d.toISOString();
}

/** Create or edit a meeting: when, where, who, and the agenda. */
export function MeetingDialog({ open, meeting, projectId, onClose, onSaved }: {
  open: boolean;
  meeting?: Meeting | null;
  projectId?: string;
  onClose: () => void;
  onSaved: (m: Meeting) => void;
}) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const { data: employees } = useLoad(() => api.get<Employee[]>("/api/employees"));
  const { data: projects } = useLoad(() => api.get<Project[]>("/api/projects"));
  const [title, setTitle] = useState(meeting?.title ?? "");
  const [when, setWhen] = useState(toLocalInput(meeting?.scheduledAt ?? nextHour()));
  const [duration, setDuration] = useState(String(meeting?.durationMinutes ?? 30));
  const [location, setLocation] = useState(meeting?.location ?? "");
  const [purpose, setPurpose] = useState(meeting?.purpose ?? "");
  const [project, setProject] = useState(meeting?.projectId ?? projectId ?? "");
  const [participants, setParticipants] = useState<string[]>(meeting?.participants.map((p) => p.employeeId) ?? []);
  const [agenda, setAgenda] = useState<AgendaRow[]>(
    meeting?.agenda.map((a) => ({ title: a.title, ownerEmployeeId: a.ownerEmployeeId ?? "", durationMinutes: a.durationMinutes ? String(a.durationMinutes) : "" })) ?? [],
  );
  const [problem, setProblem] = useState<string | null>(null);
  const byId = new Map((employees ?? []).map((e) => [e.id, e]));

  async function save() {
    setProblem(null);
    const body = {
      title, scheduledAt: new Date(when).toISOString(), durationMinutes: Number(duration) || 30, location: location || null, purpose: purpose || null,
      projectId: project || null, participantIds: participants,
      agenda: agenda.filter((a) => a.title.trim()).map((a) => ({
        title: a.title, ownerEmployeeId: a.ownerEmployeeId || null, durationMinutes: a.durationMinutes ? Number(a.durationMinutes) : null,
      })),
      version: meeting?.version,
    };
    try {
      onSaved(meeting ? await api.put<Meeting>(`/api/meetings/${meeting.id}`, body) : await api.post<Meeting>("/api/meetings", body));
    } catch (e) { setProblem(errorMessage(e)); }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{meeting ? t.edit : t.newMeeting}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {problem && <Alert severity="warning">{problem}</Alert>}
          <TextField label={t.title} value={title} onChange={(e) => setTitle(e.target.value)} slotProps={{ htmlInput: { maxLength: 200 } }} />
          <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
            <TextField fullWidth type="datetime-local" label={t.scheduledAt} value={when} onChange={(e) => setWhen(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
            <TextField fullWidth type="number" label={t.durationMin} value={duration} onChange={(e) => setDuration(e.target.value)} slotProps={{ htmlInput: { min: 5, max: 600, step: 5 } }} />
          </Stack>
          <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
            <TextField fullWidth label={t.location} value={location} onChange={(e) => setLocation(e.target.value)} />
            <TextField fullWidth select label={t.navProjects} value={project} onChange={(e) => setProject(e.target.value)}>
              <MenuItem value="">{t.none}</MenuItem>
              {(projects ?? []).map((p) => <MenuItem key={p.id} value={p.id}>{p.code} · {p.name}</MenuItem>)}
            </TextField>
          </Stack>
          <TextField label={t.purpose} value={purpose} onChange={(e) => setPurpose(e.target.value)} multiline minRows={2} />
          <Autocomplete multiple options={employees ?? []} getOptionLabel={(e) => `${e.displayName} · ${e.jobTitle}`}
            value={participants.map((id) => byId.get(id)).filter((e): e is Employee => !!e)}
            onChange={(_, v) => setParticipants(v.map((e) => e.id))}
            renderInput={(params) => <TextField {...params} label={t.participants} />} />
          <Typography variant="subtitle2">{t.agenda}</Typography>
          {agenda.map((a, i) => (
            <Stack key={i} direction={{ xs: "column", sm: "row" }} spacing={1} sx={{ alignItems: { sm: "center" } }}>
              <Typography sx={{ width: 24 }} color="text.secondary">{i + 1}.</Typography>
              <TextField size="small" fullWidth label={t.title} value={a.title} onChange={(e) => setAgenda(agenda.map((x, j) => (j === i ? { ...x, title: e.target.value } : x)))} />
              <TextField size="small" select label={t.owner} value={a.ownerEmployeeId} sx={{ minWidth: 180 }}
                onChange={(e) => setAgenda(agenda.map((x, j) => (j === i ? { ...x, ownerEmployeeId: e.target.value } : x)))}>
                <MenuItem value="">{t.none}</MenuItem>
                {(employees ?? []).map((e) => <MenuItem key={e.id} value={e.id}>{e.displayName}</MenuItem>)}
              </TextField>
              <TextField size="small" type="number" label={t.durationMin} value={a.durationMinutes} sx={{ width: 130 }}
                onChange={(e) => setAgenda(agenda.map((x, j) => (j === i ? { ...x, durationMinutes: e.target.value } : x)))} />
              <IconButton aria-label="remove" onClick={() => setAgenda(agenda.filter((_, j) => j !== i))}><DeleteIcon /></IconButton>
            </Stack>
          ))}
          <Button startIcon={<AddIcon />} sx={{ alignSelf: "flex-start" }} onClick={() => setAgenda([...agenda, { title: "", ownerEmployeeId: "", durationMinutes: "" }])}>
            {t.addAgendaItem}
          </Button>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t.cancel}</Button>
        <Button variant="contained" disabled={!title.trim() || !when} onClick={() => void save()}>{t.save}</Button>
      </DialogActions>
    </Dialog>
  );
}
