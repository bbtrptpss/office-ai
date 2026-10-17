"use client";

import { useMemo, useState } from "react";
import {
  Alert, Button, Dialog, IconButton, DialogActions, DialogContent, DialogTitle, MenuItem, Paper, Stack, Table, TableBody,
  TableCell, TableContainer, TableHead, TableRow, TextField, ToggleButton, ToggleButtonGroup,
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import EditIcon from "@mui/icons-material/EditOutlined";
import { useRouter } from "next/navigation";
import { api, Department, Employee, EmployeeKind, EmployeeStatus, RoleDefinition } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { KindIcon, Loading, PageHeader, StatusChip, useErrorMessage, useLoad } from "@/components/common";

const STATUSES: EmployeeStatus[] = ["Available", "Working", "WaitingForApproval", "Blocked", "Failed", "Inactive"];

interface Form {
  id?: string;
  displayName: string;
  kind: EmployeeKind;
  departmentId: string;
  roleDefinitionId: string;
  managerId: string;
  status: EmployeeStatus;
  userId: string | null;
  version?: number;
}

export default function EmployeesPage() {
  const { t, label } = useI18n();
  const { can } = useAuth();
  const errorMessage = useErrorMessage();
  const router = useRouter();
  const { data, error, reload } = useLoad(async () => {
    const [employees, departments, roles] = await Promise.all([
      api.get<Employee[]>("/api/employees"),
      api.get<Department[]>("/api/departments"),
      api.get<RoleDefinition[]>("/api/role-definitions"),
    ]);
    return { employees, departments, roles };
  });
  useRealtime("organizationChanged", () => void reload());

  const [filter, setFilter] = useState<"all" | EmployeeKind>("all");
  const [form, setForm] = useState<Form | null>(null);
  const [formError, setFormError] = useState<string | null>(null);

  const names = useMemo(() => new Map(data?.employees.map((e) => [e.id, e.displayName])), [data]);
  if (!data) return <Loading error={error} />;

  const rows = data.employees.filter((e) => filter === "all" || e.kind === filter);
  const rolesForDept = data.roles.filter((r) => !form || r.departmentId === form.departmentId);

  function openNew() {
    setFormError(null);
    setForm({ displayName: "", kind: "AI", departmentId: data!.departments[0]?.id ?? "", roleDefinitionId: "", managerId: "", status: "Available", userId: null });
  }

  function openEdit(e: Employee) {
    setFormError(null);
    setForm({ id: e.id, displayName: e.displayName, kind: e.kind, departmentId: e.departmentId, roleDefinitionId: e.roleDefinitionId, managerId: e.managerId ?? "", status: e.status, userId: e.userId, version: e.version });
  }

  async function save() {
    if (!form) return;
    const body = { ...form, managerId: form.managerId || null };
    try {
      if (form.id) await api.put(`/api/employees/${form.id}`, body);
      else await api.post("/api/employees", body);
      setForm(null);
      await reload();
    } catch (e) {
      setFormError(errorMessage(e));
      await reload();
    }
  }

  return (
    <>
      <PageHeader
        title={t.navEmployees}
        subtitle={`${data.employees.filter((e) => e.kind === "AI").length} ${t.ai} · ${data.employees.filter((e) => e.kind === "Human").length} ${t.human}`}
        actions={can("org.manage") && <Button variant="contained" startIcon={<AddIcon />} onClick={openNew}>{t.newEmployee}</Button>}
      />
      <ToggleButtonGroup size="small" exclusive value={filter} onChange={(_, v) => v && setFilter(v)} sx={{ mb: 2 }}>
        <ToggleButton value="all">All</ToggleButton>
        <ToggleButton value="AI">{t.ai}</ToggleButton>
        <ToggleButton value="Human">{t.human}</ToggleButton>
      </ToggleButtonGroup>

      <TableContainer component={Paper} variant="outlined">
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t.displayName}</TableCell>
              <TableCell>{t.jobTitle}</TableCell>
              <TableCell sx={{ display: { xs: "none", md: "table-cell" } }}>{t.department}</TableCell>
              <TableCell sx={{ display: { xs: "none", md: "table-cell" } }}>{t.manager}</TableCell>
              <TableCell>{t.status}</TableCell>
              <TableCell padding="checkbox" />
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((e) => (
              <TableRow key={e.id} hover onClick={() => router.push(`/employees/${e.id}`)} sx={{ cursor: "pointer" }}>
                <TableCell>
                  <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                    <KindIcon kind={e.kind} seed={e.displayName} size={32} />
                    <span>{e.displayName}</span>
                  </Stack>
                </TableCell>
                <TableCell>{e.jobTitle}</TableCell>
                <TableCell sx={{ display: { xs: "none", md: "table-cell" } }}>{e.departmentName}</TableCell>
                <TableCell sx={{ display: { xs: "none", md: "table-cell" } }}>{e.managerId ? names.get(e.managerId) : "—"}</TableCell>
                <TableCell><StatusChip value={e.status} /></TableCell>
                <TableCell padding="checkbox">
                  {can("org.manage") && (
                    <IconButton size="small" aria-label={t.edit} onClick={(ev) => { ev.stopPropagation(); openEdit(e); }}>
                      <EditIcon fontSize="small" />
                    </IconButton>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="sm">
        <DialogTitle>{form?.id ? t.edit : t.newEmployee}</DialogTitle>
        {form && (
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              {formError && <Alert severity="error">{formError}</Alert>}
              <TextField label={t.displayName} value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} required />
              <TextField select label={t.kind} value={form.kind} disabled={!!form.id && form.kind === "Human"} onChange={(e) => setForm({ ...form, kind: e.target.value as EmployeeKind })}>
                <MenuItem value="AI">{t.ai}</MenuItem>
                <MenuItem value="Human" disabled={!form.userId}>{t.human}</MenuItem>
              </TextField>
              <TextField select label={t.department} value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value, roleDefinitionId: "" })}>
                {data.departments.map((d) => <MenuItem key={d.id} value={d.id}>{d.name}</MenuItem>)}
              </TextField>
              <TextField select label={t.jobTitle} value={form.roleDefinitionId} onChange={(e) => setForm({ ...form, roleDefinitionId: e.target.value })} required>
                {rolesForDept.map((r) => (
                  <MenuItem key={r.id} value={r.id} disabled={r.requiresHuman && form.kind !== "Human"}>
                    {r.title}{r.requiresHuman ? ` (${t.requiresHuman})` : ""}
                  </MenuItem>
                ))}
              </TextField>
              <TextField select label={t.manager} value={form.managerId} onChange={(e) => setForm({ ...form, managerId: e.target.value })}>
                <MenuItem value="">{t.none}</MenuItem>
                {data.employees.filter((e) => e.id !== form.id).map((e) => (
                  <MenuItem key={e.id} value={e.id}>{e.displayName} — {e.jobTitle}</MenuItem>
                ))}
              </TextField>
              <TextField select label={t.status} value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as EmployeeStatus })}>
                {STATUSES.map((s) => <MenuItem key={s} value={s}>{label(s)}</MenuItem>)}
              </TextField>
            </Stack>
          </DialogContent>
        )}
        <DialogActions>
          <Button onClick={() => setForm(null)}>{t.cancel}</Button>
          <Button variant="contained" onClick={save} disabled={!form?.displayName || !form?.roleDefinitionId}>{t.save}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
