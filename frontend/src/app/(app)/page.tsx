"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import dynamic from "next/dynamic";
import { AgentRun, api, Assistant, ChatMessage, ClaudeReply, Dashboard, Meeting, RecentTask, TerminalResult, usd } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n";
import { useRealtime } from "@/lib/realtime";
import { Loading, useLoad } from "@/components/common";
import { Mascot } from "@/components/cute";
import { Arrow, Calendar, Chart, Check, CheckSquare, ChevronRight, Clipboard, Doc, Dots, Pen, Search, Sparkle } from "@/components/dashIcons";
import { art, FRIEND_ART } from "@/lib/characters";
import { SCENE_BLOBS } from "@/components/stageArt";
import s from "./dashboard.module.css";

/** Optional artwork for the center stage: STAGE_IMAGE, else the character pack's scene. Without either the mascot stands there. */
const STAGE_IMAGE = process.env.NEXT_PUBLIC_STAGE_IMAGE || art("scene");

// WebGL only runs in the browser; the CSS background shows the still picture until (or if) it loads.
const StageArt = dynamic(() => import("@/components/stageArt"), { ssr: false });

/** A character pack image, or the built-in mascot. */
function Art({ name, size, round, float = false }: { name: Parameters<typeof art>[0]; size: number; round?: boolean; float?: boolean }) {
  const src = art(name);
  return src
    // eslint-disable-next-line @next/next/no-img-element
    ? <img src={src} alt="" width={size} height={size} style={{ width: size, height: size, objectFit: "cover", borderRadius: round ? "50%" : 12, flexShrink: 0 }} />
    : <Mascot size={size} float={float} />;
}

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
  const { data, reload } = useLoad(() => api.get<RecentTask[]>("/api/tasks/recent?take=3"));
  useRealtime("taskChanged", () => void reload());
  const pct = dashboard.totalTasks ? (dashboard.completedTasks / dashboard.totalTasks) * 100 : 0;
  return (
    <section className={s.card}>
      <div className={s.cardHead}>
        <span className={s.badgeIcon}><CheckSquare /></span>
        <h2>{t.dashTasks}</h2>
        <Link href="/projects" className={s.right} title={t.dashSeeAll}>
          {dashboard.completedTasks}/{dashboard.totalTasks} · {t.dashSeeAll}<ChevronRight style={{ width: 14, height: 14 }} />
        </Link>
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
        <Art name="mini" size={40} />
      </div>
    </section>
  );
}

const elapsed = (from: string | null, now: number) => Math.max(0, Math.floor((now - (from ? Date.parse(from) : now)) / 1000));
const mmss = (sec: number) => `${Math.floor(sec / 60)}:${String(sec % 60).padStart(2, "0")}`;

/** The AI work running right now: who is on what, for how long, at what cost so far. */
function NowRunningCard() {
  const { t, label } = useI18n();
  const { data, reload } = useLoad(async () => (await api.get<AgentRun[]>("/api/agent-runs?take=50")).filter((r) => r.status === "Queued" || r.status === "Running"));
  useRealtime("agentRunChanged", () => void reload());
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, []);
  const runs = data ?? [];
  const cover = (size: number, moving: boolean) => (
    <div className={s.cover} style={{ width: size, height: size }}>
      {art("cover") ? <Art name="cover" size={size} /> : <Mascot size={size * 0.78} float={moving} />}
    </div>
  );
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
      <div className={s.runs}>
        {runs.length === 0 && (
          <Link href="/office" className={s.track}>
            {cover(56, false)}
            <div style={{ minWidth: 0 }}>
              <b>{t.dashNobodyRunning}</b>
              <small>{t.dashNobodyRunningHint}</small>
            </div>
          </Link>
        )}
        {runs.slice(0, 3).map((run) => (
          <Link key={run.id} href="/agents" className={s.track}>
            {cover(48, true)}
            <div style={{ minWidth: 0 }}>
              <b>{run.taskTitle}</b>
              <small>{run.employeeName} · {label(run.status)} · {mmss(elapsed(run.startedAt ?? run.createdAt, now))} · {usd(run.costUsd)}</small>
            </div>
          </Link>
        ))}
        {runs.length > 3 && <Link href="/agents" className={s.sub}>+{runs.length - 3} {t.runs}</Link>}
      </div>
    </section>
  );
}

// ---------------- center ----------------

/** `mono`: terminal output, shown as-is in a monospace font. */
type Bubble = { question?: string; answer?: string; typing?: boolean; from?: string; mono?: boolean };

/** Chat answers use light markdown: show **bold** as bold, keep the rest (line breaks are kept by the bubble's CSS). */
function Rich({ text }: { text: string }) {
  return <>{text.split(/(\*\*[^*\n]+\*\*)/g).map((part, i) =>
    part.startsWith("**") && part.endsWith("**") && part.length > 4 ? <strong key={i}>{part.slice(2, -2)}</strong> : part)}</>;
}

