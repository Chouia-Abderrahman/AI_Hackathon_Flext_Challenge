import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { type IkonUiComponentResolver, type UiComponentRendererProps, useUiNode } from '@ikonai/sdk-react-ui';
import './roof-hero.css';

interface CostLine {
  label: string;
  traditional: number | null;
  flext: number;
}

interface RoofHeroFigures {
  traditionalTotal: number;
  flextTotal: number;
  breakdown: CostLine[];
  traditionalHoursLabel: string;
  flextHoursLabel: string;
  timeReductionLabel: string;
  maintenanceDaysLabel: string;
  projectDaysLabel: string;
  rotProtectionLabel: string;
}

const DEFAULT_BREAKDOWN: CostLine[] = [
  { label: 'Material', traditional: 5_611, flext: 8_608 },
  { label: 'Installation time', traditional: 13_750, flext: 2_063 },
  { label: 'Project overheads', traditional: 11_719, flext: 1_758 },
  { label: 'Shipping & packaging', traditional: 400, flext: 2_800 },
  { label: 'FLEXT machine', traditional: null, flext: 1_918 },
];

const BATTEN_COUNT = 14;

// Flext finishes its roof well before the handle reaches the end: the traditional roof
// needs the full drag to complete, Flext is done by less than half of it.
const FLEXT_COMPLETE = 0.42;

// How long autoplay takes to carry the handle from one end to the other. Slow enough that
// the traditional roof's weather and worker cycle are readable, not so slow it stalls.
const PLAY_DURATION_MS = 11_000;

// The roof deck, drawn as the isometric parallelogram used throughout the memorandum's own
// renders — ridge at the back (narrower, further away), eave at the front (wider, nearer).
// t = 0 at the eave, t = 1 at the ridge: battens are built eave-up, the Flext bundle is
// placed at the ridge and pulled down toward the eave. Shared by both roof panels, since
// they are the same roof drawn at the same scale.
const RIDGE_LEFT = { x: 120, y: 54 };
const RIDGE_RIGHT = { x: 304, y: 36 };
const EAVE_LEFT = { x: 60, y: 214 };
const EAVE_RIGHT = { x: 365, y: 182 };

const clamp01 = (n: number) => Math.min(1, Math.max(0, n));
const lerp = (a: number, b: number, t: number) => a + (b - a) * clamp01(t);

function edgeAt(t: number) {
  return {
    xLeft: lerp(EAVE_LEFT.x, RIDGE_LEFT.x, t),
    yLeft: lerp(EAVE_LEFT.y, RIDGE_LEFT.y, t),
    xRight: lerp(EAVE_RIGHT.x, RIDGE_RIGHT.x, t),
    yRight: lerp(EAVE_RIGHT.y, RIDGE_RIGHT.y, t),
  };
}

function pointAt(t: number, s: number) {
  const edge = edgeAt(t);
  return { x: lerp(edge.xLeft, edge.xRight, s), y: lerp(edge.yLeft, edge.yRight, s) };
}

function rowT(index: number) {
  return lerp(0.05, 0.95, index / (BATTEN_COUNT - 1));
}

function money(value: number) {
  return `${Math.round(value).toLocaleString('sv-SE').replace(/\u00a0/g, ' ')} SEK`;
}

interface Weather {
  label: string;
  rain: boolean;
  snow: boolean;
  wind: boolean;
  overcast: number; // 0..1, how much the light has dropped
}

function weatherForProgress(p: number): Weather {
  if (p < 0.18) return { label: 'Calm, dry morning', rain: false, snow: false, wind: false, overcast: 0 };
  if (p < 0.42) return { label: 'Rain moves in', rain: true, snow: false, wind: false, overcast: 0.1 };
  if (p < 0.66) return { label: 'Gusting coastal wind', rain: true, snow: false, wind: true, overcast: 0.16 };
  if (p < 0.85) return { label: 'Temperature falling fast', rain: false, snow: false, wind: true, overcast: 0.09 };
  return { label: 'Snow on the battens', rain: false, snow: true, wind: false, overcast: 0.07 };
}

/** A carpenter crouched over the row they're working: helmet, hi-vis vest, a tool arm
 * reaching for the batten, and a soft contact shadow so they read as standing on the deck
 * rather than floating over it. `swing` offsets the arm-swing cycle so two workers don't
 * move in lockstep. */
