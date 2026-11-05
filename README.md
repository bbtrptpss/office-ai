# AI Software House

ระบบปฏิบัติการบริษัทซอฟต์แวร์ที่มีพนักงาน AI โดยมี CEO เป็นคน ดูรายละเอียดทั้งหมดได้ใน `AI_Software_House_Master_Prompt.txt`

สถานะปัจจุบัน: **Phase 1–27 เสร็จครบ (v0.32)** — โครงสร้างบริษัท, พนักงาน AI ทำงานผ่านเครื่องมือที่ได้รับอนุญาต, objective ที่ AI วางแผน/ทำ/รีวิวโดย CEO อนุมัติทุกขั้นสำคัญ, ส่งมอบงานลูกค้าครบ 8 ขั้น, โมเดลสำรองเมื่อ AI ตอบไม่ได้, 2FA, ต้นทุนดำเนินงาน, ค้นความรู้ด้วยความหมาย, คุณภาพแยกตามโมเดลและข้อมูลฝึกจากงานจริง, บันทึกเวลาทำงาน (รายละเอียดใน `docs/PROGRESS.md` และหน้า “บันทึกอัปเดต”)

## เปิดใช้ Claude

ใส่ `ANTHROPIC_API_KEY` ใน `.env` แล้ว `docker compose up -d` จากนั้นเปลี่ยนพนักงาน AI ให้ใช้ provider `anthropic` ที่หน้าโปรไฟล์ ถ้ายังไม่ใส่ key ระบบมี provider **จำลอง** ไว้ทดลองทั้ง flow ผลงานจากตัวจำลองมีป้าย 🧪 และไม่นับเป็นงานจริง (ปิดได้ด้วย `SIMULATED_AI=false`)

API key ใส่ได้ที่หน้า “ตั้งค่า AI” โดยตรง (เข้ารหัสก่อนเก็บ มีผลทันที ไม่ต้องรีสตาร์ต) หรือใส่ใน `.env` ก็ได้ ใช้ OpenAI หรือ Google Gemini ได้ด้วย: ใส่ key แล้วเพิ่มโมเดลพร้อมราคาต่อ 1M token ที่หน้าเดียวกัน (ระบบไม่ใส่ราคาให้เอง เพื่อไม่ให้คำนวณค่าใช้จ่ายผิด)

**ค้นคลังความรู้ด้วยความหมาย (ฟรี):** `ollama pull bge-m3` แล้วใส่ `OLLAMA_EMBED_MODEL=bge-m3` ใน `.env` และ `docker compose up -d` — ไม่ตั้ง = ค้นด้วยคำแบบเดิม

## เริ่มใช้งาน

ต้องมี Docker Desktop

```bash
docker compose up -d --build
```

