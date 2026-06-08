"use strict";
const pptxgen = require("pptxgenjs");

// ── Brand constants ────────────────────────────────────────────────────────────
const C = {
  red:       "CC2222",
  darkGrey:  "595959",
  nearBlack: "222222",
  yellow:    "FFD700",
  white:     "FFFFFF",
  midGrey:   "AAAAAA",
  codeGrey:  "1E1E1E",
  cardDark:  "2A2A2A",
  cardLine:  "383838",
  rowLight:  "FAFAFA",
  rowBorder: "E8E8E8",
};

const FONT      = "Arsenal";
const FONT_MONO = "Consolas";
const FOOTER    = "Confidential — Internal Use Only  |  ONE BCG  |  2026";

// Fresh shadow object each call — PptxGenJS mutates in-place
const mkShadow = () => ({ type: "outer", blur: 8, offset: 3, angle: 135, color: "000000", opacity: 0.12 });

// ── Helpers ────────────────────────────────────────────────────────────────────
function addFooter(slide) {
  slide.addText(FOOTER, {
    x: 0, y: 5.3, w: 10, h: 0.25,
    fontFace: FONT, fontSize: 8, color: C.midGrey,
    align: "center", valign: "middle", margin: 0,
  });
}

function sectionBadge(slide, label) {
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 0.26, w: 1.4, h: 0.23,
    fill: { color: C.red }, line: { color: C.red },
  });
  slide.addText(label, {
    x: 0.5, y: 0.26, w: 1.4, h: 0.23,
    fontFace: FONT, fontSize: 7.5, color: C.white,
    align: "center", valign: "middle", margin: 0,
  });
}