function Worker({ x, y, vest, swing }: { x: number; y: number; vest: string; swing: number }) {
  const armSwing = swing % 2 === 0 ? -1 : 1;
  return (
    <g className="roof-hero-worker" transform={`translate(${x}, ${y - 15})`}>
      <ellipse cx="0" cy="16" rx="8" ry="2.2" className="roof-hero-worker-shadow" />
      {/* back leg, bent for a crouch */}
      <path d="M -3 10 Q -6 14 -5 19 L -1 19 Q -1 14 1 10 Z" fill="var(--rh-rafter)" />
      <path d="M 3 10 Q 6 13 6 19 L 2 19 Q 2 13 1 10 Z" fill="var(--rh-rafter)" />
      {/* torso */}
      <path d="M -5 -1 Q 0 -4 5 -1 L 4 11 Q 0 13 -4 11 Z" fill={vest} />
      <rect x="-4.5" y="2" width="9" height="1.4" fill="var(--rh-plank-highlight)" opacity="0.8" />
      <rect x="-4.5" y="6" width="9" height="1.4" fill="var(--rh-plank-highlight)" opacity="0.8" />
      {/* arm reaching toward the batten, swapped by swing for a working motion */}
      <path d={`M 4 1 Q ${8 * armSwing} ${-2 + armSwing} ${9 * armSwing} 3`} stroke="var(--rh-skin)" strokeWidth="2.4" strokeLinecap="round" fill="none" />
      <rect x={9 * armSwing - 1.5} y="1.5" width="3" height="4" fill="var(--rh-fg)" opacity="0.85" />
      {/* head and helmet */}
      <circle cx="0" cy="-6.5" r="3.6" fill="var(--rh-skin)" />
      <path d="M -4 -7.5 A 4 4 0 0 1 4 -7.5 L 4.5 -6.2 L -4.5 -6.2 Z" fill="var(--rh-brand)" />
    </g>
  );
}

