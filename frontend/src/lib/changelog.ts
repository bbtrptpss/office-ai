/**
 * Update log and remaining work, shown on /updates.
 * Every change to the app adds an entry here in the same commit: newest release first.
 */

export type ChangeType = "added" | "changed" | "fixed" | "security";

export interface Text {
  th: string;
  en: string;
}

export interface Change extends Text {
  type: ChangeType;
}

export interface Release {
  version: string;
  /** YYYY-MM-DD */
  date: string;
  title: Text;
  changes: Change[];
}

export type TodoStatus = "planned" | "waitingCeo";

export interface Todo extends Text {
  status: TodoStatus;
  area: Text;
}

export const CHANGELOG: Release[] = [
  {
    version: "0.7.1",
    date: "2026-10-18",
    title: { th: "หน้าบันทึกอัปเดต", en: "Update log page" },
    changes: [
      { type: "added", th: "หน้า “บันทึกอัปเดต” แสดงสิ่งที่เพิ่ม/แก้ไขในแต่ละรอบ และรายการที่ยังต้องทำ", en: "“Update log” page listing what each release added or fixed, plus the remaining to-do list" },
      { type: "added", th: "เมนูขึ้นป้าย “ใหม่” เมื่อมีอัปเดตที่ยังไม่ได้เปิดดู", en: "The menu shows a “new” badge when there is an update you have not opened yet" },
      { type: "fixed", th: "ช่องวันที่กำหนดเสร็จในหน้าการประชุมล้นขอบการ์ดบนจอแคบ", en: "Due-date field on the meeting page overflowed its card on narrow screens" },
    ],
  },
  {
    version: "0.7.0",
    date: "2026-10-18",
    title: { th: "Phase 7 — แชท ประกาศ และการประชุม", en: "Phase 7 — chat, announcements and meetings" },
    changes: [
      { type: "added", th: "แชทบริษัท: ช่อง General, ช่องแผนก และช่องโปรเจกต์ อัปเดตสด พร้อมจำนวนข้อความที่ยังไม่อ่าน", en: "Company chat: General, department and project channels with live updates and unread counts" },
      { type: "added", th: "ประกาศภายใน: ร่าง → คนเผยแพร่, เลือกผู้รับทั้งบริษัทหรือรายแผนก, ปักหมุด, ปุ่ม “รับทราบ”", en: "Announcements: draft → published by a person, company or department audience, pinning, “acknowledge” button" },
      { type: "added", th: "การประชุม: วาระ ผู้เข้าร่วม การเข้าร่วม บันทึกการประชุม และสิ่งที่ต้องทำต่อ", en: "Meetings: agenda, participants, attendance, minutes and action items" },
      { type: "added", th: "บันทึกการประชุมของโปรเจกต์ที่อนุมัติแล้ว เก็บเป็นเอกสารโปรเจกต์ฉบับจริงอัตโนมัติ", en: "Approved minutes of a project meeting are filed as a final project document" },
      { type: "added", th: "เปลี่ยนสิ่งที่ต้องทำต่อเป็น Task ได้ในคลิกเดียว (กดซ้ำพร้อมกันก็ไม่เกิดงานซ้ำ)", en: "Turn an action item into a task in one click (double clicks never create duplicates)" },
      { type: "added", th: "พนักงาน AI อ่าน/โพสต์แชทของโปรเจกต์ตัวเอง ร่างบันทึกประชุม และร่างประกาศ (เฉพาะผู้บริหาร) ได้", en: "AI employees can read/post in their project's chat, draft minutes and (executives only) draft announcements" },
      { type: "added", th: "การ์ดประกาศล่าสุดบน Dashboard และปุ่ม “แชทโปรเจกต์” ในหน้าโปรเจกต์", en: "Latest-announcements card on the dashboard and a “Project chat” button on projects" },
      { type: "security", th: "สิทธิ์ใหม่ comms.post และ comms.manage (เฉพาะคน) — AI เผยแพร่หรืออนุมัติอะไรไม่ได้ และเห็นเฉพาะโปรเจกต์ของงานตัวเอง", en: "New permissions comms.post and comms.manage (people only) — AI cannot publish or approve and only sees its task's project" },
    ],
  },
  {
    version: "0.6.0",
    date: "2026-10-18",
    title: { th: "Phase 6 — พร้อมใช้งานจริง", en: "Phase 6 — production readiness" },
    changes: [
      { type: "security", th: "Security headers, ป้องกัน CSRF, คุกกี้ปลอดภัยนอกโหมดพัฒนา, แยกข้อมูลข้ามโปรเจกต์สำหรับเครื่องมือ AI", en: "Security headers, CSRF guard, secure cookies outside development, cross-project isolation of AI tools" },
      { type: "fixed", th: "คำขอที่ชนกัน (เริ่มซ้ำ, แก้ไขพร้อมกัน) ตอบ 409 อย่างถูกต้อง ไม่เกิด 500 หรือข้อมูลซ้ำ", en: "Racing requests (double start, concurrent edits) now return clean 409s, never 500s or duplicates" },
      { type: "added", th: "OpenTelemetry traces/metrics, log แบบ JSON, readiness check", en: "OpenTelemetry traces/metrics, JSON logs, readiness check" },
      { type: "added", th: "สคริปต์สำรอง/กู้คืนฐานข้อมูลและ repository พร้อมผลซ้อมกู้คืนจริง", en: "Backup/restore scripts for the database and repositories, with a real restore drill" },
      { type: "added", th: "คู่มือ deploy, compose สำหรับ production (HTTPS), เปลี่ยนรหัสผ่าน", en: "Deployment guide, production compose (HTTPS), change password" },
      { type: "added", th: "หน้าคุณภาพและความปลอดภัย", en: "Quality & Security overview page" },
    ],
  },
  {
    version: "0.5.0",
    date: "2026-10-18",
    title: { th: "Phase 5 — บริหารบริษัท", en: "Phase 5 — company management" },
    changes: [
      { type: "added", th: "งบประมาณ AI ระดับบริษัท/แผนก/โปรเจกต์ พร้อมแจ้งเตือน 80% / 100%", en: "AI budgets per company/department/project with 80% / 100% alerts" },
      { type: "added", th: "รายงานการเงิน (ประมาณการเทียบจริง) และรายงานผลงานพนักงาน AI", en: "Finance report (planned vs actual) and AI employee performance report" },
      { type: "added", th: "คลังความรู้ที่ต้องมีคนรับรองก่อนใช้ และเอกสารโปรเจกต์แบบมีเวอร์ชัน", en: "Knowledge base validated by people, and versioned project documents" },
      { type: "added", th: "Webhook แจ้งเตือนออกภายนอก", en: "Outbound webhook notifications" },
    ],
  },
  {
    version: "0.4.0",
    date: "2026-10-18",
    title: { th: "Phase 4 — งานส่งมอบซอฟต์แวร์", en: "Phase 4 — software delivery" },
    changes: [
      { type: "added", th: "Requirements, ADR และใบประเมินราคาที่คำนวณแบบแน่นอน", en: "Requirements, ADRs and deterministically priced estimates" },
      { type: "added", th: "Git repository ต่อโปรเจกต์, branch ต่อ task, merge request ที่ต้องมีคนอนุมัติ", en: "Git repository per project, branch per task, merge requests that need human approval" },
      { type: "security", th: "รันเทสต์ใน sandbox ที่ไม่มี network, จำกัด CPU/หน่วยความจำ/เวลา, ไม่ใช่ root", en: "Tests run in a sandbox with no network, CPU/memory/time limits, non-root" },
    ],
  },
  {
    version: "0.3.0",
    date: "2026-10-18",
    title: { th: "Phase 3 — ประสานงานหลายเอเจนต์", en: "Phase 3 — orchestration" },
    changes: [
      { type: "added", th: "วางแผนงานจากเป้าหมาย, กล่องอนุมัติ, workflow engine, รีวิวโดย AI", en: "Planning from objectives, approval inbox, workflow engine, AI reviews" },
    ],
  },
  {
    version: "0.2.0",
    date: "2026-10-18",
    title: { th: "Phase 2 — พนักงาน AI ทำงานจริง", en: "Phase 2 — AI employees at work" },
    changes: [
      { type: "added", th: "ตั้งค่าโมเดล เครื่องมือ และงบของพนักงาน AI แต่ละคน; บันทึก token และค่าใช้จ่ายทุกครั้ง", en: "Per-employee model, tools and budget; every token and cost recorded" },
      { type: "added", th: "เชื่อม Claude (Anthropic) และ provider จำลองสำหรับทดลอง (มีป้ายกำกับเสมอ)", en: "Claude (Anthropic) adapter and a clearly labelled simulated provider for trials" },
    ],
  },
  {
    version: "0.1.0",
    date: "2026-10-18",
    title: { th: "Phase 1 — รากฐานบริษัท", en: "Phase 1 — company foundation" },
    changes: [
      { type: "added", th: "ผังองค์กร แผนก ตำแหน่ง พนักงานคนและ AI, โปรเจกต์และ Task Board, สิทธิ์ และ Audit log", en: "Org chart, departments, positions, human and AI employees, projects and task board, permissions and audit log" },
    ],
  },
];

