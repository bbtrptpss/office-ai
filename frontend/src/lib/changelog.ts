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
    version: "0.11.3",
    date: "2026-10-19",
    title: { th: "รูปกลางหน้าภาพรวมเคลื่อนไหวแบบ 2.5D", en: "2.5D animated stage picture" },
    changes: [
      { type: "added", th: "รูปกลางหน้าภาพรวมมีมิติ: ตัวละครลอยเด่นจากฉากและขยับตามเมาส์ (parallax) แต่ละตัวโยกเบา ๆ คนละจังหวะ และฉากแกว่งช้า ๆ เองเมื่อไม่ได้ขยับเมาส์ — ปิดการเคลื่อนไหวอัตโนมัติถ้าเครื่องตั้ง “ลดการเคลื่อนไหว”", en: "The dashboard's center picture gains depth: characters stand out and follow the mouse (parallax), each bobs on its own beat, and the scene sways gently when the mouse is still — automatic motion is off when the device asks for reduced motion" },
    ],
  },
  {
    version: "0.11.2",
    date: "2026-10-19",
    title: { th: "เมนูย้ายไปที่ • • • มุมซ้ายบน", en: "Menu moved behind the • • • at the top left" },
    changes: [
      { type: "changed", th: "ซ่อนแถบเมนูด้านข้าง หน้าจอกว้างขึ้น — ชี้เมาส์ที่ • • • มุมซ้ายบนเพื่อเปิดเมนู (บนมือถือ/แท็บเล็ตให้แตะ) ปิดเองเมื่อเอาเมาส์ออก เลือกเมนู หรือกด Esc", en: "The side menu is hidden for a wider page — hover the • • • at the top left to open it (tap on phones/tablets); it closes when the mouse leaves, you pick an item, or press Esc" },
      { type: "added", th: "มีจุดสีชมพูข้าง • • • เมื่อมีงานรออนุมัติ แชทหรือประกาศที่ยังไม่อ่าน", en: "A pink dot next to • • • shows when approvals, chat or announcements are waiting" },
      { type: "changed", th: "ปุ่มเปิดเมนูเป็นโลโก้ (ชุดรูปตัวละคร หรือมาสคอต Puni) แทน • • •", en: "The menu button is the logo (character pack or the Puni mascot) instead of • • •" },
      { type: "changed", th: "รายการงานในหน้าภาพรวมแสดง 3 งานล่าสุด พร้อมปุ่ม “ดูทั้งหมด” ไปหน้าโปรเจกต์และ Task Board", en: "The dashboard task list shows the 3 most recent tasks, with a “See all” link to Projects & Task Board" },
      { type: "changed", th: "หน้าภาพรวมพอดีหนึ่งหน้าจอ ไม่มีแถบเลื่อน: รูปกลางปรับขนาดตามความสูงจอ", en: "The dashboard fits one screen with no scrollbar: the center picture sizes to the screen height" },
      { type: "changed", th: "การ์ด “AI กำลังทำงาน” เอาปุ่มเครื่องเล่นเพลงออก แสดงรายการงานที่กำลังรันได้สูงสุด 3 งาน พร้อมเวลาที่ใช้และค่าใช้จ่าย", en: "The “AI at work” card drops the music-player controls and lists up to 3 running jobs with time and cost" },
    ],
  },
  {
    version: "0.11.1",
    date: "2026-10-19",
    title: { th: "ชุดรูปตัวละครแบบเปิด/ปิดได้", en: "Switchable character artwork pack" },
    changes: [
      { type: "added", th: "ตั้ง CHARACTER_PACK ใน .env เพื่อใช้ชุดรูปตัวละคร (ฉากกลาง โลโก้ ปก เพื่อนผู้ช่วย การ์ดให้กำลังใจ) ไม่ตั้ง = ใช้มาสคอต Puni — โฟลเดอร์รูปไม่ขึ้น git และอยู่แค่ในเครื่องนี้", en: "Set CHARACTER_PACK in .env to use a character artwork pack (stage, logo, cover, assistant friends, cheer card); unset = the Puni mascot — pack folders are git-ignored and stay on this machine" },
      { type: "fixed", th: "รูปใน frontend/public (เช่น MASCOT_IMAGE) ไม่ขึ้นเมื่อรันด้วย Docker เพราะ image ไม่ได้ copy โฟลเดอร์ public — แก้แล้ว", en: "Images in frontend/public (e.g. MASCOT_IMAGE) did not load under Docker because the image never copied the public folder — fixed" },
    ],
  },
  {
    version: "0.11.0",
    date: "2026-10-19",
    title: { th: "หน้าภาพรวมใหม่จาก kuromi-ai", en: "New dashboard from kuromi-ai" },
    changes: [
      { type: "changed", th: "ย้ายหน้าตาจากโปรเจกต์ kuromi-ai มาเป็นหน้าภาพรวม: รายการงาน, การ์ด “AI กำลังทำงาน” แบบเครื่องเล่นเพลง, เวทีตรงกลางพร้อมกล่องถาม AI, ปฏิทินสัปดาห์, สรุปจาก AI และการ์ดให้กำลังใจ — ทุกการ์ดใช้ข้อมูลจริง", en: "The kuromi-ai mock-up is now the dashboard: task list, an “AI at work” player card, a center stage with an ask-the-AI box, week calendar, AI summary and a cheer card — all on live data" },
      { type: "added", th: "ถาม AI จากหน้าภาพรวมได้: เลือก COO / PM / CTO AI แล้วพิมพ์หรือกดปุ่มลัด (สรุปวันนี้ วางแผนต่อ งานเสี่ยง ไอเดีย) คำตอบขึ้นในกล่องข้อความ และเก็บไว้ในห้องแชท “AI Assistant”", en: "Ask the AI from the dashboard: pick COO / PM / CTO AI, type or use a shortcut (summarize, plan, risks, ideas); the answer appears in the bubble and is kept in the “AI Assistant” chat channel" },
      { type: "added", th: "AI ที่ตอบในแชทเห็นภาพรวมบริษัทแบบสด (ความคืบหน้าโปรเจกต์ งานค้าง งานรออนุมัติ ใครกำลังทำงาน) จึงสรุปจากข้อมูลจริงได้", en: "AI chat answers see a live company snapshot (project progress, open work, pending approvals, who is working), so summaries use real data" },
      { type: "added", th: "ใส่รูปเวทีของตัวเองได้ด้วย STAGE_IMAGE ใน .env (ไฟล์ที่ได้สิทธิ์ใช้งาน วางใน frontend/public)", en: "Use your own stage artwork with STAGE_IMAGE in .env (a file you have the rights to, placed in frontend/public)" },
    ],
  },
  {
    version: "0.10.4",
    date: "2026-10-19",
    title: { th: "ทุกแผนกสั่งงานแบบวางแผนได้", en: "Every department can take planned work" },
    changes: [
      { type: "added", th: "ฝ่ายปฏิบัติการองค์กร: Operations AI ได้สิทธิ์วางแผนงานของแผนก (แผนกนี้ไม่มีตำแหน่งหัวหน้า)", en: "Corporate Operations: Operations AI may now plan the department's objectives (the department has no head position)" },
      { type: "changed", th: "ฝ่ายบริหาร: เมื่อสั่งงานให้ฝ่ายบริหารโดยตรง ผู้บริหาร AI (COO, CTO, CFO, HR) รับงานและตรวจงานเองได้ — งานอื่นยังไม่มอบให้ผู้บริหารเหมือนเดิม", en: "Executive team: on objectives given to the executive department itself, the AI executives do and review the work — other work still never goes to executives" },
      { type: "changed", th: "เมนูสั่งงานแผนกมีตัวเลือกผู้วางแผน “อัตโนมัติ” ระบบเลือกหัวหน้าที่มีสิทธิ์วางแผนให้เอง", en: "The department order menu has an “Automatic” planner: the system picks the head who may plan" },
    ],
  },
  {
    version: "0.10.3",
    date: "2026-10-18",
    title: { th: "งานของแผนกใช้เฉพาะคนในแผนก", en: "Department work stays in the department" },
    changes: [
      { type: "changed", th: "สั่งงานทั้งแผนกจากออฟฟิศ 3D แล้ว ทุกชิ้นงานในแผนจะมอบหมายและตรวจงานโดยพนักงาน AI ของแผนกนั้นเท่านั้น และหัวหน้าได้รับรายชื่อทีมพร้อมทักษะไปใช้วางแผน", en: "Department orders from the 3D Office are done and reviewed only by that department's AI employees; the head plans with the team's names and skills" },
      { type: "changed", th: "ถ้าแผนกไม่มีใครทำชิ้นงานได้ (งบหมด/ยังไม่ตั้ง AI) ระบบแจ้งให้คนตัดสินใจ แทนการส่งให้แผนกอื่น และถ้าไม่มีผู้ตรวจในแผนก งานจะส่งให้คนอนุมัติโดยตรง", en: "If nobody in the department can take a task (budget/AI not set up), people are asked to decide instead of handing it to another department; with no reviewer in the department, work goes straight to human approval" },
      { type: "fixed", th: "เลือกผู้วางแผนที่ไม่มีสิทธิ์วางแผนจะถูกปฏิเสธพร้อมบอกเหตุผลทันที แทนการค้างอยู่ที่สถานะ “วางแผน”", en: "Choosing a planner who may not plan is refused with the reason at once, instead of the objective getting stuck in Planning" },
    ],
  },
  {
    version: "0.10.2",
    date: "2026-10-18",
    title: { th: "สั่งงานจากออฟฟิศ 3D", en: "Give work from the 3D Office" },
    changes: [
      { type: "added", th: "คลิกป้ายชื่อแผนกเพื่อสั่งงานทั้งแผนก: ให้หัวหน้าแผนก (AI) วางแผนแตกงานแล้วรอคุณอนุมัติแผน หรือเลือกมอบให้คนเดียวในแผนก", en: "Click a department's sign to give it work: its AI head plans and splits it (the plan waits for your approval), or hand it to one member" },
      { type: "added", th: "คลิกตัวละครแล้วกด “สั่งงาน”: เลือกโปรเจกต์ พิมพ์งาน เลือกความสำคัญ แล้ว AI เริ่มทำทันที (ปิดได้ถ้าแค่อยากมอบหมาย)", en: "Click a character and “Give work”: pick a project, describe the task and priority, and an AI starts right away (or just assign it)" },
      { type: "security", th: "ใช้สิทธิ์เดิมทุกอย่าง: สร้าง/มอบหมายงานต้องมีสิทธิ์จัดการงาน, สั่งให้ AI เริ่มต้องมีสิทธิ์สั่ง AI, สั่งทั้งแผนกต้องมีสิทธิ์จัดการโปรเจกต์ — คนที่ไม่มีสิทธิ์จะไม่เห็นเมนู", en: "Same permissions as elsewhere: creating/assigning needs task rights, starting AI needs agent.run, department plans need project.manage — people without them don't see the menu" },
    ],
  },
  {
    version: "0.10.1",
    date: "2026-10-18",
    title: { th: "ออฟฟิศ 3D: ทางเดินเชื่อมทุกห้อง + โซนนั่งเล่น", en: "3D Office: connected corridors + a lounge" },
    changes: [
      { type: "added", th: "ทุกห้องเชื่อมกันด้วยทางเดิน พนักงานที่ว่างจะเดินออกจากโต๊ะไปแวะห้องอื่น (💬) หรือไปนั่งพักที่โซนนั่งเล่น (☕) แล้วเดินกลับมา โดยเดินตามทางเดิน ไม่ทะลุกำแพง", en: "Rooms are joined by corridors: free employees walk to other rooms (💬) or take a break in the lounge (☕) and come back, following the corridors rather than through walls" },
      { type: "added", th: "โซนนั่งเล่น: โซฟา 2 ตัว โต๊ะกาแฟ บีนแบ็ก บาร์กาแฟ และชั้นหนังสือ — ที่นั่งไม่ซ้อนกัน", en: "Lounge with two sofas, a coffee table, bean bags, a coffee bar and a bookshelf — no two people share a seat" },
      { type: "changed", th: "ถ้าสถานะจริงเปลี่ยน (เช่นได้รับงาน) ระหว่างเดินเล่นหรือนั่งพัก ตัวละครจะรีบเดินกลับโต๊ะทันทีแล้วเริ่มทำงาน", en: "If the live status changes (e.g. work arrives) during a stroll or a break, the character hurries back to the desk and starts working" },
    ],
  },
  {
    version: "0.10.0",
    date: "2026-10-18",
    title: { th: "ออฟฟิศ 3D", en: "3D Office" },
    changes: [
      { type: "added", th: "เมนูใหม่ “ออฟฟิศ 3D”: ทุกแผนกเป็นห้อง มีโต๊ะ จอ และพนักงานทุกคนเป็นตัวละคร 3D หมุน ซูม และเลื่อนดูได้", en: "New “3D Office” page: each department is a room with desks, screens and every employee as a 3D character; rotate, zoom and pan" },
      { type: "added", th: "ตัวละครขยับตามสถานะจริงทันที: กำลังทำงาน = นั่งพิมพ์และจอเรือง, ว่าง = เดินเล่นรอบโต๊ะ, รออนุมัติ = ยกมือ, ติดขัด/ล้มเหลว/ปิดใช้งาน = มีป้ายบอก", en: "Characters follow live status: working = typing with a glowing screen, available = strolling by the desk, waiting approval = hand raised, blocked/failed/inactive = a status bubble" },
      { type: "added", th: "คลิกตัวละครเพื่อดูชื่อ ตำแหน่ง สถานะ และงานที่กำลังทำ พร้อมลิงก์ไปโปรไฟล์", en: "Click a character for name, role, status and current task, with a link to the profile" },
    ],
  },
  {
    version: "0.9.1",
    date: "2026-10-18",
    title: { th: "สำรองข้อมูลอัตโนมัติ + ทีม AI แก้โค้ดระบบได้", en: "Automatic backups + the AI team can work on this system" },
    changes: [
      { type: "added", th: "สำรองฐานข้อมูลและ repository อัตโนมัติทุกวันบน Windows (เก็บ 14 ชุดล่าสุด) — ทดสอบกู้คืนแล้วข้อมูลกลับมาครบ", en: "Daily automatic backup of the database and repositories on Windows (keeps 14) — a test restore brought everything back" },
      { type: "added", th: "โค้ดของระบบนี้อยู่ในโปรเจกต์ PLATFORM เป็น repository “office-ai” แล้ว สั่งทีม AI ทำงานกับโค้ดระบบได้ด้วย /run หรือหน้างาน แล้วดึงงานที่อนุมัติกลับมาตรวจก่อนรวม", en: "This system's code is now the “office-ai” repository in project PLATFORM: give the AI team work on it with /run or from tasks, then pull approved work back for review before merging" },
      { type: "changed", th: "ข้อความสรุปงานในแชทแสดงสถานะงานและเหตุผลที่หยุดเป็นภาษาไทย", en: "Chat run summaries show the task status and stop reason in Thai" },
      { type: "security", th: "เปลี่ยนรหัสผ่าน CEO จากค่าเริ่มต้นแล้ว (รหัสใหม่เก็บไว้ใน .env ที่ไม่ขึ้น git)", en: "The CEO password was changed from the default (the new one is in .env, which is not in git)" },
    ],
  },
  {
    version: "0.9.0",
    date: "2026-10-18",
    title: { th: "หน้าตาใหม่: ออฟฟิศเวทมนตร์สีพาสเทล", en: "New look: pastel magic office" },
    changes: [
      { type: "changed", th: "ธีมใหม่ตามภาพ newOffice: พื้นหลังม่วงลาเวนเดอร์–ชมพูพร้อมดาว การ์ดแบบกระจกฝ้า มุมโค้งมน ปุ่มสีม่วงพาสเทล ทั้งโหมดสว่างและมืด", en: "New theme after newOffice: lavender-to-pink starry background, frosted-glass cards, rounded corners and pastel purple buttons, in light and dark mode" },
      { type: "changed", th: "แถบด้านบนเป็นแคปซูลลอย มีแท็บลัดไปภาพรวม โปรเจกต์ แชท และการอนุมัติ และเมนูด้านข้างเป็นแผงกระจกลอย", en: "The top bar is a floating pill with quick tabs (dashboard, projects, chat, approvals); the side menu is a floating glass panel" },
      { type: "changed", th: "หน้าภาพรวมจัดเป็นวิดเจ็ต: ภารกิจวันนี้, ค่า AI เดือนนี้, เวทีมาสคอตสีชมพูตรงกลาง, ประกาศล่าสุด, ปฏิทิน และทีมงาน", en: "The dashboard is now widgets: today's missions, AI spend this month, a pink mascot stage in the middle, latest announcements, a calendar and the team" },
    ],
  },
  {
    version: "0.8.0",
    date: "2026-10-18",
    title: { th: "สั่งงาน AI จากแชทด้วย /run", en: "Give AI work from chat with /run" },
    changes: [
      { type: "added", th: "พิมพ์ “@พนักงาน AI /run สิ่งที่ต้องการ” ในห้องแชทของโปรเจกต์ ระบบสร้างงานในโปรเจกต์ มอบหมายให้ AI และเริ่มทำจริงทันที (ใช้ /ทำ ก็ได้)", en: "Write “@AI employee /run what to do” in a project channel: a task is created in the project, assigned to the AI and started right away (/ทำ works too)" },
      { type: "added", th: "AI แจ้งในแชทเมื่อรับงาน และเมื่อเสร็จจะสรุปผล ผลงาน และค่าใช้จ่ายกลับมาในห้องเดิม", en: "The AI confirms in chat when it starts and, when done, posts the outcome, deliverables and cost in the same channel" },
      { type: "security", th: "ใช้เครื่องมือ ขีดจำกัด งบ และการอนุมัติของคนเหมือนสั่งจากหน้างานทุกอย่าง สั่งได้เฉพาะคนที่มีสิทธิ์สร้างงาน มอบหมายงาน และสั่ง AI ทำงาน สั่งได้ทีละ 1 AI ต่อข้อความ และ AI สั่ง AI ไม่ได้", en: "Same tools, limits, budgets and human approvals as a run started from the task page; only people allowed to create and assign tasks and run AI can do it, one AI per message, never AI-to-AI" },
    ],
  },
  {
    version: "0.7.12",
    date: "2026-10-18",
    title: { th: "ไอคอนเปิดระบบบน Desktop + เริ่มเองเมื่อเปิดเครื่อง", en: "Desktop icon + start on sign-in" },
    changes: [
      { type: "added", th: "ไอคอน “Office AI” บน Desktop ของ Windows: ดับเบิลคลิกแล้วเปิด Docker Desktop, เริ่มระบบ, รอจนพร้อม แล้วเปิดหน้าเว็บให้", en: "An “Office AI” Windows desktop icon: double-click to start Docker Desktop and the system, wait until it is ready, then open the web app" },
      { type: "added", th: "ระบบเริ่มเองเมื่อ login เข้า Windows (shortcut ใน Startup) ติดตั้งหรือลบได้ด้วย scripts\\windows\\install-shortcuts.ps1", en: "The system starts on Windows sign-in (Startup shortcut); install or remove with scripts\\windows\\install-shortcuts.ps1" },
    ],
  },
  {
    version: "0.7.11",
    date: "2026-10-18",
    title: { th: "รอโควตา AI อย่างถูกวิธี + Network Engineer", en: "Patient retries on AI rate limits + Network Engineer" },
    changes: [
      { type: "fixed", th: "เมื่อชนโควตาของผู้ให้บริการ AI (เช่น Gemini free tier) ระบบรอตามเวลาที่ผู้ให้บริการบอกแล้วลองใหม่ได้สูงสุด 5 ครั้ง แทนการล้มเหลวทันที", en: "On AI provider rate limits (e.g. Gemini free tier) the system waits as long as the provider asks and retries up to 5 times instead of failing" },
      { type: "fixed", th: "ถ้าโควตาหมดแบบรอนานเกิน 1 นาที (เช่นโควตารายวัน) ระบบหยุดทันทีและบอกเหตุผลจริง แทนการรอจนหมดเวลา และในแชท AI รอไม่เกิน 45 วินาทีแล้วอธิบายเหตุผล", en: "When a quota needs more than a minute (e.g. a daily quota) the system stops at once with the real reason instead of waiting out the time limit; chat replies wait at most 45 seconds, then explain" },
      { type: "changed", th: "บันทึกข้อความ error จริงจากผู้ให้บริการ AI ไว้ใน log และประวัติงาน เพื่อให้รู้สาเหตุ", en: "The provider's actual error message is kept in logs and run history" },
      { type: "added", th: "แผนกออกแบบและการตลาด พร้อมพนักงาน AI 7 ตำแหน่ง และ Network Engineer AI สำหรับดูแล config เซิร์ฟเวอร์ผ่าน repository", en: "Design and Marketing departments with 7 AI roles, and a Network Engineer AI that maintains server configuration through the repository" },
    ],
  },
  {
    version: "0.7.10",
    date: "2026-10-18",
    title: { th: "เลือกโมเดลจากรายการของผู้ให้บริการ", en: "Pick models from the provider's list" },
    changes: [
      { type: "added", th: "ช่อง Model ID ตอนเพิ่มโมเดลแนะนำรายชื่อโมเดลที่ key ใช้ได้จริง (ดึงจากผู้ให้บริการ ไม่เสียค่าใช้จ่าย) ไม่ต้องเดาชื่อ", en: "The Model ID field suggests the models the key can actually use (fetched from the provider at no cost)" },
      { type: "added", th: "ใช้งาน Google Gemini 3.8 Flash (free tier) กับ HR AI, Backend Developer AI และ Documentation AI — ตอบในแชทได้จริงแล้ว", en: "Google Gemini 3.8 Flash (free tier) is in use for HR AI, Backend Developer AI and Documentation AI — real chat answers work" },
    ],
  },
  {
    version: "0.7.9",
    date: "2026-10-18",
    title: { th: "AI ตอบในแชทเมื่อถูก @mention", en: "AI answers in chat when @mentioned" },
    changes: [
      { type: "added", th: "พิมพ์ @ชื่อพนักงาน AI ในแชท แล้ว AI จะตอบในช่องนั้น โดยอ่าน 20 ข้อความล่าสุดเป็นบริบท พร้อมแสดง “กำลังพิมพ์คำตอบ…”", en: "@mention an AI employee in chat and it answers in that channel, using the last 20 messages as context, with an “is writing” indicator" },
      { type: "security", th: "AI ในแชทพูดคุยได้อย่างเดียว ใช้เครื่องมือหรือแก้ข้อมูลไม่ได้ — ตอบเฉพาะเมื่อ “คน” mention (AI ไม่สั่ง AI) สูงสุด 3 ตัวต่อข้อความ", en: "In chat, AI can only talk — no tools or data changes; it answers only when a person mentions it (never AI-to-AI), at most 3 per message" },
      { type: "changed", th: "ค่าใช้จ่ายของคำตอบในแชทนับเป็นค่าใช้จ่ายจริงและอยู่ภายใต้งบเดียวกัน ถ้างบหมดหรือยังไม่ได้ตั้ง key AI จะบอกเหตุผลในแชทแทน", en: "Chat answers are recorded as real costs under the same budgets; if the budget is used up or no key is set, the AI explains in chat instead" },
    ],
  },
  {
    version: "0.7.8",
    date: "2026-10-18",
    title: { th: "ใส่ API key ได้ที่หน้าตั้งค่า AI", en: "API keys on the AI settings page" },
    changes: [
      { type: "added", th: "ใส่ API key ของ Claude / OpenAI / Gemini ได้ที่หน้าตั้งค่า AI มีผลทันทีโดยไม่ต้องแก้ .env หรือรีสตาร์ต (ถ้าไม่ได้ใส่ ระบบใช้ค่าใน .env เหมือนเดิม)", en: "Enter Claude / OpenAI / Gemini API keys on the AI settings page — effective immediately, no .env edit or restart (.env still works as a fallback)" },
      { type: "added", th: "ปุ่มทดสอบการเชื่อมต่อ ตรวจ key กับผู้ให้บริการโดยไม่เสียค่าใช้จ่าย", en: "“Test connection” checks a key with the provider at no cost" },
      { type: "security", th: "Key ถูกเข้ารหัสก่อนเก็บ แสดงแค่ 4 ตัวท้าย ไม่ถูกส่งกลับทาง API และไม่ลง Audit log — เฉพาะคนที่มีสิทธิ์ agent.manage (ไม่ใช่ AI) ตั้งค่าได้", en: "Keys are encrypted at rest, shown only as the last 4 characters, never returned by the API or written to the audit log — only people with agent.manage (never AI) can set them" },
      { type: "fixed", th: "เพิ่มโมเดลใหม่พร้อมราคาได้แล้วที่หน้าตั้งค่า AI (เวอร์ชัน 0.7.5 บอกว่าทำได้ แต่ยังไม่มีปุ่มจริง)", en: "Models can now be added with their prices on the AI settings page (v0.7.5 said so, but the button did not exist)" },
    ],
  },
  {
    version: "0.7.7",
    date: "2026-10-18",
    title: { th: "งาน AI ทำต่อได้หลังเซิร์ฟเวอร์รีสตาร์ต", en: "AI runs resume after a server restart" },
    changes: [
      { type: "changed", th: "ถ้าเซิร์ฟเวอร์รีสตาร์ตระหว่างที่พนักงาน AI ทำงาน งานจะทำต่อจากขั้นล่าสุดที่บันทึกไว้ แทนที่จะล้มเหลวและเริ่มใหม่ (สูงสุด 2 ครั้งต่องาน)", en: "If the server restarts while an AI employee is working, the run continues from its last saved step instead of failing and starting over (at most twice per run)" },
      { type: "fixed", th: "เครื่องมือที่ทำไปแล้วจะไม่ถูกทำซ้ำเมื่อทำต่อ — มีเพียงคำถามถึงโมเดลที่ค้างอยู่เท่านั้นที่ถามใหม่", en: "Tools that already ran are not repeated on resume — only the model call that was in flight is asked again" },
      { type: "added", th: "ประวัติการทำงานของ AI แสดงข้อความเมื่องานถูกทำต่อหลังรีสตาร์ต", en: "The run transcript notes when a run was resumed after a restart" },
    ],
  },
  {
    version: "0.7.6",
    date: "2026-10-18",
    title: { th: "เชื่อม GitHub / GitLab", en: "GitHub / GitLab remotes" },
    changes: [
      { type: "added", th: "เชื่อม repository กับ GitHub / GitLab (หรือ Git host ใดก็ได้ที่เป็น HTTPS) — โค้ดที่คนอนุมัติ merge แล้วถูกส่งขึ้นอัตโนมัติ และกด “ส่งขึ้นตอนนี้” ได้", en: "Connect a repository to GitHub / GitLab (any HTTPS Git host) — approved merges are pushed automatically, or use “Push now”" },
      { type: "added", th: "สร้าง repository โดยนำเข้าจาก GitHub / GitLab ที่มีอยู่แล้ว", en: "Create a repository by importing an existing GitHub / GitLab one" },
      { type: "security", th: "ส่งเฉพาะ branch หลัก ไม่เคย force push, branch งานของ AI ไม่ออกนอกเซิร์ฟเวอร์ และเฉพาะคนที่มีสิทธิ์ security.manage เลือกปลายทางได้", en: "Only the main branch is pushed, never forced; AI task branches stay on the server; only security.manage can choose the destination" },
      { type: "security", th: "Token อ่านจากตัวแปร GIT_REMOTE_* บนเซิร์ฟเวอร์เท่านั้น ไม่อยู่ใน URL, ฐานข้อมูล หรือข้อความ error", en: "Tokens come only from GIT_REMOTE_* server variables — never in URLs, the database or error messages" },
    ],
  },
  {
    version: "0.7.5",
    date: "2026-10-18",
    title: { th: "รองรับ OpenAI และ Google Gemini", en: "OpenAI and Google Gemini support" },
    changes: [
      { type: "added", th: "Provider ใหม่: OpenAI (และ endpoint ที่รองรับ API แบบเดียวกัน) และ Google Gemini — เปิดใช้เมื่อใส่ OPENAI_API_KEY / GEMINI_API_KEY", en: "New providers: OpenAI (and compatible endpoints) and Google Gemini — enabled by OPENAI_API_KEY / GEMINI_API_KEY" },
      { type: "added", th: "ใช้เครื่องมือของพนักงาน AI ได้ครบเหมือน Claude พร้อมนับ token แยกส่วนที่ cache เพื่อคิดราคาให้ถูก", en: "Full tool use like Claude, with cached tokens counted separately so costs are priced correctly" },
      { type: "security", th: "API key ส่งผ่าน header เท่านั้น ไม่อยู่ใน URL หรือ log และไม่เก็บในฐานข้อมูล", en: "API keys travel only in headers — never in URLs, logs or the database" },
    ],
  },
  {
    version: "0.7.4",
    date: "2026-10-18",
    title: { th: "สมาชิกโปรเจกต์ @mention และการแจ้งเตือน", en: "Project members, @mentions and notifications" },
    changes: [
      { type: "security", th: "ช่องแชทโปรเจกต์เห็นได้เฉพาะสมาชิกโปรเจกต์ (เจ้าของ, คนที่ได้รับมอบหมายงาน, คนที่ถูกเพิ่ม) และผู้จัดการ", en: "Project chat channels are visible only to project members (owner, task assignees, people added) and managers" },
      { type: "added", th: "จัดการสมาชิกโปรเจกต์ได้จากปุ่ม “สมาชิก” ในช่องแชทโปรเจกต์", en: "Manage project members from the “Members” button in a project channel" },
      { type: "added", th: "พิมพ์ @ ในแชทเพื่อเลือกคนที่จะกล่าวถึง ชื่อที่ถูกกล่าวถึงจะเป็นตัวเน้นสี", en: "Type @ in chat to mention someone; mentions are highlighted" },
      { type: "added", th: "กระดิ่งแจ้งเตือน: ถูกกล่าวถึง, ถูกเชิญเข้าประชุม, ได้รับสิ่งที่ต้องทำจากการประชุม (อัปเดตทันที เห็นเฉพาะของตัวเอง)", en: "Notification bell: mentions, meeting invites and action items (live, private to each person)" },
      { type: "fixed", th: "แก้ไขผู้เข้าร่วมหรือวาระของการประชุมที่สร้างแล้วไม่ได้ (ขึ้นว่าข้อมูลถูกแก้ไขโดยคนอื่น)", en: "Editing an existing meeting's participants or agenda failed with “changed by someone else”" },
    ],
  },
  {
    version: "0.7.3",
    date: "2026-10-18",
    title: { th: "เข้ารหัสกุญแจ cookie ในฐานข้อมูล", en: "Cookie keys encrypted at rest" },
    changes: [
      { type: "security", th: "กุญแจเข้ารหัส cookie (DataProtection) ถูกเข้ารหัสด้วย certificate ก่อนเก็บลงฐานข้อมูล — บังคับใช้ใน production", en: "Auth-cookie keys (DataProtection) are encrypted with a certificate before they reach the database — required in production" },
      { type: "added", th: "สคริปต์ scripts/new-dataprotection-cert.sh สร้าง certificate และรองรับการเปลี่ยน certificate โดยกุญแจเดิมยังอ่านได้", en: "scripts/new-dataprotection-cert.sh creates the certificate; rotation keeps older keys readable" },
      { type: "security", th: "ระบบไม่ยอมเริ่มถ้าไฟล์ certificate หายหรือรหัสผ่านผิด", en: "The API refuses to start when the certificate is missing or the password is wrong" },
    ],
  },
  {
    version: "0.7.2",
    date: "2026-10-18",
    title: { th: "อนุมัติเพิ่มงบ workflow ได้จริง", en: "Budget escalations raise the workflow budget" },
    changes: [
      { type: "fixed", th: "อนุมัติคำขอ “งบเต็ม” แล้ว workflow ได้งบเพิ่มและทำงานต่อทันที (เดิมอนุมัติแล้วไม่มีผล)", en: "Approving a “budget reached” request now raises the workflow budget and work continues (before, approving did nothing)" },
      { type: "added", th: "กรอกงบใหม่ได้ในหน้าการอนุมัติ (ค่าเริ่มต้น = 2 เท่าของงบเดิม) ต้องมากกว่ายอดที่ใช้ไปแล้ว", en: "Enter the new budget on the approval (default: double the current one); it must exceed what is already spent" },
      { type: "fixed", th: "เมื่อใช้งบใหม่จนเต็มอีกครั้ง ระบบขออนุมัติใหม่ได้ (เดิมขอได้ครั้งเดียว)", en: "Reaching the raised budget asks again (before, it could only ask once)" },
    ],
  },
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
  { status: "waitingCeo", area: { th: "AI", en: "AI" }, th: "เลือกแผนแบบเสียเงิน: Gemini free tier ใช้ได้แค่ 20 ครั้ง/วัน ไม่พอทำงานจริง — เติมเครดิต Anthropic หรือเปิด billing ของ Gemini (ทั้งคู่ต้องผูกบัตร)", en: "Choose a paid plan: the Gemini free tier allows only 20 requests/day — add Anthropic credit or enable Gemini billing (both need a card)" },
  { status: "waitingCeo", area: { th: "AI", en: "AI" }, th: "ถ้ามีบัญชี OpenAI / Gemini: ใส่ key และเพิ่มโมเดลพร้อมราคาในหน้าตั้งค่า AI", en: "If OpenAI / Gemini accounts exist: set the keys and add their models with prices in AI settings" },
  { status: "waitingCeo", area: { th: "การเงิน", en: "Finance" }, th: "กำหนดงบ AI รายเดือนสำหรับช่วงทดลอง (ตอนนี้ USD 5 ต่อพนักงาน AI)", en: "Set the monthly AI budget for the trial (currently USD 5 per AI employee)" },
  { status: "waitingCeo", area: { th: "Deploy", en: "Deploy" }, th: "เลือกที่ deploy ระบบจริง", en: "Choose where to deploy" },
  { status: "planned", area: { th: "AI", en: "AI" }, th: "1 ม.ค. 2027: แก้ราคา Gemini 3.8 Flash ในหน้าตั้งค่า AI เป็น $1.50 / $7.50 ต่อล้าน token (ราคาโปรหมด)", en: "1 Jan 2027: update Gemini 3.8 Flash prices in AI settings to $1.50 / $7.50 per million tokens (intro pricing ends)" },
];

export const LATEST_VERSION = CHANGELOG[0].version;

/** localStorage key holding the last version this browser opened on /updates. */
export const SEEN_KEY = "updates.seenVersion";
