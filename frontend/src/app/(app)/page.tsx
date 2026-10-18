"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { AgentRun, api, Assistant, ChatMessage, Dashboard, Meeting, RecentTask, usd } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, useLoad } from "@/components/common";
import { Mascot } from "@/components/cute";
import { Arrow, Calendar, Chart, Check, CheckSquare, ChevronRight, Clipboard, Doc, Dots, Next, Pen, Play, Prev, Search, Sparkle } from "@/components/dashIcons";
import s from "./dashboard.module.css";

/** Optional artwork for the center stage (a file you have the rights to, placed in /public). Without it the mascot stands there. */
const STAGE_IMAGE = process.env.NEXT_PUBLIC_STAGE_IMAGE;

const cx = (...names: (string | false | undefined)[]) => names.filter(Boolean).join(" ");

export default function DashboardPage() {
  const { can } = useAuth();
  const { data, error, reload } = useLoad(() => api.get<Dashboard>("/api/dashboard"));
  useRealtime("taskChanged", () => void reload());
  useRealtime("organizationChanged", () => void reload());
  if (!data) return <Loading error={error} />;

  return (
    <div className={s.root}>
      <div className={s.grid}>
        <div className={s.col}>
          {can("task.read") && <TaskListCard dashboard={data} />}
          {can("agent.read") && <NowRunningCard />}
        </div>
        <div className={cx(s.col, s.colCenter)}>
          <Stage />
        </div>
        <div className={cx(s.col, s.colRight)}>
          <WeekCalendarCard />
          {can("agent.read") && <AiSummaryCard />}
          <CheerCard />
        </div>
      </div>
    </div>
  );
}

// ---------------- left ----------------

function TaskListCard({ dashboard }: { dashboard: Dashboard }) {
  const { t, label } = useI18n();
  const { data, reload } = useLoad(() => api.get<RecentTask[]>("/api/tasks/recent?take=6"));
  useRealtime("taskChanged", () => void reload());
  const pct = dashboard.totalTasks ? (dashboard.completedTasks / dashboard.totalTasks) * 100 : 0;
  return (
    <section className={s.card}>
      <div className={s.cardHead}>
        <span className={s.badgeIcon}><CheckSquare /></span>
        <h2>{t.dashTasks}</h2>
        <span className={s.right}>{t.dashTasksRecent} · {dashboard.completedTasks}/{dashboard.totalTasks}</span>
      </div>
      <ul className={s.tasks}>
        {(data ?? []).map((task) => (
          <li key={task.id}>
            <Link href={`/projects/${task.projectId}`} className={cx(s.task, task.status === "Done" && s.done)}>
              <span className={s.check}><Check /></span>
              <div style={{ minWidth: 0 }}>
                <div className={s.t}>{task.title}</div>
                <div className={s.m}>{task.projectName} · {label(task.status)}{task.assigneeName ? ` · ${task.assigneeName}` : ""}</div>
              </div>
            </Link>
          </li>
        ))}
        {data?.length === 0 && <li className={s.empty}>{t.dashNoTasks}</li>}
      </ul>
      <div className={s.taskFoot}>
        <div className={s.progress}><span style={{ width: `${pct}%` }} /></div>
        <Mascot size={40} float={false} />
      </div>
    </section>
  );
}

const elapsed = (from: string | null, now: number) => Math.max(0, Math.floor((now - (from ? Date.parse(from) : now)) / 1000));
const mmss = (sec: number) => `${Math.floor(sec / 60)}:${String(sec % 60).padStart(2, "0")}`;

/** The mock-up's music player, playing the AI work that is running right now. */
function NowRunningCard() {
  const { t, label } = useI18n();
  const { data, reload } = useLoad(async () => (await api.get<AgentRun[]>("/api/agent-runs?take=50")).filter((r) => r.status === "Queued" || r.status === "Running"));
  useRealtime("agentRunChanged", () => void reload());
  const [index, setIndex] = useState(0);
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);
  const runs = data ?? [];
  const run = runs.length ? runs[index % runs.length] : undefined;
  const secs = run ? elapsed(run.startedAt ?? run.createdAt, now) : 0;
  const shown = Math.max(600, secs); // the bar fills over the default 10-minute run limit
  return (
    <section className={s.card}>
      <div className={s.cardHead}>
        <span className={s.badgeIcon}><Sparkle /></span>
        <div>
          <h2 style={{ fontSize: 16 }}>{t.dashNowRunning}</h2>
          <div className={s.sub}>{runs.length ? `${runs.length} ${t.runs}` : t.dashNowRunningSub}</div>
        </div>
        <Link href="/agents" className={s.right} aria-label={t.navActivity}><ChevronRight /></Link>
      </div>
      <div className={s.track}>
        <div className={s.cover}><Mascot size={56} float={!!run} /></div>
        <div style={{ minWidth: 0 }}>
          <b>{run ? run.taskTitle : t.dashNobodyRunning}</b>
          <small>{run ? `${run.employeeName} · ${label(run.status)} · ${run.turns} turns` : t.dashNobodyRunningHint}</small>
        </div>
      </div>
      <div className={s.seek}><span style={{ width: `${run ? (secs / shown) * 100 : 0}%` }} /></div>
      <div className={s.times}>
        <span>{mmss(secs)}</span>
        <span>{run ? usd(run.costUsd) : "—"}</span>
      </div>
      <div className={s.controls}>
        <button className={s.skip} title="Previous" disabled={runs.length < 2} onClick={() => setIndex((i) => (i - 1 + runs.length) % runs.length)}><Prev /></button>
        <Link className={s.play} href={run ? `/agents` : "/office"} title={run ? t.navActivity : t.navOffice}><Play /></Link>
        <button className={s.skip} title="Next" disabled={runs.length < 2} onClick={() => setIndex((i) => (i + 1) % runs.length)}><Next /></button>
      </div>
    </section>
  );
}

