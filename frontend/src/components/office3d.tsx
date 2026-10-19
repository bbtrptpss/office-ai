"use client";

import { Suspense, useMemo, useRef, useState } from "react";
import { Canvas, useFrame, type ThreeEvent } from "@react-three/fiber";
import { Html, OrbitControls, RoundedBox, useTexture } from "@react-three/drei";
import * as THREE from "three";
import type { EmployeeKind, EmployeeStatus } from "@/lib/api";
import { art } from "@/lib/characters";
import { brand } from "@/theme";

/** Cut-out figure from the character pack; when set, people are this picture (always facing the camera) instead of chibis. */
const SPRITE = art("kuromi_3d");
const SPRITE_H = 1.15;

/** One person in the 3D office. Status and task come from the live API; everything else is decoration. */
export interface OfficePerson {
  id: string;
  name: string;
  jobTitle: string;
  kind: EmployeeKind;
  status: EmployeeStatus;
  taskTitle?: string;
}

export interface OfficeRoom {
  id: string;
  name: string;
  people: OfficePerson[];
}

const DESK_COLS = 3;
const DESK_DX = 1.7;
const DESK_DZ = 1.9;
const ROOM_GAP = 1.6; // corridor width between rooms
const ROOMS_PER_ROW = 3;
const WALK_SPEED = 1.1;
const RUSH_SPEED = 2.6; // back to the desk when work arrives

/** Pastel floor per room, cycling. */
const FLOORS = ["#F3E8FF", "#FCE7F3", "#E0F2FE", "#ECFCCB", "#FEF3C7", "#EDE9FE", "#FFE4E6", "#DCFCE7"];

/** Original chibi characters (not any brand's): picked from the name so a person always looks the same. */
const SPECIES = [
  { kind: "cat", body: "#C4B5FD", head: "#FFFFFF", ear: "#C4B5FD" },
  { kind: "bunny", body: "#F9A8D4", head: "#FFFFFF", ear: "#FBCFE8" },
  { kind: "bear", body: "#FCD34D", head: "#FEF3C7", ear: "#F59E0B" },
  { kind: "imp", body: brand.purple, head: "#E9D5FF", ear: brand.pink },
  { kind: "pup", body: "#93C5FD", head: "#FFFFFF", ear: "#60A5FA" },
  { kind: "sheep", body: "#FDE68A", head: "#FFFBEB", ear: "#FFFFFF" },
] as const;

function hash(s: string) {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 16777619);
  return h >>> 0;
}

