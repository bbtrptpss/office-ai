# การติดตั้งใช้งานจริง (Production)

คู่มือนี้ติดตั้ง AI Software House บนเซิร์ฟเวอร์ Linux เครื่องเดียวด้วย Docker Compose โดยมี HTTPS อัตโนมัติผ่าน Caddy

## สิ่งที่ต้องมี

- เซิร์ฟเวอร์ Linux ที่มี Docker Engine 25+ และ Docker Compose 2.24+ (รองรับ `volume-subpath` และ `!reset`)
- โดเมน 2 ชื่อที่ชี้มาที่เซิร์ฟเวอร์นี้ เช่น `app.example.com` (หน้าเว็บ) และ `api.example.com` (API)
- เปิดพอร์ต 80 และ 443 สู่อินเทอร์เน็ต ส่วนพอร์ตอื่นปิดทั้งหมด

## 1. ตั้งค่า `.env`

คัดลอก `.env.example` เป็น `.env` แล้วกำหนดค่าเหล่านี้ (ทุกตัวบังคับ ระบบจะไม่ยอมเริ่มถ้าขาด):

| ตัวแปร | ความหมาย |
| --- | --- |
| `APP_DOMAIN`, `API_DOMAIN` | โดเมนของหน้าเว็บและ API |
| `POSTGRES_PASSWORD` | รหัสผ่านฐานข้อมูล (สุ่มยาวอย่างน้อย 32 ตัวอักษร) |
| `CEO_EMAIL`, `CEO_PASSWORD` | บัญชี CEO ที่สร้างตอนเริ่มครั้งแรกเท่านั้น — เปลี่ยนรหัสผ่านหลังเข้าสู่ระบบครั้งแรก |
| `SANDBOX_RUNNER_TOKEN` | รหัสลับระหว่าง API กับ sandbox runner (สุ่มยาว) |
| `DATAPROTECTION_CERT_PASSWORD` | รหัสผ่านของ certificate ที่เข้ารหัสกุญแจ cookie (ดูด้านล่าง) |
| `ANTHROPIC_API_KEY` | (ไม่บังคับ) เปิดใช้ Claude; ไม่ใส่ = ปิด AI จริง |
| `GIT_REMOTE_GITHUB`, `GIT_REMOTE_GITLAB` | (ไม่บังคับ) token สำหรับส่งโค้ดที่อนุมัติแล้วขึ้น GitHub / GitLab — ใช้ token ที่จำกัดสิทธิ์เฉพาะ repository นั้น (contents: write) แล้วตั้งชื่อตัวแปรที่หน้า repository ของโปรเจกต์ |
| `OPENAI_API_KEY`, `GEMINI_API_KEY` | (ไม่บังคับ) เปิดใช้ OpenAI / Google Gemini — จากนั้นเพิ่มโมเดลและราคาที่หน้า “ตั้งค่า AI” ก่อนกำหนดให้พนักงาน AI |
| `INTEGRATION_*` | (ไม่บังคับ) URL ของ webhook เช่น `INTEGRATION_SLACK_URL=https://hooks.slack.com/...` |

สร้างค่าสุ่มได้ด้วย `openssl rand -base64 36`

จากนั้นสร้าง certificate สำหรับเข้ารหัสกุญแจ cookie (ทำครั้งเดียว ต้องมี `openssl`):

```bash
set -a; . ./.env; set +a
scripts/new-dataprotection-cert.sh        # เขียนไฟล์ secrets/dataprotection.pfx
```

เก็บไฟล์ `.pfx` และรหัสผ่านไว้กับชุดสำรองข้อมูลด้วย ถ้าหาย กุญแจที่เข้ารหัสไว้จะอ่านไม่ได้ (ทุกคนถูก sign out แต่ข้อมูลอื่นไม่เสียหาย)

> secret อยู่ใน `.env` หรือ secret manager ของเครื่อง ยกเว้น AI key ที่ใส่ผ่านหน้า “ตั้งค่า AI” ซึ่งเข้ารหัสแล้วเก็บในฐานข้อมูล ตั้งสิทธิ์ไฟล์ด้วย `chmod 600 .env`
>
> ระบบจะไม่ยอมเริ่มถ้า `POSTGRES_PASSWORD`, `CEO_PASSWORD` หรือ `SANDBOX_RUNNER_TOKEN` เป็นค่าตัวอย่าง หรือสั้นกว่า 16 ตัวอักษร

## 2. เริ่มระบบ

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
docker compose -f docker-compose.yml -f docker-compose.prod.yml ps
curl -fsS https://$API_DOMAIN/health/ready   # ต้องได้ Healthy
```

การเริ่มครั้งแรกจะรัน migration และสร้างข้อมูลตั้งต้นให้อัตโนมัติ

สิ่งที่ production override ทำต่างจากเครื่องพัฒนา:

- ไม่เปิดพอร์ตฐานข้อมูล, API และเว็บสู่ภายนอก ให้เข้าผ่าน Caddy (HTTPS) อย่างเดียว
- ปิด provider จำลอง (`AiProviders__Simulated__Enabled=false`)
- cookie ส่งผ่าน HTTPS เท่านั้น เปิด HSTS และเขียน log เป็น JSON
- เชื่อ header จาก proxy (`Security__TrustProxy`) เพื่อให้การจำกัดการล็อกอินแยกตามผู้ใช้จริง — ห้ามเปิดถ้ามีทางเข้าถึง API โดยไม่ผ่าน Caddy
- webhook ต้องเป็น `https://` เท่านั้น