**Windows: เปิดด้วยไอคอนบน Desktop / เริ่มเองเมื่อเปิดเครื่อง** — รันครั้งเดียว:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\windows\install-shortcuts.ps1
```

จะได้ไอคอน “Office AI” บน Desktop (เปิด Docker Desktop → `docker compose up -d` → รอระบบพร้อม → เปิดเบราว์เซอร์)
และ shortcut ใน Startup ที่เริ่มระบบให้เองตอน login (ไม่เปิดเบราว์เซอร์) — ไม่เอา Startup ใส่ `-NoStartup`, ลบทั้งคู่ใส่ `-Remove`
log อยู่ที่ `%LOCALAPPDATA%\OfficeAI\start.log` · ถ้าแก้โค้ดแล้วยังต้อง `docker compose up -d --build` เอง

**Windows: สำรองข้อมูลอัตโนมัติทุกวัน** — `powershell -ExecutionPolicy Bypass -File scripts\windows\install-backup-task.ps1`
(ทุกวัน 12:00 ถ้าปิดเครื่องอยู่จะทำทันทีที่เปิด เก็บ 14 ชุดล่าสุดใน `backups/` · สำรองเองตอนนี้: `scripts\windows\backup-office-ai.ps1` · กู้คืน: `scripts/restore.sh`)

**คุยกับ Claude จากหน้าภาพรวม (Claude Code บนเครื่องนี้):** ต้องมี `claude` ที่ login แล้ว, Node และ `CLAUDE_BRIDGE_TOKEN` ใน `.env`
ตัวเชื่อม `scripts/windows/claude-bridge.mjs` เปิดเองเมื่อกดไอคอน Office AI (หรือรัน `node scripts/windows/claude-bridge.mjs`) — Claude รันคำสั่ง/อ่านไฟล์ไม่ได้ แต่เสนองานให้พนักงาน AI ได้ (สร้างงาน มอบหมาย และเริ่มทำ เมื่อคนที่คุยกดยืนยันแต่ละงาน ด้วยสิทธิ์ของคนนั้น) ใช้โควตาเดียวกับ Claude Code ของคุณ

**Terminal บนหน้าภาพรวม (เฉพาะ CEO ที่เปิดการยืนยันสองขั้นที่ “การตั้งค่าของฉัน”):** ปิดเป็นค่าเริ่มต้น เปิดได้โดยใส่ `CLAUDE_BRIDGE_TERMINAL=true` ใน `.env` แล้วเปิดตัวเชื่อมใหม่ · ต้องพิมพ์รหัส 6 หลักจากแอป Authenticator เพื่อเปิด (เปิดค้าง 15 นาทีนับจากคำสั่งล่าสุด) การ์ด Claude ของ CEO สลับเป็น Terminal ได้ — พิมพ์คำสั่ง PowerShell แล้วรันในโฟลเดอร์โปรเจกต์บนเครื่องนี้ผ่านตัวเชื่อมตัวเดียวกัน (ไม่จำกัดคำสั่ง ครั้งละ 1 คำสั่ง ไม่เกิน 10 นาที ทุกคำสั่งอยู่ใน audit log) กด “คุยกับ Claude” เพื่อกลับไปแชทแบบเดิม · พิมพ์ `claude <ข้อความ>` เพื่อถาม Claude Code ในโฟลเดอร์โปรเจกต์ (คุยต่อเนื่อง, `claude /new` เริ่มใหม่, แก้ไฟล์ได้เองโดยไม่ต้องอนุมัติ) — แบบโต้ตอบเต็มจอใช้ในหน้าเว็บไม่ได้

**ให้ทีม AI แก้โค้ดระบบนี้:** โค้ดอยู่ในโปรเจกต์ PLATFORM → repository `office-ai` ภายในระบบ
`scripts\windows\platform-repo.ps1 push` ส่งโค้ดในเครื่องเข้าไป · `... pull` ดึงงานที่ AI ทำและอนุมัติ merge แล้วมาไว้ที่ branch `ai/main` ให้ตรวจก่อน merge

| บริการ | URL |
| --- | --- |
| หน้าเว็บ | http://localhost:3000 |
| API | http://localhost:5000 (health: `/health`) |
| PostgreSQL | `localhost:15432` (เปิดเฉพาะเครื่องตัวเอง) |

บัญชีเริ่มต้นสำหรับ dev: `ceo@officeai.local` / `ChangeMe-Dev-2026`
**ต้องเปลี่ยนก่อนใช้จริง** โดยคัดลอก `.env.example` เป็น `.env` แล้วแก้ค่า ซึ่งค่าจะมีผลกับฐานข้อมูลใหม่เท่านั้น

ระบบรัน migration และใส่ข้อมูลตั้งต้นอัตโนมัติ: 6 แผนก, 31 ตำแหน่ง, พนักงาน AI 30 ตัว และ CEO 1 คน

## ติดตั้งใช้งานจริง

ดู [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) — HTTPS อัตโนมัติ, secrets, สำรอง/กู้คืน, monitoring

## พัฒนาแบบไม่ใช้ Docker

```bash
docker compose up -d db                        # เปิดแค่ฐานข้อมูล
export ConnectionStrings__Default="Host=localhost;Port=15432;Database=officeai;Username=officeai;Password=<POSTGRES_PASSWORD ใน .env>"
cd backend && dotnet run --project src/OfficeAI.Api   # API ที่ http://localhost:5000
cd frontend && npm install && npm run dev      # เว็บที่ http://localhost:3000
```

## ทดสอบ

```bash
cd backend && dotnet test     # integration test กับ PostgreSQL จริง (Testcontainers, ต้องเปิด Docker)
cd frontend && npm run lint && npm run build
```

## โครงสร้าง

```
backend/
  src/OfficeAI.Domain          entity, สถานะงาน (state machine), รหัสสิทธิ์
  src/OfficeAI.Application     กฎธุรกิจ (องค์กร, โปรเจกต์, งาน, dependency)
  src/OfficeAI.Infrastructure  EF Core + PostgreSQL, Identity, audit อัตโนมัติ, seed
  src/OfficeAI.Api             Minimal API, ตรวจสิทธิ์, SignalR (/hubs/events)
  tests/OfficeAI.Tests         integration tests
frontend/                      Next.js 16 + Material UI + motion
```

## ธีมและมาสคอต

ธีมเป็นโทนม่วง ลาเวนเดอร์ และชมพู มาสคอต "Puni" และตัวละครพนักงาน AI ออกแบบใหม่ทั้งหมด ไม่ได้ใช้ภาพตัวละครของ Sanrio

ถ้ามีรูปที่ได้สิทธิ์ใช้งาน ให้วางไฟล์ไว้ใน `frontend/public/characters/` แล้วตั้งค่า `MASCOT_IMAGE=/characters/<ไฟล์>` ใน `.env` จากนั้น rebuild ด้วย `docker compose up -d --build web`
รูปฉากกลางหน้าภาพรวมก็ทำแบบเดียวกันด้วย `STAGE_IMAGE=/characters/<ไฟล์>` (ไม่ตั้งค่า = ใช้มาสคอต Puni)

Animation จะลดลงอัตโนมัติถ้าผู้ใช้เปิด "ลดการเคลื่อนไหว" ในระบบปฏิบัติการ

## ความปลอดภัยใน Phase 1

- ตรวจสิทธิ์ทุก endpoint ที่ฝั่งเซิร์ฟเวอร์ (`resource.action`) และบันทึกคำขอที่ถูกปฏิเสธลง Audit log
- บัญชี AI Agent ใช้สิทธิ์ `security.manage` และ `task.approve` ไม่ได้ ไม่ว่าจะได้ role อะไร และ login ด้วยรหัสผ่านไม่ได้
- ล็อกบัญชี 15 นาทีหลังใส่รหัสผิด 5 ครั้ง และจำกัด rate ของการ login
- AuditLogs แก้ไขหรือลบไม่ได้ เพราะมี trigger ในฐานข้อมูลบล็อกไว้
- กันการแก้ข้อมูลชนกันด้วย optimistic concurrency (xmin): ถ้าแก้ซ้อนกันจะได้ 409

