"use client";

import { Box, Card, CardContent, Chip, Grid, Stack, Typography } from "@mui/material";
import CheckCircleIcon from "@mui/icons-material/CheckCircle";
import RadioButtonUncheckedIcon from "@mui/icons-material/RadioButtonUnchecked";
import { api, DeliveryStages, StageCheck } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, useLoad } from "./common";

/** The eight delivery stages (Master Prompt §7) with their gates, worked out from the project's recorded data. */
export function StagesPanel({ projectId }: { projectId: string }) {
  const { t } = useI18n();
  // Check and deliverable codes come from the server; an unknown code shows as-is.
  const text = (key: string) => (t as unknown as Record<string, string | undefined>)[key];
  const { data, error, reload } = useLoad(() => api.get<DeliveryStages>(`/api/projects/${projectId}/stages`), projectId);
  useRealtime("taskChanged", () => void reload());
  useRealtime("approvalsChanged", () => void reload());
  if (!data) return <Loading error={error} />;

  const check = (c: StageCheck) => (
    <Stack key={c.code} direction="row" spacing={0.75} sx={{ alignItems: "flex-start", py: 0.25 }}>
      {c.met
        ? <CheckCircleIcon sx={{ fontSize: 18, mt: "2px", color: "success.main" }} />
        : <RadioButtonUncheckedIcon sx={{ fontSize: 18, mt: "2px", color: "text.disabled" }} />}
      <Typography variant="body2" sx={{ flex: 1 }}>
        {text(`sc_${c.code}`) ?? c.code}
        {c.detail && <Box component="span" sx={{ color: "text.secondary" }}> · {c.detail}</Box>}
      </Typography>
    </Stack>
  );
  const section = (title: string, items: StageCheck[]) => items.length > 0 && (
    <Box sx={{ mb: 1 }}>
      <Typography variant="caption" color="text.secondary" fontWeight={600}>{title}</Typography>
      {items.map(check)}
    </Box>
  );

  return (
    <Stack spacing={1.5}>
      <Typography variant="body2" color="text.secondary">{t.stagesNote}</Typography>
      {data.stages.map((s) => (
        <Card key={s.code} sx={{ opacity: s.status === "upcoming" ? 0.75 : 1, borderLeft: 4, borderColor: s.status === "done" ? "success.main" : s.status === "current" ? "primary.main" : "divider" }}>
          <CardContent>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
              <Typography variant="h6" sx={{ flex: 1 }}>{s.number}. {t[`stage_${s.code}`]}</Typography>
              <Chip size="small" color={s.status === "done" ? "success" : s.status === "current" ? "primary" : "default"} label={t[`stageStatus_${s.status}`]} />
            </Stack>
            <Grid container spacing={2}>
              <Grid size={{ xs: 12, md: 4 }}>{section(t.stageEntry, s.entry)}</Grid>
              <Grid size={{ xs: 12, md: 4 }}>{section(t.stageExit, s.exit)}</Grid>
              <Grid size={{ xs: 12, md: 4 }}>
                {section(t.stageApproval, [s.approval])}
                <Typography variant="caption" color="text.secondary" fontWeight={600}>{t.stageDeliverables}</Typography>
                <Stack direction="row" sx={{ flexWrap: "wrap", gap: 0.5, mt: 0.5 }}>
                  {s.deliverables.map((d) => <Chip key={d} size="small" variant="outlined" label={text(`sd_${d}`) ?? d} />)}
                </Stack>
              </Grid>
            </Grid>
          </CardContent>
        </Card>
      ))}
    </Stack>
  );
}
