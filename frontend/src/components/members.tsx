"use client";

import { useState } from "react";
import { Alert, Autocomplete, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, List, Stack, TextField, Typography } from "@mui/material";
import DeleteIcon from "@mui/icons-material/DeleteOutline";
import { api, ChannelMember, Employee } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useErrorMessage, useLoad } from "./common";
import { Avatar } from "./cute";

/**
 * Members of a project (or of a non-project chat channel, read-only). Owners and task assignees are members automatically;
 * people with project.manage add others by hand. Membership also decides who can see the project without project.all.
 */
export function MembersDialog({ title, projectId, members, onClose, onChanged }: {
  title: string; projectId: string | null; members: ChannelMember[]; onClose: () => void; onChanged: () => void;
}) {
  const { t } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const canEdit = can("project.manage") && !!projectId;
  const { data: employees } = useLoad(() => (canEdit ? api.get<Employee[]>("/api/employees") : Promise.resolve([] as Employee[])), String(canEdit));
  const [problem, setProblem] = useState<string | null>(null);
  const memberIds = new Set(members.map((m) => m.employeeId));

  async function change(fn: () => Promise<unknown>) {
    setProblem(null);
    try { await fn(); onChanged(); } catch (e) { setProblem(errorMessage(e)); }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{t.members} · {title}</DialogTitle>
      <DialogContent>
        {projectId && <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{t.projectMembersHint}</Typography>}
        {problem && <Alert severity="warning" sx={{ mb: 1 }}>{problem}</Alert>}
        <List dense disablePadding>
          {members.map((m) => (
            <Stack key={m.employeeId} direction="row" spacing={1.5} sx={{ alignItems: "center", py: 0.75 }}>
              <Avatar kind={m.kind} seed={m.displayName} size={30} />
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography variant="body2" noWrap>{m.displayName}</Typography>
                <Typography variant="caption" color="text.secondary" noWrap component="div">{m.isExplicit ? m.jobTitle : `${m.jobTitle} · ${t.memberByRole}`}</Typography>
              </Box>
              {canEdit && m.isExplicit && (
                <IconButton size="small" aria-label="remove" onClick={() => change(() => api.del(`/api/projects/${projectId}/members/${m.employeeId}`))}>
                  <DeleteIcon fontSize="small" />
                </IconButton>
              )}
            </Stack>
          ))}
        </List>
        {canEdit && (
          <Autocomplete sx={{ mt: 2 }} options={(employees ?? []).filter((e) => !memberIds.has(e.id))} value={null}
            getOptionLabel={(e) => `${e.displayName} · ${e.jobTitle}`}
            onChange={(_, e) => e && change(() => api.post(`/api/projects/${projectId}/members/${e.id}`))}
            renderInput={(params) => <TextField {...params} size="small" label={t.addMember} />} />
        )}
      </DialogContent>
      <DialogActions><Button onClick={onClose}>{t.close}</Button></DialogActions>
    </Dialog>
  );
}

/** Loads a project's members and shows them in MembersDialog. */
export function ProjectMembersDialog({ projectId, title, onClose }: { projectId: string; title: string; onClose: () => void }) {
  const { data, error, reload } = useLoad(() => api.get<ChannelMember[]>(`/api/projects/${projectId}/members`), projectId);
  if (!data) return error ? <Dialog open onClose={onClose}><DialogContent><Alert severity="warning">{String(error)}</Alert></DialogContent></Dialog> : null;
  return <MembersDialog title={title} projectId={projectId} members={data} onClose={onClose} onChanged={() => void reload()} />;
}