// ---------------- center ----------------

type Bubble = { question?: string; answer?: string; typing?: boolean; from?: string };

const CHIPS = [
  { key: "dashChipSummary", prompt: "dashPromptSummary", Icon: Doc },
  { key: "dashChipPlan", prompt: "dashPromptPlan", Icon: Calendar },
  { key: "dashChipRisk", prompt: "dashPromptRisk", Icon: Search },
  { key: "dashChipIdea", prompt: "dashPromptIdea", Icon: Pen },
] as const;

/**
 * The mock-up's stage. Questions go to an AI assistant as a chat message in the "AI Assistant" channel (so answers are
 * priced, budgeted and talk-only like any chat answer); the bubble shows the reply when it arrives.
 */
function Stage() {
  const { t, label } = useI18n();
  const { me, can } = useAuth();
  const { data: assistant, reload } = useLoad(() => api.get<Assistant>("/api/assistant"));
  useRealtime("organizationChanged", () => void reload());
  const [pickedId, setPickedId] = useState<string | null>(null);
  const [input, setInput] = useState("");
  const [bubble, setBubble] = useState<Bubble | null>(null);
  const waiting = useRef<{ after: string; employeeId: string } | null>(null);
  const [pending, setPending] = useState(false);

  const members = assistant?.members ?? [];
  const current = members.find((m) => m.employeeId === pickedId) ?? members[0];
  const canAsk = can("comms.post") && !!current && !!assistant;

  const checkReply = async () => {
    const w = waiting.current;
    if (!w || !assistant) return;
    const messages = await api.get<ChatMessage[]>(`/api/chat/channels/${assistant.channelId}/messages?limit=20`);
    const reply = messages.find((m) => m.authorEmployeeId === w.employeeId && m.createdAt > w.after && !m.isDeleted);
    if (!reply) return;
    waiting.current = null;
    setPending(false);
    setBubble((b) => ({ question: b?.question, answer: reply.body ?? "", from: reply.authorName }));
  };
  useRealtime("chatMessage", () => void checkReply());
  useEffect(() => {
    // Realtime can miss a message (reconnects); poll gently while waiting, and give up after two minutes.
    const id = setInterval(() => {
      const w = waiting.current;
      if (!w) return;
      if (Date.now() - Date.parse(w.after) > 120_000) {
        waiting.current = null;
        setPending(false);
        setBubble((b) => ({ question: b?.question, answer: t.dashNoAnswer }));
      } else void checkReply();
    }, 2500);
    return () => clearInterval(id);
  });

  const ask = async (e: React.FormEvent) => {
    e.preventDefault();
    const question = input.trim();
    if (!question || !canAsk) return;
    setInput("");
    setBubble({ question, typing: true });
    try {
      const sent = await api.post<ChatMessage>(`/api/chat/channels/${assistant!.channelId}/messages`, {
        body: `@${current!.displayName} ${question}`, mentionedEmployeeIds: [current!.employeeId],
      });
      waiting.current = { after: sent.createdAt, employeeId: current!.employeeId };
      setPending(true);
    } catch (err) {
      setBubble({ question, answer: err instanceof Error ? err.message : String(err) });
    }
  };

  const status = (m: NonNullable<typeof current>) =>
    m.currentTask ? `${t.dashFriendWorking} ${m.currentTask}` : m.openTasks > 0 ? `${label(m.status)} · ${t.dashFriendOpen} ${m.openTasks}` : label(m.status);

  return (
    <>
      <div className={s.stage} style={STAGE_IMAGE ? { backgroundImage: `url("${STAGE_IMAGE}")` } : undefined}>
        <span className={s.sparkle} style={{ left: "22%", top: 60 }}>✦</span>
        <span className={s.sparkle} style={{ left: "70%", top: 120, animationDelay: "1s" }}>✦</span>
        <span className={s.sparkle} style={{ left: "30%", top: 250, animationDelay: "2s", fontSize: 16 }}>✦</span>
        {!STAGE_IMAGE && (
          <>
            <div className={s.stageGlow} />
            <div className={s.stageMascot}><Mascot size={230} /></div>
          </>
        )}

        <div className={s.bubble} aria-live="polite">
          {!bubble ? (
            <>
              <b>{t.hello} {me?.displayName}! {current ? `${t.dashIAm} ${current.displayName}` : ""}</b>
              {current ? t.dashAskHelp : t.dashNoAssistant}
            </>
          ) : (
            <>
              {bubble.question && <div className={s.you}>{t.dashYou}: {bubble.question}</div>}
              {bubble.typing ? (
                <div className={s.typing}><span /><span /><span /></div>
              ) : (
                <>
                  <b>{bubble.from ?? current?.displayName}</b>
                  {bubble.answer}
                </>
              )}
            </>
          )}
        </div>

        {members.length > 0 && (
          <div className={s.friends}>
            {members.map((m, i) => (
              <button key={m.employeeId} className={cx(s.friend, s[`friend${i % 4}`], m.employeeId === current?.employeeId && s.active)}
                onClick={() => { setPickedId(m.employeeId); setBubble({ answer: `${m.jobTitle} — ${status(m)}`, from: m.displayName }); }}>
                <Mascot size={32} float={false} />
                <span style={{ minWidth: 0 }}>
                  <b>{m.displayName}</b>
                  <small>{status(m)}</small>
                </span>
              </button>
            ))}
          </div>
        )}
      </div>

      <div className={s.composer}>
        <form className={s.ask} onSubmit={ask}>
          <input value={input} onChange={(e) => setInput(e.target.value)} disabled={!canAsk} autoComplete="off" maxLength={3000}
            placeholder={current ? t.dashAskPlaceholder.replace("{name}", current.displayName) : t.dashNoAssistant} />
          <button type="submit" className={s.send} title={t.send} disabled={!canAsk || !input.trim() || pending}><Arrow /></button>
        </form>
        <div className={s.chips}>
          {CHIPS.map(({ key, prompt, Icon }) => (
            <button key={key} className={s.chip} onClick={() => setInput(t[prompt])}><Icon />{t[key]}</button>
          ))}
          <Link href={assistant ? `/chat?channel=${assistant.channelId}` : "/chat"} className={s.chip}><Dots />{t.dashChipChat}</Link>
        </div>
      </div>
    </>
  );
}

