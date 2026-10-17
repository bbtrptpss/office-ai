"use client";

import { useState } from "react";
import {
  Chip, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography,
} from "@mui/material";
import { api, AuditLog } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { Loading, PageHeader, useLoad } from "@/components/common";

const ENTITY_TYPES = ["", "Department", "EmployeeRoleDefinition", "EmployeeProfile", "Project", "Milestone", "WorkTask", "TaskAssignment", "TaskDependency", "User", "UserRoles", "RolePermissions", "Endpoint"];

function Changes({ log }: { log: AuditLog }) {
  const parse = (s: string | null) => (s ? (JSON.parse(s) as Record<string, unknown>) : {});
  const before = parse(log.oldValues);
  const after = parse(log.newValues);
  const keys = Array.from(new Set([...Object.keys(before), ...Object.keys(after)]));
  return (
    <Stack spacing={0.25}>
      {keys.map((k) => (
        <Typography key={k} variant="caption" sx={{ fontFamily: "monospace", wordBreak: "break-word" }}>
          {k}: {k in before && <s>{JSON.stringify(before[k])}</s>} {k in after && JSON.stringify(after[k])}
        </Typography>
      ))}
    </Stack>
  );
}

const ACTION_COLORS: Record<string, "success" | "info" | "error" | "warning" | "default"> = {
  Created: "success", Updated: "info", Deleted: "error", AccessDenied: "warning", PermissionChangeDenied: "error",
  LoginFailed: "warning", LockedOut: "error",
};

export default function AuditPage() {
  const { t, lang } = useI18n();
  const [entityType, setEntityType] = useState("");
  const { data, error } = useLoad(
    () => api.get<AuditLog[]>(`/api/audit?take=200${entityType ? `&entityType=${entityType}` : ""}`),
    entityType,
  );

  return (
    <>
      <PageHeader title={t.navAudit} />
      <TextField select size="small" label={t.entity} value={entityType} onChange={(e) => setEntityType(e.target.value)} sx={{ mb: 2, minWidth: 240 }}>
        {ENTITY_TYPES.map((x) => <MenuItem key={x} value={x}>{x || "All"}</MenuItem>)}
      </TextField>
      {!data ? (
        <Loading error={error} />
      ) : (
        <TableContainer component={Paper} variant="outlined">
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{t.when}</TableCell>
                <TableCell>{t.actor}</TableCell>
                <TableCell>{t.action}</TableCell>
                <TableCell>{t.entity}</TableCell>
                <TableCell sx={{ display: { xs: "none", md: "table-cell" } }}>{t.changes}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.map((log) => (
                <TableRow key={log.id}>
                  <TableCell sx={{ whiteSpace: "nowrap" }}>{new Date(log.occurredAt).toLocaleString(lang === "th" ? "th-TH" : "en-GB")}</TableCell>
                  <TableCell>{log.actorName ?? "—"}</TableCell>
                  <TableCell><Chip size="small" label={log.action} color={ACTION_COLORS[log.action] ?? "default"} variant="outlined" /></TableCell>
                  <TableCell>
                    <Typography variant="body2">{log.entityType}</Typography>
                    <Typography variant="caption" color="text.secondary" sx={{ wordBreak: "break-all" }}>{log.entityId}</Typography>
                  </TableCell>
                  <TableCell sx={{ display: { xs: "none", md: "table-cell" }, maxWidth: 420 }}><Changes log={log} /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
    </>
  );
}
