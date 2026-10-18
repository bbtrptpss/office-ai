"use client";

import { useEffect, useState } from "react";
import { animate, useReducedMotion } from "motion/react";
import Link from "next/link";
import { Box, Button, Card, CardContent, Chip, Grid, IconButton, LinearProgress, Stack, Typography } from "@mui/material";
import CheckBoxIcon from "@mui/icons-material/CheckBoxRounded";
import CheckBoxBlankIcon from "@mui/icons-material/CheckBoxOutlineBlankRounded";
import NotificationsIcon from "@mui/icons-material/NotificationsRounded";
import GroupsIcon from "@mui/icons-material/GroupsRounded";
import ChevronLeftIcon from "@mui/icons-material/ChevronLeftRounded";
import ChevronRightIcon from "@mui/icons-material/ChevronRightRounded";
import { Announcement, api, Dashboard, UsageSummary, usd } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, useLoad } from "@/components/common";
import { Avatar, Mascot, OfficeScene, Rise, Sparkles } from "@/components/cute";
import { brand } from "@/theme";

function CountUp({ value }: { value: number }) {
  const reduce = useReducedMotion();
  const [shown, setShown] = useState(reduce ? value : 0);
  useEffect(() => {
    if (reduce) return;
    const controls = animate(0, value, { duration: 0.9, ease: "easeOut", onUpdate: (v) => setShown(Math.round(v)) });
    return () => controls.stop();
  }, [value, reduce]);
  return <>{reduce ? value : shown}</>;
}

/** Widget header as in the reference design: title, small uppercase subtitle, an icon on the right. */
function WidgetTitle({ title, sub, icon }: { title: string; sub?: string; icon?: React.ReactNode }) {
  return (
    <Stack direction="row" sx={{ alignItems: "flex-start", mb: 1.5 }}>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Typography variant="h6" sx={{ lineHeight: 1.2 }}>{title}</Typography>
        {sub && <Typography variant="overline" color="primary" sx={{ fontSize: 10.5, lineHeight: 1.8 }}>{sub}</Typography>}
      </Box>
      {icon && <Box sx={{ color: "primary.main", display: "flex" }}>{icon}</Box>}
    </Stack>
  );
}

