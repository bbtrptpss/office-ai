/** Line icons from the kuromi-ai mock-up, used by the dashboard. */
import type { SVGProps } from "react";

function Icon({ children, ...props }: SVGProps<SVGSVGElement>) {
  return (
    <svg className="i" viewBox="0 0 24 24" aria-hidden="true" {...props}>
      {children}
    </svg>
  );
}

type P = SVGProps<SVGSVGElement>;

export const Star = (p: P) => <Icon {...p}><path d="M12 3l2.5 5.5L20 9l-4 4 1 6-5-3-5 3 1-6-4-4 5.5-.5z" /></Icon>;
export const ChevronDown = (p: P) => <Icon {...p}><path d="M6 9l6 6 6-6" /></Icon>;
export const ChevronRight = (p: P) => <Icon {...p}><path d="M9 6l6 6-6 6" /></Icon>;
export const Plus = (p: P) => <Icon {...p}><path d="M12 5v14M5 12h14" /></Icon>;
export const Search = (p: P) => <Icon {...p}><circle cx="11" cy="11" r="7" /><path d="M20 20l-3.5-3.5" /></Icon>;
export const Gear = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="3" /><path d="M12 2l2 3 3.5-.5.5 3.5 3 2-2 3 .5 3.5-3.5.5-2 3-3-2-3 2-2-3-3.5-.5.5-3.5-2-3 3-2 .5-3.5L10 5z" /></Icon>;
export const Bell = (p: P) => <Icon {...p}><path d="M6 16V11a6 6 0 0 1 12 0v5l2 2H4z" /><path d="M10 21h4" /></Icon>;
export const CheckSquare = (p: P) => <Icon {...p}><rect x="4" y="4" width="16" height="16" rx="4" /><path d="M8 12l3 3 5-6" /></Icon>;
export const Check = (p: P) => <Icon {...p}><path d="M5 12l4 4 10-10" /></Icon>;
export const Music = (p: P) => <Icon {...p}><path d="M9 18V5l11-2v13" /><circle cx="6" cy="18" r="3" /><circle cx="17" cy="16" r="3" /></Icon>;
export const Prev = (p: P) => <Icon {...p}><path d="M6 5h2v14H6zM20 5v14L9 12z" /></Icon>;
export const Next = (p: P) => <Icon {...p}><path d="M16 5h2v14h-2zM4 5v14l11-7z" /></Icon>;
export const Play = (p: P) => <Icon {...p}><path d="M8 5v14l11-7z" /></Icon>;
export const Pause = (p: P) => <Icon {...p}><path d="M7 5h4v14H7zM13 5h4v14h-4z" /></Icon>;
export const Heart = (p: P) => <Icon {...p}><path d="M12 20s-7-4.5-9-9a4.8 4.8 0 0 1 9-3 4.8 4.8 0 0 1 9 3c-2 4.5-9 9-9 9z" /></Icon>;
export const Clip = (p: P) => <Icon {...p}><path d="M20 12l-8 8a5 5 0 0 1-7-7l9-9a3.5 3.5 0 0 1 5 5l-9 9a2 2 0 0 1-3-3l8-8" /></Icon>;
export const Globe = (p: P) => <Icon {...p}><circle cx="12" cy="12" r="9" /><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18" /></Icon>;
export const Arrow = (p: P) => <Icon {...p}><path d="M5 12h14M13 6l6 6-6 6" /></Icon>;
export const Doc = (p: P) => <Icon {...p}><rect x="5" y="3" width="14" height="18" rx="3" /><path d="M9 8h6M9 12h6M9 16h3" /></Icon>;
export const Pen = (p: P) => <Icon {...p}><path d="M4 20l4-1 11-11-3-3L5 16z" /></Icon>;
export const Calendar = (p: P) => <Icon {...p}><rect x="4" y="5" width="16" height="15" rx="3" /><path d="M8 3v4M16 3v4M4 10h16" /></Icon>;
export const Dots = (p: P) => <Icon {...p}><circle cx="5" cy="12" r="1" /><circle cx="12" cy="12" r="1" /><circle cx="19" cy="12" r="1" /></Icon>;
export const Sparkle = (p: P) => <Icon {...p}><path d="M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z" /></Icon>;
export const Clipboard = (p: P) => <Icon {...p}><rect x="5" y="3" width="14" height="18" rx="3" /><path d="M9 3v4h6V3" /></Icon>;
export const Chart = (p: P) => <Icon {...p}><rect x="4" y="4" width="16" height="16" rx="3" /><path d="M8 15l3-3 2 2 3-4" /></Icon>;