function RoofHeroRenderer({ nodeId, context, className }: UiComponentRendererProps) {
  const node = useUiNode(context.store, nodeId);
  const trackRef = useRef<HTMLDivElement>(null);
  const [progress, setProgress] = useState(0);
  const [dragging, setDragging] = useState(false);
  const [playing, setPlaying] = useState(false);
  const [showBreakdown, setShowBreakdown] = useState(false);
  const [everMoved, setEverMoved] = useState(false);

  // The animation loop needs the current position every frame, and state is a frame behind
  // inside a rAF callback — so progress lives in a ref too and the two move together.
  const progressRef = useRef(0);

  const applyProgress = useCallback((value: number) => {
    progressRef.current = value;
    setProgress(value);
    setEverMoved(true);
  }, []);

  const figures = (node?.props?.['figures'] as RoofHeroFigures | undefined) ?? {
    traditionalTotal: 31_480,
    flextTotal: 17_147,
    breakdown: DEFAULT_BREAKDOWN,
    traditionalHoursLabel: '20–30',
    flextHoursLabel: '3–4',
    timeReductionLabel: 'Up to 85%',
    maintenanceDaysLabel: '85% fewer maintenance days',
    projectDaysLabel: 'Up to 1.5 fewer project days per roof',
    rotProtectionLabel: '10–20 years longer roof life',
  };

  const headline = (node?.props?.['headline'] as string | undefined) ?? 'Your house is built in a factory. Why is the roof still built by hand?';
  const subheadline = (node?.props?.['subheadline'] as string | undefined) ?? 'Drag the handle below. Both roofs build at once — Flext finishes first.';

  const seekFromClientX = useCallback((clientX: number) => {
    const el = trackRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    applyProgress(clamp01((clientX - rect.left) / rect.width));
  }, [applyProgress]);

  // Autoplay: advance by real elapsed time so the speed is constant regardless of frame
  // rate, and stop dead at the end rather than wrapping.
  useEffect(() => {
    if (!playing) return;

    let frame = 0;
    let last = performance.now();

    const step = (now: number) => {
      const next = Math.min(1, progressRef.current + (now - last) / PLAY_DURATION_MS);
      last = now;
      applyProgress(next);

      if (next >= 1) {
        setPlaying(false);
        return;
      }

      frame = requestAnimationFrame(step);
    };

    frame = requestAnimationFrame(step);
    return () => cancelAnimationFrame(frame);
  }, [playing, applyProgress]);

  const togglePlay = useCallback(() => {
    setPlaying((wasPlaying) => {
      if (wasPlaying) return false;
      // Pressing play at the end replays from the start instead of doing nothing.
      if (progressRef.current >= 1) applyProgress(0);
      return true;
    });
  }, [applyProgress]);

  const handlePointerDown = useCallback((event: React.PointerEvent<HTMLDivElement>) => {
    // Taking hold of the handle hands control back to the person — autoplay yields rather
    // than fighting the drag.
    setPlaying(false);
    // Capture keeps the drag alive once the pointer leaves the track, but it throws for a
    // pointer id the browser no longer considers active — and an exception here would kill
    // the drag before it starts. The drag works without capture, so failing is survivable.
    try {
      (event.target as HTMLElement).setPointerCapture?.(event.pointerId);
    } catch {
      /* no capture: the drag still tracks while the pointer stays over the track */
    }
    setDragging(true);
    seekFromClientX(event.clientX);
  }, [seekFromClientX]);

  const handlePointerMove = useCallback((event: React.PointerEvent<HTMLDivElement>) => {
    if (!dragging) return;
    seekFromClientX(event.clientX);
  }, [dragging, seekFromClientX]);

  const handlePointerUp = useCallback((event: React.PointerEvent<HTMLDivElement>) => {
    try {
      (event.target as HTMLElement).releasePointerCapture?.(event.pointerId);
    } catch {
      /* nothing was captured — releasing is a no-op */
    }
    setDragging(false);
  }, []);

  // Both roofs build from the same drag, in parallel — Flext's rate is simply faster, so it
  // reaches 1 long before the traditional roof does.
  const tradProgress = progress;
  const flextProgress = clamp01(progress / FLEXT_COMPLETE);
  const tradDone = tradProgress >= 1;
  const flextDone = flextProgress >= 1;

  const weather = useMemo(() => weatherForProgress(tradProgress), [tradProgress]);
  const traditionalFilled = Math.round(tradProgress * BATTEN_COUNT);

  const flextStage: 'place' | 'pull' | 'attach' = flextDone
    ? 'attach'
    : flextProgress < 0.33 ? 'place' : flextProgress < 0.66 ? 'pull' : 'attach';
  const pullFraction = clamp01((flextProgress - 0.33) / 0.33);
  const attachFraction = clamp01((flextProgress - 0.66) / 0.34);
  const bundleT = lerp(0.95, 0.05, pullFraction);
  const attachedThreshold = lerp(0.95, 0.05, attachFraction);

  const activeRowIndex = Math.min(traditionalFilled, BATTEN_COUNT - 1);
  const activeT = rowT(activeRowIndex);
  const workersVisible = tradProgress > 0.01 && tradProgress < 1;
  const worker1 = pointAt(activeT, 0.32);
  const worker2 = pointAt(activeT, 0.68);

  const traditionalCost = lerp(0, figures.traditionalTotal, tradProgress);
  const flextCost = lerp(0, figures.flextTotal, flextProgress);

  return (
    <div className={`roof-hero ${className ?? ''}`}>
      <div className="roof-hero-head">
        <h2 className="roof-hero-headline">{headline}</h2>
        <p className="roof-hero-sub">{subheadline}</p>
      </div>

      <div className="roof-hero-stage">
        <div className="roof-hero-canvases">
          {/* Traditional roof: battens built eave-up, exposed to the weather, the whole drag long. */}
          <div className="roof-hero-canvas">
            <div className="roof-hero-panel-chip">
              <span className="roof-hero-phase-dot" />
              {tradDone ? 'Done, finally' : 'On-site, by hand'}
            </div>

            <div className="roof-hero-weather">{weather.label}</div>

            <div className="roof-hero-overcast" style={{ opacity: weather.overcast }} />
            <div className={`roof-hero-rain roof-hero-rain-far ${weather.rain ? 'active' : ''}`} />
            <div className={`roof-hero-rain roof-hero-rain-near ${weather.rain ? 'active' : ''}`} />
            <div className={`roof-hero-wind-streaks ${weather.wind ? 'active' : ''}`} />
            <div className={`roof-hero-wind-gust ${weather.wind ? 'active' : ''}`} />
            <div className={`roof-hero-snow roof-hero-snow-far ${weather.snow ? 'active' : ''}`} />
            <div className={`roof-hero-snow roof-hero-snow-near ${weather.snow ? 'active' : ''}`} />
            <div className={`roof-hero-snow-drift ${weather.snow ? 'active' : ''}`} />

            <svg className="roof-hero-svg" viewBox="0 0 400 260" preserveAspectRatio="xMidYMid meet">
              <defs>
                <linearGradient
                  id="rh-deck-trad"
                  gradientUnits="userSpaceOnUse"
                  x1={(RIDGE_LEFT.x + RIDGE_RIGHT.x) / 2} y1={(RIDGE_LEFT.y + RIDGE_RIGHT.y) / 2}
                  x2={(EAVE_LEFT.x + EAVE_RIGHT.x) / 2} y2={(EAVE_LEFT.y + EAVE_RIGHT.y) / 2}
                >
                  <stop offset="0%" stopColor="var(--rh-rafter)" />
                  <stop offset="100%" stopColor="var(--rh-deck-near)" />
                </linearGradient>
                <filter id="rh-shadow-trad" x="-20%" y="-20%" width="140%" height="140%">
                  <feDropShadow dx="0" dy="2" stdDeviation="2" floodOpacity="0.35" />
                </filter>
              </defs>

              {/* fascia: a sliver of thickness along the eave, grounding the deck in 3D */}
              <polygon
                points={`${EAVE_LEFT.x},${EAVE_LEFT.y} ${EAVE_RIGHT.x},${EAVE_RIGHT.y} ${EAVE_RIGHT.x},${EAVE_RIGHT.y + 10} ${EAVE_LEFT.x},${EAVE_LEFT.y + 10}`}
                className="roof-hero-fascia"
              />

              <polygon
                points={`${RIDGE_LEFT.x},${RIDGE_LEFT.y} ${RIDGE_RIGHT.x},${RIDGE_RIGHT.y} ${EAVE_RIGHT.x},${EAVE_RIGHT.y} ${EAVE_LEFT.x},${EAVE_LEFT.y}`}
                fill="url(#rh-deck-trad)"
                filter="url(#rh-shadow-trad)"
              />

              {/* ridge cap, the one square edge on an otherwise sloped plane */}
              <line x1={RIDGE_LEFT.x} y1={RIDGE_LEFT.y} x2={RIDGE_RIGHT.x} y2={RIDGE_RIGHT.y} className="roof-hero-ridge-cap" />

              {Array.from({ length: BATTEN_COUNT }, (_, i) => {
                const t = rowT(i);
                const edge = edgeAt(t);
                const built = i < traditionalFilled;
                return (
                  <g key={`t-row-${i}`} className="roof-hero-batten" opacity={built ? 1 : 0.4}>
                    <line x1={edge.xLeft} y1={edge.yLeft + 1.6} x2={edge.xRight} y2={edge.yRight + 1.6}
                      stroke="var(--rh-plank-shadow)" strokeWidth={built ? 5.5 : 1.5} strokeLinecap="round" />
                    <line x1={edge.xLeft} y1={edge.yLeft} x2={edge.xRight} y2={edge.yRight}
                      stroke={built ? 'var(--rh-wood)' : 'var(--rh-rafter)'} strokeWidth={built ? 5 : 1.5} strokeLinecap="round" />
                    {built && (
                      <line x1={edge.xLeft} y1={edge.yLeft - 1.4} x2={edge.xRight} y2={edge.yRight - 1.4}
                        stroke="var(--rh-plank-highlight)" strokeWidth="1.1" strokeLinecap="round" opacity="0.7" />
                    )}
                  </g>
                );
              })}

              {workersVisible && (
                <>
                  <Worker x={worker1.x} y={worker1.y} vest="var(--rh-brand)" swing={activeRowIndex} />
                  <Worker x={worker2.x} y={worker2.y} vest="var(--rh-rafter)" swing={activeRowIndex + 1} />
                </>
              )}
            </svg>

            <div className={`roof-hero-panel-stat ${tradDone ? 'visible' : ''}`}>
              {money(figures.traditionalTotal)} · {figures.traditionalHoursLabel} h
            </div>
          </div>

          {/* Flext roof: the same deck, placed, pulled out and attached far faster. */}
          <div className="roof-hero-canvas">
            <div className={`roof-hero-panel-chip ${flextDone ? 'is-done' : ''}`}>
              <span className="roof-hero-phase-dot is-flext" />
              {flextDone ? 'Done already' : 'Factory-made, installed fast'}
            </div>

            <svg className="roof-hero-svg" viewBox="0 0 400 260" preserveAspectRatio="xMidYMid meet">
              <defs>
                <linearGradient
                  id="rh-deck-flext"
                  gradientUnits="userSpaceOnUse"
                  x1={(RIDGE_LEFT.x + RIDGE_RIGHT.x) / 2} y1={(RIDGE_LEFT.y + RIDGE_RIGHT.y) / 2}
                  x2={(EAVE_LEFT.x + EAVE_RIGHT.x) / 2} y2={(EAVE_LEFT.y + EAVE_RIGHT.y) / 2}
                >
                  <stop offset="0%" stopColor="var(--rh-rafter)" />
                  <stop offset="100%" stopColor="var(--rh-deck-near)" />
                </linearGradient>
                <filter id="rh-shadow-flext" x="-20%" y="-20%" width="140%" height="140%">
                  <feDropShadow dx="0" dy="2" stdDeviation="2" floodOpacity="0.35" />
                </filter>
              </defs>

              <polygon
                points={`${EAVE_LEFT.x},${EAVE_LEFT.y} ${EAVE_RIGHT.x},${EAVE_RIGHT.y} ${EAVE_RIGHT.x},${EAVE_RIGHT.y + 10} ${EAVE_LEFT.x},${EAVE_LEFT.y + 10}`}
                className="roof-hero-fascia"
              />

              <polygon
                points={`${RIDGE_LEFT.x},${RIDGE_LEFT.y} ${RIDGE_RIGHT.x},${RIDGE_RIGHT.y} ${EAVE_RIGHT.x},${EAVE_RIGHT.y} ${EAVE_LEFT.x},${EAVE_LEFT.y}`}
                fill="url(#rh-deck-flext)"
                filter="url(#rh-shadow-flext)"
              />

              <line x1={RIDGE_LEFT.x} y1={RIDGE_LEFT.y} x2={RIDGE_RIGHT.x} y2={RIDGE_RIGHT.y} className="roof-hero-ridge-cap" />

              {!flextDone && flextStage !== 'place' && flextProgress > 0 && [0.16, 0.5, 0.84].map((s) => {
                const top = pointAt(0.97, s);
                const bottom = pointAt(bundleT, s);
                return (
                  <g key={`strap-${s}`} className="roof-hero-strap">
                    <line x1={top.x} y1={top.y} x2={bottom.x} y2={bottom.y} stroke="var(--rh-brand)" strokeWidth="2" opacity={0.85} />
                    <rect x={bottom.x - 3} y={bottom.y - 2} width="6" height="4" rx="1" fill="var(--rh-brand)" />
                  </g>
                );
              })}

              {Array.from({ length: BATTEN_COUNT }, (_, i) => {
                const t = rowT(i);
                const edge = edgeAt(t);
                const attached = flextDone || t >= attachedThreshold - 0.001;
                const reached = !flextDone && flextStage === 'attach' && t <= bundleT + 0.08;
                return (
                  <g key={`f-row-${i}`} className="roof-hero-batten" opacity={attached ? 1 : reached ? 0.6 : 0.3}>
                    <line x1={edge.xLeft} y1={edge.yLeft + 1.6} x2={edge.xRight} y2={edge.yRight + 1.6}
                      stroke="var(--rh-plank-shadow)" strokeWidth={attached ? 5.5 : 1.5} strokeLinecap="round" />
                    <line x1={edge.xLeft} y1={edge.yLeft} x2={edge.xRight} y2={edge.yRight}
                      stroke={attached ? 'var(--rh-success)' : 'var(--rh-rafter)'} strokeWidth={attached ? 5 : 1.5} strokeLinecap="round" />
                    {attached && (
                      <line x1={edge.xLeft} y1={edge.yLeft - 1.4} x2={edge.xRight} y2={edge.yRight - 1.4}
                        stroke="var(--rh-plank-highlight)" strokeWidth="1.1" strokeLinecap="round" opacity="0.7" />
                    )}
                  </g>
                );
              })}

              {!flextDone && flextProgress > 0 && (() => {
                // a bound bundle: several individual lengths of timber, strapped together,
                // exactly as the memorandum's own renders show it mid-pull.
                const strips = [-3, -1.4, 0, 1.4, 3];
                return (
                  <g className="roof-hero-bundle">
                    {strips.map((offset) => {
                      const a = edgeAt(clamp01(bundleT + offset * 0.004));
                      return (
                        <line key={offset} x1={a.xLeft} y1={a.yLeft + offset} x2={a.xRight} y2={a.yRight + offset}
                          stroke="var(--rh-wood)" strokeWidth="2.4" strokeLinecap="round" />
                      );
                    })}
                  </g>
                );
              })()}
            </svg>

            <div className={`roof-hero-panel-stat is-flext ${flextDone ? 'visible' : ''}`}>
              {money(figures.flextTotal)} · {figures.flextHoursLabel} h · {figures.timeReductionLabel.toLowerCase()} faster
            </div>
          </div>
        </div>

        <div className="roof-hero-counter-row">
          <div className="roof-hero-counter">
            <div className="roof-hero-counter-label">Traditional cost</div>
            <div className="roof-hero-counter-value">{money(traditionalCost)}</div>
            <div className="roof-hero-counter-sub">{tradDone ? 'Finished — the long way.' : 'Climbing with every manual step.'}</div>
          </div>

          <div className="roof-hero-counter">
            <div className="roof-hero-counter-label">Flext cost</div>
            <div className="roof-hero-counter-value is-flext">{money(flextCost)}</div>
            <div className="roof-hero-counter-sub">
              {flextDone
                ? `Finished — ${figures.maintenanceDaysLabel.toLowerCase()}.`
                : `${flextStage === 'place' ? 'Placing' : flextStage === 'pull' ? 'Pulling out' : 'Attaching'} the bundle.`}
            </div>
          </div>
        </div>

        <button type="button" className="roof-hero-breakdown-toggle" onClick={() => setShowBreakdown((v) => !v)}>
          {showBreakdown ? 'Hide cost breakdown' : 'Where does the cost go?'}
        </button>

        {showBreakdown && (
          <div className="roof-hero-breakdown">
            {figures.breakdown.map((line) => {
              const lineMax = Math.max(line.traditional ?? 0, line.flext);
              return (
                <div key={line.label} className="roof-hero-breakdown-line">
                  <div className="roof-hero-breakdown-label">{line.label}</div>
                  <div>{line.traditional === null ? 'Traditional — none' : `Traditional — ${money(line.traditional)}`}</div>
                  <div className="roof-hero-breakdown-bar-track">
                    <div
                      className="roof-hero-breakdown-bar-fill is-traditional"
                      style={{ width: `${lineMax > 0 ? ((line.traditional ?? 0) / lineMax) * 100 : 0}%` }}
                    />
                  </div>
                  <div>Flext — {money(line.flext)}</div>
                  <div className="roof-hero-breakdown-bar-track">
                    <div
                      className="roof-hero-breakdown-bar-fill is-flext"
                      style={{ width: `${lineMax > 0 ? (line.flext / lineMax) * 100 : 0}%` }}
                    />
                  </div>
                </div>
              );
            })}
            <div className="roof-hero-breakdown-note">Flext costs more as a material, much less as a finished roof.</div>
          </div>
        )}
      </div>

      <div className="roof-hero-scrubber">
        <button
          type="button"
          className="roof-hero-play"
          onClick={togglePlay}
          aria-label={playing ? 'Pause' : progress >= 1 ? 'Play again' : 'Play'}
        >
          {playing ? (
            <svg viewBox="0 0 16 16" aria-hidden="true"><rect x="4" y="3" width="3" height="10" rx="1" /><rect x="9" y="3" width="3" height="10" rx="1" /></svg>
          ) : progress >= 1 ? (
            <svg className="is-stroked" viewBox="0 0 16 16" aria-hidden="true" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
              <path d="M13 8a5 5 0 1 1-1.6-3.7" /><path d="M13 2.5V5h-2.5" />
            </svg>
          ) : (
            <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M5 3.3v9.4a.6.6 0 0 0 .92.5l7.2-4.7a.6.6 0 0 0 0-1l-7.2-4.7A.6.6 0 0 0 5 3.3Z" /></svg>
          )}
        </button>
        <div
          className="roof-hero-scrubber-track"
          ref={trackRef}
          onPointerDown={handlePointerDown}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerUp}
          onPointerCancel={handlePointerUp}
        >
          <div className="roof-hero-scrubber-fill" style={{ width: `${progress * 100}%` }} />
          <div className="roof-hero-scrubber-mark" style={{ left: `${FLEXT_COMPLETE * 100}%` }} title="Flext finishes here" />
          <div
            className={`roof-hero-scrubber-thumb ${dragging ? 'is-dragging' : ''}`}
            style={{ left: `${progress * 100}%` }}
          />
          {!everMoved && <div className="roof-hero-scrubber-hint">Press play, or drag →</div>}
        </div>
        <span className="roof-hero-scrubber-caption">Both roofs done</span>
      </div>
    </div>
  );
}

const MemoRoofHeroRenderer = memo(RoofHeroRenderer);

export function createRoofHeroResolver(): IkonUiComponentResolver {
  return (node) => {
    if (node.type !== 'custom.roof-hero') return undefined;
    return MemoRoofHeroRenderer;
  };
}