export default function DashboardPage() {
  const { t } = useI18n();
  const { me, can } = useAuth();
  const { data, error, reload } = useLoad(() => api.get<Dashboard>("/api/dashboard"));
  useRealtime("taskChanged", () => void reload());
  useRealtime("organizationChanged", () => void reload());

  if (!data) return <Loading error={error} />;
  const pct = data.totalTasks ? Math.round((data.completedTasks / data.totalTasks) * 100) : 0;

  // "Today's missions": each line is checked when there is nothing to do about it.
  const missions = [
    { label: t.waitingApproval, count: data.waitingApproval, href: "/approvals" },
    { label: t.tasksBlocked, count: data.blockedTasks, href: "/projects" },
    { label: t.tasksOverdue, count: data.overdueTasks, href: "/projects" },
    { label: `${t.tasksDone} ${data.completedTasks} / ${data.totalTasks}`, count: data.totalTasks - data.completedTasks, href: "/projects", plain: true },
  ];

  return (
    <Grid container spacing={2}>
      {/* Left column: missions + AI spend */}
      <Grid size={{ xs: 12, md: 6, lg: 3 }} sx={{ order: { xs: 2, lg: 1 } }}>
        <Stack spacing={2} sx={{ height: "100%" }}>
          <Rise index={1}>
            <Card sx={{ height: "100%" }}>
              <CardContent>
                <WidgetTitle title={t.todayMissions} sub={t.todayMissionsSub} icon={<Mascot size={34} float={false} />} />
                <Stack divider={<Box sx={{ borderBottom: 1, borderColor: "divider" }} />}>
                  {missions.map((m) => {
                    const done = m.count === 0;
                    return (
                      <Stack key={m.label} component={Link} href={m.href} direction="row" spacing={1}
                        sx={{ alignItems: "center", py: 0.9, color: "inherit", textDecoration: "none", "&:hover .label": { color: "primary.main" } }}>
                        {done ? <CheckBoxIcon color="primary" fontSize="small" /> : <CheckBoxBlankIcon sx={{ color: "text.disabled" }} fontSize="small" />}
                        <Typography className="label" variant="body2" sx={{ flex: 1, transition: "color .2s" }}>{m.label}</Typography>
                        {!done && !m.plain && <Chip size="small" color="secondary" label={m.count} sx={{ height: 20, fontWeight: 700 }} />}
                      </Stack>
                    );
                  })}
                </Stack>
                <Stack direction="row" spacing={1} sx={{ alignItems: "center", mt: 1.5 }}>
                  <LinearProgress variant="determinate" value={pct} sx={{ height: 8, flex: 1 }} />
                  <Typography variant="caption" sx={{ fontWeight: 700, color: "primary.main" }}><CountUp value={pct} />%</Typography>
                </Stack>
                {can("task.read") && (
                  <Button fullWidth variant="contained" component={Link} href="/approvals" sx={{ mt: 2 }}>
                    {data.waitingApproval > 0 ? `${t.openApprovals} (${data.waitingApproval})` : t.openApprovals}
                  </Button>
                )}
              </CardContent>
            </Card>
          </Rise>
          {can("agent.read") && <AiSpendWidget />}
        </Stack>
      </Grid>

      {/* Center: the pink hero, like the reference's figure stage */}
      <Grid size={{ xs: 12, lg: 6 }} sx={{ order: { xs: 1, lg: 2 } }}>
        <Rise>
          <Card sx={{ height: "100%", position: "relative", overflow: "hidden", minHeight: { lg: 520 }, background: `linear-gradient(170deg, #F9DCEB 0%, #F4D0E4 55%, #EBCBEA 100%)`, border: "6px solid rgba(255,255,255,.7)" }}>
            <Sparkles count={16} />
            <CardContent sx={{ position: "relative", height: "100%", display: "flex", flexDirection: "column", p: { xs: 2.5, md: 3.5 }, color: brand.ink }}>
              <Typography variant="overline" sx={{ color: brand.purpleDeep, opacity: 0.8 }}>✦ {t.appName}</Typography>
              <Typography variant="h4" sx={{ fontSize: { xs: 26, md: 32 } }}>{t.hello} {me?.displayName}</Typography>
              <Typography sx={{ opacity: 0.8 }}>{t.heroLine}</Typography>
              <Box sx={{ flex: 1, display: "grid", placeItems: "center", py: 2 }}>
                <Box sx={{ position: "relative" }}>
                  <Box aria-hidden sx={{ position: "absolute", inset: "12% -10% -6%", borderRadius: "50%", bgcolor: "rgba(255,255,255,.55)", filter: "blur(18px)" }} />
                  <Box sx={{ position: "relative" }}><Mascot size={190} /></Box>
                </Box>
              </Box>
              <Stack direction="row" spacing={1} sx={{ justifyContent: "center", flexWrap: "wrap", rowGap: 1, mb: 2 }}>
                <Chip label={`🤖 ${data.aiWorking} ${t.aiWorking}`} sx={{ bgcolor: "rgba(255,255,255,.75)", color: brand.ink }} />
                <Chip label={`💤 ${data.aiAvailable} ${t.aiAvailable}`} sx={{ bgcolor: "rgba(255,255,255,.75)", color: brand.ink }} />
                <Chip label={`📁 ${data.activeProjects} ${t.activeProjects}`} sx={{ bgcolor: "rgba(255,255,255,.75)", color: brand.ink }} />
              </Stack>
              <Box sx={{ bgcolor: "rgba(255,255,255,.6)", borderRadius: "22px", p: 1.5 }}>
                <OfficeScene working={data.aiWorking} total={data.aiEmployees} />
              </Box>
            </CardContent>
          </Card>
        </Rise>
      </Grid>

      {/* Right column: announcements, calendar, team */}
      <Grid size={{ xs: 12, md: 6, lg: 3 }} sx={{ order: 3 }}>
        <Stack spacing={2}>
          {can("org.read") && <AnnouncementsWidget />}
          <Rise index={3}><CalendarWidget /></Rise>
          <Rise index={4}>
            <Card>
              <CardContent>
                <WidgetTitle title={t.team} sub={t.teamSub} icon={<GroupsIcon />} />
                {data.workload.length === 0 ? (
                  <Typography variant="body2" color="text.secondary">{t.empty}</Typography>
                ) : (
                  <Stack spacing={1}>
                    {data.workload.slice(0, 5).map((w) => (
                      <Stack key={w.employeeId} direction="row" spacing={1} sx={{ alignItems: "center" }}>
                        <Avatar kind={w.kind} seed={w.displayName} size={28} />
                        <Typography variant="body2" noWrap sx={{ flex: 1 }}>{w.displayName}</Typography>
                        <Chip size="small" label={w.openTasks} sx={{ height: 20, bgcolor: brand.lilac, color: brand.purpleDeep, fontWeight: 700 }} />
                      </Stack>
                    ))}
                  </Stack>
                )}
                <Stack direction="row" spacing={2} sx={{ mt: 1.5, color: "text.secondary" }}>
                  <Typography variant="caption">🤖 {data.aiEmployees} {t.aiEmployees}</Typography>
                  <Typography variant="caption">🧑‍💼 {data.humanEmployees} {t.humanEmployees}</Typography>
                </Stack>
              </CardContent>
            </Card>
          </Rise>
        </Stack>
      </Grid>
    </Grid>
  );
}

