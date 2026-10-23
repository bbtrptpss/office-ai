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
    version: "0.29.0",
    date: "2026-10-23",
    title: { th: "ต้นทุนดำเนินงาน (ค่าไฟ แพ็กเกจ AI server)", en: "Operating costs (electricity, AI plans, servers)" },
    changes: [
      { type: "added", th: "หน้าการเงินมี “ต้นทุนดำเนินงาน”: ใส่ค่าใช้จ่ายรายเดือนที่ไม่ใช่ค่า AI ต่อ token (ค่าไฟ แพ็กเกจ AI แบบเหมาจ่าย server ฮาร์ดแวร์ ซอฟต์แวร์) เป็นบาทหรือดอลลาร์ มีเดือนเริ่ม/สิ้นสุด และติดป้าย “ค่าประมาณ” ได้ (เช่นค่าไฟที่คำนวณจากวัตต์) — แก้ปัญหาที่ Ollama / Claude Code ราคา 0 ทำให้รายงานดูถูกเกินจริง", en: "Finance has “Operating costs”: monthly costs that are not per-token AI charges (electricity, flat-rate AI plans, servers, hardware, software), in THB or USD, with first/last month and an “estimate” flag (e.g. electricity worked out from watts) — so zero-priced Ollama / Claude Code no longer make reports look cheaper than they are" },
      { type: "added", th: "ปันส่วนได้ 3 แบบ: ลงโปรเจกต์เดียว, ตามสัดส่วน token ที่แต่ละโปรเจกต์ใช้ในเดือนนั้น (เลือกเฉพาะผู้ให้บริการได้ เช่นค่าไฟ GPU ตามการใช้ ollama) หรือค่าใช้จ่ายส่วนกลาง ยอดแต่ละเดือนรวมกันได้ตรงทุกสตางค์ เดือนที่ไม่มีการใช้จะแสดงว่ายังไม่ได้ปันส่วน", en: "Three allocations: one project, by the AI tokens each project used that month (optionally one provider, e.g. GPU electricity by ollama use), or company overhead. Each month's shares add up to the satang; months with no use show as unallocated" },
      { type: "changed", th: "รายงานการเงินแสดงต้นทุนดำเนินงานเดือนนี้ ต้นทุนรวม (AI + ดำเนินงาน) และคอลัมน์ต่อโปรเจกต์ กำไรคาดการณ์และรายงานปิดโปรเจกต์บวกต้นทุนดำเนินงานที่ปันส่วนมาด้วย ส่วนงบ AI ยังนับเฉพาะค่า AI จึงไม่ทำให้งาน AI หยุด", en: "The finance report shows this month's operating costs, the total (AI + operating) and a per-project column; the profitability forecast and the completion report add the allocated operating costs. AI budgets still count AI charges only, so these never stop AI work" },
    ],
  },
  {
    version: "0.28.0",
    date: "2026-10-23",
    title: { th: "การยืนยันสองขั้น และ Terminal ที่ต้องใส่รหัส", en: "Two-factor sign-in and a terminal behind a code" },
    changes: [
      { type: "security", th: "การยืนยันสองขั้น (2FA) ด้วยแอป Authenticator: เปิดได้ที่ “การตั้งค่าของฉัน” (สแกน QR แล้วยืนยันด้วยรหัส 6 หลัก) ได้รหัสกู้คืน 10 รหัสที่แสดงครั้งเดียว เมื่อเปิดแล้วการเข้าสู่ระบบต้องใส่รหัสจากแอป (หรือรหัสกู้คืน) หลังรหัสผ่าน รหัสผิดนับรวมกับการล็อกบัญชี 5 ครั้ง ปิดได้ด้วยรหัสผ่าน + รหัสจากแอป", en: "Two-factor sign-in (2FA) with an authenticator app: turn it on in “My settings” (scan the QR code, confirm with a 6-digit code) and get 10 recovery codes shown once. Once on, signing in needs a code from the app (or a recovery code) after the password; wrong codes count towards the 5-attempt lockout. Turning it off needs the password and a code" },
      { type: "security", th: "Terminal บนหน้าภาพรวมใช้ได้เฉพาะ CEO ที่เปิด 2FA แล้ว และต้องพิมพ์รหัส 6 หลักจากแอปเพื่อเปิด เปิดค้างได้ 15 นาทีนับจากคำสั่งล่าสุด และล็อกทันทีเมื่อออกจากระบบ เปลี่ยนรหัสผ่าน ปิด 2FA หรือรีสตาร์ตระบบ — คำสั่งที่พิมพ์ไว้ตอนล็อกจะรันต่อเองหลังใส่รหัสถูก", en: "The overview Terminal is for a CEO with 2FA on, and opens only with a 6-digit code from the app. It stays open 15 minutes after the last command and locks at once on sign-out, a password change, turning 2FA off or a restart — the command typed while locked runs once the code is accepted" },
      { type: "added", th: "บันทึกการตรวจสอบมีรายการเปิด/ปิด 2FA สร้างรหัสกู้คืนใหม่ เปิด Terminal และใส่รหัส Terminal ผิด (ไม่เคยบันทึกกุญแจหรือรหัสกู้คืน)", en: "The audit log records 2FA on/off, new recovery codes, terminal unlocks and wrong terminal codes (never the key or the recovery codes)" },
    ],
  },
  {
    version: "0.27.0",
    date: "2026-10-23",
    title: { th: "โมเดลสำรองเมื่อ AI ตอบไม่ได้", en: "Fallback models when an AI cannot answer" },
    changes: [
      { type: "added", th: "หน้าตั้งค่า AI มี “ลำดับโมเดลสำรอง” (สูงสุด 10 ตัว เลื่อนขึ้น/ลง/เอาออกได้): ถ้าโมเดลของพนักงาน AI ตอบไม่ได้ เพราะผู้ให้บริการล่มหลังลองซ้ำแล้ว โควตาหมด (เช่น Gemini free tier 20 ครั้ง/วัน) หรือ key/เครดิตใช้ไม่ได้ งานจะไปต่อกับโมเดลถัดไปในบทสนทนาเดิม ไม่เริ่มใหม่ และบันทึกในประวัติการทำงานว่าสลับจากตัวไหนไปตัวไหนเพราะอะไร", en: "AI settings has “Fallback models” (up to 10, reorder or remove): when an AI employee's model cannot answer — provider down after retries, quota used up (e.g. Gemini's 20 free requests a day), or key/credit unusable — the run carries on with the next model in the same conversation instead of starting over, and the transcript notes which model it switched from and to, and why" },
      { type: "added", th: "แชทกับ AI ใช้ลำดับโมเดลสำรองเดียวกัน และพนักงานที่ยังไม่ได้ตั้งผู้ให้บริการของตัวเองก็รับงานผ่านโมเดลสำรองได้ (ระบบมอบหมายงานนับว่าพร้อมทำงาน)", en: "Chat answers use the same fallback order, and an employee whose own provider is not set up can still take work on a fallback model (the workflow engine counts it as available)" },
      { type: "changed", th: "โมเดลที่เพิ่งล้มจะถูกข้ามไปชั่วคราว (โควตารายวันหรือปัญหา key 1 ชั่วโมง ล่มชั่วคราว 2 นาที) งานถัดไปเริ่มที่โมเดลสำรองทันทีโดยไม่เสียเวลาลองตัวเดิม ค่าใช้จ่ายแต่ละรอบคิดตามราคาโมเดลที่ตอบจริง ส่วนคำขอที่ผิดเอง (เช่นยาวเกิน) ไม่สลับโมเดล", en: "A model that just failed is skipped for a while (1 hour for a daily quota or key problem, 2 minutes for an outage), so the next run starts on the fallback right away; each turn is priced on the model that answered it. Faulty requests (e.g. too long) do not switch models" },
      { type: "fixed", th: "Gemini รับบทสนทนาที่โมเดลอื่นเคยเรียกเครื่องมือไว้แล้วได้ (ใส่ thought signature แทนให้) จึงรับช่วงงานกลางทางได้", en: "Gemini accepts a conversation in which another model already called tools (a placeholder thought signature is sent), so it can take over a run midway" },
    ],
  },
  {
    version: "0.26.2",
    date: "2026-10-23",
    title: { th: "อัปเดตแพ็กเกจที่มีช่องโหว่", en: "Patched a vulnerable package" },
    changes: [
      { type: "security", th: "อัปเดต System.Security.Cryptography.Pkcs (ที่ MailKit ใช้ส่งอีเมล) จาก 10.0.0 เป็น 10.0.13 ปิดช่องโหว่ระดับสูง GHSA-846f-f764-76mx และระดับกลาง GHSA-2fmv-rv2c-59x7", en: "Updated System.Security.Cryptography.Pkcs (used by MailKit for email) from 10.0.0 to 10.0.13, closing the high-severity GHSA-846f-f764-76mx and moderate GHSA-2fmv-rv2c-59x7" },
    ],
  },
  {
    version: "0.26.1",
    date: "2026-10-23",
    title: { th: "แชทแสดง Markdown และแก้หน้าตั้งค่า AI", en: "Markdown in chat and AI settings fixes" },
    changes: [
      { type: "changed", th: "ข้อความในแชทแสดงหัวข้อ (#, ##, ###), รายการ (-), ตัวหนา (**…**) และ `code` เป็นรูปแบบจริง แทนเครื่องหมายดิบ — เช่นรายงานประจำวันและคำตอบของ AI", en: "Chat messages show headings (#, ##, ###), bullets (-), bold (**…**) and `code` formatted instead of raw marks — e.g. the daily report and AI replies" },
      { type: "fixed", th: "รายงานประจำวันแสดงสถานะงานที่ยังเปิดอยู่เป็นภาษาไทยแบบกระดานงาน (เช่น “รอตรวจ”) แทนชื่อภาษาอังกฤษ", en: "The daily report names open-task statuses as the task board does (e.g. “รอตรวจ”) instead of the English names" },
      { type: "fixed", th: "การ์ด ollama และ claude-code ในหน้าตั้งค่า AI ไม่ถาม API key แล้ว แสดงว่าไม่ต้องใช้ key และทดสอบผ่านจะขึ้น “เชื่อมต่อได้”", en: "The ollama and claude-code cards on AI settings no longer ask for an API key; they say none is needed and a passing test shows “Connected”" },
    ],
  },
  {
    version: "0.26.0",
    date: "2026-10-22",
    title: { th: "AI ฟรีบนเครื่อง (Ollama, Claude Code) และรายงานประจำวัน", en: "Free AI on this PC (Ollama, Claude Code) and the daily report" },
    changes: [
      { type: "added", th: "ผู้ให้บริการ AI “ollama”: ใช้โมเดลที่รันบนเครื่องนี้ผ่าน Ollama ไม่ต้องมี key ไม่เสียค่า token (ปิดได้โดยตั้ง OLLAMA_URL ให้ว่าง)", en: "“ollama” AI provider: models running on this PC through Ollama, no key, no token cost (empty OLLAMA_URL turns it off)" },
      { type: "added", th: "ผู้ให้บริการ AI “claude-code”: พนักงาน AI ใช้ Claude Code ที่ล็อกอินไว้บนเครื่องนี้ (คิดเงินตามแพ็กเกจ Claude ไม่ใช่ตาม token) เลือกโมเดลได้", en: "“claude-code” AI provider: AI employees use the Claude Code signed in on this PC (billed to the Claude plan, not per token), with a choice of model" },
      { type: "changed", th: "โมเดลของผู้ให้บริการที่ไม่คิดเงินตาม token (Ollama, Claude Code) ตั้งราคาเป็น 0 ได้", en: "Models of providers not billed per token (Ollama, Claude Code) may have zero prices" },
      { type: "added", th: "รายงานประจำวัน: หลังเที่ยงคืน (เวลาไทย) สรุปงานที่ AI ทำเมื่อวาน — งานเสร็จ ผลงาน จำนวนรัน/ล้มเหลว คำขออนุมัติที่รอ และค่าใช้จ่าย — โพสต์ในแชทบริษัทพร้อมแจ้งเตือน (GET /api/reports/daily)", en: "Daily report: after midnight (Bangkok time) a summary of yesterday's AI work — tasks done, deliverables, runs/failures, pending approvals and cost — is posted to the company chat with a notification (GET /api/reports/daily)" },
      { type: "changed", th: "AI ตอบแชทโดยอ้างอิงคลังความรู้ที่ผ่านการตรวจแล้ว และค้นหาความรู้ภาษาไทยด้วยแท็กได้ (เช่น “ลาป่วยได้กี่วัน” เจอหัวข้อที่แท็ก “ลาป่วย”)", en: "Chat answers draw on validated company knowledge, and Thai questions match knowledge by tag (“ลาป่วยได้กี่วัน” finds the entry tagged “ลาป่วย”)" },
      { type: "fixed", th: "พนักงาน AI จบรอบทันทีหลังส่งต่องาน (มอบหมาย ขอรีวิว แจ้งติดปัญหา ส่งแผน/รีวิว) และขอรีวิวไม่ได้ขณะงานย่อยที่มอบหมายไว้ยังไม่เสร็จ — โมเดลเล็กเคยทำงานต่อเองหรือมอบหมายซ้ำ", en: "An AI employee's run ends as soon as it hands the task on (delegate, request review, report blocked, submit a plan or review), and it cannot request review while delegated sub-tasks are open — small models used to carry on or delegate twice" },
    ],
  },
  {
    version: "0.25.0",
    date: "2026-10-20",
    title: { th: "แก้ไขและยกเลิกโปรเจกต์", en: "Edit and cancel projects" },
    changes: [
      { type: "added", th: "ปุ่ม “แก้ไขโปรเจกต์” ในหน้าโปรเจกต์ (ผู้มีสิทธิ์ project.manage): แก้รหัส ชื่อ คำอธิบาย สถานะ งบประมาณ และวันที่ได้", en: "“Edit project” button on the project page (project.manage): change code, name, description, status, budget and dates" },
      { type: "added", th: "เปลี่ยนสถานะเป็น “ยกเลิก” จะถามยืนยันก่อน แล้วยกเลิกงานที่ยังไม่เสร็จทั้งหมดในโปรเจกต์ หยุด AI ที่กำลังทำงานในโปรเจกต์ และยกเลิกคำขออนุมัติที่รออยู่ งานที่เสร็จแล้วยังอยู่เหมือนเดิม", en: "Switching to Cancelled asks first, then cancels every unfinished task in the project, stops AI runs in it and withdraws pending approvals; finished tasks stay" },
    ],
  },
  {
    version: "0.24.7",
    date: "2026-10-20",
    title: { th: "สถานะ “ล้มเหลว” ค้าง", en: "Stuck “failed” status" },
    changes: [
      { type: "fixed", th: "พนักงาน AI ที่งานล่าสุดล้มเหลว จะกลับเป็น “ว่าง” เองเมื่องานนั้นถูกยกเลิกหรือเสร็จ หรือโปรเจกต์ของงานถูกยกเลิก/ปิด (ตรวจทุก 30 วินาทีและตอนเปิดเซิร์ฟเวอร์) เดิมจะค้าง “ล้มเหลว” จนกว่าจะได้รันงานใหม่", en: "An AI employee whose last run failed goes back to Available once that task is cancelled or done, or its project cancelled or completed (checked every 30 seconds and at start-up); before, Failed stayed until their next run" },
    ],
  },
  {
    version: "0.24.6",
    date: "2026-10-20",
    title: { th: "ออฟฟิศ 3D ลานน้ำพุกลางหมู่บ้าน", en: "3D office village square with a fountain" },
    changes: [
      { type: "changed", th: "ห้องทุกห้องเรียงเป็นวงกลมรอบถนนวงแหวนปูหิน ประตูทุกห้องหันเข้าหาศูนย์กลาง กลางวงเป็นลานหินมีน้ำพุเวทมนตร์ (น้ำเรืองแสง คริสตัลลอยหมุนบนยอด) ทางเดิน 4 ทางจากถนนเข้าลาน และแปลงเห็ด/คริสตัล", en: "Rooms stand in a circle round a cobbled ring road with every gate facing the middle: a paved plaza with a magic fountain (glowing water, a floating crystal on top), four paths in from the road, and mushroom / crystal beds" },
      { type: "changed", th: "ตัวละครเดินออกประตู ไปตามถนนวงแหวนทางที่ใกล้กว่า แล้วเข้าประตูห้องอื่น มีเสาไฟสองข้างถนนระหว่างห้องและโคมลอยหน้าประตูทุกห้อง กำแพงด้านที่หันเข้ากล้องของแต่ละห้องเตี้ยลงตามมุมห้อง กล้องเริ่มต้นเห็นทั้งหมู่บ้าน", en: "Characters leave through their gate, take the shorter way round the ring road and enter the other gate; lamp posts line the road between rooms and a lantern floats before every gate; each room's walls facing the camera stay low whatever its angle, and the starting view shows the whole village" },
    ],
  },
  {
    version: "0.24.5",
    date: "2026-10-20",
    title: { th: "ตัวละครออฟฟิศ 3D ตามตำแหน่ง", en: "3D office figures by position" },
    changes: [
      { type: "changed", th: "หัวหน้าแผนกในออฟฟิศ 3D เป็นรูป kuromi_3d.png ส่วนคนอื่นสุ่มจาก mymelody_3d.png, pompompurin_3d.png, cinnamoroll_3d.png (แต่ละคนได้ตัวเดิมเสมอ)", en: "Department heads in the 3D office use kuromi_3d.png; everyone else gets one of mymelody_3d.png, pompompurin_3d.png, cinnamoroll_3d.png (each person always the same one)" },
      { type: "changed", th: "หัวหน้าแผนก = คนที่ตำแหน่งสูงสุดระดับ Head ขึ้นไปในแผนก ถ้าเท่ากันเลือกคนที่รายงานตรงต่อคนนอกแผนก (ฝ่ายบริหาร = CEO) แผนกที่มีแต่พนักงานระดับ Staff จะไม่มีหัวหน้า", en: "A department head is its most senior Head-or-above person, and among equals the one reporting outside the department (Executive = the CEO); a department of staff has no head" },
    ],
  },
  {
    version: "0.24.4",
    date: "2026-10-20",
    title: { th: "แก้ error หน้าโปรเจกต์", en: "Project page error fixed" },
    changes: [
      { type: "fixed", th: "หน้าโปรเจกต์ไม่ขึ้น error “<div> cannot be a descendant of <p>” อีก (คำอธิบายใต้หัวข้อหน้าที่มีชิปสถานะ ตอนนี้ไม่ได้อยู่ในย่อหน้าแล้ว)", en: "The project page no longer logs “<div> cannot be a descendant of <p>” (page header subtitles holding status chips are no longer inside a paragraph)" },
    ],
  },
  {
    version: "0.24.3",
    date: "2026-10-20",
    title: { th: "ออฟฟิศ 3D แบบถนนหมู่บ้านแฟนตาซี", en: "3D office as a fantasy village street" },
    changes: [
      { type: "changed", th: "ห้องแต่ละแผนกเป็นอาคารแยกกัน มีกำแพงล้อมรอบทุกด้านและสนามหญ้าคั่น ตั้งเป็นแถวสองฝั่งถนนปูหิน ทุกห้องมีประตูซุ้มโค้งหันออกถนน โต๊ะทำงานหันไปทางประตู", en: "Each department is its own walled building with grass in between, in rows on both sides of a cobbled street; every room has an arched gate onto the street and its desks face the gate" },
      { type: "changed", th: "ตัวละครเดินออกทางประตู ไปตามถนน แล้วเข้าประตูห้องอื่น ถ้ามีหลายถนนจะเดินผ่านถนนข้างทางซ้าย มีเสาไฟและโคมลอยเหนือถนน", en: "Characters leave through their gate, walk along the street and enter the other room's gate (a side street joins several streets); lamp posts and floating lanterns line the streets" },
    ],
  },
  {
    version: "0.24.2",
    date: "2026-10-20",
    title: { th: "แก้ error ในหน้าออฟฟิศ 3D", en: "3D office console errors fixed" },
    changes: [
      { type: "fixed", th: "หน้าออฟฟิศไม่ขึ้น error “Attempted to synchronously unmount a root” อีก (ป้ายชื่อห้องและบับเบิลสถานะใช้ตัววางป้ายของเราเองแทน Html ของ drei ที่ปิดตัวเองผิดจังหวะ)", en: "The office page no longer logs “Attempted to synchronously unmount a root” (room signs and status bubbles use our own pin instead of drei's Html, which unmounted at the wrong moment)" },
      { type: "fixed", th: "เลิกใช้เงาแบบ PCFSoft ที่ three.js ถอดออกแล้ว ไม่ขึ้นคำเตือนอีก", en: "Shadows use PCF directly instead of the removed PCFSoft type, so three.js no longer warns" },
    ],
  },
  {
    version: "0.24.1",
    date: "2026-10-20",
    title: { th: "ออฟฟิศ 3D โลกแฟนตาซี", en: "Fantasy 3D office" },
    changes: [
      { type: "changed", th: "ออฟฟิศ 3D กลายเป็นเกาะลอยฟ้าในโลกแฟนตาซี: ท้องฟ้ายามสนธยาพร้อมดาวและดวงจันทร์ ก้อนเมฆ เกาะเล็กลอยรอบ ๆ ต้นไม้สีพาสเทล เห็ดเรืองแสง คริสตัล หิ่งห้อย และโคมลอยเหนือทางเดิน", en: "The 3D office is now a floating island in a fantasy world: twilight sky with stars and a moon, clouds, small drifting islets, pastel trees, glowing mushrooms, crystals, fireflies and lanterns over the corridors" },
      { type: "changed", th: "แต่ละห้องเป็นหอกิลด์: พื้นไม้ กำแพงหินมีใบเสมา หอคอยหลังคาแหลมที่มุมห้อง และธงประจำห้อง โต๊ะทำงานเป็นโต๊ะนักเวทย์ มีกระจกวิเศษแทนจอ (เรืองแสงเมื่อกำลังทำงาน) เทียน และคริสตัลลอย", en: "Each room is a guild hall: plank floor, stone walls with battlements, a spired tower in the corner and a room banner; desks are mage desks with a scrying mirror for a screen (glowing while working), a candle and a floating crystal" },
      { type: "changed", th: "ห้องพักผ่อนเป็นโรงเตี๊ยมเวทมนตร์: หม้อยาเดือดปุด ๆ บนวงเวทย์ที่หมุนช้า ๆ ม้านั่งไม้ เก้าอี้เห็ด และบาร์ยาวิเศษ", en: "The lounge is a magic tavern: a bubbling cauldron on a slowly turning magic circle, wooden benches, mushroom stools and a potion bar" },
    ],
  },
  {
    version: "0.24.0",
    date: "2026-10-20",
    title: { th: "อีเมลแจ้งเตือนและปฏิทิน", en: "Email notifications and calendar" },
    changes: [
      { type: "added", th: "หน้า “การตั้งค่าของฉัน” ใหม่: แต่ละคนเปิดรับสำเนาการแจ้งเตือนทางอีเมลได้เอง (ถูก @ ถึง เชิญประชุม งานจากที่ประชุม) ส่งเฉพาะที่เกิดหลังเปิดใช้ พร้อมปุ่มส่งอีเมลทดสอบและรายการอีเมลล่าสุด", en: "New “My settings” page: each person can opt in to email copies of their notifications (mentions, meeting invites, action items); only what happens after opting in is sent; test email button and recent emails" },
      { type: "added", th: "อีเมลเชิญประชุมแนบไฟล์ปฏิทิน (.ics) ให้กด “เพิ่มลงปฏิทิน” ได้จาก Gmail / Outlook", en: "Meeting invite emails carry a calendar invitation (.ics) that Gmail / Outlook can add to the calendar" },
      { type: "added", th: "ลิงก์ปฏิทินส่วนตัวสำหรับ subscribe ใน Google Calendar / Outlook / Apple Calendar: การประชุมของฉันและวันกำหนดส่งของงานที่ฉันรับผิดชอบ สร้างใหม่หรือปิดลิงก์ได้ทุกเมื่อ", en: "Private calendar link to subscribe to in Google Calendar / Outlook / Apple Calendar: my meetings and the due dates of my tasks; create a new link or turn it off at any time" },
      { type: "added", th: "ปุ่ม “เพิ่มลงปฏิทิน (.ics)” ในหน้าการประชุม", en: "“Add to calendar (.ics)” button on a meeting" },
      { type: "security", th: "ระบบไม่ส่งอีเมลใดๆ จนกว่าผู้ดูแลจะตั้งบัญชี SMTP ใน .env; รหัสผ่านอยู่ใน environment เท่านั้น ลิงก์ปฏิทินเก็บแค่ค่า hash และแสดงลิงก์ครั้งเดียว", en: "Nothing is emailed until an administrator sets an SMTP account in .env; the password lives only in the environment; calendar links are stored as hashes and shown once" },
      { type: "fixed", th: "เซิร์ฟเวอร์ที่ตั้งภาษาไทยเขียนปี พ.ศ. (2569) ลงในวันที่ของเอกสาร บันทึกการประชุม และข้อความถึง AI ตอนนี้ใช้รูปแบบสากลเสมอ", en: "A Thai-culture server wrote Buddhist-era years (2569) into documents, meeting minutes and AI prompts; server text now always uses invariant formats" },
    ],
  },
  {
    version: "0.23.1",
    date: "2026-10-20",
    title: { th: "ปุ่ม “ลองใหม่” ให้ AI ที่ทำงานล้มเหลว", en: "“Try again” for failed AI runs" },
    changes: [
      { type: "added", th: "ปุ่ม “ลองใหม่” บนรันที่ล้มเหลว ชนเพดาน หรือถูกหยุด (ในหน้าต่างรายละเอียดรัน และข้างปุ่ม “ให้ AI ทำงานนี้” ของงาน) สั่ง AI คนเดิมทำใหม่ทันที พร้อมเขียนคำแนะนำถึง AI ได้", en: "“Try again” on failed, limit-reached or stopped runs (in the run drawer and next to “Run with AI” on the task) runs the same AI employee again, with an optional note for it" },
      { type: "changed", th: "ถ้างานติดขัดอยู่ ระบบปลดให้เอง ถ้ามีคำขอ “ต้องตัดสินใจ” ค้างในกล่องอนุมัติ จะนับเป็นการกด “ลองใหม่” และ workflow สั่ง AI ทำต่อในรอบถัดไป", en: "A blocked task is unblocked; a pending “Needs a decision” request is answered as “try again” and the workflow starts the AI on its next round" },
    ],
  },
  {
    version: "0.23.0",
    date: "2026-10-19",
    title: { th: "AI ตรวจ lint / static analysis และหารหัสลับในโค้ดก่อนเปิด merge request", en: "AI lint / static analysis and secret scanning before merge requests" },
    changes: [
      { type: "added", th: "ตั้ง “คำสั่ง lint / static analysis” ต่อ repository ได้ (เช่น eslint + prettier --check หรือ dotnet format --verify-no-changes) ผ่านปุ่ม “แก้คำสั่งตรวจ” ในแท็บ Repository คนเท่านั้นที่ตั้งได้", en: "Set a “lint / static analysis command” per repository (e.g. eslint + prettier --check, or dotnet format --verify-no-changes) from “Edit checks” on the Repository tab; only people can set it" },
      { type: "added", th: "เครื่องมือ AI `sandbox.run_lint` รันคำสั่งนั้นใน sandbox แบบไม่มี network และเก็บผลเป็นหลักฐานคู่กับผลเทสต์ (รายการผลตรวจแสดงว่าเป็นเทสต์หรือ lint) คนกด “รัน lint” เองได้ด้วย", en: "AI tool `sandbox.run_lint` runs it in the network-less sandbox and keeps the result as evidence next to test runs (the list shows tests or lint); people can “Run lint” too" },
      { type: "security", th: "เครื่องมือ AI `repo.scan_secrets` หา API key, token, private key และรหัสผ่านที่เขียนลงโค้ดในส่วนที่งานนั้นเพิ่ม รายงานแค่ไฟล์ บรรทัด และชนิด ไม่แสดงค่าจริง", en: "AI tool `repo.scan_secrets` finds API keys, tokens, private keys and hard-coded passwords in the lines a task added, reporting file, line and kind but never the value" },
      { type: "changed", th: "เปิด merge request ได้เมื่อ commit ล่าสุดผ่านเทสต์ ผ่าน lint (ถ้าตั้งคำสั่งไว้) และไม่มีรหัสลับใน commit; รายงานคุณภาพ ขั้นตอนส่งมอบ และความพร้อม release นับเฉพาะผลเทสต์", en: "Merge requests need the latest commit to pass tests, pass lint (when set) and contain no credentials; quality reports, delivery stages and release readiness count test runs only" },
    ],
  },
  {
    version: "0.22.0",
    date: "2026-10-19",
    title: { th: "ชุดส่งมอบลูกค้า และบันทึกการรับงาน", en: "Customer delivery package and acceptance" },
    changes: [
      { type: "added", th: "แท็บ “ส่งมอบลูกค้า” ในหน้าโปรเจกต์: รวม release เอกสารฉบับที่ส่ง (ล็อกตามเวอร์ชัน) ผลเทสต์ และบันทึกการส่งมอบเป็นชุด (รหัส PKG-001…)", en: "Project “Customer delivery” tab: bundle a release, the exact document versions handed over, the test evidence and handover notes into a package (codes PKG-001…)" },
      { type: "added", th: "ตรวจความพร้อมก่อนส่ง: release ต้องขึ้น production แล้ว เทสต์ของ release ผ่าน มีรายงานปิดโปรเจกต์และบันทึกการส่งมอบ; เอกสารฉบับร่างและข้อบกพร่องค้างแสดงเป็นคำเตือน", en: "Readiness before delivery: release in production, its tests passed, completion report and handover notes included; draft documents and open defects show as warnings" },
      { type: "added", th: "ดาวน์โหลดชุดส่งมอบเป็นไฟล์ Markdown ไฟล์เดียว (รายการเอกสาร บันทึกส่งมอบ release notes ขั้นตอน deploy/rollback ผลเทสต์ และเนื้อหาเอกสารทั้งหมด)", en: "Download the package as one Markdown file (contents, handover, release notes, deployment/rollback, test evidence and every document's text)" },
      { type: "added", th: "คนบันทึกว่าส่งแล้ว และลูกค้ารับงานหรือขอแก้ ถ้าขอแก้ระบบสร้างงานความสำคัญสูงพร้อมรายละเอียดให้อัตโนมัติ (AI บันทึกขั้นตอนนี้ไม่ได้)", en: "People record the delivery and whether the customer accepted or asked for changes; a change request creates a High-priority task with the details (AI cannot record these steps)" },
      { type: "changed", th: "ขั้นตอนส่งมอบขั้น 8 เพิ่มเกณฑ์ “ส่งชุดส่งมอบให้ลูกค้าแล้ว” และ “ลูกค้ารับงานแล้ว”", en: "Delivery stage 8 adds “delivery package sent” and “customer accepted the delivery”" },
    ],
  },
  {
    version: "0.21.0",
    date: "2026-10-19",
    title: { th: "รับงานลูกค้า: ตรวจข้อมูลที่ขาดและถามกลับก่อนเปิดโปรเจกต์", en: "Customer intake: find what is missing and ask before opening a project" },
    changes: [
      { type: "added", th: "หน้า “รับงานลูกค้า” ใหม่: บันทึกคำขอพร้อมลูกค้า ผู้ติดต่อ ความต้องการ เป้าหมาย ผู้ใช้ แพลตฟอร์ม ข้อจำกัด งบ และวันที่ต้องการ (รหัส LEAD-001…)", en: "New “Customer intake” page: record a request with customer, contact, need, goal, users, platforms, constraints, budget and desired date (codes LEAD-001…)" },
      { type: "added", th: "ระบบบอกว่ายังขาดข้อมูลอะไร และร่างคำถามถามกลับให้ทีละเรื่องเป็นภาษาที่เลือก ผู้ดูแลส่งคำถามให้ลูกค้าเองแล้วนำคำตอบกลับมาบันทึก (ระบบไม่ติดต่อลูกค้าเอง)", en: "The system lists what is still missing and drafts one question per gap in the chosen language; a person relays them and records the customer's answers (the system never contacts the customer)" },
      { type: "added", th: "ยืนยันว่าข้อมูลครบได้เมื่อข้อมูลจำเป็นครบและทุกคำถามได้คำตอบ แล้วเปิดเป็นโปรเจกต์สถานะร่างได้ในคลิกเดียว พร้อมงบ กำหนดส่ง และเอกสาร Project brief ที่รวมคำขอและคำตอบทั้งหมด หรือปิดว่าไม่ได้งานพร้อมเหตุผล", en: "Confirm it is complete once the essentials are known and every question is answered, then open it as a Draft project in one step with budget, due date and a Project brief holding the request and all answers; or close it as lost with a reason" },
      { type: "changed", th: "ขั้นตอนส่งมอบขั้น 1 เพิ่มเกณฑ์ “คำถามถึงลูกค้าได้คำตอบครบ” — คำถามเพิ่มเติมหลังเปิดโปรเจกต์แล้วจะทำให้ขั้นนี้กลับมาค้างจนกว่าจะได้คำตอบ", en: "Delivery stage 1 adds “customer questions all answered” — a follow-up question after the project opened holds the stage until it is answered" },
      { type: "security", th: "ข้อมูลลูกค้าเห็นได้เฉพาะคนที่เห็นทุกโปรเจกต์ (project.all) และแก้ได้เฉพาะคนที่จัดการโปรเจกต์ได้", en: "Customer data is visible only to people who can see every project (project.all) and editable only by people who manage projects" },
    ],
  },
  {
    version: "0.20.0",
    date: "2026-10-19",
    title: { th: "เตรียม release และอนุมัติก่อนขึ้น production", en: "Release preparation and approval before production" },
    changes: [
      { type: "added", th: "แท็บ “Release” ในหน้าโปรเจกต์: release notes ขั้นตอน deploy แผน rollback และปัญหาที่รู้อยู่แล้ว ต่อเวอร์ชัน", en: "Project “Releases” tab: release notes, deployment instructions, rollback plan and known issues per version" },
      { type: "added", th: "ตรวจความพร้อมอัตโนมัติ: ต้องมีแผนครบ เทสต์ผ่านบน commit ล่าสุดของ branch หลัก และไม่มีข้อบกพร่องระดับวิกฤต; ข้อบกพร่องค้าง งานติดขัด และ MR ที่เปิดอยู่แสดงเป็นคำเตือนและรายการปัญหาที่ยังค้าง", en: "Automatic readiness check: plans written, tests passing on the current commit of the main branch, no critical defects; open defects, blocked tasks and open merge requests show as warnings and unresolved issues" },
      { type: "added", th: "ส่งขออนุมัติขึ้น production ในกล่องอนุมัติ (ผูกกับ commit ที่ตรวจ ถ้า branch ขยับหลังส่งจะอนุมัติไม่ได้ และคนที่ส่งอนุมัติเองไม่ได้); หลังอนุมัติ คนบันทึกว่า deploy แล้วหรือ rollback — ระบบไม่ deploy เอง", en: "Submit for production approval in the approval inbox (bound to the checked commit: refused if the branch moved, and the requester cannot approve); afterwards people record the deployment or a rollback — the system never deploys by itself" },
      { type: "added", th: "เครื่องมือ AI `release.prepare` สำหรับ Release Manager และ DevOps เขียนร่าง release ได้ แต่ส่งอนุมัติไม่ได้", en: "AI tool `release.prepare` for Release Manager and DevOps: drafts releases, cannot submit them" },
      { type: "changed", th: "ขั้นตอนส่งมอบขั้น 7 ใช้ release ที่ได้รับอนุมัติเป็นเกณฑ์อนุมัติ และเพิ่มเกณฑ์ “มีขั้นตอน deploy และแผน rollback”", en: "Delivery stage 7 now uses an approved release as its approval, and adds the “deployment and rollback plans written” check" },
    ],
  },
  {
    version: "0.19.0",
    date: "2026-10-19",
    title: { th: "บันทึกข้อบกพร่อง (QA) ส่งงานแก้กลับให้นักพัฒนา", en: "Defect log (QA) that sends fixes back to the developer" },
    changes: [
      { type: "added", th: "แท็บ “ข้อบกพร่อง” ในหน้าโปรเจกต์: บันทึกข้อบกพร่องพร้อมขั้นตอนทำซ้ำ ผลที่ควรเป็น/ที่เกิดจริง ความรุนแรง และงานที่พบ (รหัส DEF-001…)", en: "Project “Defects” tab: record a defect with steps to reproduce, expected/actual result, severity and the task it was found in (codes DEF-001…)" },
      { type: "added", th: "ทุกข้อบกพร่องสร้างงานแก้ไขให้อัตโนมัติ ความสำคัญตามความรุนแรง และมอบให้คนที่ทำงานนั้นล่าสุด; ยืนยันว่าแก้แล้ว / เปิดใหม่ (สร้างงานแก้ใหม่) / ปฏิเสธ (ยกเลิกงานแก้ที่ยังไม่เริ่ม) ทำได้เฉพาะคนที่มีสิทธิ์อนุมัติงาน", en: "Each defect creates a fix task with a priority from its severity, assigned to whoever last worked on the task; verify / reopen (new fix task) / reject (cancels an unstarted fix task) are for people with task approval rights" },
      { type: "added", th: "Quality gate: งานที่มีข้อบกพร่องค้างจะปิดเป็น Done ไม่ได้ ทั้งจากกระดานงานและจากกล่องอนุมัติ; ขั้นตอนส่งมอบขั้น QA เพิ่มเกณฑ์ “ไม่มีข้อบกพร่องค้าง”", en: "Quality gate: a task with an open defect cannot be marked Done, from the board or the approval inbox; the QA delivery stage adds the “no open defects” exit check" },
      { type: "added", th: "เครื่องมือ AI `defect.report` สำหรับตำแหน่งสายคุณภาพ (QA, Automation Test, Code Reviewer, Security, Release)", en: "AI tool `defect.report` for the quality positions (QA, Automation Test, Code Reviewer, Security, Release)" },
    ],
  },
  {
    version: "0.18.1",
    date: "2026-10-19",
    title: { th: "โปรไฟล์พนักงานครบขึ้น และแก้การเรียงความสำคัญของงาน", en: "Fuller employee profile, and task priority ordering fixed" },
    changes: [
      { type: "added", th: "หน้าโปรไฟล์พนักงานแสดงหัวหน้า หน้าที่ ทักษะ งานที่รับผิดชอบอยู่ (กดเปิดงานได้) ผลงานล่าสุด และคุณภาพงานทั้งหมด (งาน AI สำเร็จ รีวิวผ่าน/ขอให้แก้ ผ่านรีวิวครั้งแรก ผลงานที่รับ การใช้งาน)", en: "Employee profiles show manager, responsibilities, skills, current assignments (linked), recent deliverables and all-time quality (AI runs succeeded, reviews approved / changes requested, first-pass approval, deliverables accepted, usage)" },
      { type: "fixed", th: "การเรียงงานตามความสำคัญเรียงตามตัวอักษรอยู่ (กลาง ก่อน วิกฤต) ทำให้ workflow engine หยิบงานวิกฤตทำทีหลัง และกระดานงาน/ผู้ช่วยเรียงผิด ตอนนี้เรียงวิกฤต → สูง → กลาง → ต่ำ แล้ว", en: "Ordering by priority was alphabetical (Medium before Critical), so the workflow engine started critical work last and the task board and assistant listed tasks in the wrong order; now Critical → High → Medium → Low" },
    ],
  },
  {
    version: "0.18.0",
    date: "2026-10-19",
    title: { th: "ขั้นตอนส่งมอบงาน 8 ขั้นพร้อมเกณฑ์", en: "Eight delivery stages with gates" },
    changes: [
      { type: "added", th: "หน้าโปรเจกต์มีแท็บ “ขั้นตอนส่งมอบ”: 8 ขั้นตั้งแต่รับเรื่องลูกค้าจนส่งมอบและสรุปบทเรียน แต่ละขั้นมีเกณฑ์เข้า เกณฑ์ออก สิ่งที่ต้องส่ง และการอนุมัติ ตรวจจากข้อมูลจริง (requirements ที่อนุมัติ ใบประเมิน ADR merge request เทสต์ เอกสารฉบับจริง บทเรียน) ขั้นปัจจุบันคือขั้นแรกที่ยังไม่ผ่าน", en: "Projects have a “Delivery stages” tab: eight stages from lead intake to delivery and retrospective, each with entry and exit criteria, deliverables and the approval it needs, checked against recorded data (approved requirements, estimate, ADRs, merge requests, tests, final documents, lessons). The current stage is the first one not yet met" },
      { type: "added", th: "ขั้นพัฒนาต้องมีใบประเมินที่อนุมัติและ ADR ที่ยอมรับก่อน (เกณฑ์เข้า) ส่วนการ deploy ขึ้น production ยังทำนอกระบบตามเดิม", en: "Development needs an approved estimate and an accepted architecture decision first (entry criteria); production deployment still happens outside the system" },
    ],
  },
  {
    version: "0.17.0",
    date: "2026-10-19",
    title: { th: "นโยบายการเก็บรักษาข้อมูล", en: "Data retention policy" },
    changes: [
      { type: "security", th: "หน้าสิทธิ์และผู้ใช้มีส่วน “การเก็บรักษาข้อมูล”: ตั้งจำนวนวันเก็บบันทึกการทำงานของ AI, log เทสต์, การแจ้งเตือน, ประวัติ webhook และข้อความแชตแยกกัน (30–3650 วัน) ระบบล้างข้อมูลที่เก่ากว่ากำหนดวันละครั้ง และบันทึกจำนวนที่ลบใน audit log", en: "Security page has a “Data retention” section: separate periods for AI run transcripts, test logs, notifications, webhook records and chat messages (30–3650 days); older data is cleaned up daily and the counts go to the audit log" },
      { type: "added", th: "ค่าเริ่มต้นคือเก็บทุกอย่างตลอด ไม่มีอะไรถูกลบจนกว่าจะตั้งค่า ปุ่ม “ล้างข้อมูลตอนนี้” แสดงจำนวนที่จะลบก่อนแล้วให้ยืนยัน ข้อมูลการเงิน เอกสาร ความรู้ ใบประเมิน และ audit log ไม่ถูกลบเลย", en: "By default everything is kept; nothing is removed until a period is set. “Clean up now” shows what would go and asks first. Money, documents, knowledge, estimates and the audit log are never removed" },
    ],
  },
  {
    version: "0.16.0",
    date: "2026-10-19",
    title: { th: "จำกัดสิทธิ์การมองเห็นรายโปรเจกต์", en: "Per-project access control" },
    changes: [
      { type: "security", th: "สิทธิ์ใหม่ “เห็นทุกโปรเจกต์” (project.all) ให้ CEO, Manager และ Auditor เป็นค่าเริ่มต้น คนที่ไม่มีสิทธิ์นี้ (เช่น Member) จะเห็นเฉพาะโปรเจกต์ที่ตัวเองเป็นสมาชิก (เจ้าของ, ได้รับมอบหมายงาน หรือถูกเพิ่มเป็นสมาชิก) — งาน เอกสาร ใบประเมิน repository งาน AI การอนุมัติ ความรู้ของโปรเจกต์ และค่าใช้จ่ายของโปรเจกต์อื่นจะเหมือนไม่มีอยู่ (404) ปรับสิทธิ์นี้ได้ที่หน้าสิทธิ์และผู้ใช้", en: "New “see every project” permission (project.all), granted to CEO, Manager and Auditor by default. Without it (e.g. Member) people see only projects they belong to (owner, task assignee or added member); other projects' tasks, documents, estimates, repositories, AI runs, approvals, project knowledge and costs behave as if they don't exist (404). Change it on the Security page" },
      { type: "added", th: "หน้าโปรเจกต์มีปุ่ม “สมาชิก” สำหรับเพิ่ม/ลบสมาชิก (เดิมทำได้เฉพาะในแชต) แชตของโปรเจกต์ใช้กติกาเดียวกัน", en: "Projects have a “Members” button to add or remove members (previously only in chat); project chat follows the same rule" },
    ],
  },
  {
    version: "0.15.0",
    date: "2026-10-19",
    title: { th: "สรุปปิดโปรเจกต์และร่างรายงานปิดโปรเจกต์", en: "Project closeout and completion report draft" },
    changes: [
      { type: "added", th: "หน้าโปรเจกต์มีแท็บ “สรุปปิดโปรเจกต์” (สำหรับคนที่ดูการเงินได้): เทียบแผนจากใบประเมินกับข้อมูลที่บันทึกจริง — งานเสร็จ/เลยกำหนดตาม milestone, ค่า AI และกำไรคาดการณ์, เทสต์ MR รีวิว ผลงาน และบทเรียน พร้อมรายการประเด็นที่ยังค้าง", en: "Projects have a “Project closeout” tab (for people who can see finance): the approved estimate's plan against recorded data — tasks done/overdue by milestone, AI cost and forecast margin, tests, MRs, reviews, deliverables and lessons, with a list of open issues" },
      { type: "added", th: "ปุ่ม “สร้างร่างรายงานปิดโปรเจกต์” เขียนรายงานจากตัวเลขเหล่านี้ (ภาษาตามที่ใช้อยู่) เป็นเอกสารฉบับร่างเวอร์ชันใหม่ในแท็บเอกสาร ตัวเลขไม่ได้ให้ AI แต่ง และไม่ใส่ชั่วโมงทำงานจริงของคนเพราะระบบไม่ได้บันทึก", en: "“Draft completion report” writes a report from these figures (in the current language) as a new draft version in Documents; the numbers are calculated, not written by AI, and people's actual hours are left out because the system doesn't record them" },
    ],
  },
  {
    version: "0.14.5",
    date: "2026-10-19",
    title: { th: "กระดานงานอยู่ในแถบบน", en: "Task board in the top bar" },
    changes: [
      { type: "changed", th: "แถบเมนูด้านบนมี “กระดานงาน” ต่อจากโปรเจกต์ (ยังอยู่ในเมนูหลักใต้โลโก้เหมือนเดิม)", en: "The top bar now has “Task board” next to Projects (it stays in the main menu under the logo too)" },
      { type: "fixed", th: "จอกว้างไม่ถึง 1280px แถบบนแสดงเฉพาะไอคอน ชี้เพื่อดูชื่อเมนู เดิมชื่อเมนูล้นแถบบนจอขนาดกลาง", en: "Below 1280px wide the top bar shows icons only, with the name on hover; the labels used to overflow on medium screens" },
    ],
  },
  {
    version: "0.14.4",
    date: "2026-10-19",
    title: { th: "ซ่อนช่องเดือนในแท็บกำไร", en: "Month picker hidden on the profitability tab" },
    changes: [
      { type: "fixed", th: "หน้าการเงิน: แท็บกำไรและสถานการณ์จำลองไม่แสดงช่องเลือกเดือนแล้ว เพราะตัวเลขในแท็บนี้ไม่ได้ขึ้นกับเดือน", en: "Finance: the profitability and scenario tab no longer shows the month picker, since its figures don't depend on the month" },
    ],
  },
  {
    version: "0.14.3",
    date: "2026-10-19",
    title: { th: "Claude ใช้ prompt caching", en: "Prompt caching for Claude" },
    changes: [
      { type: "changed", th: "การเรียก Claude เปิด prompt caching อัตโนมัติ: แต่ละรอบของงานส่ง tools, system prompt และประวัติเดิมซ้ำ ส่วนนั้นจะอ่านจาก cache ในราคาถูกกว่ามาก (ค่า cache คิดเงินแยกตามราคาในหน้าตั้งค่า AI อยู่แล้ว) — ยังไม่ได้ทดสอบกับ API จริงเพราะบัญชี Anthropic ยังไม่มีเครดิต", en: "Claude calls use automatic prompt caching: each turn of a run resends the same tools, system prompt and history, which is then read from cache at a much lower price (cache tokens are already priced separately in AI settings) — not yet exercised against the real API because the Anthropic account has no credit" },
    ],
  },
  {
    version: "0.14.2",
    date: "2026-10-19",
    title: { th: "กระดานงานทั้งบริษัท", en: "Company task board" },
    changes: [
      { type: "added", th: "เมนูใหม่ “กระดานงานทั้งบริษัท” (/tasks): งานจากทุกโปรเจกต์ที่ยังเปิดอยู่ในกระดานเดียว แยกตามสถานะ กรองตามโปรเจกต์ ผู้รับผิดชอบ ความสำคัญ หรือเฉพาะงานเลยกำหนด อัปเดตสด กดการ์ดเพื่อเปิดงานนั้นในโปรเจกต์", en: "New “Company task board” menu (/tasks): tasks from every open project on one board by status, filtered by project, assignee, priority or overdue only, live; a card opens that task in its project" },
      { type: "changed", th: "ลิงก์ “ดูทั้งหมด” ในการ์ดงานบนหน้าภาพรวมไปที่กระดานงานทั้งบริษัท", en: "The dashboard task card's “See all” goes to the company task board" },
    ],
  },
  {
    version: "0.14.1",
    date: "2026-10-19",
    title: { th: "เลือกโมเดลตามความซับซ้อนของงาน", en: "Model choice by task complexity" },
    changes: [
      { type: "added", th: "งานมีช่อง “ความซับซ้อน” (งานทั่วไป / มาตรฐาน / ซับซ้อน): งานทั่วไปรันด้วยโมเดลระดับ Routine ที่ถูกที่สุดของผู้ให้บริการเดียวกัน งานซับซ้อนใช้โมเดลระดับขั้นสูง ถ้าไม่มีโมเดลระดับนั้นจะใช้โมเดลที่ตั้งไว้ของพนักงาน — งานเดิมทั้งหมดเป็น “มาตรฐาน” จึงไม่มีอะไรเปลี่ยน", en: "Tasks have a Complexity (Routine / Standard / Complex): routine tasks run on the cheapest routine-tier model of the same provider, complex tasks on its advanced-tier model, falling back to the employee's configured model — every existing task is Standard, so nothing changes until you set it" },
      { type: "added", th: "AI ผู้วางแผนระบุความซับซ้อนของแต่ละงานในแผนได้ (แสดงในหน้าอนุมัติแผน) และงานย่อยที่ถูกมอบต่อใช้ความซับซ้อนเดียวกับงานแม่", en: "The AI planner can set each plan task's complexity (shown on plan approval), and delegated sub-tasks inherit their parent's complexity" },
    ],
  },
  {
    version: "0.14.0",
    date: "2026-10-19",
    title: { th: "กำไรโดยประมาณและสถานการณ์จำลอง", en: "Profitability and scenario planning" },
    changes: [
      { type: "added", th: "หน้าการเงินมีแท็บ “กำไรและสถานการณ์จำลอง”: กำไรตามแผนและกำไรคาดการณ์ของทุกโปรเจกต์ที่มีใบประเมินที่อนุมัติแล้ว (คาดการณ์ = ต้นทุนตามแผน + ค่า AI จริงที่เกินแผน) ขึ้น ⚠ เมื่อกำไรคาดการณ์หายไปเกินครึ่งหรือติดลบ", en: "Finance has a “Profitability & scenarios” tab: planned and forecast margin for every project with an approved estimate (forecast = planned cost + actual AI spend above plan), flagged ⚠ when the forecast loses more than half the planned margin or goes negative" },
      { type: "added", th: "สถานการณ์จำลอง (what-if): ปรับชั่วโมง ค่าแรง ค่า AI เผื่อความเสี่ยง หรือราคา แล้วเทียบกับแผน คำนวณแบบ deterministic และไม่บันทึกหรือแก้ใบประเมินจริง", en: "Scenario planning (what-if): change hours, rates, AI cost, contingency or price and compare with the plan; deterministic, and nothing is saved or changed on the real estimate" },
    ],
  },
  {
    version: "0.13.2",
    date: "2026-10-19",
    title: { th: "ตัวละครในออฟฟิศ 3D ใช้รูปจากชุดตัวละคร", en: "3D office people use the character pack figure" },
    changes: [
      { type: "changed", th: "เมื่อตั้ง CHARACTER_PACK และมี kuromi_3d.png คนในออฟฟิศ 3D จะเป็นรูปนี้ หันหาคนดูเสมอ ยังเดิน ไปเลาจน์ แวะคุย และเปลี่ยนท่าตามสถานะเหมือนเดิม (โยก กระโดด เอียงตัว ฟุบ) — ไม่ตั้ง = ตัว chibi เดิม", en: "With CHARACTER_PACK set and kuromi_3d.png present, 3D office people are that picture, always facing the viewer; they still walk, visit the lounge, chat and pose by status (waddle, hop, lean, slump) — unset = the original chibis" },
    ],
  },
  {
    version: "0.13.1",
    date: "2026-10-19",
    title: { th: "พิมพ์ claude ใน Terminal ได้", en: "Type claude in the terminal" },
    changes: [
      { type: "added", th: "ใน Terminal พิมพ์ “claude <ข้อความ>” เพื่อถาม Claude Code ในโฟลเดอร์โปรเจกต์ได้ คุยต่อเนื่องจนกว่าจะพิมพ์ “claude /new” ถ้าพิมพ์ “claude -...” จะส่ง flag ต่อให้ claude.cmd ตรงๆ และพิมพ์ “claude” เฉยๆ เพื่อดูวิธีใช้", en: "In the terminal, “claude <message>” asks Claude Code in the project folder and keeps the conversation until “claude /new”; “claude -...” passes the flags straight to claude.cmd, and “claude” alone shows how to use it" },
      { type: "changed", th: "claude ใน Terminal แก้ไฟล์ในโฟลเดอร์โปรเจกต์ได้เอง (โหมด acceptEdits) ส่วนการรันคำสั่งอื่นยังถูกปฏิเสธ", en: "claude in the terminal can edit files in the project folder on its own (acceptEdits mode); running other commands is still denied" },
      { type: "added", th: "การ์ดกลางหน้าภาพรวมมีประวัติแชทแล้ว เลื่อนดูคำถาม-คำตอบย้อนหลังได้ แยกกันระหว่าง Terminal, Claude และพนักงาน AI แต่ละคน (เก็บในเบราว์เซอร์นี้ ล่าสุด 40 รายการต่อช่อง) และกด “ล้างประวัติ” ได้", en: "The dashboard stage keeps a chat history: scroll back through questions and answers, kept apart for the Terminal, Claude and each AI employee (stored in this browser, the latest 40 per assistant), with “Clear history”" },
      { type: "fixed", th: "พิมพ์ claude แล้วไม่ขึ้น error “running scripts is disabled” แล้ว เพราะ Terminal เรียก claude.cmd แทน claude.ps1 (ไม่ต้องเปลี่ยน execution policy ของเครื่อง)", en: "Typing claude no longer fails with “running scripts is disabled”: the terminal calls claude.cmd instead of claude.ps1 (the PC's execution policy is unchanged)" },
    ],
  },
  {
    version: "0.13.0",
    date: "2026-10-19",
    title: { th: "Terminal บนหน้า dashboard", en: "Terminal on the dashboard" },
    changes: [
      { type: "added", th: "สำหรับ CEO การ์ด Claude บนหน้า dashboard กลายเป็น Terminal แล้ว พิมพ์คำสั่ง PowerShell ในช่องเดิม คำสั่งจะรันในโฟลเดอร์โปรเจกต์บนเครื่องนี้ และ output แสดงใน bubble (รันได้ครั้งละ 1 คำสั่ง ไม่เกิน 10 นาที) ถ้าจะคุยกับ Claude แบบเดิม กดปุ่ม “คุยกับ Claude”", en: "For the CEO, the dashboard's Claude card is now a terminal: type a PowerShell command in the same box, it runs in the project folder on this PC and the output shows in the bubble (one command at a time, up to 10 minutes); “Chat with Claude” switches back to the Claude chat" },
      { type: "security", th: "Terminal ใช้ได้เฉพาะบทบาท CEO ไม่จำกัดคำสั่ง ทุกคำสั่งบันทึกลง audit log ก่อนรัน และตัวเชื่อมยังรับเฉพาะการเชื่อมต่อจากเครื่องนี้ (loopback) พร้อม token", en: "The terminal is for the CEO role only and does not restrict commands; every command is written to the audit log before it runs, and the bridge still accepts only loopback connections with its token" },
    ],
  },
  {
    version: "0.12.1",
    date: "2026-10-19",
    title: { th: "สั่งงานผ่าน Claude ได้แล้ว", en: "Claude can hand out work" },
    changes: [
      { type: "added", th: "บอก Claude ให้ทำงานได้เลย เช่น “ให้ทำหน้า login ในโปรเจกต์ WEB” — Claude เลือกพนักงาน AI ที่เหมาะ สร้างงานในโปรเจกต์ มอบหมาย และสั่งเริ่มทำทันที (ครั้งละไม่เกิน 3 งาน) ผลการสั่งแสดงใต้คำตอบ และพนักงาน AI รายงานเมื่อเสร็จในห้องแชท “AI Assistant”", en: "Tell Claude to get something done, e.g. “build the login page in project WEB” — Claude picks a fitting AI employee, creates the task in the project, assigns it and starts the work right away (up to 3 tasks at a time); the outcome shows under its reply and the AI employee reports back in the “AI Assistant” chat" },
      { type: "security", th: "Claude ยังรันคำสั่งหรืออ่านไฟล์บนเครื่องไม่ได้ สั่งงานได้ผ่านระบบเท่านั้น ด้วยสิทธิ์ของคนที่คุย (ต้องมีสิทธิ์สร้างงาน มอบหมายงาน และสั่ง AI ทำงาน) ภายใต้งบและขีดจำกัดของพนักงาน AI เหมือนเดิม", en: "Claude still cannot run commands or read files on the PC; it only hands out work through the system, with the asking person's permissions (task.manage, task.assign, agent.run) and the AI employees' usual budgets and limits" },
    ],
  },
  {
    version: "0.12.0",
    date: "2026-10-19",
    title: { th: "คุยกับ Claude จากหน้าภาพรวม", en: "Chat with Claude from the dashboard" },
    changes: [
      { type: "added", th: "“Claude” เป็นผู้ช่วยตัวแรกบนเวทีหน้าภาพรวม: ใช้ Claude Code ที่ login ไว้บนเครื่องนี้ (แพ็กเกจของคุณ ไม่ต้องเติมเครดิต API) คุยต่อเนื่องได้ กด “แชทใหม่” เพื่อเริ่มใหม่ และเห็นข้อมูลบริษัทแบบสด", en: "“Claude” is the first assistant on the dashboard stage: the Claude Code signed in on this PC (your plan, no API credit needed), with a continuing conversation, a “New chat” button and live company data" },
      { type: "security", th: "คุยได้อย่างเดียว: Claude ไม่มีเครื่องมือใด ๆ (รันคำสั่ง/อ่านไฟล์ไม่ได้) ใช้ได้เฉพาะคนที่มีสิทธิ์จัดการ AI ตัวเชื่อมรับคำขอจากเครื่องนี้เท่านั้นและต้องมี token ใน .env", en: "Talk only: Claude has no tools (cannot run commands or read files); only people with agent.manage can use it; the bridge accepts requests from this PC only, with a token from .env" },
      { type: "added", th: "บทสนทนากับ Claude เก็บไว้ในห้องแชท “AI Assistant” และตัวเชื่อมเปิดเองเมื่อเริ่มระบบด้วยไอคอน Office AI", en: "Claude conversations are kept in the “AI Assistant” chat channel, and the bridge starts with the Office AI icon" },
    ],
  },
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
  { status: "waitingCeo", area: { th: "เชื่อมต่อ", en: "Integrations" }, th: "ถ้าต้องการอีเมลแจ้งเตือน: ใส่บัญชี SMTP ใน .env (EMAIL_HOST, EMAIL_FROM, EMAIL_USERNAME, EMAIL_PASSWORD — Gmail ใช้ App Password) แล้ว restart", en: "For email notifications: put an SMTP account in .env (EMAIL_HOST, EMAIL_FROM, EMAIL_USERNAME, EMAIL_PASSWORD — Gmail needs an App Password) and restart" },
  { status: "planned", area: { th: "AI", en: "AI" }, th: "โมเดล Qwen3-8B ที่ฝึกกับงานของบริษัท: ฝึกเสร็จแล้วแปลงเป็น GGUF ลงใน Ollama ประเมินผล แล้วเพิ่มในหน้าตั้งค่า AI", en: "Qwen3-8B fine-tuned on the company's work: once trained, convert to GGUF for Ollama, evaluate, and add it in AI settings" },
  { status: "planned", area: { th: "โครงสร้าง", en: "Infrastructure" }, th: "Redis และที่เก็บไฟล์แบบ S3 — ทำเมื่อเลือกที่ deploy แล้ว", en: "Redis and S3-compatible storage — once the deploy target is chosen" },
  { status: "planned", area: { th: "AI", en: "AI" }, th: "1 ม.ค. 2027: แก้ราคา Gemini 3.8 Flash ในหน้าตั้งค่า AI เป็น $1.50 / $7.50 ต่อล้าน token (ราคาโปรหมด)", en: "1 Jan 2027: update Gemini 3.8 Flash prices in AI settings to $1.50 / $7.50 per million tokens (intro pricing ends)" },
];

export const LATEST_VERSION = CHANGELOG[0].version;

/** localStorage key holding the last version this browser opened on /updates. */
export const SEEN_KEY = "updates.seenVersion";
