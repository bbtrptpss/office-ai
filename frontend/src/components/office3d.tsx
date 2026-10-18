"use client";

import { useMemo, useRef, useState } from "react";
import { Canvas, useFrame, type ThreeEvent } from "@react-three/fiber";
import { Html, OrbitControls, RoundedBox } from "@react-three/drei";
import * as THREE from "three";
import type { EmployeeKind, EmployeeStatus } from "@/lib/api";
import { brand } from "@/theme";

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
const ROOM_GAP = 1.2;
const ROOMS_PER_ROW = 3;

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

/** Status bubble shown above a character. */
const BUBBLE: Partial<Record<EmployeeStatus, { icon: string; bg: string }>> = {
  Working: { icon: "⌨️", bg: "#EDE9FE" },
  WaitingForApproval: { icon: "✋", bg: "#FEF3C7" },
  Blocked: { icon: "🧱", bg: "#FFE4E6" },
  Failed: { icon: "⚠️", bg: "#FECACA" },
  Inactive: { icon: "💤", bg: "#E5E7EB" },
};

export function roomSize(maxPeople: number) {
  const rows = Math.max(1, Math.ceil(maxPeople / DESK_COLS));
  return { w: DESK_COLS * DESK_DX + 1.6, d: rows * DESK_DZ + 1.8 };
}

export default function Office3D({ rooms, selectedId, onSelect, labels }: {
  rooms: OfficeRoom[];
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  labels: (status: EmployeeStatus) => string;
}) {
  const size = roomSize(Math.max(1, ...rooms.map((r) => r.people.length)));
  const cols = Math.min(ROOMS_PER_ROW, Math.max(1, rooms.length));
  const rowsOfRooms = Math.ceil(rooms.length / cols);
  const totalW = cols * size.w + (cols - 1) * ROOM_GAP;
  const totalD = rowsOfRooms * size.d + (rowsOfRooms - 1) * ROOM_GAP;
  const span = Math.max(totalW, totalD);

  return (
    <Canvas shadows flat dpr={[1, 2]} camera={{ position: [span * 0.32, span * 0.5, span * 0.62], fov: 40 }} onPointerMissed={() => onSelect(null)}>
      <ambientLight intensity={0.75} />
      <hemisphereLight args={["#ffffff", "#E9D5FF", 0.6]} />
      <directionalLight position={[span * 0.4, span, span * 0.3]} intensity={1.1} castShadow
        shadow-mapSize={[2048, 2048]} shadow-camera-left={-span} shadow-camera-right={span} shadow-camera-top={span} shadow-camera-bottom={-span} />
      <group position={[-totalW / 2, 0, -totalD / 2]}>
        {rooms.map((room, i) => (
          <Room key={room.id} room={room} index={i} w={size.w} d={size.d}
            x={(i % cols) * (size.w + ROOM_GAP)} z={Math.floor(i / cols) * (size.d + ROOM_GAP)}
            selectedId={selectedId} onSelect={onSelect} labels={labels} />
        ))}
      </group>
      <OrbitControls makeDefault enableDamping maxPolarAngle={Math.PI / 2.3} minDistance={4} maxDistance={span * 1.6} target={[0, 0, 0]} />
    </Canvas>
  );
}

