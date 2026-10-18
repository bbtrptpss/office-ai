"use client";

import { useEffect, useRef, useState } from "react";
import { Canvas, useFrame, useThree } from "@react-three/fiber";
import { useReducedMotion } from "motion/react";
import * as THREE from "three";

/**
 * 2.5D stage: one flat picture made to feel 3D. A hand-made depth map (soft blobs over the characters) lets the
 * characters shift more than the room when the pointer moves (parallax), and each character bobs on its own beat.
 * Blob positions fit /characters/kuromi/scene.png; any other picture still gets the gentle parallax of the center.
 */
export interface DepthBlob { x: number; y: number; rx: number; ry: number; depth: number; bob: number }

/** Character blobs for scene.png, in image fractions from the top-left. */
export const SCENE_BLOBS: DepthBlob[] = [
  { x: 0.455, y: 0.46, rx: 0.23, ry: 0.2, depth: 1, bob: 1.6 },   // Kuromi
  { x: 0.45, y: 0.69, rx: 0.14, ry: 0.08, depth: 0.85, bob: 1.6 }, // Kuromi's laptop, moves with her
  { x: 0.17, y: 0.52, rx: 0.11, ry: 0.18, depth: 0.9, bob: 1.9 },  // My Melody
  { x: 0.8, y: 0.47, rx: 0.16, ry: 0.1, depth: 0.8, bob: 1.7 },    // Cinnamoroll
  { x: 0.85, y: 0.69, rx: 0.12, ry: 0.1, depth: 0.9, bob: 2.1 },   // Pompompurin
];

const MAX = 6;

const vertex = /* glsl */ `
varying vec2 vUv;
void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }
`;

const fragment = /* glsl */ `
uniform sampler2D map;
uniform vec2 cover;
uniform vec2 pointer;
uniform float time;
uniform float motion;
uniform vec4 blobs[${MAX}];   // x, y (uv), rx, ry
uniform vec2 extra[${MAX}];   // depth, bob speed
uniform int count;
varying vec2 vUv;

void main() {
  vec2 uv = (vUv - 0.5) * cover + 0.5;
  float depth = 0.15 * (1.0 - smoothstep(0.0, 0.6, distance(uv, vec2(0.5)))); // the room bulges a little toward the middle
  vec2 bob = vec2(0.0);
  for (int i = 0; i < ${MAX}; i++) {
    if (i >= count) break;
    vec2 d = (uv - blobs[i].xy) / blobs[i].zw;
    float w = exp(-dot(d, d) * 1.8) * extra[i].x;
    depth = max(depth, w);
    bob.y += w * sin(time * extra[i].y + float(i) * 1.7) * 0.0045;
  }
  vec2 offset = pointer * (depth * 0.022 - 0.006) + bob * motion;
  gl_FragColor = texture2D(map, clamp(uv - offset, 0.001, 0.999));
}
`;

function makeUniforms() {
  return {
    map: { value: null as THREE.Texture | null },
    cover: { value: new THREE.Vector2(1, 1) },
    pointer: { value: new THREE.Vector2() },
    time: { value: 0 },
    motion: { value: 1 },
    blobs: { value: Array.from({ length: MAX }, () => new THREE.Vector4()) },
    extra: { value: Array.from({ length: MAX }, () => new THREE.Vector2()) },
    count: { value: 0 },
  };
}

/** One animation step: ease toward the pointer (or a slow idle sway) and advance the bob clock. */
function advance(u: Record<string, THREE.IUniform>, p: { x: number; y: number; active: boolean }, t: number, delta: number, motion: boolean) {
  const tx = p.active ? p.x : motion ? Math.sin(t * 0.35) * 0.5 : 0;
  const ty = p.active ? p.y : motion ? Math.cos(t * 0.27) * 0.35 : 0;
  const k = 1 - Math.exp(-delta * 4);
  const cur = u.pointer.value as THREE.Vector2;
  cur.set(cur.x + (tx - cur.x) * k, cur.y + (ty - cur.y) * k);
  u.time.value = t;
  u.motion.value = motion ? 1 : 0;
}

function Picture({ src, blobs, pointer, motion }: { src: string; blobs: DepthBlob[]; pointer: React.RefObject<{ x: number; y: number; active: boolean }>; motion: boolean }) {
  const { viewport, size } = useThree();
  const [texture, setTexture] = useState<THREE.Texture | null>(null);
  useEffect(() => {
    let alive = true;
    new THREE.TextureLoader().load(src, (t) => {
      t.minFilter = THREE.LinearFilter;
      t.generateMipmaps = false;
      if (alive) setTexture(t);
    });
    return () => { alive = false; };
  }, [src]);

  // Uniforms are created once and then changed through the material ref (never during render).
  const [uniforms] = useState(makeUniforms);
  const material = useRef<THREE.ShaderMaterial>(null);

  useEffect(() => {
    const u = material.current?.uniforms;
    if (!u || !texture) return;
    blobs.slice(0, MAX).forEach((b, i) => {
      u.blobs.value[i].set(b.x, 1 - b.y, b.rx, b.ry);
      u.extra.value[i].set(b.depth, b.bob);
    });
    u.count.value = Math.min(blobs.length, MAX);
    u.map.value = texture;
    // "cover" fit with a small zoom margin so parallax never shows the picture's edge
    const img = texture.image as { width: number; height: number };
    const a = size.width / size.height, b = img.width / img.height;
    u.cover.value.set(a > b ? 1 : a / b, a > b ? b / a : 1).multiplyScalar(0.98);
  }, [texture, blobs, size.width, size.height]);

  useFrame(({ clock }, delta) => {
    if (material.current) advance(material.current.uniforms, pointer.current!, clock.elapsedTime, delta, motion);
  });

  return (
    <mesh scale={[viewport.width, viewport.height, 1]} visible={!!texture}>
      <planeGeometry args={[1, 1]} />
      <shaderMaterial ref={material} vertexShader={vertex} fragmentShader={fragment} uniforms={uniforms} toneMapped={false} />
    </mesh>
  );
}

/** Fills its (positioned) parent. Pointer movement over `trackRef` drives the parallax. */
export default function StageArt({ src, blobs, trackRef }: { src: string; blobs: DepthBlob[]; trackRef: React.RefObject<HTMLElement | null> }) {
  const reduce = useReducedMotion();
  const pointer = useRef({ x: 0, y: 0, active: false });
  useEffect(() => {
    const el = trackRef.current;
    if (!el) return;
    const move = (e: PointerEvent) => {
      const r = el.getBoundingClientRect();
      pointer.current = { x: ((e.clientX - r.left) / r.width) * 2 - 1, y: -(((e.clientY - r.top) / r.height) * 2 - 1), active: true };
    };
    const leave = () => { pointer.current = { ...pointer.current, active: false }; };
    el.addEventListener("pointermove", move);
    el.addEventListener("pointerleave", leave);
    return () => { el.removeEventListener("pointermove", move); el.removeEventListener("pointerleave", leave); };
  }, [trackRef]);

  return (
    <Canvas flat linear orthographic dpr={[1, 2]} camera={{ position: [0, 0, 5], zoom: 1 }} gl={{ antialias: false, alpha: true }}
      style={{ position: "absolute", inset: 0, pointerEvents: "none" }}>
      <Picture src={src} blobs={blobs} pointer={pointer} motion={!reduce} />
    </Canvas>
  );
}
