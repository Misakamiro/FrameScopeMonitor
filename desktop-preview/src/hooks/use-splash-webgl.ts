import { useEffect, useRef, type RefObject } from "react";
import { deriveSplashColors } from "@/lib/color-utils";

/**
 * 启动页 WebGL 霓虹线背景动画。
 * 移植自 Mineradio 03-splash.js 的 initMineradioSplashWebgl + drawMineradioSplashWebgl，
 * 用片段着色器全屏绘制流动的霓虹光线束。
 *
 * 与 Mineradio 的差异：三通道配色不再写死红/青/金，而是从传入的主题主色（primaryColor）
 * 动态派生（主色 / 亮部高光 / 色相偏移点缀），跟随用户主题色自动切换。
 */

type Rgb = [number, number, number];

interface SplashColors {
  c1: Rgb;
  c2: Rgb;
  c3: Rgb;
}

const VERTEX_SOURCE = [
  "attribute vec2 aPosition;",
  "varying vec2 vUv;",
  "void main(){",
  "  vUv = aPosition * 0.5 + 0.5;",
  "  gl_Position = vec4(aPosition, 0.0, 1.0);",
  "}",
].join("\n");

const FRAGMENT_SOURCE = [
  "precision highp float;",
  "varying vec2 vUv;",
  "uniform vec2 uResolution;",
  "uniform float uTime;",
  "uniform vec3 uColor1;",
  "uniform vec3 uColor2;",
  "uniform vec3 uColor3;",
  "",
  "float saturate(float v){ return clamp(v, 0.0, 1.0); }",
  "float ease(float v){ v = saturate(v); return v * v * (3.0 - 2.0 * v); }",
  "mat2 rot(float a){ float c = cos(a); float s = sin(a); return mat2(c, -s, s, c); }",
  "float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }",
  "float noise(vec2 p){",
  "  vec2 i = floor(p);",
  "  vec2 f = fract(p);",
  "  vec2 u = f * f * (3.0 - 2.0 * f);",
  "  return mix(mix(hash(i), hash(i + vec2(1.0,0.0)), u.x), mix(hash(i + vec2(0.0,1.0)), hash(i + vec2(1.0,1.0)), u.x), u.y);",
  "}",
  "",
  "float animatedLoop(vec2 uv, float t, float channel){",
  "  vec2 q = uv;",
  "  q *= rot(0.28 + sin(t * 0.18) * 0.12);",
  "  q.x += 0.055 * sin(t * 0.30 + channel);",
  "  q.y += 0.040 * cos(t * 0.24 + channel * 1.7);",
  "  float ang = atan(q.y, q.x);",
  "  float angularShift = sin(ang * 3.0 + t * 0.72 + channel * 1.9) * 0.078;",
  "  angularShift += sin(ang * 7.0 - t * 0.54 + channel) * 0.020;",
  "  float neonD = length(q) + angularShift;",
  "  float warpD = length(q * vec2(1.34 + 0.06 * sin(t * 0.25), 0.82 + 0.04 * cos(t * 0.31)));",
  "  warpD += 0.026 * sin(q.x * 4.4 + t * 0.62) + 0.018 * sin(q.y * 5.2 - t * 0.45);",
  "  float diamondD = abs(q.x) * 1.20 + abs(q.y) * 0.84;",
  "  float d = mix(warpD, diamondD, 0.32);",
  "  d = mix(d, neonD, 0.20 + 0.04 * sin(t * 0.18 + channel));",
  "  float pattern = mod((q.x + q.y) * 0.62 + sin(q.x * 5.5 + t) * 0.015 + sin(q.y * 7.0 - t * 0.75) * 0.012, 0.20);",
  "  float acc = 0.0;",
  "  for (int i = 1; i <= 6; i++) {",
  "    float fi = float(i);",
  "    float f = fract(t * 0.152 - channel * 0.018 + 0.011 * fi) * 4.70 - d + pattern;",
  "    acc += 0.00110 * fi * fi / max(abs(f), 0.0065);",
  "  }",
  "  float threadCoord = q.x * 0.92 - q.y * 0.58 + 0.030 * sin(q.x * 5.2 + t * 0.72);",
  "  float threadLines = 0.0065 / max(abs(sin((threadCoord + t * 0.10 + channel * 0.035) * 27.0)), 0.070);",
  "  acc += threadLines * (0.50 + 0.30 * sin(ang * 1.2 + t + channel));",
  "  return min(acc, 1.95);",
  "}",
  "",
  "void main(){",
  "  vec2 p = vUv * 2.0 - 1.0;",
  "  p.x *= uResolution.x / max(uResolution.y, 1.0);",
  "  float t = uTime;",
  "  float intro = ease(t / 0.72);",
  "  float bloomIn = ease((t - 0.10) / 1.10);",
  "  float climax = exp(-pow((t - 3.62) / 0.58, 2.0));",
  "  float preClimax = ease((t - 2.15) / 1.25) * (1.0 - ease((t - 3.86) / 0.72));",
  "  float afterglow = exp(-pow((t - 4.14) / 0.62, 2.0));",
  "  float calm = 1.0 - 0.22 * ease((t - 4.75) / 0.70);",
  "  float settle = 1.0 - 0.34 * ease((t - 5.05) / 0.52);",
  "  vec2 uv = p * (0.98 + 0.05 * sin(t * 0.25));",
  "  uv += vec2(0.0, -0.025);",
  "  vec2 flowAxis = normalize(vec2(0.86, -0.50));",
  "  vec2 crossAxis = vec2(-flowAxis.y, flowAxis.x);",
  "  float lane = dot(p, flowAxis);",
  "  float crossLane = dot(p, crossAxis);",
  "  float syncWave = sin(crossLane * 5.4 + lane * 1.1 - t * 1.85);",
  "  uv += flowAxis * syncWave * 0.055 * climax;",
  "  uv += crossAxis * sin(lane * 7.2 + t * 1.25) * 0.034 * climax;",
  "  uv *= 1.0 + 0.045 * preClimax - 0.020 * climax;",
  "  vec3 ch1 = uColor1;",
  "  vec3 ch2 = uColor2;",
  "  vec3 ch3 = uColor3;",
  "  float a = animatedLoop(uv, t, 0.0);",
  "  float b = animatedLoop(uv * 1.018 + vec2(0.012, -0.008), t + 0.18, 1.0);",
  "  float c = animatedLoop(uv * 0.986 + vec2(-0.010, 0.010), t + 0.35, 2.0);",
  "  vec3 loopCol = ch1 * a + ch2 * b + ch3 * c;",
  "  float tunnel = animatedLoop(uv * 1.42 + vec2(sin(t * 0.2) * 0.08, cos(t * 0.17) * 0.05), t * 1.12 + 1.7, 2.7);",
  "  loopCol += mix(ch2, ch3, 0.35 + 0.25 * sin(t)) * tunnel * (0.30 + 0.24 * preClimax);",
  "  float syncBand = exp(-pow((lane + 0.08 * sin(t * 0.72)) / 0.62, 2.0));",
  "  float phaseThread = pow(0.5 + 0.5 * sin(crossLane * 13.5 + lane * 2.2 - t * 3.1), 8.0);",
  "  float phaseThread2 = pow(0.5 + 0.5 * sin(crossLane * 9.0 - lane * 5.4 + t * 2.4), 10.0);",
  "  vec3 climaxCol = (mix(ch2, ch3, 0.36) * phaseThread + ch1 * phaseThread2 * 0.52) * syncBand * climax;",
  "  float afterBand = exp(-pow((lane - 0.34) / 0.72, 2.0));",
  "  climaxCol += mix(ch1, ch2, vUv.x) * afterBand * afterglow * 0.13;",
  "  float centerBeam = exp(-abs(p.y + 0.005 * sin(t * 3.0)) * 24.0) * (0.14 + 0.52 * exp(-pow((t - 0.74) / 0.34, 2.0)));",
  "  float bladeMask = smoothstep(-1.55, -0.08, p.x) * (1.0 - smoothstep(0.08, 1.55, p.x));",
  "  vec3 blade = mix(ch1, ch2, vUv.x) * centerBeam * bladeMask * (0.40 + 0.28 * climax);",
  "  float flare = exp(-dot(p, p) * 3.6) * exp(-pow((t - 0.88) / 0.40, 2.0));",
  "  vec3 col = vec3(0.002, 0.004, 0.005);",
  "  col += loopCol * (0.56 + 0.46 * bloomIn) * calm * settle;",
  "  col += climaxCol * 0.22;",
  "  float diagonalGlint = exp(-pow(lane * 1.2 + crossLane * 0.10, 2.0) / 0.030) * climax;",
  "  col += blade + uColor2 * flare * 0.22 + uColor3 * diagonalGlint * 0.09;",
  "  float scan = 0.92 + 0.08 * sin((vUv.y * uResolution.y + t * 52.0) * 0.72);",
  "  float grain = noise(vUv * uResolution.xy * 0.52 + t * 17.0) - 0.5;",
  "  col *= scan;",
  "  col += grain * 0.018;",
  "  col *= intro;",
  "  col = max(col - vec3(0.010, 0.012, 0.012), 0.0);",
  "  col = vec3(1.0) - exp(-max(col, 0.0) * (0.62 + 0.18 * climax));",
  "  float vignette = smoothstep(1.52, 0.20, length(p * vec2(0.78, 1.04)));",
  "  col *= 0.38 + 0.86 * vignette;",
  "  col += vec3(0.006, 0.012, 0.018) * (1.0 - vignette);",
  "  gl_FragColor = vec4(col, 1.0);",
  "}",
].join("\n");

