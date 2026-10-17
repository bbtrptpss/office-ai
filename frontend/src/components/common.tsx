"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Alert, Box, Chip, ChipProps, CircularProgress, Stack, Typography } from "@mui/material";
import { ApiError, EmployeeKind } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { Avatar, Mascot } from "./cute";

const messageOf = (e: unknown) => (e instanceof Error ? e.message : String(e));

/** Loads data on mount (and whenever `key` changes) and exposes a reload function. */
export function useLoad<T>(load: () => Promise<T>, key = "") {
  const loadRef = useRef(load);
  const [state, setState] = useState<{ data: T | null; error: string | null }>({ data: null, error: null });

  useEffect(() => {
    loadRef.current = load;
  });

  const reload = useCallback(async () => {
    try {
      setState({ data: await loadRef.current(), error: null });
    } catch (e) {
      setState((s) => ({ ...s, error: messageOf(e) }));
    }
  }, []);

  useEffect(() => {
    let cancelled = false;
    loadRef
      .current()
      .then((data) => !cancelled && setState({ data, error: null }))
      .catch((e) => !cancelled && setState((s) => ({ ...s, error: messageOf(e) })));
    return () => {
      cancelled = true;
    };
  }, [key]);

  return { data: state.data, error: state.error, reload };
}

export function PageHeader({ title, actions, subtitle }: { title: string; subtitle?: React.ReactNode; actions?: React.ReactNode }) {
  return (
    <Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ mb: 3, alignItems: { sm: "center" } }}>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Typography variant="h5">{title}</Typography>
        {subtitle && (
          <Typography color="text.secondary" variant="body2">
            {subtitle}
          </Typography>
        )}
      </Box>
      {actions && <Stack direction="row" spacing={1}>{actions}</Stack>}
    </Stack>
  );
}

export function Loading({ error }: { error?: string | null }) {
  const { t } = useI18n();
  if (error) return <Alert severity="error">{error}</Alert>;
  return (
    <Stack spacing={1} sx={{ alignItems: "center", color: "text.secondary", py: 6 }}>
      <Mascot size={72} />
      <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
        <CircularProgress size={16} color="secondary" />
        <span>{t.loading}</span>
      </Stack>
    </Stack>
  );
}

const STATUS_COLORS: Record<string, ChipProps["color"]> = {
  Done: "success", Completed: "success", Available: "success",
  InProgress: "primary", Working: "primary", Active: "primary", Assigned: "info",
  InReview: "secondary", WaitingApproval: "warning", WaitingForApproval: "warning",
  Blocked: "error", Failed: "error",
  Critical: "error", High: "warning",
  Succeeded: "success", Running: "primary", Queued: "info", LimitReached: "warning", Cancelled: "default",
  Planning: "info", AwaitingPlanApproval: "warning", Executing: "primary", Pending: "warning", Approved: "success", Expired: "default", ChangesRequested: "warning",
  Merged: "success", Passed: "success", Open: "info", Conflict: "error", Proposed: "info", Must: "error", Should: "warning",
  Ok: "success", Warning: "warning", Exceeded: "error", Validated: "success", Final: "success", Sent: "success", Archived: "default",
  Denied: "error", TimedOut: "warning", InvalidInput: "warning", Submitted: "info", Accepted: "success", Rejected: "error",
};

export function StatusChip({ value }: { value: string }) {
  const { label } = useI18n();
  return <Chip size="small" label={label(value)} color={STATUS_COLORS[value] ?? "default"} variant="outlined" />;
}

export function KindIcon({ kind, seed, size = 28 }: { kind: EmployeeKind; seed?: string; size?: number }) {
  return <Avatar kind={kind} seed={seed ?? kind} size={size} />;
}

/** Turns an API error into a user-facing message. */
export function useErrorMessage() {
  const { t } = useI18n();
  return (e: unknown) => {
    if (e instanceof ApiError) {
      if (e.status === 403) return t.forbidden;
      if (e.code === "concurrency_conflict") return t.conflict;
      return e.message;
    }
    return String(e);
  };
}
