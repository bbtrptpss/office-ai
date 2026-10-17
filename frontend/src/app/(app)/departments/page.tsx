"use client";

import { useState } from "react";
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Paper,
  Stack, Switch, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import { api, Department, OrgLevel, RoleDefinition } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, useErrorMessage, useLoad } from "@/components/common";

const LEVELS: OrgLevel[] = ["Executive", "Head", "Senior", "Staff"];

type DeptForm = Partial<Department> & { code: string; name: string };
type RoleForm = Partial<RoleDefinition> & { code: string; title: string; departmentId: string; level: OrgLevel; skillsText: string };

export default function DepartmentsPage() {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const canManage = can("org.manage");
  const { data, error, reload } = useLoad(async () => {
    const [departments, roles] = await Promise.all([
      api.get<Department[]>("/api/departments"),
      api.get<RoleDefinition[]>("/api/role-definitions"),
    ]);
    return { departments, roles };
  });
  useRealtime("organizationChanged", () => void reload());

  const [dept, setDept] = useState<DeptForm | null>(null);
  const [role, setRole] = useState<RoleForm | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  if (!data) return <Loading error={error} />;

  async function run(action: () => Promise<unknown>, close: () => void) {
    try {
      await action();
      close();
    } catch (e) {
      setFormError(errorMessage(e));
    }
    await reload();
  }

  const saveDept = () =>
    run(
      () => (dept!.id ? api.put(`/api/departments/${dept!.id}`, dept) : api.post("/api/departments", dept)),
      () => setDept(null),
    );

  const saveRole = () => {
    const body = { ...role!, skills: role!.skillsText.split(",").map((s) => s.trim()).filter(Boolean) };
    return run(
      () => (role!.id ? api.put(`/api/role-definitions/${role!.id}`, body) : api.post("/api/role-definitions", body)),
      () => setRole(null),
    );
  };

  return (
    <>
      <PageHeader
        title={t.navDepartments}
        actions={canManage && (
          <>
            <Button startIcon={<AddIcon />} onClick={() => { setFormError(null); setDept({ code: "", name: "", sortOrder: data.departments.length, isActive: true }); }}>
              {t.newDepartment}
            </Button>
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setFormError(null); setRole({ code: "", title: "", departmentId: data.departments[0]?.id ?? "", level: "Staff", skillsText: "", requiresHuman: false, isActive: true }); }}>
              {t.newPosition}
            </Button>
          </>
        )}
      />

      <Stack spacing={3}>
        {data.departments.map((d) => {
          const roles = data.roles.filter((r) => r.departmentId === d.id);
          return (
            <Box key={d.id}>
              <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
                <Typography variant="h6">{d.name}</Typography>
                <Chip size="small" label={d.code} />
                {!d.isActive && <Chip size="small" color="warning" label="inactive" />}
                {canManage && (
                  <Button size="small" onClick={() => { setFormError(null); setDept({ ...d }); }}>{t.edit}</Button>
                )}
              </Stack>
              <TableContainer component={Paper} variant="outlined">
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>{t.jobTitle}</TableCell>
                      <TableCell>{t.level}</TableCell>
                      <TableCell sx={{ display: { xs: "none", md: "table-cell" } }}>{t.skills}</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {roles.map((r) => (
                      <TableRow
                        key={r.id}
                        hover
                        sx={{ cursor: canManage ? "pointer" : "default" }}
                        onClick={() => { if (!canManage) return; setFormError(null); setRole({ ...r, skillsText: r.skills.join(", ") }); }}
                      >
                        <TableCell>
                          {r.title} {r.requiresHuman && <Chip size="small" color="primary" variant="outlined" label={t.requiresHuman} />}
                        </TableCell>
                        <TableCell>{label(r.level)}</TableCell>
                        <TableCell sx={{ display: { xs: "none", md: "table-cell" } }}>{r.skills.join(", ")}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            </Box>
          );
        })}
      </Stack>

      <Dialog open={!!dept} onClose={() => setDept(null)} fullWidth maxWidth="sm">
        <DialogTitle>{dept?.id ? t.edit : t.newDepartment}</DialogTitle>
        {dept && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              {formError && <Alert severity="error">{formError}</Alert>}
              <TextField label={t.code} value={dept.code} onChange={(e) => setDept({ ...dept, code: e.target.value })} required />
              <TextField label={t.name} value={dept.name} onChange={(e) => setDept({ ...dept, name: e.target.value })} required />
              <TextField label={t.description} value={dept.description ?? ""} onChange={(e) => setDept({ ...dept, description: e.target.value })} multiline />
              <FormControlLabel control={<Switch checked={dept.isActive ?? true} onChange={(e) => setDept({ ...dept, isActive: e.target.checked })} />} label="Active" />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setDept(null)}>{t.cancel}</Button>
          <Button variant="contained" onClick={saveDept} disabled={!dept?.code || !dept?.name}>{t.save}</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={!!role} onClose={() => setRole(null)} fullWidth maxWidth="sm">
        <DialogTitle>{role?.id ? t.edit : t.newPosition}</DialogTitle>
        {role && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              {formError && <Alert severity="error">{formError}</Alert>}
              <TextField label={t.code} value={role.code} onChange={(e) => setRole({ ...role, code: e.target.value })} required />
              <TextField label={t.jobTitle} value={role.title} onChange={(e) => setRole({ ...role, title: e.target.value })} required />
              <TextField select label={t.department} value={role.departmentId} onChange={(e) => setRole({ ...role, departmentId: e.target.value })}>
                {data.departments.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
              </TextField>
              <TextField select label={t.level} value={role.level} onChange={(e) => setRole({ ...role, level: e.target.value as OrgLevel })}>
                {LEVELS.map((l) => <MenuItem key={l} value={l}>{label(l)}</MenuItem>)}
              </TextField>
              <TextField label={t.skills} helperText="comma, separated" value={role.skillsText} onChange={(e) => setRole({ ...role, skillsText: e.target.value })} />
              <TextField label={t.description} value={role.description ?? ""} onChange={(e) => setRole({ ...role, description: e.target.value })} multiline />
              <TextField label="System instructions" value={role.systemInstructions ?? ""} onChange={(e) => setRole({ ...role, systemInstructions: e.target.value })} multiline minRows={3} />
              <FormControlLabel control={<Switch checked={!!role.requiresHuman} onChange={(e) => setRole({ ...role, requiresHuman: e.target.checked })} />} label={t.requiresHuman} />
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setRole(null)}>{t.cancel}</Button>
          <Button variant="contained" onClick={saveRole} disabled={!role?.code || !role?.title}>{t.save}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