/** This month's real AI spend, styled like the reference's small player card. */
function AiSpendWidget() {
  const { t } = useI18n();
  const { data, reload } = useLoad(() => api.get<UsageSummary>("/api/ai/usage"));
  useRealtime("agentRunChanged", () => void reload());
  if (!data) return null;
  return (
    <Rise index={2}>
      <Card>
        <CardContent>
          <WidgetTitle title={t.aiCostMonth} sub={t.realOnly} />
          <Stack direction="row" spacing={1.5} sx={{ alignItems: "center" }}>
            <Box sx={{ width: 56, height: 56, borderRadius: "16px", display: "grid", placeItems: "center", background: `linear-gradient(140deg, ${brand.purpleDeep}, ${brand.purple})`, flexShrink: 0 }}>
              <Mascot size={44} float={false} />
            </Box>
            <Box sx={{ minWidth: 0 }}>
              <Typography variant="h5" sx={{ lineHeight: 1.1 }}>{usd(data.thisMonthUsd)}</Typography>
              <Typography variant="caption" color="text.secondary">
                {data.runsThisMonth} {t.runs} · {data.thisMonthTokens.toLocaleString()} {t.tokens}
              </Typography>
            </Box>
          </Stack>
          {data.activeRuns > 0 && <LinearProgress sx={{ mt: 1.5, height: 6 }} />}
        </CardContent>
      </Card>
    </Rise>
  );
}

