# AI Software House

ระบบปฏิบัติการบริษัทซอฟต์แวร์ที่มีพนักงาน AI โดยมี CEO เป็นคน ดูรายละเอียดทั้งหมดได้ใน `AI_Software_House_Master_Prompt.txt`

สถานะปัจจุบัน: **Phase 1 — โครงสร้างบริษัท** (Auth, สิทธิ์, แผนก, ตำแหน่ง, พนักงานคนและ AI, โปรเจกต์, Task Board, Audit log)

## เริ่มใช้งาน

ต้องมี Docker Desktop

```bash
docker compose up -d --build
```

| บริการ | URL |
| --- | --- |
| หน้าเว็บ | http://localhost:3000 |
| API | http://localhost:5000 (health: `/health`) |
| PostgreSQL | `localhost:15432` (เปิดเฉพาะเครื่องตัวเอง) |

บัญชีเริ่มต้นสำหรับ dev: `ceo@officeai.local` / `ChangeMe-Dev-2026`
**ต้องเปลี่ยนก่อนใช้จริง** โดยคัดลอก `.env.example` เป็น `.env` แล้วแก้ค่า ซึ่งค่าจะมีผลกับฐานข้อมูลใหม่เท่านั้น

ระบบรัน migration และใส่ข้อมูลตั้งต้นอัตโนมัติ: 6 แผนก, 31 ตำแหน่ง, พนักงาน AI 30 ตัว และ CEO 1 คน

## พัฒนาแบบไม่ใช้ Docker

```bash
docker compose up -d db                        # เปิดแค่ฐานข้อมูล
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

Animation จะลดลงอัตโนมัติถ้าผู้ใช้เปิด "ลดการเคลื่อนไหว" ในระบบปฏิบัติการ

## ความปลอดภัยใน Phase 1

- ตรวจสิทธิ์ทุก endpoint ที่ฝั่งเซิร์ฟเวอร์ (`resource.action`) และบันทึกคำขอที่ถูกปฏิเสธลง Audit log
- บัญชี AI Agent ใช้สิทธิ์ `security.manage` และ `task.approve` ไม่ได้ ไม่ว่าจะได้ role อะไร และ login ด้วยรหัสผ่านไม่ได้
- ล็อกบัญชี 15 นาทีหลังใส่รหัสผิด 5 ครั้ง และจำกัด rate ของการ login
- AuditLogs แก้ไขหรือลบไม่ได้ เพราะมี trigger ในฐานข้อมูลบล็อกไว้
- กันการแก้ข้อมูลชนกันด้วย optimistic concurrency (xmin): ถ้าแก้ซ้อนกันจะได้ 409

เรื่องที่ยังต้องทำก่อนขึ้น production (Phase 6): HTTPS, เข้ารหัสกุญแจ DataProtection, จัดการ secret และจำกัดสิทธิ์ระดับโปรเจกต์
