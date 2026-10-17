"use client";

import { useState } from "react";
import { Card, CardContent, Grid, Stack, ToggleButton, ToggleButtonGroup, Typography } from "@mui/material";
import { api, AgentRun, UsageSummary, usd } from "@/lib/api";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, PageHeader, useLoad } from "@/components/common";
import { Rise } from "@/components/cute";
import { RunList } from "@/components/agents";

type Filter = "all" | "active" | "problems";

export default function AgentActivityPage() {
  const { t } = useI18n();
  const [filter, setFilter] = useState<Filter>("all");
  const { data, error, reload } = useLoad(async () => {
    const [runs, usage] = await Promise.all([
      api.get<AgentRun[]>("/api/agent-runs?take=100"),
      api.get<UsageSummary>("/api/ai/usage"),
    ]);
    return { runs, usage };
  });
  useRealtime("agentRunChanged", () => void reload());

  if (!data) return <Loading error={error} />;
  const runs = data.runs.filter((r) =>
    filter === "all" ? true
      : filter === "active" ? r.status === "Queued" || r.status === "Running"
      : r.status === "Failed" || r.status === "LimitReached");

  const stats = [
    { label: `${t.aiCostMonth} · ${t.realOnly}`, value: usd(data.usage.thisMonthUsd), emoji: "💸" },
    { label: `${t.tokens} · ${t.realOnly}`, value: data.usage.thisMonthTokens.toLocaleString(), emoji: "🔤" },
    { label: t.runs, value: String(data.usage.runsThisMonth), emoji: "🏃" },
    { label: t.activeRuns, value: String(data.usage.activeRuns), emoji: "💜" },
  ];

  return (
    <>
      <PageHeader title={t.navActivity} />
      <Grid container spacing={2} sx={{ mb: 3 }}>
        {stats.map((s, i) => (
          <Grid key={s.label} size={{ xs: 6, md: 3 }}>
            <Rise index={i}>
              <Card sx={{ height: "100%" }}>
                <CardContent>
                  <Stack direction="row" sx={{ justifyContent: "space-between" }}>
                    <Typography variant="body2" color="text.secondary">{s.label}</Typography>
                    <span aria-hidden>{s.emoji}</span>
                  </Stack>
                  <Typography variant="h5">{s.value}</Typography>
                </CardContent>
              </Card>
            </Rise>
          </Grid>
        ))}
      </Grid>

      <ToggleButtonGroup size="small" exclusive value={filter} onChange={(_, v) => v && setFilter(v)} sx={{ mb: 2 }}>
        <ToggleButton value="all">All</ToggleButton>
        <ToggleButton value="active">{t.activeRuns}</ToggleButton>
        <ToggleButton value="problems">⚠</ToggleButton>
      </ToggleButtonGroup>
      <RunList runs={runs} />
    </>
  );
}