function AnnouncementsWidget() {
  const { t, lang } = useI18n();
  const { data, reload } = useLoad(() => api.get<Announcement[]>("/api/announcements"));
  useRealtime("announcementsChanged", () => void reload());
  useRealtime("announcementRead", () => void reload());
  if (!data) return null;
  const fmt = new Intl.DateTimeFormat(lang === "th" ? "th-TH" : "en-GB", { dateStyle: "medium" });
  return (
    <Rise index={2}>
      <Card>
        <CardContent>
          <WidgetTitle title={t.latestAnnouncements} sub={t.messageCrow} icon={<NotificationsIcon />} />
          {data.length === 0 ? (
            <Typography variant="body2" color="text.secondary">{t.allClear}</Typography>
          ) : (
            <Stack spacing={1.25}>
              {data.slice(0, 3).map((a) => (
                <Stack key={a.id} component={Link} href="/announcements" direction="row" spacing={1.25}
                  sx={{ alignItems: "center", color: "inherit", textDecoration: "none" }}>
                  <Box sx={{ width: 34, height: 34, borderRadius: "12px", bgcolor: a.isRead ? brand.lilac : brand.pinkSoft, display: "grid", placeItems: "center", flexShrink: 0 }}>
                    {a.isPinned ? "📌" : a.priority === "Important" ? "❗" : "📣"}
                  </Box>
                  <Box sx={{ minWidth: 0 }}>
                    <Typography variant="body2" noWrap sx={{ fontWeight: a.isRead ? 400 : 700 }}>{a.title}</Typography>
                    <Typography variant="caption" color="text.secondary" noWrap component="div">
                      {a.departmentName ?? t.wholeCompany} · {fmt.format(new Date(a.publishedAt ?? a.createdAt))}
                    </Typography>
                  </Box>
                </Stack>
              ))}
            </Stack>
          )}
        </CardContent>
      </Card>
    </Rise>
  );
}

/** A month grid with today highlighted; arrows move between months. */
function CalendarWidget() {
  const { t, lang } = useI18n();
  const today = new Date();
  const [month, setMonth] = useState(() => new Date(today.getFullYear(), today.getMonth(), 1));
  const locale = lang === "th" ? "th-TH" : "en-GB";
  const title = new Intl.DateTimeFormat(locale, { month: "long", year: "numeric" }).format(month);
  const weekday = new Intl.DateTimeFormat(locale, { weekday: "narrow" });
  const start = new Date(month);
  start.setDate(1 - month.getDay()); // weeks start on Sunday
  const days = Array.from({ length: 42 }, (_, i) => new Date(start.getFullYear(), start.getMonth(), start.getDate() + i));
  const sameDay = (a: Date, b: Date) => a.toDateString() === b.toDateString();
  return (
    <Card>
      <CardContent>
        <Stack direction="row" sx={{ alignItems: "center", mb: 1 }}>
          <Box sx={{ flex: 1 }}>
            <Typography variant="h6" sx={{ lineHeight: 1.2 }}>{t.calendar}</Typography>
            <Typography variant="overline" color="primary" sx={{ fontSize: 10.5 }}>{title}</Typography>
          </Box>
          <IconButton size="small" aria-label="previous month" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() - 1, 1))}><ChevronLeftIcon /></IconButton>
          <IconButton size="small" aria-label="next month" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() + 1, 1))}><ChevronRightIcon /></IconButton>
        </Stack>
        <Box sx={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", textAlign: "center", rowGap: 0.5 }}>
          {days.slice(0, 7).map((d) => (
            <Typography key={`h${d.getDay()}`} variant="caption" color="text.secondary" sx={{ fontWeight: 600 }}>{weekday.format(d)}</Typography>
          ))}
          {days.map((d) => {
            const isToday = sameDay(d, today);
            const inMonth = d.getMonth() === month.getMonth();
            return (
              <Box key={d.toISOString()} sx={{
                mx: "auto", width: 28, height: 28, borderRadius: "50%", display: "grid", placeItems: "center", fontSize: 13,
                fontWeight: isToday ? 700 : 400,
                color: isToday ? "#fff" : inMonth ? "text.primary" : "text.disabled",
                bgcolor: isToday ? "primary.main" : "transparent",
                boxShadow: isToday ? `0 4px 12px ${brand.purple}55` : "none",
              }}>
                {d.getDate()}
              </Box>
            );
          })}
        </Box>
      </CardContent>
    </Card>
  );
}