function compileShader(
  gl: WebGLRenderingContext,
  type: number,
  source: string,
): WebGLShader | null {
  const shader = gl.createShader(type);
  if (!shader) return null;
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    console.warn("NexBox splash shader compile failed:", gl.getShaderInfoLog(shader));
    gl.deleteShader(shader);
    return null;
  }
  return shader;
}

export function useSplashWebgl(
  canvasRef: RefObject<HTMLCanvasElement | null>,
  primaryColor: string,
) {
  // 主题色变化时刷新派生配色（启动页渲染期间也能跟随）
  const colorsRef = useRef<SplashColors>(deriveSplashColors(primaryColor));
  useEffect(() => {
    colorsRef.current = deriveSplashColors(primaryColor);
  }, [primaryColor]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    // 系统开启“减少动态效果”时仅显示静态 CSS 背景
    if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;

    let gl: WebGLRenderingContext | null = null;
    try {
      gl = canvas.getContext("webgl", {
        alpha: true,
        antialias: false,
        depth: false,
        stencil: false,
        premultipliedAlpha: false,
        preserveDrawingBuffer: false,
        powerPreference: "high-performance",
      }) as WebGLRenderingContext | null;
      if (!gl) gl = canvas.getContext("experimental-webgl") as WebGLRenderingContext | null;
    } catch {
      gl = null;
    }
    if (!gl) return;

    const vertexShader = compileShader(gl, gl.VERTEX_SHADER, VERTEX_SOURCE);
    const fragmentShader = compileShader(gl, gl.FRAGMENT_SHADER, FRAGMENT_SOURCE);
    if (!vertexShader || !fragmentShader) {
      gl.getExtension("WEBGL_lose_context")?.loseContext();
      return;
    }

    const program = gl.createProgram();
    if (!program) {
      gl.getExtension("WEBGL_lose_context")?.loseContext();
      return;
    }
    gl.attachShader(program, vertexShader);
    gl.attachShader(program, fragmentShader);
    gl.linkProgram(program);
    gl.deleteShader(vertexShader);
    gl.deleteShader(fragmentShader);
    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
      console.warn("NexBox splash shader link failed:", gl.getProgramInfoLog(program));
      gl.deleteProgram(program);
      gl.getExtension("WEBGL_lose_context")?.loseContext();
      return;
    }

    const buffer = gl.createBuffer();
    if (!buffer) {
      gl.deleteProgram(program);
      gl.getExtension("WEBGL_lose_context")?.loseContext();
      return;
    }
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);

    const positionLoc = gl.getAttribLocation(program, "aPosition");
    const resolutionLoc = gl.getUniformLocation(program, "uResolution");
    const timeLoc = gl.getUniformLocation(program, "uTime");
    const color1Loc = gl.getUniformLocation(program, "uColor1");
    const color2Loc = gl.getUniformLocation(program, "uColor2");
    const color3Loc = gl.getUniformLocation(program, "uColor3");

    gl.disable(gl.DEPTH_TEST);
    gl.disable(gl.CULL_FACE);

    const resize = () => {
      const dpr = Math.min(1.6, Math.max(1, window.devicePixelRatio || 1));
      const w = Math.max(1, Math.floor(window.innerWidth * dpr));
      const h = Math.max(1, Math.floor(window.innerHeight * dpr));
      canvas.width = w;
      canvas.height = h;
      gl!.viewport(0, 0, w, h);
    };
    resize();
    window.addEventListener("resize", resize);

    const startedAt = performance.now();
    let rafId = 0;
    const draw = () => {
      const elapsed = (performance.now() - startedAt) / 1000;
      gl!.viewport(0, 0, canvas.width, canvas.height);
      gl!.useProgram(program);
      gl!.bindBuffer(gl!.ARRAY_BUFFER, buffer);
      gl!.enableVertexAttribArray(positionLoc);
      gl!.vertexAttribPointer(positionLoc, 2, gl!.FLOAT, false, 0, 0);
      const col = colorsRef.current;
      gl!.uniform3f(color1Loc, col.c1[0], col.c1[1], col.c1[2]);
      gl!.uniform3f(color2Loc, col.c2[0], col.c2[1], col.c2[2]);
      gl!.uniform3f(color3Loc, col.c3[0], col.c3[1], col.c3[2]);
      gl!.uniform2f(resolutionLoc, canvas.width, canvas.height);
      gl!.uniform1f(timeLoc, elapsed);
      gl!.drawArrays(gl!.TRIANGLES, 0, 3);
      rafId = requestAnimationFrame(draw);
    };
    rafId = requestAnimationFrame(draw);

    return () => {
      cancelAnimationFrame(rafId);
      window.removeEventListener("resize", resize);
      gl!.deleteBuffer(buffer);
      gl!.deleteProgram(program);
      // 注意：不要调用 loseContext()。React StrictMode 在 dev 下会挂载→清理→再挂载，
      // loseContext 会永久销毁上下文导致第二次挂载拿不到 WebGL；
      // 组件卸载后 canvas 从 DOM 移除即可释放 GPU 资源，StrictMode 下则复用同一上下文重初始化。
    };
  }, [canvasRef]);
}