// ---------------- right ----------------

function WeekCalendarCard() {
  const { t, lang } = useI18n();
  const locale = lang === "th" ? "th-TH" : "en-GB";
  const [offset, setOffset] = useState(0);
  const [today] = useState(() => new Date());
  const start = useMemo(() => {
    const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() + offset * 7);
    d.setDate(d.getDate() - ((d.getDay() + 6) % 7)); // Monday
    return d;
  }, [offset, today]);
  const days = Array.from({ length: 7 }, (_, i) => new Date(start.getFullYear(), start.getMonth(), start.getDate() + i));
  const end = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 7);
  const { data } = useLoad(() => api.get<Meeting[]>(`/api/meetings?from=${start.toISOString()}&to=${end.toISOString()}`), start.toISOString());
  const [selected, setSelected] = useState(today.toDateString());
  const same = (a: Date, b: string) => a.toDateString() === b;
  const list = (data ?? []).filter((m) => same(new Date(m.scheduledAt), selected) && m.status !== "Cancelled")
    .sort((a, b) => a.scheduledAt.localeCompare(b.scheduledAt));
  const fmt = new Intl.DateTimeFormat(locale, { day: "numeric", month: "short" });
  const time = new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" });
  const wd = new Intl.DateTimeFormat(locale, { weekday: "short" });
  return (
    <section className={s.card}>
      <div className={s.cardHead}>
        <span className={s.badgeIcon}><Calendar /></span>
        <div>
          <h2 style={{ fontSize: 16 }}>{t.dashCalendar}</h2>
          <div className={s.sub}>{fmt.format(days[0])} – {fmt.format(days[6])}</div>
        </div>
        <span className={s.right}>
          <button onClick={() => setOffset((o) => o - 1)} aria-label="previous week" style={{ transform: "scaleX(-1)" }}><ChevronRight /></button>
          <button onClick={() => setOffset((o) => o + 1)} aria-label="next week"><ChevronRight /></button>
        </span>
      </div>
      <div className={s.week}>
        {days.map((d) => <div key={`n${d.getDay()}`} className={s.dn}>{wd.format(d)}</div>)}
        {days.map((d) => (
          <button key={d.toDateString()} onClick={() => setSelected(d.toDateString())}
            className={cx(s.day, same(d, selected) && s.sel, same(d, today.toDateString()) && s.today,
              (data ?? []).some((m) => same(new Date(m.scheduledAt), d.toDateString())) && s.has)}>
            {d.getDate()}
          </button>
        ))}
      </div>
      <ul className={s.events}>
        {list.length ? list.map((m) => (
          <li key={m.id}><time>{time.format(new Date(m.scheduledAt))}</time><Link href="/meetings">{m.title}</Link></li>
        )) : <li className={s.none}>{t.dashNoEvents}</li>}
      </ul>
    </section>
  );
}