export const TODO: Todo[] = [
  { status: "waitingCeo", area: { th: "AI", en: "AI" }, th: "ใส่ ANTHROPIC_API_KEY เพื่อให้ Claude ทำงานจริงครั้งแรก", en: "Set ANTHROPIC_API_KEY for the first real Claude run" },
  { status: "waitingCeo", area: { th: "AI", en: "AI" }, th: "แจ้งว่ามีบัญชี AI provider ใดบ้าง (OpenAI / Gemini)", en: "Confirm which AI provider accounts exist (OpenAI / Gemini)" },
  { status: "waitingCeo", area: { th: "การเงิน", en: "Finance" }, th: "กำหนดงบ AI รายเดือนสำหรับช่วงทดลอง (ตอนนี้ USD 5 ต่อพนักงาน AI)", en: "Set the monthly AI budget for the trial (currently USD 5 per AI employee)" },
  { status: "waitingCeo", area: { th: "Deploy", en: "Deploy" }, th: "เลือกที่ deploy ระบบจริง", en: "Choose where to deploy" },
  { status: "planned", area: { th: "AI", en: "AI" }, th: "Adapter สำหรับ OpenAI / Gemini", en: "OpenAI / Gemini adapters" },
  { status: "planned", area: { th: "โค้ด", en: "Code" }, th: "เชื่อม GitHub / GitLab แทน repository ในเครื่อง", en: "GitHub / GitLab hosting behind the repository interface" },
  { status: "planned", area: { th: "แชท", en: "Chat" }, th: "จำกัดสมาชิกช่องแชทโปรเจกต์ (ตอนนี้ทุกคนที่ล็อกอินอ่านได้ทุกช่อง)", en: "Per-project channel membership (today every signed-in person can read all channels)" },
  { status: "planned", area: { th: "แชท", en: "Chat" }, th: "การ @mention และการแจ้งเตือนในแชท", en: "@mentions and chat notifications" },
  { status: "planned", area: { th: "ความปลอดภัย", en: "Security" }, th: "เข้ารหัส DataProtection keys ด้วย certificate", en: "Encrypt DataProtection keys at rest with a certificate" },
  { status: "planned", area: { th: "Workflow", en: "Workflow" }, th: "อนุมัติเพิ่มงบแล้วให้ workflow ได้งบเพิ่มอัตโนมัติ", en: "An approved budget escalation should raise the workflow budget by itself" },
  { status: "planned", area: { th: "AI", en: "AI" }, th: "ทำงาน AI ต่อจากจุดที่ค้างได้เมื่อระบบรีสตาร์ต (ตอนนี้ล้มเหลวแล้วลองใหม่)", en: "Resume interrupted AI runs mid-call (today they fail and are retried)" },
];

export const LATEST_VERSION = CHANGELOG[0].version;

/** localStorage key holding the last version this browser opened on /updates. */
export const SEEN_KEY = "updates.seenVersion";