## 3. หลังติดตั้ง

1. เข้า `https://APP_DOMAIN` ด้วยบัญชี CEO แล้วเปลี่ยนรหัสผ่านทันที (เมนูชื่อผู้ใช้ด้านล่างซ้าย)
2. ตั้งค่า **ตั้งค่าบริษัท** (อัตรา USD→THB, เรตค่าแรง) และ **การเงิน → งบประมาณ AI** ระดับบริษัทต่อเดือน
3. ตั้งค่า **ตั้งค่า AI** ตรวจราคาโมเดลให้ตรงกับสัญญาของคุณ
4. เพิ่ม image ของ sandbox ที่อนุญาตได้ด้วยตัวแปร `Sandbox__AllowedImages__N` ที่ service `api` และ `sandbox-runner`

## 4. สำรองและกู้คืนข้อมูล

```bash
scripts/backup.sh                       # เขียนไปที่ backups/<เวลา UTC>/ พร้อม SHA256SUMS
scripts/restore.sh backups/<เวลา>       # กู้คืนทับระบบจริง (หยุด api/web/runner ระหว่างกู้)

# ซ้อมกู้คืนโดยไม่แตะข้อมูลจริง
RESTORE_DB=officeai_drill RESTORE_VOLUME=officeai-repos-drill scripts/restore.sh backups/<เวลา>
```

แนะนำให้ตั้ง cron สำรองทุกคืนและส่งไฟล์ไปเก็บนอกเครื่อง ไฟล์สำรองมี audit log, กุญแจ cookie และโค้ดทั้งหมด จึงต้องเข้ารหัสระหว่างเก็บ

## 5. การอัปเกรด

```bash
scripts/backup.sh
git pull
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

migration รันอัตโนมัติตอน API เริ่ม และทุก migration มีเทสต์ที่อัปเกรดฐานข้อมูลซึ่งมีข้อมูลเดิมอยู่แล้ว

## 6. การเฝ้าดูระบบ

- **Health:** `/health` (process ทำงาน) และ `/health/ready` (เชื่อมฐานข้อมูลได้)
- **Log:** `docker compose logs -f api` เป็น JSON ที่มี correlation id ทุกคำขอ
- **Metrics/Traces:** ตั้ง `OTEL_EXPORTER_OTLP_ENDPOINT` ที่ service `api` เพื่อส่งไป Grafana/Tempo/Jaeger ฯลฯ
  - metrics ของระบบ: `officeai.agent_runs.finished`, `officeai.tool_executions`, `officeai.ai_cost`, `officeai.ai_tokens`, `officeai.engine_ticks`, `officeai.approvals.decided`

## ข้อควรรู้ด้านความปลอดภัย

- `sandbox-runner` เป็น service เดียวที่ต่อกับ Docker socket ซึ่งมีสิทธิ์เทียบเท่า root ของเครื่อง จึงไม่เปิดพอร์ตออกนอกเครือข่ายภายใน รับเฉพาะคำขอที่มี `SANDBOX_RUNNER_TOKEN` และรันได้เฉพาะ image ที่อยู่ใน allow-list
- คำสั่งติดตั้ง dependency ของ repository รันในเครือข่าย `officeai-sandbox-egress` ซึ่งออกได้ทาง `sandbox-proxy` เท่านั้น และไปได้เฉพาะ HTTPS ถึง registry ใน `deploy/sandbox-proxy/squid.conf` สคริปต์ของ package ทำงานในขั้นนี้ จึงไม่มีการส่ง secret เข้า container
- ถ้าต้องการแยกขาดยิ่งขึ้น ให้ย้าย runner ไปเครื่องแยก หรือใช้ rootless Docker / gVisor (`--runtime runsc`)
- กุญแจเข้ารหัส cookie (DataProtection) ถูกเข้ารหัสด้วย certificate ก่อนเก็บลงฐานข้อมูล การเปลี่ยน certificate: สร้างไฟล์ใหม่ ย้ายไฟล์เดิมไปไว้ข้างกัน แล้วตั้ง `DataProtection__PreviousCertificatePaths__0=<path ไฟล์เดิม>` ให้ service `api` เพื่อให้อ่านกุญแจเก่าได้จนหมดอายุ (90 วัน)
- เซิร์ฟเวอร์ตัวเดียวรองรับ workflow engine ได้หลาย instance เพราะใช้ PostgreSQL advisory lock แต่คิวงาน AI อยู่ในหน่วยความจำของแต่ละ instance จึงแนะนำให้รัน API instance เดียว