function Room({ room, index, x, z, w, d, selectedId, onSelect, labels }: {
  room: OfficeRoom; index: number; x: number; z: number; w: number; d: number;
  selectedId: string | null; onSelect: (id: string | null) => void; labels: (s: EmployeeStatus) => string;
}) {
  const floor = FLOORS[index % FLOORS.length];
  const working = room.people.filter((p) => p.status === "Working").length;
  return (
    <group position={[x, 0, z]}>
      {/* floor + low walls on the back and left, like a dollhouse cut open toward the camera */}
      <RoundedBox args={[w, 0.12, d]} radius={0.05} position={[w / 2, -0.06, d / 2]} receiveShadow>
        <meshStandardMaterial color={floor} />
      </RoundedBox>
      <mesh position={[w / 2, 0.45, 0.05]} castShadow receiveShadow>
        <boxGeometry args={[w, 0.9, 0.1]} />
        <meshStandardMaterial color="#FFFFFF" />
      </mesh>
      <mesh position={[0.05, 0.45, d / 2]} castShadow receiveShadow>
        <boxGeometry args={[0.1, 0.9, d]} />
        <meshStandardMaterial color="#FAF5FF" />
      </mesh>
      {/* window with stars on the back wall */}
      <mesh position={[w * 0.75, 0.55, 0.11]}>
        <planeGeometry args={[1.1, 0.5]} />
        <meshStandardMaterial color="#C7D2FE" emissive="#A5B4FC" emissiveIntensity={0.25} />
      </mesh>
      <Plant position={[w - 0.35, 0, 0.4]} />
      <Html position={[w / 2, 2.1, -0.2]} center distanceFactor={18} zIndexRange={[10, 0]}>
        <div style={{ whiteSpace: "nowrap", padding: "4px 12px", borderRadius: 999, background: "rgba(255,255,255,.9)", boxShadow: "0 4px 14px rgba(110,80,170,.18)", fontSize: 14, fontWeight: 600, color: brand.ink, pointerEvents: "none" }}>
          {room.name} · {working}/{room.people.length}
        </div>
      </Html>
      {room.people.map((p, i) => {
        const col = i % DESK_COLS;
        const row = Math.floor(i / DESK_COLS);
        return (
          <Workstation key={p.id} person={p} position={[0.9 + col * DESK_DX + DESK_DX / 2 - 0.1, 0, 1.35 + row * DESK_DZ]}
            selected={selectedId === p.id} onSelect={onSelect} label={labels(p.status)} />
        );
      })}
    </group>
  );
}

function Plant({ position }: { position: [number, number, number] }) {
  return (
    <group position={position}>
      <mesh position-y={0.15} castShadow><cylinderGeometry args={[0.13, 0.1, 0.3, 16]} /><meshStandardMaterial color={brand.pinkSoft} /></mesh>
      <mesh position-y={0.42} castShadow><sphereGeometry args={[0.2, 16, 16]} /><meshStandardMaterial color="#86EFAC" /></mesh>
    </group>
  );
}

/** Desk, chair, monitor and the person; the person's pose follows their live status. */
function Workstation({ person, position, selected, onSelect, label }: {
  person: OfficePerson; position: [number, number, number]; selected: boolean; onSelect: (id: string | null) => void; label: string;
}) {
  const screen = useRef<THREE.MeshStandardMaterial>(null);
  const working = person.status === "Working";
  useFrame(({ clock }) => {
    if (screen.current) screen.current.emissiveIntensity = working ? 0.7 + Math.sin(clock.elapsedTime * 4) * 0.25 : 0.05;
  });
  return (
    <group position={position}>
      {/* desk */}
      <RoundedBox args={[1.2, 0.08, 0.6]} radius={0.03} position={[0, 0.62, 0.35]} castShadow receiveShadow>
        <meshStandardMaterial color="#FFFFFF" />
      </RoundedBox>
      {[-0.52, 0.52].map((lx) => (
        <mesh key={lx} position={[lx, 0.3, 0.35]} castShadow><boxGeometry args={[0.06, 0.6, 0.5]} /><meshStandardMaterial color={brand.lavender} /></mesh>
      ))}
      {/* monitor */}
      <mesh position={[0, 0.95, 0.52]} castShadow>
        <boxGeometry args={[0.62, 0.4, 0.04]} />
        <meshStandardMaterial color="#3A3150" />
      </mesh>
      <mesh position={[0, 0.95, 0.497]} rotation-y={Math.PI}>
        <planeGeometry args={[0.56, 0.34]} />
        <meshStandardMaterial ref={screen} color={working ? "#DDD6FE" : "#4B4166"} emissive={working ? "#C4B5FD" : "#000000"} />
      </mesh>
      <mesh position={[0, 0.7, 0.52]}><cylinderGeometry args={[0.03, 0.05, 0.12, 8]} /><meshStandardMaterial color="#3A3150" /></mesh>
      {/* chair */}
      <mesh position={[0, 0.38, -0.25]} castShadow><cylinderGeometry args={[0.26, 0.26, 0.08, 20]} /><meshStandardMaterial color={brand.pink} /></mesh>
      <mesh position={[0, 0.19, -0.25]}><cylinderGeometry args={[0.04, 0.04, 0.34, 8]} /><meshStandardMaterial color="#9CA3AF" /></mesh>
      <Character person={person} selected={selected} onSelect={onSelect} label={label} />
    </group>
  );
}