/** The owner's own Claude Code session (see ClaudeChatService); shown first among the assistants when set up. */
const CLAUDE_ID = "claude";

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
  const stageRef = useRef<HTMLDivElement>(null);
  const [pending, setPending] = useState(false);

  const [newChat, setNewChat] = useState(false);
  const claudeOn = !!assistant?.claudeAvailable && can("agent.manage");
  // The CEO's Claude card is a terminal (PowerShell in the project folder on this PC) unless they switch back to the Claude chat.
  const isCeo = !!me?.roles.includes("CEO");
  const [chatMode, setChatMode] = useState(false);
  const terminal = isCeo && !chatMode;
  const members = [
    ...(claudeOn ? [{ employeeId: CLAUDE_ID, displayName: terminal ? "Terminal" : "Claude", jobTitle: terminal ? t.dashTerminalTitle : t.dashClaudeTitle, status: "Available" as const, openTasks: 0, currentTask: null }] : []),
    ...(assistant?.members ?? []),
  ];
  const current = members.find((m) => m.employeeId === pickedId) ?? members[0];
  const isClaude = current?.employeeId === CLAUDE_ID;
  const canAsk = !!current && !!assistant && (isClaude || can("comms.post"));

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
    if (isClaude && terminal) {
      setPending(true);
      try {
        const res = await api.post<TerminalResult>("/api/assistant/terminal", { command: question });
        const footer = res.timedOut ? t.dashTerminalTimedOut : `exit ${res.exitCode} · ${(res.ms / 1000).toFixed(1)}s`;
        setBubble({ question: `PS> ${question}`, answer: `${res.output || t.dashTerminalNoOutput}

${footer}`, from: "Terminal", mono: true });
      } catch (err) {
        setBubble({ question: `PS> ${question}`, answer: err instanceof Error ? err.message : String(err), from: "Terminal" });
      } finally {
        setPending(false);
      }
      return;
    }
    if (isClaude) {
      setPending(true);
      try {
        const res = await api.post<ClaudeReply>("/api/assistant/claude", { message: question, newChat });
        setNewChat(false);
        setBubble({ question, answer: res.reply, from: "Claude" });
      } catch (err) {
        setBubble({ question, answer: err instanceof Error ? err.message : String(err), from: "Claude" });
      } finally {
        setPending(false);
      }
      return;
    }
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
    m.employeeId === CLAUDE_ID ? (terminal ? t.dashTerminalStatus : t.dashClaudeStatus) : m.currentTask ? `${t.dashFriendWorking} ${m.currentTask}` : m.openTasks > 0 ? `${label(m.status)} · ${t.dashFriendOpen} ${m.openTasks}` : label(m.status);

  return (
    <>
      <div ref={stageRef} className={s.stage} style={STAGE_IMAGE ? { backgroundImage: `url("${STAGE_IMAGE}")` } : undefined}>
        {STAGE_IMAGE && <StageArt src={STAGE_IMAGE} blobs={STAGE_IMAGE === art("scene") ? SCENE_BLOBS : []} trackRef={stageRef} />}
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
                  {bubble.mono ? <pre className={s.mono}>{bubble.answer}</pre> : <Rich text={bubble.answer ?? ""} />}
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
                <Art name={m.employeeId === CLAUDE_ID ? "logo" : FRIEND_ART[(i - (claudeOn ? 1 : 0)) % FRIEND_ART.length]} size={32} round />
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
            placeholder={isClaude && terminal ? t.dashTerminalPlaceholder : current ? t.dashAskPlaceholder.replace("{name}", current.displayName) : t.dashNoAssistant} />
          <button type="submit" className={s.send} title={t.send} disabled={!canAsk || !input.trim() || pending}><Arrow /></button>
        </form>
        <div className={s.chips}>
          {CHIPS.map(({ key, prompt, Icon }) => (
            <button key={key} className={s.chip} onClick={() => setInput(t[prompt])}><Icon />{t[key]}</button>
          ))}
          {isClaude && !terminal && (
            <button className={s.chip} onClick={() => { setNewChat(true); setBubble({ answer: t.dashClaudeNew, from: "Claude" }); }}>
              <Sparkle />{t.dashChipNewChat}
            </button>
          )}
          {isClaude && isCeo && (
            <button className={s.chip} onClick={() => { setChatMode(!chatMode); setBubble(null); }}>
              <Sparkle />{terminal ? t.dashChipToClaude : t.dashChipToTerminal}
            </button>
          )}
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
      {art("cheer")
        // eslint-disable-next-line @next/next/no-img-element
        ? <img className={s.cheerImg} src={art("cheer")} alt="" />
        : <div className={s.cheerArt}><Mascot size={110} /></div>}
    </div>
  );
}