const SEEN_RUNS = "dashboard.seenRuns";
const TONES = {
  Succeeded: { bg: "var(--purple-soft)", fg: "var(--purple-deep)", Icon: Doc },
  LimitReached: { bg: "var(--yellow-soft)", fg: "#c08a1e", Icon: Clipboard },
  Failed: { bg: "var(--pink-soft)", fg: "#d76b97", Icon: Chart },
  Cancelled: { bg: "var(--blue-soft)", fg: "#5b7fc0", Icon: Clipboard },
} as const;

/** The latest finished AI work, with an unread marker per browser. */
function AiSummaryCard() {
  const { t, label, lang } = useI18n();
  const { data, reload } = useLoad(async () => (await api.get<AgentRun[]>("/api/agent-runs?take=20")).filter((r) => r.finishedAt).slice(0, 3));
  useRealtime("agentRunChanged", () => void reload());
  const [seen, setSeen] = useState<string[]>(() => {
    try { return JSON.parse(localStorage.getItem(SEEN_RUNS) ?? "[]"); } catch { return []; }
  });
  const markRead = (id: string) => {
    const next = [...new Set([...seen, id])].slice(-100);
    setSeen(next);
    try { localStorage.setItem(SEEN_RUNS, JSON.stringify(next)); } catch { /* storage unavailable */ }
  };
  const runs = data ?? [];
  const unread = runs.filter((r) => !seen.includes(r.id)).length;
  const [now] = useState(() => Date.now());
  const rel = new Intl.RelativeTimeFormat(lang === "th" ? "th" : "en", { numeric: "auto" });
  const ago = (iso: string) => {
    const min = Math.round((Date.parse(iso) - now) / 60000);
    return Math.abs(min) < 60 ? rel.format(min, "minute") : Math.abs(min) < 1440 ? rel.format(Math.round(min / 60), "hour") : rel.format(Math.round(min / 1440), "day");
  };
  return (
    <section className={s.card}>
      <div className={s.cardHead}>
        <span style={{ color: "var(--purple)" }}><Sparkle style={{ width: 20, height: 20 }} /></span>
        <h2 style={{ fontSize: 15 }}>{t.dashSummary}</h2>
        <span className={s.newBadge}>{unread ? `${unread} ${t.dashNew}` : t.dashAllRead}</span>
      </div>
      {runs.length === 0 && <div className={s.empty}>{t.dashNoSummary}</div>}
      {runs.map((r) => {
        const tone = TONES[r.status as keyof typeof TONES] ?? TONES.Succeeded;
        return (
          <Link key={r.id} href="/agents" onClick={() => markRead(r.id)} className={cx(s.sum, !seen.includes(r.id) && s.unread)}>
            <span className={s.sumIc} style={{ background: tone.bg, color: tone.fg }}><tone.Icon /></span>
            <span className={s.sumText}>
              <b>{r.taskTitle}</b>
              <p>{r.employeeName} · {label(r.status)}{r.costUsd ? ` · ${usd(r.costUsd)}` : ""}</p>
              <small>{ago(r.finishedAt!)}</small>
            </span>
            <ChevronRight />
          </Link>
        );
      })}
    </section>
  );
}

const CHEERS = {
  th: ["ก้าวเล็ก ๆ\nพาไปสู่ฝันใหญ่ 💛", "วันนี้ทำดีแล้ว\nพักบ้างนะ ☕", "ทีม AI พร้อม\nลุยไปด้วยกัน ✨"],
  en: ["Small steps\nmake big dreams 💛", "You did great today\ntake a break ☕", "Your AI team is ready\nlet's go ✨"],
};

function CheerCard() {
  const { t, lang } = useI18n();
  const lines = CHEERS[lang === "th" ? "th" : "en"];
  const [day] = useState(() => new Date().getDate());
  const line = lines[day % lines.length];
  return (
    <div className={s.cheer}>
      <p>{line.split("\n").map((l) => <span key={l} style={{ display: "block" }}>{l}</span>)}</p>
      <small>{t.dashKeepGoing}</small>
      <div className={s.cheerArt}><Mascot size={110} /></div>
    </div>
  );
}