function Character({ person, selected, onSelect, label }: { person: OfficePerson; selected: boolean; onSelect: (id: string | null) => void; label: string }) {
  const root = useRef<THREE.Group>(null);
  const armL = useRef<THREE.Mesh>(null);
  const armR = useRef<THREE.Mesh>(null);
  const [hover, setHover] = useState(false);
  const seed = useMemo(() => hash(person.name), [person.name]);
  const sp = SPECIES[seed % SPECIES.length];
  const phase = (seed % 1000) / 160;
  const status = person.status;
  const seated = status === "Working" || status === "Blocked" || status === "Failed" || status === "Inactive";
  const dim = status === "Inactive";

  useFrame(({ clock }) => {
    const t = clock.elapsedTime + phase;
    const g = root.current;
    if (!g) return;
    g.rotation.set(0, Math.PI, 0);
    g.scale.setScalar(1);
    g.position.set(0, seated ? 0.42 : 0, seated ? -0.25 : -0.85);
    armL.current?.rotation.set(0, 0, 0);
    armR.current?.rotation.set(0, 0, 0);
    if (status === "Working") {
      g.position.y += Math.abs(Math.sin(t * 6)) * 0.03;
      if (armL.current) armL.current.rotation.x = -1.2 + Math.sin(t * 14) * 0.25;
      if (armR.current) armR.current.rotation.x = -1.2 + Math.sin(t * 14 + 1.6) * 0.25;
    } else if (status === "Available") {
      // strolls a small loop beside the desk
      const a = t * 0.6;
      g.position.x = Math.cos(a) * 0.55;
      g.position.z = -0.85 + Math.sin(a) * 0.2;
      g.position.y = Math.abs(Math.sin(t * 5)) * 0.05;
      g.rotation.y = -a + Math.PI;
      if (armL.current) armL.current.rotation.x = Math.sin(t * 5) * 0.5;
      if (armR.current) armR.current.rotation.x = -Math.sin(t * 5) * 0.5;
    } else if (status === "WaitingForApproval") {
      g.position.y = Math.abs(Math.sin(t * 3)) * 0.08;
      if (armR.current) { armR.current.rotation.x = -2.8; armR.current.rotation.z = Math.sin(t * 6) * 0.35; }
      if (armL.current) armL.current.rotation.x = 0;
    } else if (status === "Failed") {
      g.position.x = Math.sin(t * 30) * 0.02;
      if (armL.current) armL.current.rotation.x = -0.4;
      if (armR.current) armR.current.rotation.x = -0.4;
    } else {
      // blocked / inactive: slumped, slow breathing
      g.rotation.x = status === "Inactive" ? 0.5 : 0.2; // Euler XYZ: the tilt is about world X, so + leans toward the desk
      g.scale.setScalar(1 + Math.sin(t * 1.5) * 0.015);
      if (armL.current) armL.current.rotation.x = -0.9;
      if (armR.current) armR.current.rotation.x = -0.9;
    }
  });

  const bubble = BUBBLE[status];
  const mat = (color: string) => <meshStandardMaterial color={color} transparent={dim} opacity={dim ? 0.55 : 1} />;
  return (
    <group ref={root}
      onClick={(e: ThreeEvent<MouseEvent>) => { e.stopPropagation(); onSelect(person.id); }}
      onPointerOver={(e) => { e.stopPropagation(); setHover(true); document.body.style.cursor = "pointer"; }}
      onPointerOut={() => { setHover(false); document.body.style.cursor = ""; }}>
      {/* selection ring */}
      {(selected || hover) && (
        <mesh rotation-x={-Math.PI / 2} position-y={0.02}>
          <ringGeometry args={[0.32, 0.4, 32]} />
          <meshBasicMaterial color={selected ? brand.pink : brand.purple} />
        </mesh>
      )}
      {/* body */}
      <mesh position-y={0.32} castShadow>
        <capsuleGeometry args={[0.2, 0.2, 6, 16]} />
        {mat(sp.body)}
      </mesh>
      {/* arms */}
      <mesh ref={armL} position={[-0.23, 0.42, 0]} castShadow><capsuleGeometry args={[0.055, 0.14, 4, 8]} />{mat(sp.body)}</mesh>
      <mesh ref={armR} position={[0.23, 0.42, 0]} castShadow><capsuleGeometry args={[0.055, 0.14, 4, 8]} />{mat(sp.body)}</mesh>
      {/* head */}
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
      {(bubble || hover || selected) && (
        <Html position={[0, 1.45, 0]} center distanceFactor={12} zIndexRange={[20, 0]}>
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