/** Small seeded random generator, so each character has its own repeatable habits. */
function rng(seed: number) {
  let a = seed || 1;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Status bubble shown above a character. */
const BUBBLE: Partial<Record<EmployeeStatus, { icon: string; bg: string }>> = {
  Working: { icon: "⌨️", bg: "#EDE9FE" },
  WaitingForApproval: { icon: "✋", bg: "#FEF3C7" },
  Blocked: { icon: "🧱", bg: "#FFE4E6" },
  Failed: { icon: "⚠️", bg: "#FECACA" },
  Inactive: { icon: "💤", bg: "#E5E7EB" },
};
const ACTIVITY_BUBBLE = { lounge: { icon: "☕", bg: "#FCE7F3" }, visit: { icon: "💬", bg: "#E0F2FE" } } as const;

// ---------- layout and walking routes (all in office coordinates) ----------

type V2 = [number, number];
interface Cell { x0: number; z0: number; col: number; row: number }
interface Seat { key: string; x: number; z: number; y: number; face: V2 }
interface Layout { w: number; d: number; cells: Cell[]; lounge: Cell; seats: Seat[]; totalW: number; totalD: number }

function buildLayout(roomCount: number, maxPeople: number): Layout {
  const rows = Math.max(1, Math.ceil(maxPeople / DESK_COLS));
  const w = DESK_COLS * DESK_DX + 1.6;
  const d = Math.max(rows * DESK_DZ + 1.8, 5.6); // tall enough for the lounge furniture
  const count = roomCount + 1; // the last cell is the lounge
  const cols = Math.min(ROOMS_PER_ROW, count);
  const cells = Array.from({ length: count }, (_, i) => {
    const col = i % cols, row = Math.floor(i / cols);
    return { col, row, x0: col * (w + ROOM_GAP), z0: row * (d + ROOM_GAP) };
  });
  const lounge = cells[count - 1];
  const cx = lounge.x0 + w / 2, cz = lounge.z0 + d / 2;
  const seats: Seat[] = [
    ...[-0.75, 0, 0.75].map((dx, i) => ({ key: `sofa-a${i}`, x: cx + dx, z: cz - 1.25, y: 0.3, face: [0, 1] as V2 })),
    ...[-0.75, 0, 0.75].map((dx, i) => ({ key: `sofa-b${i}`, x: cx + dx, z: cz + 1.25, y: 0.3, face: [0, -1] as V2 })),
    { key: "bean-l", x: cx - 2.3, z: cz, y: 0.18, face: [1, 0] },
    { key: "bean-r", x: cx + 2.3, z: cz, y: 0.18, face: [-1, 0] },
  ];
  const rowsOfCells = Math.ceil(count / cols);
  return { w, d, cells, lounge, seats, totalW: cols * w + (cols - 1) * ROOM_GAP, totalD: rowsOfCells * d + (rowsOfCells - 1) * ROOM_GAP };
}

const aisleX = (L: Layout, c: Cell) => c.x0 + L.w - 0.45; // open right-hand aisle of every room
const corridorZ = (L: Layout, c: Cell) => c.z0 + L.d + ROOM_GAP / 2; // corridor in front of a row
const crossX = (L: Layout, c: Cell) => c.x0 + L.w + ROOM_GAP / 2; // corridor to the right of a column

/** Walks out by the room's aisle, along the corridors, and in by the other room's aisle — never through walls. */
function route(L: Layout, from: Cell, p: V2, to: Cell, q: V2): V2[] {
  const ax = aisleX(L, from), bx = aisleX(L, to);
  if (from === to) return [[ax, p[1]], [ax, q[1]], q];
  const path: V2[] = [[ax, p[1]], [ax, corridorZ(L, from)]];
  if (from.row !== to.row) path.push([crossX(L, from), corridorZ(L, from)], [crossX(L, from), corridorZ(L, to)]);
  path.push([bx, corridorZ(L, to)], [bx, q[1]], q);
  return path;
}

/** Shared between all characters: who sits where in the lounge. */
interface World { L: Layout; taken: Map<string, string> }

export default function Office3D({ rooms, selectedId, onSelect, onRoomClick, labels, loungeName, roomHint }: {
  rooms: OfficeRoom[];
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  /** Clicking a room's name sign; leave out to keep the signs read-only. */
  onRoomClick?: (roomId: string) => void;
  labels: (status: EmployeeStatus) => string;
  loungeName: string;
  roomHint?: string;
}) {
  const maxPeople = Math.max(1, ...rooms.map((r) => r.people.length));
  const L = useMemo(() => buildLayout(rooms.length, maxPeople), [rooms.length, maxPeople]);
  const [taken] = useState(() => new Map<string, string>());
  const world = useMemo<World>(() => ({ L, taken }), [L, taken]);
  const span = Math.max(L.totalW, L.totalD);

  return (
    <Canvas shadows flat dpr={[1, 2]} camera={{ position: [span * 0.32, span * 0.5, span * 0.62], fov: 40 }} onPointerMissed={() => onSelect(null)}>
      <ambientLight intensity={0.75} />
      <hemisphereLight args={["#ffffff", "#E9D5FF", 0.6]} />
      <directionalLight position={[span * 0.4, span, span * 0.3]} intensity={1.1} castShadow
        shadow-mapSize={[2048, 2048]} shadow-camera-left={-span} shadow-camera-right={span} shadow-camera-top={span} shadow-camera-bottom={-span} />
      <group position={[-L.totalW / 2, 0, -L.totalD / 2]}>
        {/* one connected floor: the corridors between and around the rooms */}
        <RoundedBox args={[L.totalW + ROOM_GAP * 2, 0.04, L.totalD + ROOM_GAP * 2]} radius={0.02} position={[L.totalW / 2, -0.03, L.totalD / 2]} receiveShadow>
          <meshStandardMaterial color="#FFF8FC" />
        </RoundedBox>
        {rooms.map((room, i) => (
          <Room key={room.id} room={room} index={i} cell={L.cells[i]} L={L} hint={roomHint}
            onClick={onRoomClick && (() => onRoomClick(room.id))} />
        ))}
        <Lounge L={L} name={loungeName} />
        {rooms.flatMap((room, i) => room.people.map((p, j) => (
          <Character key={p.id} person={p} home={deskSpot(L.cells[i], j)} homeCell={L.cells[i]} world={world}
            selected={selectedId === p.id} onSelect={onSelect} label={labels(p.status)} />
        )))}
      </group>
      <OrbitControls makeDefault enableDamping maxPolarAngle={Math.PI / 2.3} minDistance={4} maxDistance={span * 1.6} target={[0, 0, 0]} />
    </Canvas>
  );
}

/** Where person number j of a room works: the chair is at (x, z - 0.25), the desk in front of it. */
function deskSpot(cell: Pick<Cell, "x0" | "z0">, j: number): V2 {
  const col = j % DESK_COLS, row = Math.floor(j / DESK_COLS);
  return [cell.x0 + 0.9 + col * DESK_DX + DESK_DX / 2 - 0.1, cell.z0 + 1.35 + row * DESK_DZ];
}

function Walls({ L, floor }: { L: Layout; floor: string }) {
  return (
    <>
      {/* floor + low walls on the back and left, like a dollhouse cut open toward the camera */}
      <RoundedBox args={[L.w, 0.12, L.d]} radius={0.05} position={[L.w / 2, -0.06, L.d / 2]} receiveShadow>
        <meshStandardMaterial color={floor} />
      </RoundedBox>
      <mesh position={[L.w / 2, 0.45, 0.05]} castShadow receiveShadow>
        <boxGeometry args={[L.w, 0.9, 0.1]} />
        <meshStandardMaterial color="#FFFFFF" />
      </mesh>
      <mesh position={[0.05, 0.45, L.d / 2]} castShadow receiveShadow>
        <boxGeometry args={[0.1, 0.9, L.d]} />
        <meshStandardMaterial color="#FAF5FF" />
      </mesh>
    </>
  );
}

function Label({ position, text, onClick, hint }: { position: [number, number, number]; text: string; onClick?: () => void; hint?: string }) {
  const [hover, setHover] = useState(false);
  return (
    <Html position={position} center distanceFactor={18} zIndexRange={[10, 0]}>
      <div role={onClick ? "button" : undefined} title={hint} onClick={onClick}
        onPointerEnter={() => setHover(true)} onPointerLeave={() => setHover(false)}
        style={{
          whiteSpace: "nowrap", padding: "4px 12px", borderRadius: 999, fontSize: 14, fontWeight: 600, userSelect: "none",
          background: hover && onClick ? brand.purple : "rgba(255,255,255,.9)", color: hover && onClick ? "#fff" : brand.ink,
          boxShadow: "0 4px 14px rgba(110,80,170,.18)", cursor: onClick ? "pointer" : "default", pointerEvents: onClick ? "auto" : "none",
          transition: "background .15s, color .15s",
        }}>
        {text}{onClick && hover ? " ✎" : ""}
      </div>
    </Html>
  );
}

function Room({ room, index, cell, L, onClick, hint }: { room: OfficeRoom; index: number; cell: Cell; L: Layout; onClick?: () => void; hint?: string }) {
  const working = room.people.filter((p) => p.status === "Working").length;
  return (
    <group position={[cell.x0, 0, cell.z0]}>
      <Walls L={L} floor={FLOORS[index % FLOORS.length]} />
      <mesh position={[L.w * 0.75, 0.55, 0.11]}>
        <planeGeometry args={[1.1, 0.5]} />
        <meshStandardMaterial color="#C7D2FE" emissive="#A5B4FC" emissiveIntensity={0.25} />
      </mesh>
      <Plant position={[L.w - 0.35, 0, 0.4]} />
      <Label position={[L.w / 2, 2.1, -0.2]} text={`${room.name} · ${working}/${room.people.length}`} onClick={onClick} hint={hint} />
      {room.people.map((p, j) => {
        const [x, z] = deskSpot({ x0: 0, z0: 0 }, j);
        return <Desk key={p.id} position={[x, 0, z]} working={p.status === "Working"} />;
      })}
    </group>
  );
}

function Plant({ position, scale = 1 }: { position: [number, number, number]; scale?: number }) {
  return (
    <group position={position} scale={scale}>
      <mesh position-y={0.15} castShadow><cylinderGeometry args={[0.13, 0.1, 0.3, 16]} /><meshStandardMaterial color={brand.pinkSoft} /></mesh>
      <mesh position-y={0.42} castShadow><sphereGeometry args={[0.2, 16, 16]} /><meshStandardMaterial color="#86EFAC" /></mesh>
    </group>
  );
}

/** Desk, chair and monitor; the screen glows while its owner is working. */
function Desk({ position, working }: { position: [number, number, number]; working: boolean }) {
  const screen = useRef<THREE.MeshStandardMaterial>(null);
  useFrame(({ clock }) => {
    if (screen.current) screen.current.emissiveIntensity = working ? 0.7 + Math.sin(clock.elapsedTime * 4) * 0.25 : 0.05;
  });
  return (
    <group position={position}>
      <RoundedBox args={[1.2, 0.08, 0.6]} radius={0.03} position={[0, 0.62, 0.35]} castShadow receiveShadow>
        <meshStandardMaterial color="#FFFFFF" />
      </RoundedBox>
      {[-0.52, 0.52].map((lx) => (
        <mesh key={lx} position={[lx, 0.3, 0.35]} castShadow><boxGeometry args={[0.06, 0.6, 0.5]} /><meshStandardMaterial color={brand.lavender} /></mesh>
      ))}
      <mesh position={[0, 0.95, 0.52]} castShadow>
        <boxGeometry args={[0.62, 0.4, 0.04]} />
        <meshStandardMaterial color="#3A3150" />
      </mesh>
      <mesh position={[0, 0.95, 0.497]} rotation-y={Math.PI}>
        <planeGeometry args={[0.56, 0.34]} />
        <meshStandardMaterial ref={screen} color={working ? "#DDD6FE" : "#4B4166"} emissive={working ? "#C4B5FD" : "#000000"} />
      </mesh>
      <mesh position={[0, 0.7, 0.52]}><cylinderGeometry args={[0.03, 0.05, 0.12, 8]} /><meshStandardMaterial color="#3A3150" /></mesh>
      <mesh position={[0, 0.38, -0.25]} castShadow><cylinderGeometry args={[0.26, 0.26, 0.08, 20]} /><meshStandardMaterial color={brand.pink} /></mesh>
      <mesh position={[0, 0.19, -0.25]}><cylinderGeometry args={[0.04, 0.04, 0.34, 8]} /><meshStandardMaterial color="#9CA3AF" /></mesh>
    </group>
  );
}

/** The lounge: sofas around a coffee table, bean bags, a coffee bar and a bookshelf. */
function Lounge({ L, name }: { L: Layout; name: string }) {
  const c = L.lounge;
  const cx = L.w / 2, cz = L.d / 2;
  return (
    <group position={[c.x0, 0, c.z0]}>
      <Walls L={L} floor="#FFE9F3" />
      <Label position={[cx, 2.1, -0.2]} text={name} />
      {/* rug + coffee table */}
      <mesh rotation-x={-Math.PI / 2} position={[cx, 0.005, cz]} receiveShadow><circleGeometry args={[1.9, 48]} /><meshStandardMaterial color="#FBCFE8" /></mesh>
      <mesh position={[cx, 0.32, cz]} castShadow><cylinderGeometry args={[0.55, 0.55, 0.06, 32]} /><meshStandardMaterial color="#FFFFFF" /></mesh>
      <mesh position={[cx, 0.15, cz]}><cylinderGeometry args={[0.08, 0.12, 0.3, 12]} /><meshStandardMaterial color={brand.lavender} /></mesh>
      {[[-0.2, 0.1], [0.18, -0.12]].map(([dx, dz]) => (
        <mesh key={dx} position={[cx + dx, 0.41, cz + dz]} castShadow><cylinderGeometry args={[0.06, 0.05, 0.12, 12]} /><meshStandardMaterial color={brand.pink} /></mesh>
      ))}
      <Sofa position={[cx, 0, cz - 1.25]} facing={1} color="#C4B5FD" />
      <Sofa position={[cx, 0, cz + 1.25]} facing={-1} color="#F9A8D4" />
      {[-2.3, 2.3].map((dx) => (
        <mesh key={dx} position={[cx + dx, 0.14, cz]} scale={[1, 0.55, 1]} castShadow><sphereGeometry args={[0.36, 20, 16]} /><meshStandardMaterial color={dx < 0 ? "#FDE68A" : "#93C5FD"} /></mesh>
      ))}
      {/* coffee bar on the back wall */}
      <RoundedBox args={[2.2, 0.9, 0.5]} radius={0.05} position={[1.6, 0.45, 0.4]} castShadow receiveShadow><meshStandardMaterial color="#FFFFFF" /></RoundedBox>
      <mesh position={[1.1, 1.08, 0.4]} castShadow><boxGeometry args={[0.4, 0.36, 0.3]} /><meshStandardMaterial color="#3A3150" /></mesh>
      <mesh position={[1.1, 1.08, 0.56]}><circleGeometry args={[0.06, 16]} /><meshStandardMaterial color={brand.star} emissive={brand.star} emissiveIntensity={0.6} /></mesh>
      {[1.7, 1.95, 2.2].map((x, i) => (
        <mesh key={x} position={[x, 0.96, 0.4]} castShadow><cylinderGeometry args={[0.06, 0.05, 0.12, 12]} /><meshStandardMaterial color={[brand.pink, brand.lavender, "#86EFAC"][i]} /></mesh>
      ))}
      {/* bookshelf on the left wall */}
      <group position={[0.35, 0, L.d - 1.4]}>
        <mesh position-y={0.6} castShadow><boxGeometry args={[0.4, 1.2, 1.2]} /><meshStandardMaterial color="#FFFFFF" /></mesh>
        {[0.3, 0.7, 1.05].map((y, r) =>
          [-0.4, -0.15, 0.1, 0.35].map((dz, k) => (
            <mesh key={`${r}-${k}`} position={[0.08, y, dz]}><boxGeometry args={[0.28, 0.26, 0.16]} /><meshStandardMaterial color={["#C4B5FD", "#F9A8D4", "#93C5FD", "#FDE68A"][(k + r) % 4]} /></mesh>
          )))}
      </group>
      <Plant position={[L.w - 0.4, 0, 0.45]} scale={1.4} />
      <Plant position={[0.45, 0, 0.45]} scale={1.2} />
    </group>
  );
}

function Sofa({ position, facing, color }: { position: [number, number, number]; facing: 1 | -1; color: string }) {
  return (
    <group position={position} rotation-y={facing === 1 ? 0 : Math.PI}>
      <RoundedBox args={[2.4, 0.28, 0.75]} radius={0.1} position={[0, 0.16, 0]} castShadow receiveShadow><meshStandardMaterial color={color} /></RoundedBox>
      <RoundedBox args={[2.4, 0.5, 0.2]} radius={0.08} position={[0, 0.45, -0.38]} castShadow><meshStandardMaterial color={color} /></RoundedBox>
      {[-1.2, 1.2].map((x) => (
        <RoundedBox key={x} args={[0.2, 0.42, 0.75]} radius={0.08} position={[x, 0.3, 0]} castShadow><meshStandardMaterial color={color} /></RoundedBox>
      ))}
    </group>
  );
}

// ---------- characters ----------

type Mode = "home" | "walk" | "lounge" | "visit";

function Character({ person, home, homeCell, world, selected, onSelect, label }: {
  person: OfficePerson; home: V2; homeCell: Cell; world: World; selected: boolean; onSelect: (id: string | null) => void; label: string;
}) {
  const root = useRef<THREE.Group>(null);
  const armL = useRef<THREE.Mesh>(null);
  const armR = useRef<THREE.Mesh>(null);
  const facing = useRef<THREE.Group>(null); // sprite: turned toward the camera every frame
  const pose = useRef<THREE.Group>(null); // sprite: lean / hop / squash standing in for the arm poses
  const [q] = useState(() => new THREE.Quaternion());
  const [hover, setHover] = useState(false);
  const [activity, setActivity] = useState<"lounge" | "visit" | null>(null);
  const seed = useMemo(() => hash(person.name), [person.name]);
  const sp = SPECIES[seed % SPECIES.length];
  const phase = (seed % 1000) / 160;
  const status = person.status;
  const dim = status === "Inactive";
  const stand: V2 = [home[0], home[1] - 0.85];

  const st = useRef({
    mode: "home" as Mode, after: "home" as Mode, path: [] as V2[], speed: WALK_SPEED, rushing: false,
    cell: homeCell, dest: homeCell, x: stand[0], z: stand[1], heading: Math.PI, until: -1, seat: null as Seat | null, random: rng(seed),
  });

  const walkTo = (path: V2[], dest: Cell, after: Mode, speed = WALK_SPEED) => {
    Object.assign(st.current, { path, dest, after, speed, mode: "walk" as Mode });
    setActivity(null);
  };

  useFrame(({ clock }, delta) => {
    const g = root.current;
    if (!g) return;
    const s = st.current;
    const now = clock.elapsedTime;
    const t = now + phase;
    const dt = Math.min(delta, 0.5); // walking keeps time even when the browser slows frames down
    const { L, taken } = world;
    const available = status === "Available";
    if (s.until < 0) s.until = now + 2 + s.random() * 10;

    // Work (or any other status change) calls people back to their desk at once.
    if (!available && !s.rushing && s.mode !== "home") {
      if (s.seat) { taken.delete(s.seat.key); s.seat = null; }
      walkTo(route(L, s.cell, [s.x, s.z], homeCell, stand), homeCell, "home", RUSH_SPEED);
      s.rushing = true;
    }

    g.rotation.set(0, Math.PI, 0);
    g.scale.setScalar(1);
    armL.current?.rotation.set(0, 0, 0);
    armR.current?.rotation.set(0, 0, 0);
    const p = pose.current;
    p?.position.set(0, 0, 0);
    p?.rotation.set(0, 0, 0);
    p?.scale.set(1, 1, 1);

    if (s.mode === "walk") {
      const target = s.path[0];
      if (!target) {
        s.cell = s.dest;
        s.mode = s.after;
        s.rushing = false;
        s.until = now + (s.mode === "lounge" ? 12 + s.random() * 18 : s.mode === "visit" ? 6 + s.random() * 8 : 4 + s.random() * 10);
        if (s.mode === "lounge" || s.mode === "visit") setActivity(s.mode);
      } else {
        const dx = target[0] - s.x, dz = target[1] - s.z;
        const dist = Math.hypot(dx, dz);
        const step = s.speed * dt;
        if (dist <= step) { s.x = target[0]; s.z = target[1]; s.path.shift(); }
        else { s.x += (dx / dist) * step; s.z += (dz / dist) * step; s.heading = Math.atan2(-dx, -dz); }
      }
      const swing = Math.sin(t * s.speed * 6);
      g.position.set(s.x, Math.abs(swing) * 0.06, s.z);
      g.rotation.y = s.heading;
      if (armL.current) armL.current.rotation.x = swing * 0.6;
      if (armR.current) armR.current.rotation.x = -swing * 0.6;
      if (p) p.rotation.z = swing * 0.12; // waddle
      return;
    }

    if (s.mode === "lounge" && s.seat) {
      g.position.set(s.seat.x, s.seat.y, s.seat.z);
      g.rotation.y = Math.atan2(-s.seat.face[0], -s.seat.face[1]);
      if (armR.current) armR.current.rotation.x = Math.sin(t * 0.8) > 0.6 ? -2.2 : -0.6; // a sip now and then
      if (p) { p.rotation.z = Math.sin(t * 0.8) > 0.6 ? 0.12 : Math.sin(t * 0.5) * 0.04; p.scale.y = 1 + Math.sin(t * 1.5) * 0.015; }
      if (now > s.until) {
        const seat = s.seat;
        taken.delete(seat.key);
        s.seat = null;
        s.x = seat.x; s.z = seat.z;
        walkTo(route(L, L.lounge, [seat.x, seat.z], homeCell, stand), homeCell, "home");
      }
      return;
    }

    if (s.mode === "visit") {
      g.position.set(s.x, Math.abs(Math.sin(t * 2.5)) * 0.04, s.z);
      g.rotation.y = s.heading + Math.sin(t * 0.7) * 0.6;
      if (armL.current) armL.current.rotation.x = -0.5 + Math.sin(t * 3) * 0.4; // talking with their hands
      if (p) p.rotation.z = Math.sin(t * 3) * 0.08;
      if (now > s.until) walkTo(route(L, s.cell, [s.x, s.z], homeCell, stand), homeCell, "home");
      return;
    }

    // At the desk: the pose follows the live status.
    const seated = status === "Working" || status === "Blocked" || status === "Failed" || status === "Inactive";
    s.x = stand[0]; s.z = stand[1];
    g.position.set(home[0], seated ? 0.42 : 0, seated ? home[1] - 0.25 : stand[1]);
    if (status === "Working") {
      g.position.y += Math.abs(Math.sin(t * 6)) * 0.03;
      if (armL.current) armL.current.rotation.x = -1.2 + Math.sin(t * 14) * 0.25;
      if (armR.current) armR.current.rotation.x = -1.2 + Math.sin(t * 14 + 1.6) * 0.25;
      if (p) p.rotation.z = Math.sin(t * 14) * 0.03; // busy typing jitter
    } else if (available) {
      // strolls a small loop beside the desk, then now and then heads out
      const a = t * 0.6;
      s.x = stand[0] + Math.cos(a) * 0.5;
      s.z = stand[1] + Math.sin(a) * 0.15;
      g.position.set(s.x, Math.abs(Math.sin(t * 5)) * 0.05, s.z);
      g.rotation.y = -a + Math.PI;
      if (armL.current) armL.current.rotation.x = Math.sin(t * 5) * 0.5;
      if (armR.current) armR.current.rotation.x = -Math.sin(t * 5) * 0.5;
      if (p) p.rotation.z = Math.sin(t * 5) * 0.1;
      if (now > s.until) {
        const roll = s.random();
        const free = L.seats.filter((x) => !taken.has(x.key));
        const others = L.cells.filter((c) => c !== homeCell && c !== L.lounge);
        if (roll < 0.45 && free.length > 0) {
          const seat = free[Math.floor(s.random() * free.length)];
          taken.set(seat.key, person.id);
          s.seat = seat;
          walkTo(route(L, homeCell, [s.x, s.z], L.lounge, [seat.x, seat.z]), L.lounge, "lounge");
        } else if (roll < 0.85 && others.length > 0) {
          const cell = others[Math.floor(s.random() * others.length)];
          const spot: V2 = [cell.x0 + 1 + s.random() * (L.w - 2.4), cell.z0 + L.d - 0.55];
          walkTo(route(L, homeCell, [s.x, s.z], cell, spot), cell, "visit");
        } else {
          s.until = now + 5 + s.random() * 10;
        }
      }
    } else if (status === "WaitingForApproval") {
      g.position.y = Math.abs(Math.sin(t * 3)) * 0.08;
      if (armR.current) { armR.current.rotation.x = -2.8; armR.current.rotation.z = Math.sin(t * 6) * 0.35; }
      if (p) p.rotation.z = Math.sin(t * 6) * 0.15; // waving for attention
    } else if (status === "Failed") {
      g.position.x += Math.sin(t * 30) * 0.02;
      if (armL.current) armL.current.rotation.x = -0.4;
      if (armR.current) armR.current.rotation.x = -0.4;
      if (p) p.scale.y = 0.94;
    } else {
      // blocked / inactive: slumped over the desk, slow breathing
      g.rotation.x = status === "Inactive" ? 0.5 : 0.2; // Euler XYZ: the tilt is about world X, so + leans toward the desk
      g.scale.setScalar(1 + Math.sin(t * 1.5) * 0.015);
      if (armL.current) armL.current.rotation.x = -0.9;
      if (armR.current) armR.current.rotation.x = -0.9;
      // the sprite ignores the group's tilt (it faces the camera), so it droops sideways instead
      if (p) { p.rotation.z = status === "Inactive" ? 0.35 : 0.18; p.scale.y = (status === "Inactive" ? 0.88 : 0.94) + Math.sin(t * 1.5) * 0.015; }
    }
  });

  // Runs after the pose above: undo the group's turn so the sprite always looks at the camera.
  useFrame(({ camera }) => {
    if (!facing.current || !root.current) return;
    root.current.updateWorldMatrix(true, false);
    root.current.getWorldQuaternion(q);
    facing.current.quaternion.copy(q.invert().multiply(camera.quaternion));
  });

  const bubble = BUBBLE[status] ?? (activity ? ACTIVITY_BUBBLE[activity] : undefined);
  const mat = (color: string) => <meshStandardMaterial color={color} transparent={dim} opacity={dim ? 0.55 : 1} />;
  return (
    <group ref={root}
      onClick={(e: ThreeEvent<MouseEvent>) => { e.stopPropagation(); onSelect(person.id); }}
      onPointerOver={(e) => { e.stopPropagation(); setHover(true); document.body.style.cursor = "pointer"; }}
      onPointerOut={() => { setHover(false); document.body.style.cursor = ""; }}>
      {(selected || hover) && (
        <mesh rotation-x={-Math.PI / 2} position-y={0.02}>
          <ringGeometry args={[0.32, 0.4, 32]} />
          <meshBasicMaterial color={selected ? brand.pink : brand.purple} />
        </mesh>
      )}
      {SPRITE ? (
        <>
          <mesh rotation-x={-Math.PI / 2} position-y={0.012}>
            <circleGeometry args={[0.28, 24]} />
            <meshBasicMaterial color="#3A3150" transparent opacity={0.16} depthWrite={false} />
          </mesh>
          <group ref={facing}>
            <group ref={pose}>
              <Suspense fallback={null}>
                <SpriteBody src={SPRITE} dim={dim} glow={person.kind === "AI" ? (status === "Working" ? 1 : 0.2) : null} />
              </Suspense>
            </group>
          </group>
        </>
      ) : (
      <>
      <mesh position-y={0.32} castShadow>
        <capsuleGeometry args={[0.2, 0.2, 6, 16]} />
        {mat(sp.body)}
      </mesh>
      <mesh ref={armL} position={[-0.23, 0.42, 0]} castShadow><capsuleGeometry args={[0.055, 0.14, 4, 8]} />{mat(sp.body)}</mesh>
      <mesh ref={armR} position={[0.23, 0.42, 0]} castShadow><capsuleGeometry args={[0.055, 0.14, 4, 8]} />{mat(sp.body)}</mesh>
      <group position-y={0.78}>
        <mesh castShadow><sphereGeometry args={[0.3, 24, 24]} />{mat(sp.head)}</mesh>
        <Ears kind={sp.kind} color={sp.ear} dim={dim} />
        {/* face: local -z, which the 180° turn points toward the desk and the camera */}
        <mesh position={[-0.1, 0.02, -0.27]}><sphereGeometry args={[0.035, 12, 12]} /><meshBasicMaterial color="#2A2140" /></mesh>
        <mesh position={[0.1, 0.02, -0.27]}><sphereGeometry args={[0.035, 12, 12]} /><meshBasicMaterial color="#2A2140" /></mesh>
        <mesh position={[-0.17, -0.06, -0.24]}><sphereGeometry args={[0.045, 12, 12]} /><meshBasicMaterial color="#F9A8D4" /></mesh>
        <mesh position={[0.17, -0.06, -0.24]}><sphereGeometry args={[0.045, 12, 12]} /><meshBasicMaterial color="#F9A8D4" /></mesh>
        {person.kind === "AI" && (
          <group position-y={0.3}>
            <mesh position-y={0.08}><cylinderGeometry args={[0.012, 0.012, 0.16, 6]} /><meshStandardMaterial color="#6B7280" /></mesh>
            <mesh position-y={0.18}><sphereGeometry args={[0.045, 12, 12]} /><meshStandardMaterial color={brand.star} emissive={brand.star} emissiveIntensity={status === "Working" ? 1 : 0.2} /></mesh>
          </group>
        )}
      </group>
      </>
      )}
      {(bubble || hover || selected) && (
        <Html position={[0, SPRITE ? SPRITE_H + 0.35 : 1.45, 0]} center distanceFactor={12} zIndexRange={[20, 0]}>
          <div style={{ pointerEvents: "none", display: "flex", flexDirection: "column", alignItems: "center", gap: 2 }}>
            {(hover || selected) && (
              <div style={{ whiteSpace: "nowrap", padding: "3px 10px", borderRadius: 10, background: "rgba(255,255,255,.95)", fontSize: 12, color: brand.ink, boxShadow: "0 3px 10px rgba(0,0,0,.12)" }}>
                <b>{person.name}</b> · {label}
              </div>
            )}
            {bubble && <div style={{ width: 28, height: 28, borderRadius: "50%", display: "grid", placeItems: "center", background: bubble.bg, fontSize: 15, boxShadow: "0 3px 8px rgba(0,0,0,.12)" }}>{bubble.icon}</div>}
          </div>
        </Html>
      )}
    </group>
  );
}

/** The pack picture on a plane, pivoting at the feet; AI employees get a little glowing star by the head. */
function SpriteBody({ src, dim, glow }: { src: string; dim: boolean; glow: number | null }) {
  const texture = useTexture(src, (t) => { (t as THREE.Texture).colorSpace = THREE.SRGBColorSpace; });
  const img = texture.image as { width: number; height: number };
  const w = SPRITE_H * (img.width / img.height);
  return (
    <>
      <mesh position-y={SPRITE_H / 2}>
        <planeGeometry args={[w, SPRITE_H]} />
        <meshBasicMaterial map={texture} alphaTest={0.4} transparent={dim} opacity={dim ? 0.55 : 1} toneMapped={false} side={THREE.DoubleSide} />
      </mesh>
      {glow !== null && (
        <mesh position={[w * 0.42, SPRITE_H * 0.98, 0.02]}>
          <sphereGeometry args={[0.05, 12, 12]} />
          <meshStandardMaterial color={brand.star} emissive={brand.star} emissiveIntensity={glow} />
        </mesh>
      )}
    </>
  );
}

function Ears({ kind, color, dim }: { kind: (typeof SPECIES)[number]["kind"]; color: string; dim: boolean }) {
  const m = <meshStandardMaterial color={color} transparent={dim} opacity={dim ? 0.55 : 1} />;
  switch (kind) {
    case "cat":
      return <>{[-1, 1].map((s) => <mesh key={s} position={[s * 0.17, 0.26, 0]} rotation-z={-s * 0.35} castShadow><coneGeometry args={[0.09, 0.18, 12]} />{m}</mesh>)}</>;
    case "bunny":
      return <>{[-1, 1].map((s) => <mesh key={s} position={[s * 0.11, 0.42, 0]} rotation-z={-s * 0.15} castShadow><capsuleGeometry args={[0.06, 0.28, 4, 10]} />{m}</mesh>)}</>;
    case "bear":
      return <>{[-1, 1].map((s) => <mesh key={s} position={[s * 0.21, 0.21, 0]} castShadow><sphereGeometry args={[0.09, 14, 14]} />{m}</mesh>)}</>;
    case "imp":
      return <>{[-1, 1].map((s) => <mesh key={s} position={[s * 0.15, 0.3, 0]} rotation-z={-s * 0.25} castShadow><coneGeometry args={[0.06, 0.2, 4]} />{m}</mesh>)}</>;
    case "pup":
      return <>{[-1, 1].map((s) => <mesh key={s} position={[s * 0.29, 0.02, 0]} rotation-z={s * 0.3} scale={[0.6, 1.3, 0.6]} castShadow><sphereGeometry args={[0.1, 12, 12]} />{m}</mesh>)}</>;
    case "sheep":
      return <>{[-0.12, 0, 0.12].map((dx) => <mesh key={dx} position={[dx, 0.27, 0.02]} castShadow><sphereGeometry args={[0.09, 12, 12]} />{m}</mesh>)}</>;
  }
}
