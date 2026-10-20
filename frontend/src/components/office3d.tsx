"use client";

import { createContext, Suspense, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { createRoot, type Root } from "react-dom/client";
import { Canvas, useFrame, useThree, type ThreeEvent } from "@react-three/fiber";
import { OrbitControls, RoundedBox, Sparkles, Stars, useTexture } from "@react-three/drei";
import * as THREE from "three";
import { mergeGeometries } from "three/examples/jsm/utils/BufferGeometryUtils.js";
import type { EmployeeKind, EmployeeStatus } from "@/lib/api";
import { art } from "@/lib/characters";
import { brand } from "@/theme";

/** Cut-out figures from the character pack; when set, people are pictures (always facing the camera) instead of chibis. */
const SPRITE = art("kuromi_3d"); // department heads
const TEAM_SPRITES = [art("mymelody_3d"), art("pompompurin_3d"), art("cinnamoroll_3d")]; // everyone else, picked from the name
const SPRITE_H = 1.15;

/** One person in the 3D office. Status and task come from the live API; everything else is decoration. */
export interface OfficePerson {
  id: string;
  name: string;
  jobTitle: string;
  kind: EmployeeKind;
  /** Heads their department (shown as the head figure). */
  head?: boolean;
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
const ROOM_GAP = 1.6; // grass between neighbouring rooms, and the side street
const STREET = 2.6; // the cobbled street the rooms face
const ROOMS_PER_ROW = 4;
const WALK_SPEED = 1.1;
const RUSH_SPEED = 2.6; // back to the desk when work arrives

/** Pastel floor per room, cycling. */
const FLOORS = ["#F3E8FF", "#FCE7F3", "#E0F2FE", "#ECFCCB", "#FEF3C7", "#EDE9FE", "#FFE4E6", "#DCFCE7"];
/** Banner on each room's back wall, cycling. */
const BANNERS = [brand.purple, brand.pink, "#60A5FA", "#34D399", "#F59E0B", "#A78BFA", "#FB7185", "#2DD4BF"];

/** Fantasy materials: wood, stone, slate roofs, gold trim and glowing magic. */
const WOOD = "#C99B6D";
const WOOD_DARK = "#8A6246";
const STONE = "#E6DDF4";
const ROOF = "#7C5CC4";
const GOLD = "#E9C46A";
const LEAVES = ["#F9A8D4", "#C4B5FD", "#A7F3D0", "#FBCFE8"];
const MAGIC = ["#F472B6", "#A78BFA", "#60A5FA", "#34D399"];

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
/** A room's plot. `flip` rooms sit below their street and are turned around, so every gate opens onto the street. */
interface Cell { x0: number; z0: number; col: number; row: number; flip: boolean; streetZ: number }
interface Seat { key: string; x: number; z: number; y: number; face: V2 }
interface Layout { w: number; d: number; cells: Cell[]; lounge: Cell; seats: Seat[]; streets: number[]; cols: number; totalW: number; totalD: number }

/**
 * A village street: rooms stand in pairs of rows on both sides of a cobbled street, every gate facing it; with more
 * rooms than one street holds, the next pair of rows gets its own street and a side street joins them on the left.
 * Inside a room everything is in room coordinates: z = 0 is the back wall, z = d the gate side.
 */
function buildLayout(roomCount: number, maxPeople: number): Layout {
  const rows = Math.max(1, Math.ceil(maxPeople / DESK_COLS));
  const w = DESK_COLS * DESK_DX + 1.6;
  const d = Math.max(rows * DESK_DZ + 1.8, 5.6); // tall enough for the lounge furniture
  const count = roomCount + 1; // the last cell is the lounge
  const cols = Math.min(ROOMS_PER_ROW, Math.ceil(count / 2));
  const pairH = 2 * d + STREET + ROOM_GAP;
  const cells = Array.from({ length: count }, (_, i) => {
    const pair = Math.floor(i / (2 * cols)), k = i % (2 * cols);
    const flip = k >= cols, col = k % cols;
    const top = pair * pairH;
    return { col, row: pair * 2 + (flip ? 1 : 0), flip, x0: col * (w + ROOM_GAP), z0: flip ? top + d + STREET : top, streetZ: top + d + STREET / 2 };
  });
  const lounge = cells[count - 1];
  const at = (lx: number, lz: number) => toWorld({ w, d } as Layout, lounge, lx, lz);
  const turn = (f: V2): V2 => (lounge.flip ? [-f[0], -f[1]] : f);
  const cx = w / 2, cz = d / 2;
  const seat = (key: string, lx: number, lz: number, y: number, face: V2): Seat => {
    const [x, z] = at(lx, lz);
    return { key, x, z, y, face: turn(face) };
  };
  const seats: Seat[] = [
    ...[-0.75, 0, 0.75].map((dx, i) => seat(`sofa-a${i}`, cx + dx, cz - 1.25, 0.3, [0, 1])),
    ...[-0.75, 0, 0.75].map((dx, i) => seat(`sofa-b${i}`, cx + dx, cz + 1.25, 0.3, [0, -1])),
    seat("bean-l", cx - 2.3, cz, 0.18, [1, 0]),
    seat("bean-r", cx + 2.3, cz, 0.18, [-1, 0]),
  ];
  const streets = [...new Set(cells.map((c) => c.streetZ))];
  const last = streets[streets.length - 1];
  return {
    w, d, cells, lounge, seats, streets, cols,
    totalW: cols * w + (cols - 1) * ROOM_GAP,
    totalD: Math.max(...cells.map((c) => c.z0 + d), last + STREET / 2),
  };
}

/** Room coordinates to office coordinates (a flipped room is turned 180° inside its plot). */
function toWorld(L: Layout, c: Cell, lx: number, lz: number): V2 {
  return c.flip ? [c.x0 + L.w - lx, c.z0 + L.d - lz] : [c.x0 + lx, c.z0 + lz];
}
const toLocal = toWorld; // turning 180° twice is the identity, so the same formula maps back

const AISLE = 0.45; // the free aisle runs inside the right-hand wall, from the desks to the gate
const SIDE_STREET_X = -ROOM_GAP / 2;

/** Out along the aisle, through the gate, along the street (and the side street between streets), in through the other gate. */
function route(L: Layout, from: Cell, p: V2, to: Cell, q: V2): V2[] {
  const ax = L.w - AISLE;
  const pz = toLocal(L, from, p[0], p[1])[1], qz = toLocal(L, to, q[0], q[1])[1];
  if (from === to) return [toWorld(L, from, ax, pz), toWorld(L, from, ax, qz), q];
  const outGate = toWorld(L, from, ax, L.d), inGate = toWorld(L, to, ax, L.d);
  const path: V2[] = [toWorld(L, from, ax, pz), outGate, [outGate[0], from.streetZ]];
  if (from.streetZ !== to.streetZ) path.push([SIDE_STREET_X, from.streetZ], [SIDE_STREET_X, to.streetZ]);
  path.push([inGate[0], to.streetZ], inGate, toWorld(L, to, ax, qz), q);
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
  const kit = useMemo(() => makeKit(L), [L]);
  useEffect(() => () => { Object.values(kit).forEach((x) => x.dispose()); }, [kit]);

  return (
    <Canvas shadows={{ type: THREE.PCFShadowMap }} flat dpr={[1, 2]} camera={{ position: [span * 0.32, span * 0.5, span * 0.62], fov: 40, far: span * 20 }} onPointerMissed={() => onSelect(null)}>
      <fog attach="fog" args={["#D9C6F2", span * 1.8, span * 5]} />
      <SkyDome radius={span * 8} />
      <Stars radius={span * 4} depth={span * 2} count={1800} factor={5} saturation={0.4} fade speed={0.6} />
      <mesh position={[-span * 3, span * 2.2, -span * 4]}>
        <sphereGeometry args={[span * 0.35, 32, 16]} />
        <meshBasicMaterial color="#FFF6D6" fog={false} />
      </mesh>
      <ambientLight intensity={0.6} color="#EDE4FF" />
      <hemisphereLight args={["#FFE9F6", "#5B4391", 0.6]} />
      <directionalLight color="#FFF1DE" position={[span * 0.4, span, span * 0.3]} intensity={1.1} castShadow
        shadow-mapSize={[2048, 2048]} shadow-camera-left={-span} shadow-camera-right={span} shadow-camera-top={span} shadow-camera-bottom={-span} />
      <Island L={L} />
      <Clouds span={span} />
      <KitContext.Provider value={kit}>
        <group position={[-L.totalW / 2, 0, -L.totalD / 2]}>
          {/* cobbled streets between the rows, and the side street joining them */}
          {L.streets.map((z) => (
            <RoundedBox key={z} args={[L.totalW + ROOM_GAP * 2, 0.04, STREET]} radius={0.02} position={[L.totalW / 2, -0.03, z]} receiveShadow>
              <meshStandardMaterial color="#EEE6F7" map={kit.path} />
            </RoundedBox>
          ))}
          {L.streets.length > 1 && (
            <RoundedBox args={[ROOM_GAP, 0.04, L.streets[L.streets.length - 1] - L.streets[0]]} radius={0.02}
              position={[SIDE_STREET_X, -0.03, (L.streets[0] + L.streets[L.streets.length - 1]) / 2]} receiveShadow>
              <meshStandardMaterial color="#EEE6F7" map={kit.sidePath} />
            </RoundedBox>
          )}
          {rooms.map((room, i) => (
            <Room key={room.id} room={room} index={i} cell={L.cells[i]} L={L} hint={roomHint}
              onClick={onRoomClick && (() => onRoomClick(room.id))} />
          ))}
          <Lounge L={L} name={loungeName} />
          <Lanterns L={L} />
          {rooms.flatMap((room, i) => room.people.map((p, j) => (
            <Character key={p.id} person={p} home={toWorld(L, L.cells[i], ...deskSpot(j))} homeCell={L.cells[i]} world={world}
              selected={selectedId === p.id} onSelect={onSelect} label={labels(p.status)} />
          )))}
        </group>
      </KitContext.Provider>
      {/* fireflies drifting over the whole island */}
      <Sparkles count={Math.round(60 + span * 6)} scale={[L.totalW + 4, 3, L.totalD + 4]} position={[0, 1.6, 0]} size={5} speed={0.35} opacity={0.9} color={brand.star} />
      <OrbitControls makeDefault enableDamping maxPolarAngle={Math.PI / 2.3} minDistance={4} maxDistance={span * 1.6} target={[0, 0, 0]} />
    </Canvas>
  );
}

// ---------- the fantasy world around the office ----------

/** Shared textures and geometry, made once per layout. */
interface Kit {
  floor: THREE.Texture;
  wallBack: THREE.Texture;
  wallSide: THREE.Texture;
  path: THREE.Texture;
  sidePath: THREE.Texture;
  /** Battlements on the tall walls: rooms whose back faces away from the camera, and turned-around rooms. */
  merlons: THREE.BufferGeometry;
  merlonsFlip: THREE.BufferGeometry;
}
const KitContext = createContext<Kit | null>(null);

function canvasTexture(w: number, h: number, repeat: V2, draw: (g: CanvasRenderingContext2D, r: () => number) => void) {
  const c = document.createElement("canvas");
  c.width = w;
  c.height = h;
  draw(c.getContext("2d")!, rng(w * 31 + h));
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(...repeat);
  t.anisotropy = 4;
  return t;
}

/** Light patterns that each material tints with its own colour: wooden planks, stone blocks, cobbles. */
function makeKit(L: Layout): Kit {
  const planks = (repeat: V2) => canvasTexture(256, 256, repeat, (g, r) => {
    g.fillStyle = "#fff";
    g.fillRect(0, 0, 256, 256);
    for (let y = 0; y < 256; y += 32) {
      g.fillStyle = `rgba(110,80,50,${0.03 + r() * 0.07})`;
      g.fillRect(0, y, 256, 32);
      g.strokeStyle = "rgba(110,80,50,.35)";
      g.lineWidth = 2;
      const cut = r() * 256;
      g.beginPath(); g.moveTo(0, y); g.lineTo(256, y); g.moveTo(cut, y); g.lineTo(cut, y + 32); g.stroke();
      g.strokeStyle = "rgba(110,80,50,.1)";
      g.lineWidth = 1;
      for (let k = 0; k < 3; k++) {
        const gy = y + 6 + r() * 20;
        g.beginPath(); g.moveTo(0, gy); g.bezierCurveTo(80, gy + 3, 170, gy - 3, 256, gy); g.stroke();
      }
    }
  });
  const bricks = (repeat: V2) => canvasTexture(256, 128, repeat, (g, r) => {
    g.fillStyle = "#fff";
    g.fillRect(0, 0, 256, 128);
    g.strokeStyle = "rgba(90,70,140,.35)";
    g.lineWidth = 3;
    for (let row = 0; row < 4; row++) {
      for (let x = row % 2 ? -32 : 0; x < 256; x += 64) {
        g.fillStyle = `rgba(90,70,140,${r() * 0.1})`;
        g.fillRect(x, row * 32, 64, 32);
        g.strokeRect(x, row * 32, 64, 32);
      }
    }
  });
  const cobbles = (repeat: V2) => canvasTexture(256, 256, repeat, (g, r) => {
    g.fillStyle = "#fff";
    g.fillRect(0, 0, 256, 256);
    g.strokeStyle = "rgba(100,90,130,.3)";
    g.lineWidth = 3;
    for (let y = 0; y < 256; y += 51.2) {
      for (let x = 0; x < 256; x += 51.2) {
        g.fillStyle = `rgba(100,90,130,${0.04 + r() * 0.1})`;
        g.beginPath();
        g.roundRect(x + 3 + r() * 4, y + 3 + r() * 4, 40 + r() * 4, 40 + r() * 4, 12);
        g.fill(); g.stroke();
      }
    }
  });
  const battlements = (flip: boolean) => {
    const parts: THREE.BufferGeometry[] = [];
    const add = (x: number, z: number, alongX: boolean) =>
      parts.push(new THREE.BoxGeometry(alongX ? 0.24 : 0.14, 0.16, alongX ? 0.14 : 0.24).translate(x, 0.98, z));
    // the tall long wall is the back one, or for a turned-around room the gate wall (up to the gate)
    if (flip) for (let x = 0.75; x < L.w - 1.1; x += 0.5) add(x, L.d - 0.05, true);
    else for (let x = 0.75; x < L.w - 0.1; x += 0.5) add(x, 0.05, true);
    for (let z = flip ? 0.25 : 0.75; z < L.d - (flip ? 0.75 : 0.1); z += 0.5) add(0.05, z, false);
    for (let z = 0.25; z < L.d - 0.1; z += 0.5) add(L.w - 0.05, z, false);
    const g = mergeGeometries(parts)!;
    parts.forEach((p) => p.dispose());
    return g;
  };
  return {
    floor: planks([L.w / 2, L.d / 2]),
    wallBack: bricks([L.w / 1.6, 1]),
    wallSide: bricks([L.d / 1.6, 1]),
    path: cobbles([(L.totalW + ROOM_GAP * 2) / 2.5, STREET / 2.5]),
    sidePath: cobbles([ROOM_GAP / 2.5, Math.max(1, (L.streets[L.streets.length - 1] - L.streets[0]) / 2.5)]),
    merlons: battlements(false),
    merlonsFlip: battlements(true),
  };
}

/** Twilight sky: deep violet overhead, pink at the horizon, lavender haze below the island. */
function SkyDome({ radius }: { radius: number }) {
  const material = useMemo(() => new THREE.ShaderMaterial({
    side: THREE.BackSide,
    depthWrite: false,
    uniforms: {
      top: { value: new THREE.Color("#2B1B58") },
      mid: { value: new THREE.Color("#8E6BD6") },
      horizon: { value: new THREE.Color("#F7B9D7") },
      below: { value: new THREE.Color("#B9A0EC") },
    },
    vertexShader: `varying vec3 vDir;
void main() { vDir = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`,
    fragmentShader: `uniform vec3 top; uniform vec3 mid; uniform vec3 horizon; uniform vec3 below; varying vec3 vDir;
void main() {
  float h = vDir.y;
  vec3 c = h > 0.0 ? (h < 0.22 ? mix(horizon, mid, h / 0.22) : mix(mid, top, smoothstep(0.22, 0.8, h))) : mix(horizon, below, smoothstep(0.0, 0.35, -h));
  gl_FragColor = vec4(c, 1.0);
  #include <colorspace_fragment>
}`,
  }), []);
  useEffect(() => () => material.dispose(), [material]);
  return <mesh material={material} renderOrder={-1}><sphereGeometry args={[radius, 32, 16]} /></mesh>;
}

let rockGeometry: THREE.BufferGeometry | null = null;
/** A cone of rock with a jagged surface; points at the same place move the same way, so the seams stay closed. */
function rock() {
  if (rockGeometry) return rockGeometry;
  const g = new THREE.CylinderGeometry(1, 0.08, 1, 12, 5);
  const p = g.attributes.position;
  for (let i = 0; i < p.count; i++) {
    const x = p.getX(i), y = p.getY(i), z = p.getZ(i);
    if (y > 0.49) continue;
    const r = rng(hash(`${Math.round(x * 1000)},${Math.round(y * 1000)},${Math.round(z * 1000)}`));
    const k = 1 + (r() - 0.5) * 0.35;
    p.setXYZ(i, x * k, y + (r() - 0.5) * 0.08, z * k);
  }
  g.computeVertexNormals();
  return (rockGeometry = g);
}

interface Prop { kind: "tree" | "mushroom" | "crystal"; x: number; z: number; s: number; hue: number }

/** Trees, glowing mushrooms and crystals on the grass around the paving; the side facing the camera stays low. */
function scatter(L: Layout, a: number, b: number): Prop[] {
  const r = rng(L.cells.length * 977 + 13);
  const hx = L.totalW / 2 + ROOM_GAP + 0.35, hz = L.totalD / 2 + ROOM_GAP + 0.35;
  const out: Prop[] = [];
  for (let tries = 0; tries < 400 && out.length < 34; tries++) {
    const th = r() * Math.PI * 2, k = 0.55 + r() * 0.38;
    const x = Math.cos(th) * a * k, z = Math.sin(th) * b * k;
    if (Math.abs(x) < hx && Math.abs(z) < hz) continue;
    if (out.some((p) => Math.hypot(p.x - x, p.z - z) < 0.9)) continue;
    const roll = r();
    const kind = z > 0 ? (roll < 0.6 ? "mushroom" : "crystal") : roll < 0.55 ? "tree" : roll < 0.8 ? "mushroom" : "crystal";
    out.push({ kind, x, z, s: 0.8 + r() * 0.6, hue: Math.floor(r() * 4) });
  }
  return out;
}

/** The office floats on a grassy island in the sky, with a few small islets drifting around it. */
function Island({ L }: { L: Layout }) {
  const a = (L.totalW / 2 + ROOM_GAP) * 1.45, b = (L.totalD / 2 + ROOM_GAP) * 1.45;
  const depth = Math.max(a, b) * 0.9;
  const props = useMemo(() => scatter(L, a, b), [L, a, b]);
  const islets = useMemo(() => [0.7, 2.3, 3.7, 5.3].map((th, i) => {
    const x = Math.cos(th) * a * 1.4, z = Math.sin(th) * b * 1.4;
    return { x, z, y: z > 0 ? -1.8 : 0.5 + i * 0.25, phase: i * 1.7 };
  }), [a, b]);
  return (
    <group>
      <mesh position-y={-0.2} scale={[a, 1, b]} receiveShadow><cylinderGeometry args={[1, 1, 0.24, 48]} /><meshStandardMaterial color="#A6DDB0" /></mesh>
      <mesh position-y={-0.42} scale={[a, 1, b]}><cylinderGeometry args={[1, 0.97, 0.22, 48]} /><meshStandardMaterial color="#B98F72" /></mesh>
      <mesh geometry={rock()} dispose={null} position-y={-0.53 - depth / 2} scale={[a * 0.97, depth, b * 0.97]}>
        <meshStandardMaterial color="#7D6AA6" flatShading />
      </mesh>
      {props.map((p, i) => {
        const at: [number, number, number] = [p.x, -0.08, p.z];
        if (p.kind === "tree") return <Tree key={i} position={at} scale={p.s} color={LEAVES[p.hue]} />;
        if (p.kind === "mushroom") return <Mushrooms key={i} position={at} scale={p.s} hue={p.hue} />;
        return <Crystal key={i} position={at} scale={p.s} hue={p.hue} />;
      })}
      {islets.map((it, i) => (
        <Islet key={i} position={[it.x, it.y, it.z]} phase={it.phase}>
          {i % 2 ? <Tree position={[0, 0.07, 0]} scale={0.8} color={LEAVES[i]} /> : <Crystal position={[0, 0.07, 0]} scale={1.3} hue={i} />}
        </Islet>
      ))}
    </group>
  );
}

function Islet({ position, phase, children }: { position: [number, number, number]; phase: number; children: React.ReactNode }) {
  const g = useRef<THREE.Group>(null);
  useFrame(({ clock }) => { if (g.current) g.current.position.y = position[1] + Math.sin(clock.elapsedTime * 0.6 + phase) * 0.25; });
  return (
    <group ref={g} position={position}>
      <mesh scale={[0.9, 1, 0.9]}><cylinderGeometry args={[1, 1, 0.14, 12]} /><meshStandardMaterial color="#A6DDB0" /></mesh>
      <mesh geometry={rock()} dispose={null} position-y={-0.67} scale={[0.85, 1.2, 0.85]}><meshStandardMaterial color="#7D6AA6" flatShading /></mesh>
      {children}
    </group>
  );
}

/** Puffy pink-white clouds slowly circling below the island. */
function Clouds({ span }: { span: number }) {
  const g = useRef<THREE.Group>(null);
  const puffs = useMemo(() => {
    const r = rng(42);
    return Array.from({ length: 14 }, (_, i) => {
      const th = (i / 14) * Math.PI * 2 + r() * 0.3, d = span * (0.9 + r() * 0.9);
      return { x: Math.cos(th) * d, z: Math.sin(th) * d, y: -2.5 - r() * 3, s: 0.8 + r() * 1.4 };
    });
  }, [span]);
  useFrame((_, dt) => { if (g.current) g.current.rotation.y += dt * 0.01; });
  return (
    <group ref={g}>
      {puffs.map((p, i) => (
        <group key={i} position={[p.x, p.y, p.z]} scale={p.s}>
          {[[0, 0, 0, 1], [0.9, -0.1, 0.2, 0.75], [-0.85, -0.15, -0.1, 0.7], [0.3, 0.35, -0.2, 0.65]].map(([x, y, z, r], k) => (
            <mesh key={k} position={[x, y, z]}><sphereGeometry args={[r, 16, 12]} /><meshStandardMaterial color="#FFF1F8" emissive="#F5D0FE" emissiveIntensity={0.25} /></mesh>
          ))}
        </group>
      ))}
    </group>
  );
}

/** Lamp posts at the mouths of the gaps between rooms, and lanterns floating over the streets. */
function Lanterns({ L }: { L: Layout }) {
  const floating = useMemo(() => L.streets.flatMap((z) => Array.from({ length: L.cols }, (_, c) => [c * (L.w + ROOM_GAP) + L.w / 2, z] as V2)), [L]);
  const posts = useMemo(() => {
    const xs = [...Array.from({ length: L.cols - 1 }, (_, c) => (c + 1) * (L.w + ROOM_GAP) - ROOM_GAP / 2), L.totalW + ROOM_GAP * 0.6];
    return L.streets.flatMap((z) => xs.flatMap((x) => [[x, z - STREET / 2 + 0.2], [x, z + STREET / 2 - 0.2]] as V2[]));
  }, [L]);
  const refs = useRef<(THREE.Group | null)[]>([]);
  useFrame(({ clock }) => refs.current.forEach((g, i) => {
    if (!g) return;
    g.position.y = 2.4 + Math.sin(clock.elapsedTime * 0.9 + i * 1.3) * 0.12;
    g.rotation.y = clock.elapsedTime * 0.3 + i;
  }));
  return (
    <>
      {floating.map(([x, z], i) => (
        <group key={i} ref={(g) => { refs.current[i] = g; }} position={[x, 2.4, z]}>
          <Lantern />
        </group>
      ))}
      {posts.map(([x, z], i) => (
        <group key={`p${i}`} position={[x, 0, z]}>
          <mesh position-y={0.85} castShadow><cylinderGeometry args={[0.035, 0.055, 1.7, 8]} /><meshStandardMaterial color="#3A3150" /></mesh>
          <mesh position-y={0.04}><cylinderGeometry args={[0.1, 0.12, 0.08, 10]} /><meshStandardMaterial color="#3A3150" /></mesh>
          <group position-y={1.82}><Lantern /></group>
        </group>
      ))}
    </>
  );
}

function Lantern() {
  return (
    <>
      <mesh position-y={0.17} rotation-y={Math.PI / 4}><coneGeometry args={[0.15, 0.12, 4]} /><meshStandardMaterial color={brand.purpleDeep} /></mesh>
      <mesh><boxGeometry args={[0.16, 0.22, 0.16]} /><meshStandardMaterial color="#FFE7A8" emissive="#FFC75A" emissiveIntensity={1} /></mesh>
      <mesh position-y={-0.13}><boxGeometry args={[0.19, 0.03, 0.19]} /><meshStandardMaterial color={brand.purpleDeep} /></mesh>
    </>
  );
}

function Tree({ position, scale = 1, color }: { position: [number, number, number]; scale?: number; color: string }) {
  return (
    <group position={position} scale={scale}>
      <mesh position-y={0.45} castShadow><cylinderGeometry args={[0.07, 0.12, 0.9, 8]} /><meshStandardMaterial color={WOOD_DARK} /></mesh>
      {[[0, 1.05, 0, 0.42], [0.24, 0.9, 0.1, 0.3], [-0.22, 0.92, -0.05, 0.32], [0.02, 1.38, 0.02, 0.28]].map(([x, y, z, r], i) => (
        <mesh key={i} position={[x, y, z]} castShadow><icosahedronGeometry args={[r, 1]} /><meshStandardMaterial color={color} flatShading /></mesh>
      ))}
      {[[0.3, 1.1, 0.25], [-0.28, 1.2, 0.2], [0.1, 0.85, 0.36]].map(([x, y, z], i) => (
        <mesh key={i} position={[x, y, z]}><sphereGeometry args={[0.04, 8, 8]} /><meshStandardMaterial color={brand.star} emissive={brand.star} emissiveIntensity={1} /></mesh>
      ))}
    </group>
  );
}

/** A little cluster of glowing mushrooms. */
function Mushrooms({ position, scale = 1, hue = 0 }: { position: [number, number, number]; scale?: number; hue?: number }) {
  const c = MAGIC[hue % MAGIC.length];
  return (
    <group position={position} scale={scale}>
      {[[0, 0, 1], [0.18, 0.08, 0.7], [-0.14, 0.12, 0.55]].map(([x, z, s], i) => (
        <group key={i} position={[x, 0, z]} scale={s}>
          <mesh position-y={0.15} castShadow><cylinderGeometry args={[0.05, 0.07, 0.3, 10]} /><meshStandardMaterial color="#FFF4E0" /></mesh>
          <mesh position-y={0.32} scale={[1, 0.6, 1]} castShadow><sphereGeometry args={[0.17, 16, 12]} /><meshStandardMaterial color={c} emissive={c} emissiveIntensity={0.55} /></mesh>
        </group>
      ))}
    </group>
  );
}

/** Glowing crystals growing out of the ground. */
function Crystal({ position, scale = 1, hue = 0 }: { position: [number, number, number]; scale?: number; hue?: number }) {
  const c = MAGIC[(hue + 1) % MAGIC.length];
  return (
    <group position={position} scale={scale}>
      {[[0, 0.38, 0, 0], [0.14, 0.24, 0.06, -0.5], [-0.12, 0.2, -0.04, 0.45]].map(([x, h, z, tilt], i) => (
        <mesh key={i} position={[x, h * 0.55, z]} rotation-z={tilt} scale={[0.09, h, 0.09]} castShadow>
          <octahedronGeometry args={[1]} />
          <meshStandardMaterial color={c} emissive={c} emissiveIntensity={0.6} flatShading transparent opacity={0.9} />
        </mesh>
      ))}
    </group>
  );
}

/** Where person number j works, in room coordinates: the chair is at (x, z - 0.25), the desk in front of it, facing the gate. */
function deskSpot(j: number): V2 {
  const col = j % DESK_COLS, row = Math.floor(j / DESK_COLS);
  return [0.9 + col * DESK_DX + DESK_DX / 2 - 0.1, 1.35 + row * DESK_DZ];
}

/** A guild hall: plank floor, stone walls all round with a gate onto the street, and a tower in the corner away from the camera. */
function Walls({ L, floor, banner, flip }: { L: Layout; floor: string; banner: string; flip: boolean }) {
  const kit = useContext(KitContext);
  // the wall on the camera side stays low, like a dollhouse cut open toward the viewer
  const backH = flip ? 0.45 : 0.9, frontH = flip ? 0.9 : 0.45;
  const gateL = L.w - 0.85; // the gate wall runs from the left corner to the gate
  return (
    <>
      <RoundedBox args={[L.w, 0.12, L.d]} radius={0.05} position={[L.w / 2, -0.06, L.d / 2]} receiveShadow>
        <meshStandardMaterial color={floor} map={kit?.floor} />
      </RoundedBox>
      <mesh position={[L.w / 2, backH / 2, 0.05]} castShadow receiveShadow>
        <boxGeometry args={[L.w, backH, 0.1]} />
        <meshStandardMaterial color={STONE} map={kit?.wallBack} />
      </mesh>
      <mesh position={[gateL / 2, frontH / 2, L.d - 0.05]} castShadow receiveShadow>
        <boxGeometry args={[gateL, frontH, 0.1]} />
        <meshStandardMaterial color={STONE} map={kit?.wallBack} />
      </mesh>
      {[0.05, L.w - 0.05].map((x) => (
        <mesh key={x} position={[x, 0.45, L.d / 2]} castShadow receiveShadow>
          <boxGeometry args={[0.1, 0.9, L.d]} />
          <meshStandardMaterial color="#DCD1EE" map={kit?.wallSide} />
        </mesh>
      ))}
      {kit && <mesh geometry={flip ? kit.merlonsFlip : kit.merlons} dispose={null} castShadow><meshStandardMaterial color={STONE} /></mesh>}
      <Gate position={[L.w - AISLE, 0, L.d - 0.05]} color={banner} />
      <group position={[0.1, 0, flip ? L.d - 0.1 : 0.1]}>
        <mesh position-y={0.85} castShadow><cylinderGeometry args={[0.36, 0.42, 1.7, 14]} /><meshStandardMaterial color={STONE} /></mesh>
        <mesh position-y={1.72}><cylinderGeometry args={[0.45, 0.45, 0.1, 14]} /><meshStandardMaterial color="#CFC2E6" /></mesh>
        <mesh position-y={2.18} castShadow><coneGeometry args={[0.52, 0.85, 14]} /><meshStandardMaterial color={ROOF} /></mesh>
        {/* window toward the camera */}
        <mesh position={[0.27, 1.25, flip ? -0.27 : 0.27]} rotation-y={flip ? (Math.PI * 3) / 4 : Math.PI / 4}>
          <planeGeometry args={[0.14, 0.22]} />
          <meshStandardMaterial color="#FFE7A8" emissive="#FFC75A" emissiveIntensity={0.9} />
        </mesh>
        <mesh position-y={2.75}><cylinderGeometry args={[0.012, 0.012, 0.35, 6]} /><meshStandardMaterial color={GOLD} /></mesh>
        <mesh position={[0.1, 2.85, 0]}><planeGeometry args={[0.2, 0.12]} /><meshStandardMaterial color={banner} side={THREE.DoubleSide} /></mesh>
      </group>
    </>
  );
}

/** Stone gate posts under a slate arch with a glowing keystone and a lantern, opening onto the street. */
function Gate({ position, color }: { position: [number, number, number]; color: string }) {
  return (
    <group position={position}>
      {[-0.4, 0.4].map((x) => (
        <group key={x} position-x={x}>
          <mesh position-y={0.65} castShadow><boxGeometry args={[0.16, 1.3, 0.16]} /><meshStandardMaterial color={STONE} /></mesh>
          <mesh position-y={1.33}><boxGeometry args={[0.2, 0.06, 0.2]} /><meshStandardMaterial color="#CFC2E6" /></mesh>
        </group>
      ))}
      <mesh position-y={1.33} castShadow><torusGeometry args={[0.4, 0.06, 8, 24, Math.PI]} /><meshStandardMaterial color={ROOF} /></mesh>
      <mesh position-y={1.75} scale={[0.07, 0.1, 0.07]}><octahedronGeometry args={[1]} /><meshStandardMaterial color={GOLD} emissive={GOLD} emissiveIntensity={0.8} flatShading /></mesh>
      <mesh position-y={1.6}><cylinderGeometry args={[0.008, 0.008, 0.14, 4]} /><meshStandardMaterial color="#3A3150" /></mesh>
      <group position-y={1.45} scale={0.6}><Lantern /></group>
      {/* doormat in the room's colour */}
      <mesh position={[0, 0.004, 0.32]} rotation-x={-Math.PI / 2}><planeGeometry args={[0.7, 0.45]} /><meshStandardMaterial color={color} /></mesh>
    </group>
  );
}

/** The room's banner on the back wall, with a golden star emblem. */
function Banner({ position, color, turn = false }: { position: [number, number, number]; color: string; turn?: boolean }) {
  return (
    <group position={position} rotation-y={turn ? Math.PI : 0}>
      <mesh position-y={0.24} rotation-z={Math.PI / 2}><cylinderGeometry args={[0.02, 0.02, 0.62, 6]} /><meshStandardMaterial color={GOLD} /></mesh>
      <mesh><planeGeometry args={[0.5, 0.42]} /><meshStandardMaterial color={color} /></mesh>
      <mesh position-y={-0.2825} rotation-z={-Math.PI / 2} scale={[0.5, 1, 1]}><circleGeometry args={[0.29, 3]} /><meshStandardMaterial color={color} /></mesh>
      <mesh position-z={0.005}><ringGeometry args={[0.09, 0.12, 24]} /><meshStandardMaterial color={GOLD} emissive={GOLD} emissiveIntensity={0.5} /></mesh>
      <mesh position-z={0.005} rotation-z={Math.PI / 2}><circleGeometry args={[0.06, 5]} /><meshStandardMaterial color={GOLD} emissive={GOLD} emissiveIntensity={0.5} /></mesh>
    </group>
  );
}

const pinAt = new THREE.Vector3();
const pinCam = new THREE.Vector3();

/**
 * HTML pinned to a point in the scene: drei's <Html> cut down to what the office uses. drei unmounts its React root
 * synchronously inside the canvas commit, which React reports as an error ("Attempted to synchronously unmount a root");
 * this one hides the element at once and unmounts on the next tick.
 */
function Pin({ position, center, distanceFactor, zIndexRange, children }: {
  position: [number, number, number]; center?: boolean; distanceFactor: number; zIndexRange: [number, number]; children: ReactNode;
}) {
  const group = useRef<THREE.Group>(null);
  const host = useRef<{ el: HTMLDivElement; root: Root } | null>(null);
  const gl = useThree((s) => s.gl);

  useLayoutEffect(() => {
    const target = gl.domElement.parentNode;
    const el = document.createElement("div");
    el.style.cssText = "position:absolute;top:0;left:0;pointer-events:none;transform-origin:0 0;visibility:hidden;";
    target?.appendChild(el);
    const h = (host.current = { el, root: createRoot(el) });
    return () => {
      if (host.current === h) host.current = null;
      el.style.display = "none";
      setTimeout(() => { h.root.unmount(); el.remove(); });
    };
  }, [gl]);

  useLayoutEffect(() => {
    host.current?.root.render(<div style={{ position: "absolute", transform: center ? "translate3d(-50%,-50%,0)" : "none" }}>{children}</div>);
  });

  useFrame(({ camera, size }) => {
    const g = group.current, h = host.current;
    if (!g || !h) return;
    g.updateWorldMatrix(true, false);
    pinAt.setFromMatrixPosition(g.matrixWorld);
    const dist = pinAt.distanceTo(pinCam.setFromMatrixPosition(camera.matrixWorld));
    pinAt.project(camera);
    const cam = camera as THREE.PerspectiveCamera;
    const scale = distanceFactor / (2 * Math.tan((cam.fov * Math.PI) / 360) * dist);
    const a = (zIndexRange[1] - zIndexRange[0]) / (cam.far - cam.near);
    h.el.style.visibility = pinAt.z > 1 ? "hidden" : "visible";
    h.el.style.zIndex = String(Math.round(a * dist + zIndexRange[1] - a * cam.far));
    h.el.style.transform = `translate3d(${(pinAt.x * 0.5 + 0.5) * size.width}px,${(-pinAt.y * 0.5 + 0.5) * size.height}px,0) scale(${scale})`;
  });

  return <group ref={group} position={position} />;
}

function Label({ position, text, onClick, hint }: { position: [number, number, number]; text: string; onClick?: () => void; hint?: string }) {
  const [hover, setHover] = useState(false);
  return (
    <Pin position={position} center distanceFactor={18} zIndexRange={[10, 0]}>
      <div role={onClick ? "button" : undefined} title={hint} onClick={onClick}
        onPointerEnter={() => setHover(true)} onPointerLeave={() => setHover(false)}
        style={{
          whiteSpace: "nowrap", padding: "4px 14px", borderRadius: 999, fontSize: 14, fontWeight: 600, userSelect: "none",
          background: hover && onClick ? brand.purple : "linear-gradient(180deg, #FFFBEF, #F6E8C8)", color: hover && onClick ? "#fff" : brand.ink,
          border: `1.5px solid ${GOLD}`, boxShadow: "0 4px 16px rgba(60,30,110,.35)", cursor: onClick ? "pointer" : "default", pointerEvents: onClick ? "auto" : "none",
          transition: "background .15s, color .15s",
        }}>
        ✦ {text}{onClick && hover ? " ✎" : ""}
      </div>
    </Pin>
  );
}

/** A room's own coordinates: z = 0 at the back wall, the gate at z = d; turned-around rooms face the street from below. */
function Plot({ L, cell, children }: { L: Layout; cell: Cell; children: ReactNode }) {
  return (
    <group position={cell.flip ? [cell.x0 + L.w, 0, cell.z0 + L.d] : [cell.x0, 0, cell.z0]} rotation-y={cell.flip ? Math.PI : 0}>
      {children}
    </group>
  );
}

function Room({ room, index, cell, L, onClick, hint }: { room: OfficeRoom; index: number; cell: Cell; L: Layout; onClick?: () => void; hint?: string }) {
  const working = room.people.filter((p) => p.status === "Working").length;
  const banner = BANNERS[index % BANNERS.length];
  return (
    <Plot L={L} cell={cell}>
      <Walls L={L} floor={FLOORS[index % FLOORS.length]} banner={banner} flip={cell.flip} />
      {/* the banner hangs on whichever tall wall the camera looks at */}
      <Banner position={cell.flip ? [L.w * 0.4, 0.66, L.d - 0.115] : [L.w * 0.62, 0.66, 0.115]} color={banner} turn={cell.flip} />
      <Crystal position={[L.w - 0.35, 0, 0.4]} hue={index} />
      <Label position={[L.w - AISLE, 2.3, L.d]} text={`${room.name} · ${working}/${room.people.length}`} onClick={onClick} hint={hint} />
      {room.people.map((p, j) => {
        const [x, z] = deskSpot(j);
        return <Desk key={p.id} position={[x, 0, z]} working={p.status === "Working"} />;
      })}
    </Plot>
  );
}

/** A mage's desk: wooden top, a scrying mirror for a screen, a candle and a floating crystal; the mirror glows while its owner is working. */
function Desk({ position, working }: { position: [number, number, number]; working: boolean }) {
  const glass = useRef<THREE.MeshStandardMaterial>(null);
  const crystal = useRef<THREE.Mesh>(null);
  const flame = useRef<THREE.Mesh>(null);
  const phase = position[0] * 7.3 + position[2] * 3.1;
  useFrame(({ clock }) => {
    const t = clock.elapsedTime + phase;
    if (glass.current) glass.current.emissiveIntensity = working ? 0.75 + Math.sin(t * 4) * 0.25 : 0.06;
    if (crystal.current) {
      crystal.current.rotation.y = t * (working ? 1.8 : 0.4);
      crystal.current.position.y = 1.2 + Math.sin(t * 1.5) * 0.05;
    }
    if (flame.current) flame.current.scale.y = 1.8 + Math.sin(t * 13) * 0.25 + Math.sin(t * 7.3) * 0.15;
  });
  return (
    <group position={position}>
      <RoundedBox args={[1.2, 0.08, 0.6]} radius={0.03} position={[0, 0.62, 0.35]} castShadow receiveShadow>
        <meshStandardMaterial color={WOOD} />
      </RoundedBox>
      {[-0.52, 0.52].map((lx) => (
        <mesh key={lx} position={[lx, 0.3, 0.35]} castShadow><boxGeometry args={[0.07, 0.6, 0.5]} /><meshStandardMaterial color={WOOD_DARK} /></mesh>
      ))}
      <mesh position={[0, 0.98, 0.53]} rotation-x={Math.PI / 2} castShadow><cylinderGeometry args={[0.24, 0.24, 0.03, 28]} /><meshStandardMaterial color="#3A3150" /></mesh>
      <mesh position={[0, 0.98, 0.51]}><torusGeometry args={[0.24, 0.03, 10, 32]} /><meshStandardMaterial color={GOLD} metalness={0.5} roughness={0.35} /></mesh>
      <mesh position={[0, 0.98, 0.505]} rotation-y={Math.PI}>
        <circleGeometry args={[0.22, 32]} />
        <meshStandardMaterial ref={glass} color={working ? "#DDD6FE" : "#3E3360"} emissive={working ? "#B794F6" : "#000000"} />
      </mesh>
      <mesh position={[0, 0.71, 0.53]}><cylinderGeometry args={[0.025, 0.05, 0.14, 8]} /><meshStandardMaterial color={GOLD} /></mesh>
      <mesh ref={crystal} position={[0.45, 1.2, 0.45]} scale={[0.06, 0.11, 0.06]}>
        <octahedronGeometry args={[1]} />
        <meshStandardMaterial color="#A5F3FC" emissive="#67E8F9" emissiveIntensity={working ? 0.9 : 0.25} flatShading />
      </mesh>
      <mesh position={[-0.42, 0.72, 0.42]}><cylinderGeometry args={[0.035, 0.035, 0.12, 10]} /><meshStandardMaterial color="#FFF4E0" /></mesh>
      <mesh ref={flame} position={[-0.42, 0.81, 0.42]} scale={[1, 1.8, 1]}><sphereGeometry args={[0.022, 8, 8]} /><meshBasicMaterial color="#FFB547" /></mesh>
      <mesh position={[0.25, 0.68, 0.25]} rotation-y={0.3} castShadow><boxGeometry args={[0.22, 0.04, 0.16]} /><meshStandardMaterial color={brand.purple} /></mesh>
      {/* stool with a cushion */}
      <mesh position={[0, 0.38, -0.25]} castShadow><cylinderGeometry args={[0.26, 0.26, 0.08, 20]} /><meshStandardMaterial color={brand.pink} /></mesh>
      <mesh position={[0, 0.19, -0.25]}><cylinderGeometry args={[0.05, 0.07, 0.34, 8]} /><meshStandardMaterial color={WOOD_DARK} /></mesh>
    </group>
  );
}

/** The lounge: benches around a bubbling cauldron on a magic circle, mushroom stools, a potion bar and a bookshelf. */
function Lounge({ L, name }: { L: Layout; name: string }) {
  const c = L.lounge;
  const cx = L.w / 2, cz = L.d / 2;
  return (
    <Plot L={L} cell={c}>
      <Walls L={L} floor="#FFE9F3" banner={brand.star} flip={c.flip} />
      <Label position={[L.w - AISLE, 2.3, L.d]} text={name} />
      <MagicCircle position={[cx, 0.006, cz]} />
      <Cauldron position={[cx, 0, cz]} />
      <Sofa position={[cx, 0, cz - 1.25]} facing={1} color="#C4B5FD" />
      <Sofa position={[cx, 0, cz + 1.25]} facing={-1} color="#F9A8D4" />
      {[-2.3, 2.3].map((dx) => <MushroomStool key={dx} position={[cx + dx, 0, cz]} color={dx < 0 ? "#F472B6" : "#A78BFA"} />)}
      {/* potion bar on the back wall */}
      <RoundedBox args={[2.2, 0.9, 0.5]} radius={0.05} position={[1.6, 0.45, 0.4]} castShadow receiveShadow><meshStandardMaterial color={WOOD} /></RoundedBox>
      <mesh position={[1.1, 0.95, 0.4]}><cylinderGeometry args={[0.08, 0.12, 0.1, 12]} /><meshStandardMaterial color={GOLD} /></mesh>
      <mesh position={[1.1, 1.13, 0.4]} castShadow>
        <sphereGeometry args={[0.15, 24, 16]} />
        <meshStandardMaterial color="#DDD6FE" emissive="#A78BFA" emissiveIntensity={0.8} transparent opacity={0.88} />
      </mesh>
      {[1.7, 1.95, 2.2].map((x, i) => {
        const color = [brand.pink, "#A78BFA", "#34D399"][i];
        return (
          <group key={x} position={[x, 0.9, 0.4]}>
            <mesh position-y={0.07} castShadow><sphereGeometry args={[0.07, 14, 10]} /><meshStandardMaterial color={color} emissive={color} emissiveIntensity={0.6} /></mesh>
            <mesh position-y={0.17}><cylinderGeometry args={[0.022, 0.025, 0.08, 8]} /><meshStandardMaterial color={color} emissive={color} emissiveIntensity={0.4} /></mesh>
            <mesh position-y={0.22}><cylinderGeometry args={[0.028, 0.028, 0.03, 8]} /><meshStandardMaterial color={WOOD_DARK} /></mesh>
          </group>
        );
      })}
      {/* bookshelf on the left wall */}
      <group position={[0.35, 0, L.d - 1.4]}>
        <mesh position-y={0.6} castShadow><boxGeometry args={[0.4, 1.2, 1.2]} /><meshStandardMaterial color={WOOD_DARK} /></mesh>
        {[0.3, 0.7, 1.05].map((y, r) =>
          [-0.4, -0.15, 0.1, 0.35].map((dz, k) => (
            <mesh key={`${r}-${k}`} position={[0.08, y, dz]}><boxGeometry args={[0.28, 0.26, 0.16]} /><meshStandardMaterial color={["#C4B5FD", "#F9A8D4", "#93C5FD", "#FDE68A"][(k + r) % 4]} /></mesh>
          )))}
      </group>
      <Mushrooms position={[L.w - 0.4, 0, 0.45]} scale={1.4} />
    </Plot>
  );
}

/** Slowly turning rings of runes on the lounge floor. */
function MagicCircle({ position }: { position: [number, number, number] }) {
  const runes = useRef<THREE.Group>(null);
  useFrame((_, dt) => { if (runes.current) runes.current.rotation.z += dt * 0.15; });
  const glow = <meshStandardMaterial color={GOLD} emissive={GOLD} emissiveIntensity={0.7} />;
  return (
    <group position={position} rotation-x={-Math.PI / 2}>
      <mesh receiveShadow><circleGeometry args={[1.9, 48]} /><meshStandardMaterial color="#FBCFE8" /></mesh>
      <group ref={runes} position-z={0.003}>
        <mesh><ringGeometry args={[1.62, 1.7, 64]} />{glow}</mesh>
        <mesh><ringGeometry args={[1.15, 1.19, 64]} />{glow}</mesh>
        {Array.from({ length: 8 }, (_, i) => {
          const a = (i / 8) * Math.PI * 2;
          return <mesh key={i} position={[Math.cos(a) * 1.42, Math.sin(a) * 1.42, 0]} rotation-z={a}><circleGeometry args={[0.09, [3, 4, 5, 6][i % 4]]} />{glow}</mesh>;
        })}
      </group>
    </group>
  );
}

/** A bubbling cauldron over a small fire, lighting up the lounge. */
function Cauldron({ position }: { position: [number, number, number] }) {
  const bubbles = useRef<THREE.Group>(null);
  useFrame(({ clock }) => {
    bubbles.current?.children.forEach((b, i) => {
      const k = (clock.elapsedTime * 0.6 + i * 0.27) % 1;
      b.position.y = 0.76 + k * 0.2;
      b.scale.setScalar(1 - k);
    });
  });
  return (
    <group position={position}>
      {[0, 1, 2].map((i) => {
        const a = (i / 3) * Math.PI * 2;
        return <mesh key={i} position={[Math.cos(a) * 0.25, 0.1, Math.sin(a) * 0.25]}><cylinderGeometry args={[0.03, 0.03, 0.2, 6]} /><meshStandardMaterial color="#2F2745" /></mesh>;
      })}
      {[0, 1, 2].map((i) => {
        const a = (i / 3) * Math.PI * 2 + 0.5;
        return <mesh key={i} position={[Math.cos(a) * 0.08, 0.08, Math.sin(a) * 0.08]}><coneGeometry args={[0.07, 0.18, 6]} /><meshBasicMaterial color="#FB923C" /></mesh>;
      })}
      <mesh position-y={0.5} castShadow>
        <sphereGeometry args={[0.4, 28, 18, 0, Math.PI * 2, Math.PI * 0.22, Math.PI * 0.78]} />
        <meshStandardMaterial color="#2F2745" roughness={0.5} metalness={0.3} side={THREE.DoubleSide} />
      </mesh>
      <mesh position-y={0.808} rotation-x={Math.PI / 2}><torusGeometry args={[0.255, 0.035, 10, 28]} /><meshStandardMaterial color="#3A3150" /></mesh>
      <mesh position-y={0.76} rotation-x={-Math.PI / 2}><circleGeometry args={[0.29, 28]} /><meshStandardMaterial color="#86EFAC" emissive="#4ADE80" emissiveIntensity={1} /></mesh>
      <group ref={bubbles}>
        {[0, 1, 2, 3].map((i) => (
          <mesh key={i} position={[Math.cos(i * 1.7) * 0.14, 0.76, Math.sin(i * 1.7) * 0.14]}>
            <sphereGeometry args={[0.04, 10, 8]} />
            <meshStandardMaterial color="#BBF7D0" emissive="#4ADE80" emissiveIntensity={0.8} />
          </mesh>
        ))}
      </group>
      <pointLight position-y={1.1} color="#A7F3D0" intensity={4} distance={5} decay={1.6} />
      <Sparkles count={24} scale={[0.6, 1.2, 0.6]} position-y={1.3} size={4} speed={0.6} color="#BBF7D0" />
    </group>
  );
}

/** A wooden bench with cushions. */
function Sofa({ position, facing, color }: { position: [number, number, number]; facing: 1 | -1; color: string }) {
  return (
    <group position={position} rotation-y={facing === 1 ? 0 : Math.PI}>
      <RoundedBox args={[2.4, 0.22, 0.75]} radius={0.06} position={[0, 0.12, 0]} castShadow receiveShadow><meshStandardMaterial color={WOOD} /></RoundedBox>
      <RoundedBox args={[2.3, 0.1, 0.68]} radius={0.04} position={[0, 0.27, 0.02]} castShadow><meshStandardMaterial color={color} /></RoundedBox>
      <RoundedBox args={[2.4, 0.55, 0.14]} radius={0.05} position={[0, 0.5, -0.36]} castShadow><meshStandardMaterial color={WOOD} /></RoundedBox>
      <RoundedBox args={[2.2, 0.36, 0.08]} radius={0.04} position={[0, 0.52, -0.27]}><meshStandardMaterial color={color} /></RoundedBox>
      {[-1.2, 1.2].map((x) => (
        <RoundedBox key={x} args={[0.16, 0.4, 0.75]} radius={0.06} position={[x, 0.3, 0]} castShadow><meshStandardMaterial color={WOOD_DARK} /></RoundedBox>
      ))}
    </group>
  );
}

/** A giant spotted mushroom to sit on. */
function MushroomStool({ position, color }: { position: [number, number, number]; color: string }) {
  return (
    <group position={position}>
      <mesh position-y={0.07}><cylinderGeometry args={[0.14, 0.18, 0.14, 14]} /><meshStandardMaterial color="#FFF4E0" /></mesh>
      <mesh position-y={0.08} scale={[1, 0.35, 1]} castShadow><sphereGeometry args={[0.38, 24, 14]} /><meshStandardMaterial color={color} emissive={color} emissiveIntensity={0.15} /></mesh>
      {[0, 1, 2, 3].map((i) => {
        const a = (i / 4) * Math.PI * 2 + 0.4;
        return <mesh key={i} position={[Math.cos(a) * 0.18, 0.19, Math.sin(a) * 0.18]} scale={[1, 0.4, 1]}><sphereGeometry args={[0.05, 10, 8]} /><meshStandardMaterial color="#FFFFFF" /></mesh>;
      })}
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
  const sprite = person.head ? SPRITE : TEAM_SPRITES[seed % TEAM_SPRITES.length] ?? SPRITE;
  const phase = (seed % 1000) / 160;
  const status = person.status;
  const dim = status === "Inactive";
  const toward = homeCell.flip ? -1 : 1; // which way along z the desk (and the gate) is from the chair
  const facing0 = homeCell.flip ? 0 : Math.PI; // turn that faces the desk
  const stand: V2 = [home[0], home[1] - 0.85 * toward];

  const st = useRef({
    mode: "home" as Mode, after: "home" as Mode, path: [] as V2[], speed: WALK_SPEED, rushing: false,
    cell: homeCell, dest: homeCell, x: stand[0], z: stand[1], heading: facing0, until: -1, seat: null as Seat | null, random: rng(seed),
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

    g.rotation.set(0, facing0, 0);
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
    g.position.set(home[0], seated ? 0.42 : 0, seated ? home[1] - 0.25 * toward : stand[1]);
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
      g.rotation.y = -a + facing0;
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
          const spot = toWorld(L, cell, 1 + s.random() * (L.w - 2.4), L.d - 0.55); // just inside the gate
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
      g.rotation.x = (status === "Inactive" ? 0.5 : 0.2) * toward; // Euler XYZ: the tilt is about world X, so it leans toward the desk
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
                <SpriteBody src={sprite!} dim={dim} glow={person.kind === "AI" ? (status === "Working" ? 1 : 0.2) : null} />
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
        <Pin position={[0, SPRITE ? SPRITE_H + 0.35 : 1.45, 0]} center distanceFactor={12} zIndexRange={[20, 0]}>
          <div style={{ pointerEvents: "none", display: "flex", flexDirection: "column", alignItems: "center", gap: 2 }}>
            {(hover || selected) && (
              <div style={{ whiteSpace: "nowrap", padding: "3px 10px", borderRadius: 10, background: "rgba(255,255,255,.95)", fontSize: 12, color: brand.ink, boxShadow: "0 3px 10px rgba(0,0,0,.12)" }}>
                <b>{person.name}</b> · {label}
              </div>
            )}
            {bubble && <div style={{ width: 28, height: 28, borderRadius: "50%", display: "grid", placeItems: "center", background: bubble.bg, fontSize: 15, boxShadow: "0 3px 8px rgba(0,0,0,.12)" }}>{bubble.icon}</div>}
          </div>
        </Pin>
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
