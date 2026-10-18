/**
 * Optional character artwork pack: a folder under /public (e.g. "/characters/kuromi") holding scene.png, logo.png, mini.png,
 * cover.png, cheer.png and friend images melody.png, cinna.png, purin.png. Set NEXT_PUBLIC_CHARACTER_PACK (CHARACTER_PACK in
 * .env) to use it; without it the built-in "Puni" mascot is drawn. Pack folders are git-ignored, so the artwork stays local.
 */
const PACK = process.env.NEXT_PUBLIC_CHARACTER_PACK?.replace(/\/+$/, "");

export type ArtName = "scene" | "logo" | "mini" | "cover" | "cheer" | "melody" | "cinna" | "purin";

/** URL of a pack image, or undefined when no pack is configured. */
export const art = (name: ArtName) => (PACK ? `${PACK}/${name}.png` : undefined);

/** Friend images in the order the stage shows its assistants. */
export const FRIEND_ART: ArtName[] = ["melody", "cinna", "purin"];