// ── Presentation ───────────────────────────────────────────────────────────────
const pres = new pptxgen();
pres.layout  = "LAYOUT_16x9";   // 10" × 5.625"
pres.author  = "ONE BCG";
pres.title   = "ToolEngine v2026 — Stakeholder Presentation";

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 01 — Title
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.nearBlack };

  // Red vertical accent
  s.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 0, w: 0.18, h: 5.625,
    fill: { color: C.red }, line: { color: C.red },
  });

  s.addText("ONE BCG", {
    x: 0.38, y: 0.32, w: 3, h: 0.38,
    fontFace: FONT, fontSize: 11, color: C.red,
    align: "left", valign: "middle", margin: 0,
  });

  s.addText("ToolEngine v2026", {
    x: 0.38, y: 1.2, w: 9.3, h: 1.0,
    fontFace: FONT, fontSize: 54, color: C.white,
    align: "left", valign: "middle", margin: 0,
  });

  s.addText("The ONE BCG AI Tool Invocation Backbone", {
    x: 0.38, y: 2.3, w: 9.2, h: 0.5,
    fontFace: FONT, fontSize: 20, color: C.yellow,
    align: "left", valign: "middle", margin: 0,
  });

  s.addShape(pres.shapes.LINE, {
    x: 0.38, y: 2.98, w: 8.4, h: 0,
    line: { color: C.darkGrey, width: 0.8 },
  });

  s.addText("Multi-tenant  ·  .NET 8  ·  Production-ready  ·  AI-governed  ·  Compliance-first", {
    x: 0.38, y: 3.1, w: 9, h: 0.34,
    fontFace: FONT, fontSize: 13, color: C.darkGrey,
    align: "left", valign: "middle", margin: 0,
  });

  s.addText("May 2026  |  Presented by ONE BCG Engineering", {
    x: 0.38, y: 4.85, w: 6, h: 0.28,
    fontFace: FONT, fontSize: 10, color: C.darkGrey,
    align: "left", valign: "middle", margin: 0,
  });

  s.addText("Confidential — Internal Use Only", {
    x: 6.7, y: 5.2, w: 3.0, h: 0.22,
    fontFace: FONT, fontSize: 8, color: C.darkGrey,
    align: "right", valign: "middle", margin: 0,
  });
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 02 — The Problem
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.white };
  sectionBadge(s, "PROBLEM");

  s.addText("Why We Built This", {
    x: 0.5, y: 0.58, w: 9, h: 0.55,
    fontFace: FONT, fontSize: 30, color: C.nearBlack,
    align: "left", valign: "middle", margin: 0,
  });

  const problems = [
    { title: "No controlled execution layer",
      body:  "AI agents invoke tools directly — no validation, no approval gate, no audit trail." },
    { title: "Ungoverned LLM calls",
      body:  "LLMs answer out-of-scope queries freely. No mechanism to classify or contain agent behaviour at runtime." },
    { title: "Cost overruns",
      body:  "Runaway agent loops and uncapped token usage generate uncontrolled spend with no per-tenant budget visibility." },
    { title: "Compliance exposure",
      body:  "GDPR, EU AI Act Art. 14, and SOC 2 require human-in-the-loop for high-risk decisions. Nothing enforces this." },
    { title: "Fragile extensibility",
      body:  "Adding a tool, provider, or tenant requires surgery on core code. No plugin contract exists." },
  ];

  problems.forEach((p, i) => {
    const y = 1.3 + i * 0.78;
    s.addShape(pres.shapes.RECTANGLE, {
      x: 0.5, y: y + 0.04, w: 0.06, h: 0.6,
      fill: { color: C.red }, line: { color: C.red },
    });
    s.addText(p.title, {
      x: 0.7, y: y + 0.04, w: 9.0, h: 0.27,
      fontFace: FONT, fontSize: 12, color: C.nearBlack,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(p.body, {
      x: 0.7, y: y + 0.32, w: 9.0, h: 0.27,
      fontFace: FONT, fontSize: 11, color: C.darkGrey,
      align: "left", valign: "middle", margin: 0,
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 03 — What We Built
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.nearBlack };
  sectionBadge(s, "SOLUTION");

  s.addText("What We Built", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 30, color: C.white,
    align: "left", valign: "middle", margin: 0,
  });

  s.addText("A production-grade AI tool invocation platform for ONE BCG.", {
    x: 0.5, y: 1.15, w: 9, h: 0.3,
    fontFace: FONT, fontSize: 13, color: C.yellow,
    align: "left", valign: "middle", margin: 0,
  });

  const caps = [
    { n: "01", title: "Governed Tool Execution",
      body: "Every invocation passes 7 pipeline behaviors — auth, validation, budgets, loop detection, approval, audit." },
    { n: "02", title: "Multi-Tenant Isolation",
      body: "Namespace-level allow/deny lists. Each tenant sees only their tools. No cross-tenant data leakage possible." },
    { n: "03", title: "Human-in-the-Loop Approval",
      body: "Risk-tiered: Low auto-approves, Medium/High route to dashboard/webhook, Critical requires OTP." },
    { n: "04", title: "LLM Agent Layer",
      body: "Scope classifier, tool guard, grounding reminder, fallback chain — three providers behind one interface." },
    { n: "05", title: "Full Observability",
      body: "OpenTelemetry traces, Serilog structured logs with PII masking, per-tenant budget meters." },
    { n: "06", title: "Compliance by Design",
      body: "NIST AI RMF, EU AI Act Art. 14, ISO 42001, SOC 2 CC6/CC7, GDPR — built in, not bolted on." },
  ];

  caps.forEach((c, i) => {
    const x = i % 2 === 0 ? 0.5 : 5.3;
    const y = 1.55 + Math.floor(i / 2) * 1.22;

    s.addShape(pres.shapes.RECTANGLE, {
      x, y, w: 4.55, h: 1.1,
      fill: { color: C.cardDark }, line: { color: C.cardLine, width: 0.5 },
    });
    s.addText(c.n, {
      x: x + 0.15, y: y + 0.1, w: 0.4, h: 0.32,
      fontFace: FONT, fontSize: 16, color: C.yellow,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(c.title, {
      x: x + 0.6, y: y + 0.1, w: 3.8, h: 0.3,
      fontFace: FONT, fontSize: 12, color: C.white,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(c.body, {
      x: x + 0.15, y: y + 0.46, w: 4.25, h: 0.56,
      fontFace: FONT, fontSize: 9.5, color: C.darkGrey,
      align: "left", valign: "top", margin: 0,
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 04 — Architecture
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.white };
  sectionBadge(s, "ARCHITECTURE");

  s.addText("Clean 6-Layer Architecture", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 28, color: C.nearBlack,
    align: "left", valign: "middle", margin: 0,
  });

  const layers = [
    { name: "Hosts  (API / CLI)",             detail: "ASP.NET Core Minimal API · JWT auth · Rate limiter · Scalar UI · Program.cs startup validation",    fill: C.red,       line: C.red,       textClr: C.white },
    { name: "Application",                    detail: "CQRS + MediatR · 7 pipeline behaviors from TenantAuth through to Audit · No infra references",       fill: "CC5522",    line: "CC5522",    textClr: C.white },
    { name: "Infrastructure",                 detail: "EF Core repos · Serilog · OpenTelemetry · Approval channels · Outbox dispatcher · LLM provider DI",   fill: C.darkGrey,  line: C.darkGrey,  textClr: C.white },
    { name: "Llm",                            detail: "AgentOrchestrator · ScopeClassifier · ScopeEnforcer · ToolGuard · Anthropic / OpenAI / Ollama adapters",fill: "336699",    line: "336699",    textClr: C.white },
    { name: "Tools.*",                        detail: "Concrete tool implementations — standalone projects, zero infra coupling, registered via AddTool<>()",  fill: "448844",    line: "448844",    textClr: C.white },
    { name: "Core.Domain  /  Core.Abstractions", detail: "Entities · Value objects · Result<T> · Contracts · Constants · Zero external dependencies",         fill: C.nearBlack, line: C.nearBlack, textClr: C.yellow },
  ];

  layers.forEach((l, i) => {
    const indent = i * 0.1;
    const y = 1.22 + i * 0.66;
    const w = 9.3 - indent * 2;

    s.addShape(pres.shapes.RECTANGLE, {
      x: 0.35 + indent, y, w, h: 0.58,
      fill: { color: l.fill, transparency: i < 2 ? 0 : 82 },
      line: { color: l.line, width: 1.5 },
    });
    s.addText(l.name, {
      x: 0.5 + indent, y: y + 0.05, w: 2.8, h: 0.5,
      fontFace: FONT, fontSize: 10.5, color: i < 2 ? l.textClr : l.line,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(l.detail, {
      x: 3.45 + indent, y: y + 0.05, w: 5.9 - indent * 2, h: 0.5,
      fontFace: FONT, fontSize: 9, color: i < 2 ? "EEEEEE" : C.darkGrey,
      align: "left", valign: "middle", margin: 0,
    });
  });

  s.addText("↑ depends on layers below", {
    x: 7.5, y: 1.5, w: 2.2, h: 0.28,
    fontFace: FONT, fontSize: 8, color: C.midGrey,
    align: "right", valign: "middle", margin: 0,
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 05 — Design Philosophy
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.nearBlack };
  sectionBadge(s, "PATTERNS");

  s.addText("Design Philosophy — 17 Applied Patterns", {
    x: 0.5, y: 0.58, w: 9, h: 0.48,
    fontFace: FONT, fontSize: 26, color: C.white,
    align: "left", valign: "middle", margin: 0,
  });

  const patterns = [
    "Clean Architecture",    "Domain Driven Design",      "CQRS + MediatR",
    "Railway / Result<T>",  "Decorator (Behaviors)",     "Repository + UoW",
    "Outbox Pattern",        "Specification Pattern",     "Strategy (Providers)",
    "Factory Method",        "Plugin / Registry",         "Idempotency Keys",
    "Scope Classification",  "Circuit Breaker (Budget)",  "W3C TraceContext",
    "FrozenDictionary Cache","Prompt Externalisation",    "",
  ];

  patterns.forEach((p, i) => {
    if (!p) return;
    const col = i % 3;
    const row = Math.floor(i / 3);
    const x = 0.45 + col * 3.1;
    const y = 1.18 + row * 0.68;

    s.addShape(pres.shapes.RECTANGLE, {
      x, y, w: 2.95, h: 0.56,
      fill: { color: C.cardDark }, line: { color: C.cardLine, width: 0.5 },
    });
    s.addShape(pres.shapes.OVAL, {
      x: x + 0.14, y: y + 0.2, w: 0.13, h: 0.13,
      fill: { color: C.red }, line: { color: C.red },
    });
    s.addText(p, {
      x: x + 0.35, y, w: 2.55, h: 0.56,
      fontFace: FONT, fontSize: 10.5, color: C.white,
      align: "left", valign: "middle", margin: 0,
    });
  });

  s.addText("Every pattern chosen to reduce coupling, maximise testability, and enable extensibility without touching existing code.", {
    x: 0.45, y: 5.1, w: 9.2, h: 0.25,
    fontFace: FONT, fontSize: 9, color: C.darkGrey,
    align: "left", valign: "middle", margin: 0,
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 06 — Request / Data Flow
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.white };
  sectionBadge(s, "DATA FLOW");

  s.addText("How a Request Flows Through ToolEngine", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 26, color: C.nearBlack,
    align: "left", valign: "middle", margin: 0,
  });

  const steps = [
    { n: "1", label: "HTTP / CLI",     sub: "JWT validated\nClaims extracted",             dark: false },
    { n: "2", label: "7 Behaviors",    sub: "Auth→Valid→Budget\nLoop→Approval→Audit",       dark: false },
    { n: "3", label: "Tool Handler",   sub: "Registry resolve\nDI-scoped execution",         dark: false },
    { n: "4", label: "LLM Agent",      sub: "Classify→Guard\nOrchestrate→Ground",           dark: true  },
    { n: "5", label: "Approval Gate",  sub: "Risk scored\nRoute to channel",                red:  true  },
    { n: "6", label: "Response",       sub: "OTel span closed\nAudit event emitted",        dark: false },
  ];

  const bW = 1.38, bH = 1.02, startX = 0.32, bY = 1.4;
  const totalGap = 10 - startX * 2 - steps.length * bW;
  const gapW = totalGap / (steps.length - 1);

  steps.forEach((st, i) => {
    const bX = startX + i * (bW + gapW);
    const fillColor = st.dark ? C.nearBlack : st.red ? C.red : "F0F0F0";
    const lineColor = st.dark ? C.nearBlack : st.red ? C.red : "DDDDDD";
    const numColor  = st.dark || st.red ? C.yellow : C.red;
    const lblColor  = st.dark || st.red ? C.white   : C.nearBlack;
    const subColor  = st.dark || st.red ? C.midGrey : C.darkGrey;

    s.addShape(pres.shapes.RECTANGLE, {
      x: bX, y: bY, w: bW, h: bH,
      fill: { color: fillColor }, line: { color: lineColor, width: 0.8 },
    });
    s.addText(st.n, {
      x: bX, y: bY + 0.05, w: bW, h: 0.3,
      fontFace: FONT, fontSize: 18, color: numColor,
      align: "center", valign: "middle", margin: 0,
    });
    s.addText(st.label, {
      x: bX, y: bY + 0.36, w: bW, h: 0.25,
      fontFace: FONT, fontSize: 10, color: lblColor,
      align: "center", valign: "middle", margin: 0,
    });
    s.addText(st.sub, {
      x: bX, y: bY + 0.62, w: bW, h: 0.38,
      fontFace: FONT, fontSize: 7.5, color: subColor,
      align: "center", valign: "top", margin: 0,
    });

    if (i < steps.length - 1) {
      s.addShape(pres.shapes.LINE, {
        x: bX + bW, y: bY + bH / 2, w: gapW, h: 0,
        line: { color: C.darkGrey, width: 1.2 },
      });
    }
  });

  s.addText("MediatR behaviors wrap the handler as decorators — each concern layered, independently testable, open/closed.", {
    x: 0.5, y: 2.58, w: 9, h: 0.28,
    fontFace: FONT, fontSize: 10, color: C.darkGrey,
    align: "center", valign: "middle", margin: 0,
  });

  // Two paths
  const paths = [
    { title: "Synchronous",  items: ["POST /tools/{ns}/{name}/{v}/invoke", "Blocking — awaits full pipeline", "200 OK or 202 Accepted on approval"] },
    { title: "Streaming SSE", items: ["POST /tools/{ns}/{name}/{v}/stream", "Server-Sent Events — handler yields chunks", "Text/event-stream; no buffering"] },
  ];

  paths.forEach((p, ci) => {
    const px = 0.5 + ci * 4.8;
    s.addText(p.title, {
      x: px, y: 3.02, w: 4.5, h: 0.28,
      fontFace: FONT, fontSize: 11, color: C.nearBlack,
      align: "left", valign: "middle", margin: 0,
    });
    p.items.forEach((it, ii) => {
      s.addShape(pres.shapes.RECTANGLE, {
        x: px, y: 3.38 + ii * 0.37, w: 0.06, h: 0.25,
        fill: { color: C.red }, line: { color: C.red },
      });
      s.addText(it, {
        x: px + 0.12, y: 3.38 + ii * 0.37, w: 4.4, h: 0.26,
        fontFace: FONT, fontSize: 10, color: C.darkGrey,
        align: "left", valign: "middle", margin: 0,
      });
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 07 — Pipeline Guards
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.nearBlack };
  sectionBadge(s, "GUARDS");

  s.addText("7 Pipeline Guards — Every Request Protected", {
    x: 0.5, y: 0.58, w: 9, h: 0.48,
    fontFace: FONT, fontSize: 25, color: C.white,
    align: "left", valign: "middle", margin: 0,
  });

  const guards = [
    { n: "1", name: "TenantAuthorization",  trigger: "Tenant not in namespace allowlist",     result: "401 Unauthorized",   resultColor: C.red     },
    { n: "2", name: "Validation",           trigger: "Input fails FluentValidation rules",     result: "400 Bad Request",    resultColor: "996600"  },
    { n: "3", name: "TokenBudget",          trigger: "Session token ceiling reached",          result: "429 Budget Exceeded",resultColor: C.red     },
    { n: "4", name: "DailyBudget",          trigger: "Tenant daily spend limit exceeded",      result: "429 Budget Exceeded",resultColor: C.red     },
    { n: "5", name: "LoopDetection",        trigger: "> MaxCalls per correlationId in TTL",    result: "429 Loop Detected",  resultColor: C.red     },
    { n: "6", name: "Approval",             trigger: "Risk ≥ Medium or explicit approval flag",result: "202 Pending",        resultColor: "336699"  },
    { n: "7", name: "Audit",                trigger: "Always — success and failure paths",     result: "Event emitted",      resultColor: "336633"  },
  ];

  guards.forEach((g, i) => {
    const y = 1.18 + i * 0.565;

    s.addText(g.n, {
      x: 0.42, y, w: 0.32, h: 0.5,
      fontFace: FONT, fontSize: 15, color: C.red,
      align: "center", valign: "middle", margin: 0,
    });
    s.addText(g.name, {
      x: 0.82, y, w: 2.6, h: 0.5,
      fontFace: FONT, fontSize: 11, color: C.white,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText("Trigger: " + g.trigger, {
      x: 3.55, y, w: 3.85, h: 0.5,
      fontFace: FONT, fontSize: 9.5, color: C.darkGrey,
      align: "left", valign: "middle", margin: 0,
    });

    s.addShape(pres.shapes.RECTANGLE, {
      x: 7.55, y: y + 0.1, w: 2.15, h: 0.3,
      fill: { color: g.resultColor, transparency: 20 },
      line: { color: g.resultColor, width: 0.8 },
    });
    s.addText(g.result, {
      x: 7.55, y: y + 0.1, w: 2.15, h: 0.3,
      fontFace: FONT, fontSize: 9, color: C.white,
      align: "center", valign: "middle", margin: 0,
    });

    if (i < guards.length - 1) {
      s.addShape(pres.shapes.LINE, {
        x: 0.42, y: y + 0.5, w: 9.3, h: 0,
        line: { color: "333333", width: 0.5 },
      });
    }
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 08 — Creating a Tool
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.white };
  sectionBadge(s, "TOOL CREATION");

  s.addText("Creating a Tool — 4 Steps", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 28, color: C.nearBlack,
    align: "left", valign: "middle", margin: 0,
  });

  const steps = [
    {
      n: "01", title: "Input / Output",
      code: "record WeatherInput(string City, string Unit = \"C\");\nrecord WeatherOutput(double Temp, string Desc);",
    },
    {
      n: "02", title: "Implement IToolHandler",
      code: "public class WeatherTool\n    : ToolHandlerBase<WeatherInput, WeatherOutput>\n{\n    public override async Task<Result<WeatherOutput>>\n        ExecuteAsync(ToolRequest<WeatherInput> req, CancellationToken ct)\n    { /* call weather API */ }\n}",
    },
    {
      n: "03", title: "Decorate with [Tool]",
      code: "[Tool(Namespace: \"weather\", Name: \"current\", Version: \"1.0\",\n      Description: \"Get current conditions for a city.\",\n      Risk: RiskLevel.Low)]",
    },
    {
      n: "04", title: "Register via Extension",
      code: "// One line in your DI setup:\nservices.AddTool<WeatherInput, WeatherOutput, WeatherTool>();\n// Auto-registers handler + metadata in IToolRegistry",
    },
  ];

  const rowH = 0.97;

  steps.forEach((st, i) => {
    const y = 1.25 + i * rowH;

    s.addText(st.n, {
      x: 0.5, y, w: 0.48, h: rowH,
      fontFace: FONT, fontSize: 19, color: C.red,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(st.title, {
      x: 1.02, y: y + 0.08, w: 2.0, h: 0.38,
      fontFace: FONT, fontSize: 11, color: C.nearBlack,
      align: "left", valign: "middle", margin: 0,
    });
    s.addShape(pres.shapes.RECTANGLE, {
      x: 3.2, y: y + 0.04, w: 6.55, h: rowH - 0.1,
      fill: { color: "F5F5F5" }, line: { color: "E0E0E0", width: 0.5 },
    });
    s.addText(st.code, {
      x: 3.35, y: y + 0.08, w: 6.25, h: rowH - 0.18,
      fontFace: FONT_MONO, fontSize: 8.5, color: C.nearBlack,
      align: "left", valign: "top", margin: 0,
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 09 — Tool Registry
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.nearBlack };
  sectionBadge(s, "REGISTRY");

  s.addText("Tool Registry — How Tools Are Discovered", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 26, color: C.white,
    align: "left", valign: "middle", margin: 0,
  });

  const flow = [
    { step: "Assembly Scan",   detail: "AddTool<>() registers handler type + ToolDescriptor in the DI container at startup." },
    { step: "IToolRegistry",   detail: "Singleton store keyed by {namespace}.{name}@{version}. Supports per-tenant overrides." },
    { step: "Resolve",         detail: "Resolve(ns, name, version, tenantId) returns ToolDescriptor or a failure Result." },
    { step: "GET /tools",      detail: "Serialisable ToolSummaryResponse projections — HandlerType excluded for safety." },
    { step: "GET /versions",   detail: "GET /tools/{ns}/{name}/versions — lists all registered versions for a tool." },
  ];

  flow.forEach((f, i) => {
    const y = 1.22 + i * 0.73;

    s.addShape(pres.shapes.OVAL, {
      x: 0.44, y: y + 0.15, w: 0.3, h: 0.3,
      fill: { color: C.red }, line: { color: C.red },
    });
    if (i < flow.length - 1) {
      s.addShape(pres.shapes.LINE, {
        x: 0.59, y: y + 0.45, w: 0, h: 0.44,
        line: { color: C.darkGrey, width: 1.0 },
      });
    }
    s.addText(f.step, {
      x: 0.88, y: y + 0.06, w: 3.9, h: 0.28,
      fontFace: FONT, fontSize: 11.5, color: C.white,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(f.detail, {
      x: 0.88, y: y + 0.36, w: 4.1, h: 0.28,
      fontFace: FONT, fontSize: 9.5, color: C.darkGrey,
      align: "left", valign: "top", margin: 0,
    });
  });

  // ToolDescriptor card
  s.addShape(pres.shapes.RECTANGLE, {
    x: 5.4, y: 1.18, w: 4.3, h: 3.95,
    fill: { color: C.cardDark }, line: { color: C.cardLine, width: 0.8 },
  });
  s.addText("ToolDescriptor", {
    x: 5.55, y: 1.25, w: 4.0, h: 0.3,
    fontFace: FONT, fontSize: 12, color: C.yellow,
    align: "left", valign: "middle", margin: 0,
  });

  const fields = [
    "FullName        →  weather.current",
    "Namespace       →  weather",
    "Name            →  current",
    "Version         →  1.0",
    "Description     →  string",
    "Type            →  Logic | Data | Gateway",
    "Risk            →  Low | Medium | High | Critical",
    "IsEnabled       →  bool",
    "TenantId        →  null = global tool",
    "HandlerType     →  System.Type  (DI key)",
    "InputSchema     →  JSON Schema",
    "OutputSchema    →  JSON Schema",
  ];

  fields.forEach((f, i) => {
    s.addText(f, {
      x: 5.55, y: 1.62 + i * 0.28, w: 4.0, h: 0.26,
      fontFace: FONT_MONO, fontSize: 8, color: C.midGrey,
      align: "left", valign: "middle", margin: 0,
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 10 — Approval Workflow
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.white };
  sectionBadge(s, "APPROVAL");

  s.addText("Approval Workflow — Risk-Tiered Human-in-the-Loop", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 23, color: C.nearBlack,
    align: "left", valign: "middle", margin: 0,
  });

  // Risk table
  const tiers = [
    { risk: "Low",      behaviour: "Auto-approve",     channel: "—",                   compliance: "No HitL required", color: "336633" },
    { risk: "Medium",   behaviour: "Suspend + notify", channel: "Dashboard / Webhook", compliance: "Logged",           color: "997700" },
    { risk: "High",     behaviour: "Suspend + notify", channel: "Dashboard / Email",   compliance: "Audit trail",      color: C.red    },
    { risk: "Critical", behaviour: "Suspend + OTP",    channel: "Email OTP (PBKDF2)",  compliance: "EU AI Act Art.14", color: "880000" },
  ];

  const cols = [
    { hdr: "Risk",       x: 0.4,  w: 1.25 },
    { hdr: "Behaviour",  x: 1.7,  w: 2.35 },
    { hdr: "Channel",    x: 4.1,  w: 2.85 },
    { hdr: "Compliance", x: 7.0,  w: 2.65 },
  ];

  // Header row
  cols.forEach(c => {
    s.addShape(pres.shapes.RECTANGLE, {
      x: c.x, y: 1.22, w: c.w, h: 0.3,
      fill: { color: C.nearBlack }, line: { color: C.nearBlack },
    });
    s.addText(c.hdr, {
      x: c.x, y: 1.22, w: c.w, h: 0.3,
      fontFace: FONT, fontSize: 10, color: C.white,
      align: "center", valign: "middle", margin: 0,
    });
  });

  tiers.forEach((t, i) => {
    const y = 1.55 + i * 0.42;
    const bg = i % 2 === 0 ? C.white : "F8F8F8";

    cols.forEach(c => {
      s.addShape(pres.shapes.RECTANGLE, {
        x: c.x, y, w: c.w, h: 0.38,
        fill: { color: bg }, line: { color: "EEEEEE", width: 0.5 },
      });
    });
    // Risk badge
    s.addShape(pres.shapes.RECTANGLE, {
      x: 0.4, y, w: 1.25, h: 0.38,
      fill: { color: t.color, transparency: 15 },
      line: { color: t.color, width: 0.8 },
    });
    s.addText(t.risk, {
      x: 0.4, y, w: 1.25, h: 0.38,
      fontFace: FONT, fontSize: 10, color: C.white,
      align: "center", valign: "middle", margin: 0,
    });
    s.addText(t.behaviour, {
      x: 1.7, y, w: 2.35, h: 0.38,
      fontFace: FONT, fontSize: 9.5, color: C.nearBlack,
      align: "center", valign: "middle", margin: 0,
    });
    s.addText(t.channel, {
      x: 4.1, y, w: 2.85, h: 0.38,
      fontFace: FONT, fontSize: 9.5, color: C.darkGrey,
      align: "center", valign: "middle", margin: 0,
    });
    s.addText(t.compliance, {
      x: 7.0, y, w: 2.65, h: 0.38,
      fontFace: FONT, fontSize: 9.5, color: C.darkGrey,
      align: "center", valign: "middle", margin: 0,
    });
  });

  // State machine
  s.addText("State Machine", {
    x: 0.4, y: 3.35, w: 3, h: 0.28,
    fontFace: FONT, fontSize: 12, color: C.nearBlack,
    align: "left", valign: "middle", margin: 0,
  });

  const states = [
    { lbl: "Pending",  color: C.darkGrey },
    { lbl: "Approved", color: "336633"   },
    { lbl: "Denied",   color: C.red      },
    { lbl: "Expired",  color: "888888"   },
  ];
  const stX = [0.4, 2.55, 4.7, 6.85];

  states.forEach((st, i) => {
    s.addShape(pres.shapes.RECTANGLE, {
      x: stX[i], y: 3.7, w: 1.85, h: 0.42,
      fill: { color: st.color, transparency: 15 },
      line: { color: st.color, width: 1.0 },
    });
    s.addText(st.lbl, {
      x: stX[i], y: 3.7, w: 1.85, h: 0.42,
      fontFace: FONT, fontSize: 11, color: C.white,
      align: "center", valign: "middle", margin: 0,
    });
    if (i < states.length - 1) {
      s.addShape(pres.shapes.LINE, {
        x: stX[i] + 1.85, y: 3.91, w: 0.7, h: 0,
        line: { color: C.darkGrey, width: 1 },
      });
    }
  });

  s.addText("Idempotency-Key prevents duplicate rows on retry.  OTP PBKDF2 + FixedTimeEquals prevents timing-oracle attacks.", {
    x: 0.4, y: 4.25, w: 9.3, h: 0.38,
    fontFace: FONT, fontSize: 9.5, color: C.darkGrey,
    align: "left", valign: "middle", margin: 0,
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 11 — LLM Agent Layer
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.nearBlack };
  sectionBadge(s, "LLM AGENT");

  s.addText("LLM Agent Layer — Contained, Governed AI", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 26, color: C.white,
    align: "left", valign: "middle", margin: 0,
  });

  const comps = [
    {
      name: "AgentScopeClassifier", color: C.yellow,
      points: [
        "Pre-flight LLM call before main orchestration loop starts",
        "Returns structured JSON: in_scope / out_of_scope / mixed",
        "Mixed: in-scope portion forwarded; rest noted in reply",
        "Fails open — classification errors pass through safely",
      ],
    },
    {
      name: "AgentScopeEnforcer", color: "44AAFF",
      points: [
        "Builds system prompt from all available tool descriptions",
        "Injects behavioural rules from externalised prompts.json",
        "No hardcoded prompt strings — all tuneable at deploy time",
        "IPromptStore injected via DI; FrozenDictionary — O(1) reads",
      ],
    },
    {
      name: "ToolGuard", color: "FF8844",
      points: [
        "Pre-LLM: blocked tools invisible to model (schema filter)",
        "Post-selection: defence-in-depth against prompt injection",
        "Pattern syntax: exact / wildcard (math.*) / global (*)",
        "DeniedTools overrides AllowedTools — deny always wins",
      ],
    },
    {
      name: "AgentOrchestrator", color: "88CC44",
      points: [
        "Runs agentic loop: classify → select → execute → ground",
        "Token budget checked before each LLM call in the loop",
        "MaxIterations hard stop (default 5) prevents runaway cost",
        "Grounding reminder injected per response for accuracy",
      ],
    },
  ];

  comps.forEach((c, i) => {
    const x = i % 2 === 0 ? 0.4 : 5.2;
    const y = 1.2 + Math.floor(i / 2) * 2.0;

    s.addShape(pres.shapes.RECTANGLE, {
      x, y, w: 4.55, h: 1.82,
      fill: { color: C.cardDark }, line: { color: C.cardLine, width: 0.8 },
    });
    s.addShape(pres.shapes.RECTANGLE, {
      x, y, w: 4.55, h: 0.06,
      fill: { color: c.color }, line: { color: c.color },
    });
    s.addText(c.name, {
      x: x + 0.14, y: y + 0.1, w: 4.2, h: 0.3,
      fontFace: FONT, fontSize: 12, color: c.color,
      align: "left", valign: "middle", margin: 0,
    });
    c.points.forEach((pt, pi) => {
      s.addText("— " + pt, {
        x: x + 0.14, y: y + 0.46 + pi * 0.33, w: 4.25, h: 0.3,
        fontFace: FONT, fontSize: 9, color: C.darkGrey,
        align: "left", valign: "middle", margin: 0,
      });
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 12 — LLM Provider Strategy
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.white };
  sectionBadge(s, "LLM PROVIDERS");

  s.addText("LLM Provider Strategy — One Interface, Three Providers", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 23, color: C.nearBlack,
    align: "left", valign: "middle", margin: 0,
  });

  const providers = [
    {
      name: "Anthropic Claude Sonnet 4.5",
      role: "Primary — production default",
      color: C.red,
      why: "Best cost-to-accuracy for tool routing in 2026 consulting workloads. ScopeClassifier always overrides temperature to 0.0 for deterministic classification.",
      pricing: "~$3 / $15 per 1M input/output tokens  |  30s timeout",
      config: '"DefaultProvider": "anthropic"\n"Model": "claude-sonnet-4-5"\n"Temperature": 1.0\nKey: ANTHROPIC_API_KEY env var',
    },
    {
      name: "OpenAI GPT-4o",
      role: "Fallback — automatic failover",
      color: C.darkGrey,
      why: "Strong tool-use accuracy. Activated automatically when Anthropic returns non-200 or times out. Caller sees no change.",
      pricing: "~$2.50 / $10 per 1M input/output tokens  |  30s timeout",
      config: '"FallbackChain": ["openai","ollama"]\n"Model": "gpt-4o"\n"Temperature": 1.0  (range 0–2)\nKey: OPENAI_API_KEY env var',
    },
    {
      name: "Ollama  (Local / Self-hosted)",
      role: "Development & data-residency deployments",
      color: "336699",
      why: "Zero API cost. Air-gapped deployments. Default in Development environment — no cloud spend during dev/test cycles.",
      pricing: "Free — compute cost only  |  120s timeout (cold-start model load)",
      config: '"DefaultProvider": "ollama"  (dev only)\n"Model": "llama3.1:8b"\n"BaseUrl": "http://localhost:11434"\nNo API key required',
    },
  ];

  providers.forEach((p, i) => {
    const y = 1.22 + i * 1.38;

    s.addShape(pres.shapes.RECTANGLE, {
      x: 0.4, y, w: 9.3, h: 1.28,
      fill: { color: C.rowLight }, line: { color: C.rowBorder, width: 0.8 },
      shadow: mkShadow(),
    });
    s.addShape(pres.shapes.RECTANGLE, {
      x: 0.4, y, w: 0.07, h: 1.28,
      fill: { color: p.color }, line: { color: p.color },
    });

    s.addText(p.name, {
      x: 0.58, y: y + 0.1, w: 4.5, h: 0.28,
      fontFace: FONT, fontSize: 12, color: C.nearBlack,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(p.role, {
      x: 0.58, y: y + 0.38, w: 4.5, h: 0.22,
      fontFace: FONT, fontSize: 9.5, color: p.color,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(p.why, {
      x: 0.58, y: y + 0.6, w: 4.5, h: 0.4,
      fontFace: FONT, fontSize: 9.5, color: C.darkGrey,
      align: "left", valign: "top", margin: 0,
    });
    s.addText(p.pricing, {
      x: 0.58, y: y + 1.02, w: 4.5, h: 0.22,
      fontFace: FONT, fontSize: 8.5, color: C.midGrey,
      align: "left", valign: "middle", margin: 0,
    });

    s.addShape(pres.shapes.RECTANGLE, {
      x: 5.2, y: y + 0.1, w: 4.3, h: 1.1,
      fill: { color: "F3F3F3" }, line: { color: "E0E0E0", width: 0.5 },
    });
    s.addText(p.config, {
      x: 5.32, y: y + 0.14, w: 4.05, h: 1.0,
      fontFace: FONT_MONO, fontSize: 8, color: C.nearBlack,
      align: "left", valign: "top", margin: 0,
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 13 — CLI Usage
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.nearBlack };
  sectionBadge(s, "CLI");

  s.addText("CLI — Run, Test & Manage From the Terminal", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 25, color: C.white,
    align: "left", valign: "middle", margin: 0,
  });

  const cmds = [
    { cmd: "dotnet run --project src/Hosts/ToolEngine.Api",          desc: "Start API — Development, SQLite, Ollama default" },
    { cmd: "GET  /dev/token?tenant=onebcg-dev",                      desc: "8-hour dev JWT — Development environment only" },
    { cmd: "GET  /tools",                                            desc: "List all registered tools (name, version, schema, risk)" },
    { cmd: "POST /tools/{ns}/{name}/{v}/invoke",                     desc: "Invoke tool synchronously — 200 OK or 202 Accepted" },
    { cmd: "POST /tools/{ns}/{name}/{v}/stream",                     desc: "Stream tool response via Server-Sent Events (SSE)" },
    { cmd: "POST /agent/chat",                                       desc: "Multi-turn LLM session — scope-classified, tool-guarded" },
    { cmd: "GET  /approvals/pending",                                desc: "List pending approvals for the authenticated tenant" },
    { cmd: "POST /approvals/{token}/decide?action=approve",          desc: "Magic-link approve/deny — token is the shared secret" },
    { cmd: "GET  /invocations/{id}/status",                          desc: "Poll approval status for a suspended invocation" },
  ];

  cmds.forEach((c, i) => {
    const y = 1.22 + i * 0.465;
    s.addShape(pres.shapes.RECTANGLE, {
      x: 0.4, y, w: 4.65, h: 0.39,
      fill: { color: C.codeGrey }, line: { color: "333333", width: 0.5 },
    });
    s.addText(c.cmd, {
      x: 0.52, y, w: 4.4, h: 0.39,
      fontFace: FONT_MONO, fontSize: 8.5, color: C.yellow,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(c.desc, {
      x: 5.18, y, w: 4.65, h: 0.39,
      fontFace: FONT, fontSize: 9.5, color: C.darkGrey,
      align: "left", valign: "middle", margin: 0,
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 14 — Scalability & Resilience
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.white };
  sectionBadge(s, "SCALABILITY");

  s.addText("Scalability & Resilience", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 28, color: C.nearBlack,
    align: "left", valign: "middle", margin: 0,
  });

  const items = [
    { title: "Horizontal scale",       body: "SQLite → PostgreSQL / SQL Server via single config key. Redis replaces in-memory cache. Zero code change." },
    { title: "Outbox pattern",         body: "Approval events persist across pod restarts. Dispatcher retries up to 5× with backoff. No lost notifications." },
    { title: "Idempotency keys",       body: "Idempotency-Key header prevents duplicate PendingApproval rows on retry — safe for at-least-once delivery." },
    { title: "OTP rate limiting",      body: "10 attempts per IP / 10 min. Per-token lockout after 5 failures. OWASP MFA Cheat Sheet compliant." },
    { title: "Token circuit-breaker",  body: "MaxTokensPerSession checked before every LLM call. MaxIterations hard-stops the loop. No runaway spend." },
    { title: "LLM fallback chain",     body: "Anthropic → OpenAI → Ollama. Provider failures are transparent. Timeouts: 30s / 30s / 120s, all tunable." },
    { title: "FrozenDictionary cache", body: "Prompts loaded once at startup — lock-free reads, O(1) lookup, zero allocations per request." },
    { title: "Tenant isolation",       body: "Tool resolution and budget counters keyed by tenantId. No shared mutable state across tenants." },
  ];

  items.forEach((it, i) => {
    const col = i % 2;
    const row = Math.floor(i / 2);
    const x = col === 0 ? 0.4 : 5.25;
    const y = 1.22 + row * 0.98;

    s.addShape(pres.shapes.RECTANGLE, {
      x, y, w: 4.55, h: 0.86,
      fill: { color: C.rowLight }, line: { color: C.rowBorder, width: 0.8 },
      shadow: mkShadow(),
    });
    s.addShape(pres.shapes.RECTANGLE, {
      x, y, w: 0.07, h: 0.86,
      fill: { color: C.red }, line: { color: C.red },
    });
    s.addText(it.title, {
      x: x + 0.18, y: y + 0.06, w: 4.2, h: 0.27,
      fontFace: FONT, fontSize: 11, color: C.nearBlack,
      align: "left", valign: "middle", margin: 0,
    });
    s.addText(it.body, {
      x: x + 0.18, y: y + 0.36, w: 4.2, h: 0.44,
      fontFace: FONT, fontSize: 9.5, color: C.darkGrey,
      align: "left", valign: "top", margin: 0,
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 15 — Compliance Coverage
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.nearBlack };
  sectionBadge(s, "COMPLIANCE");

  s.addText("Compliance Coverage — Built In, Not Bolted On", {
    x: 0.5, y: 0.58, w: 9, h: 0.5,
    fontFace: FONT, fontSize: 24, color: C.white,
    align: "left", valign: "middle", margin: 0,
  });

  const frameworks = [
    {
      name: "EU AI Act — Article 14", color: C.red,
      controls: [
        "Human oversight enforced for high-risk AI decisions via approval gate",
        "Risk tier (Low→Critical) attributed to every tool at registration time",
        "Full audit trail: approver identity, decision timestamp, denial reason",
      ],
    },
    {
      name: "NIST AI RMF", color: C.yellow,
      controls: [
        "GOVERN: scope classification prevents out-of-domain AI responses",
        "MEASURE: per-tenant token budget meters and daily spend tracking",
        "MANAGE: tool guard blocks sensitive namespaces by configuration",
      ],
    },
    {
      name: "ISO 42001", color: "44AAFF",
      controls: [
        "X-Governance-Metadata header carries AI management metadata per call",
        "Tool lifecycle documented from registration through deprecation",
        "Prompts version-controlled in prompts.json — no silent drift possible",
      ],
    },
    {
      name: "SOC 2  CC6 / CC7", color: "88CC44",
      controls: [
        "CC6: JWT HMAC-SHA256 minimum 32-byte key — enforced at startup",
        "CC6: tenant claim isolation — cross-tenant invocation is impossible",
        "CC7: audit event on every invocation; Serilog PII masking on all logs",
      ],
    },
  ];

  frameworks.forEach((fw, i) => {
    const x = i % 2 === 0 ? 0.4 : 5.25;
    const y = 1.22 + Math.floor(i / 2) * 1.92;

    s.addShape(pres.shapes.RECTANGLE, {
      x, y, w: 4.55, h: 1.8,
      fill: { color: C.cardDark }, line: { color: C.cardLine, width: 0.8 },
    });
    s.addShape(pres.shapes.RECTANGLE, {
      x, y, w: 4.55, h: 0.06,
      fill: { color: fw.color }, line: { color: fw.color },
    });
    s.addText(fw.name, {
      x: x + 0.14, y: y + 0.1, w: 4.2, h: 0.3,
      fontFace: FONT, fontSize: 12, color: fw.color,
      align: "left", valign: "middle", margin: 0,
    });
    fw.controls.forEach((ctrl, ci) => {
      s.addText("· " + ctrl, {
        x: x + 0.14, y: y + 0.48 + ci * 0.41, w: 4.25, h: 0.38,
        fontFace: FONT, fontSize: 9, color: C.darkGrey,
        align: "left", valign: "top", margin: 0,
      });
    });
  });

  addFooter(s);
}

// ══════════════════════════════════════════════════════════════════════════════
// SLIDE 16 — Close / Next Steps
// ══════════════════════════════════════════════════════════════════════════════
{
  const s = pres.addSlide();
  s.background = { color: C.nearBlack };

  s.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 0, w: 0.18, h: 5.625,
    fill: { color: C.red }, line: { color: C.red },
  });

  s.addText("ONE BCG", {
    x: 0.38, y: 0.3, w: 3, h: 0.36,
    fontFace: FONT, fontSize: 11, color: C.red,
    align: "left", valign: "middle", margin: 0,
  });

  s.addText("ToolEngine v2026 is production-ready.", {
    x: 0.38, y: 1.05, w: 9.3, h: 0.65,
    fontFace: FONT, fontSize: 34, color: C.white,
    align: "left", valign: "middle", margin: 0,
  });

  s.addText("What comes next", {
    x: 0.38, y: 1.85, w: 5, h: 0.32,
    fontFace: FONT, fontSize: 15, color: C.yellow,
    align: "left", valign: "middle", margin: 0,
  });

  const nexts = [
    "Deploy staging — swap SQLite → PostgreSQL, enable Redis cache, connect OTLP collector",
    "Onboard first tenant — configure namespace allowlist, daily budget, approval channel",
    "Implement first production tool — 4-step pattern, register, JSON schema, unit test",
    "Connect LLM agent to client workflow — POST /agent/chat, review ScopeClassifier traces",
    "Enable OpenTelemetry export — Jaeger/Grafana, set SLO alerts on budget and error rate",
  ];

  nexts.forEach((n, i) => {
    s.addShape(pres.shapes.OVAL, {
      x: 0.38, y: 2.28 + i * 0.52, w: 0.2, h: 0.2,
      fill: { color: C.red }, line: { color: C.red },
    });
    s.addText(n, {
      x: 0.7, y: 2.26 + i * 0.52, w: 9.0, h: 0.3,
      fontFace: FONT, fontSize: 11, color: C.darkGrey,
      align: "left", valign: "middle", margin: 0,
    });
  });

  s.addShape(pres.shapes.LINE, {
    x: 0.38, y: 5.08, w: 9.3, h: 0,
    line: { color: C.darkGrey, width: 0.8 },
  });

  s.addText("ONE BCG  |  ToolEngine v2026  |  Confidential — Internal Use Only", {
    x: 0.38, y: 5.15, w: 9.3, h: 0.28,
    fontFace: FONT, fontSize: 9, color: C.darkGrey,
    align: "left", valign: "middle", margin: 0,
  });
}

// ══════════════════════════════════════════════════════════════════════════════
// Write file
// ══════════════════════════════════════════════════════════════════════════════
const outPath = "D:\\WorkingFolder\\ONEBCG v2026\\onebcg-toolengine\\ToolEngine-v2026-Stakeholder.pptx";

pres.writeFile({ fileName: outPath })
  .then(() => console.log("Done:", outPath))
  .catch(err => { console.error("Error:", err); process.exit(1); });
