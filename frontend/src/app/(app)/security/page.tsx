"use client";

import { useState } from "react";
import {
  Alert, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Paper, Stack,
  Switch, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import { api, Role, User } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, useErrorMessage, useLoad } from "@/components/common";
import { RetentionPanel } from "@/components/retention";

export default function SecurityPage() {
  const { t } = useI18n();
  const { me } = useAuth();
  const errorMessage = useErrorMessage();
  const { data, error, reload } = useLoad(async () => {
    const [roles, users, permissions] = await Promise.all([
      api.get<Role[]>("/api/security/roles"),
      api.get<User[]>("/api/security/users"),
      api.get<{ code: string; description: string }[]>("/api/security/permissions"),
    ]);
    return { roles, users, permissions };
  });
  const [message, setMessage] = useState<string | null>(null);
  const [newUser, setNewUser] = useState<{ email: string; displayName: string; password: string; isAgent: boolean; roles: string[] } | null>(null);
  const [resetFor, setResetFor] = useState<User | null>(null);
  const [resetPassword, setResetPassword] = useState("");

  if (!data) return <Loading error={error} />;

  async function act(action: () => Promise<unknown>, onOk?: () => void) {
    setMessage(null);
    try {
      await action();
      onOk?.();
    } catch (e) {
      setMessage(errorMessage(e));
    }
    await reload();
  }

  function toggle(role: Role, code: string) {
    const permissions = role.permissions.includes(code) ? role.permissions.filter((p) => p !== code) : [...role.permissions, code];
    void act(() => api.put(`/api/security/roles/${role.id}/permissions`, { permissions }));
  }

  return (
    <>
      <PageHeader title={t.navSecurity} />
      {message && <Alert severity="warning" sx={{ mb: 2 }} onClose={() => setMessage(null)}>{message}</Alert>}

      <Typography variant="h6" gutterBottom>{t.roles} × {t.permissions}</Typography>
      <TableContainer component={Paper} variant="outlined" sx={{ mb: 4 }}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t.permissions}</TableCell>
              {data.roles.map((r) => <TableCell key={r.id} align="center">{r.name}</TableCell>)}
            </TableRow>
          </TableHead>
          <TableBody>
            {data.permissions.map((p) => (
              <TableRow key={p.code}>
                <TableCell>
                  <Typography variant="body2" fontFamily="monospace">{p.code}</Typography>
                  <Typography variant="caption" color="text.secondary">{p.description}</Typography>
                </TableCell>
                {data.roles.map((r) => (
                  <TableCell key={r.id} align="center">
                    <Checkbox size="small" checked={r.permissions.includes(p.code)} onChange={() => toggle(r, p.code)} />
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <Stack direction="row" sx={{ alignItems: "center", mb: 1 }}>
        <Typography variant="h6" sx={{ flex: 1 }}>{t.users}</Typography>
        <Button startIcon={<AddIcon />} onClick={() => setNewUser({ email: "", displayName: "", password: "", isAgent: false, roles: ["Member"] })}>
          {t.newUser}
        </Button>
      </Stack>
      <TableContainer component={Paper} variant="outlined">
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t.displayName}</TableCell>
              <TableCell>{t.email}</TableCell>
              <TableCell>{t.roles}</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {data.users.map((u) => (
              <TableRow key={u.id}>
                <TableCell>
                  {u.displayName} {u.isAgent && <Chip size="small" color="secondary" variant="outlined" label="AI" />}
                </TableCell>
                <TableCell>{u.email}</TableCell>
                <TableCell>
                  <Stack direction="row" spacing={0.5} sx={{ flexWrap: "wrap", rowGap: 0.5 }}>
                    {data.roles.map((r) => (
                      <Chip
                        key={r.id}
                        size="small"
                        label={r.name}
                        color={u.roles.includes(r.name) ? "primary" : "default"}
                        variant={u.roles.includes(r.name) ? "filled" : "outlined"}
                        onClick={() => {
                          const roles = u.roles.includes(r.name) ? u.roles.filter((x) => x !== r.name) : [...u.roles, r.name];
                          void act(() => api.put(`/api/security/users/${u.id}/roles`, { roles }));
                        }}
                      />
                    ))}
                  </Stack>
                </TableCell>
                <TableCell align="right" sx={{ whiteSpace: "nowrap" }}>
                  <Chip size="small" variant="outlined" color={u.isActive ? "success" : "default"} label={u.isActive ? t.userActive : t.userInactive} sx={{ mr: 1 }} />
                  {!u.isAgent && u.id !== me?.userId && (
                    <>
                      <Button size="small" onClick={() => { setResetPassword(""); setResetFor(u); }}>{t.resetPassword}</Button>
                      <Button size="small" color={u.isActive ? "error" : "primary"} onClick={() => {
                        if (u.isActive && !window.confirm(t.userTurnOffConfirm)) return;
                        void act(() => api.put(`/api/security/users/${u.id}/active`, { isActive: !u.isActive }));
                      }}>{u.isActive ? t.userTurnOff : t.userTurnOn}</Button>
                    </>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <RetentionPanel />

      <Dialog open={!!resetFor} onClose={() => setResetFor(null)} fullWidth maxWidth="xs">
        <DialogTitle>{t.resetPassword}: {resetFor?.displayName}</DialogTitle>
        <DialogContent>
          <TextField sx={{ mt: 1 }} fullWidth label={t.newPassword} type="password" autoComplete="new-password" helperText="≥ 10 chars, upper, lower, digit" value={resetPassword} onChange={(e) => setResetPassword(e.target.value)} />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setResetFor(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!resetPassword} onClick={() => act(() => api.post(`/api/security/users/${resetFor!.id}/password`, { newPassword: resetPassword }), () => { setResetFor(null); setResetPassword(""); })}>{t.save}</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={!!newUser} onClose={() => setNewUser(null)} fullWidth maxWidth="sm">
        <DialogTitle>{t.newUser}</DialogTitle>
        {newUser && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField label={t.email} type="email" value={newUser.email} onChange={(e) => setNewUser({ ...newUser, email: e.target.value })} required />
              <TextField label={t.displayName} value={newUser.displayName} onChange={(e) => setNewUser({ ...newUser, displayName: e.target.value })} required />
              <FormControlLabel control={<Switch checked={newUser.isAgent} onChange={(e) => setNewUser({ ...newUser, isAgent: e.target.checked })} />} label={t.isAgent} />
              {!newUser.isAgent && (
                <TextField label={t.password} type="password" helperText="≥ 10 chars, upper, lower, digit" value={newUser.password} onChange={(e) => setNewUser({ ...newUser, password: e.target.value })} required />
              )}
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setNewUser(null)}>{t.cancel}</Button>
          <Button variant="contained" disabled={!newUser?.email || !newUser?.displayName}
            onClick={() => act(() => api.post("/api/security/users", newUser), () => setNewUser(null))}>
            {t.create}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
