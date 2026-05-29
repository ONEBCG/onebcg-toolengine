# Payment Processing System — Technical & Functional Specification

**Document Owner:** ONE BCG  
**Version:** 1.0  
**Status:** Draft  
**Classification:** Confidential – Internal Use Only  
**Date:** 2026-05-19  

---

## Table of Contents

1. [Purpose & Scope](#1-purpose--scope)
2. [System Overview](#2-system-overview)
3. [Actors & Roles](#3-actors--roles)
4. [Payment Lifecycle — Full Workflow](#4-payment-lifecycle--full-workflow)
   - Stage 0: Payment Initiation
   - Stage 1: Payee Verification (Database Lookup)
   - Stage 2: Contractual Obligation Check (PPM Gate)
   - Stage 3: Tax Withholding Calculation (Intelligent Engine)
   - Stage 4: KYC / Sanctions Screening
   - Stage 5: Approval Gate
   - Stage 6: Payment Execution
   - Stage 7: Post-Payment Reconciliation
5. [Barriers & Safeguards Summary](#5-barriers--safeguards-summary)
6. [Outcome Matrix](#6-outcome-matrix)
7. [Intelligent Tax Engine — Design Detail](#7-intelligent-tax-engine--design-detail)
8. [KYC / Sanctions Engine — Design Detail](#8-kyc--sanctions-engine--design-detail)
9. [Data Requirements](#9-data-requirements)
10. [Integration Touchpoints](#10-integration-touchpoints)
11. [Non-Functional Requirements](#11-non-functional-requirements)
12. [Glossary](#12-glossary)

---

## 1. Purpose & Scope

This document specifies the functional, technical, and compliance requirements for a **B2B Payment Processing System**. The system orchestrates a multi-stage pipeline that evaluates every outbound payment instruction against contractual obligations, tax withholding rules, and KYC/sanctions requirements before releasing funds to the banking layer.

The system is designed to be presented as a **standalone, demonstrable capability** — independent of any existing third-party platform — to validate architectural decisions and showcase end-to-end payment intelligence to prospective clients.

**In scope:**
- Outbound B2B payment instructions (single and batch)
- Multi-jurisdiction withholding tax determination and calculation
- Contractual permissibility checks against agreement documents (PPM)
- KYC / sanctions screening via World Check or equivalent API
- Human-in-the-loop approval before bank release
- Full audit trail of all checks and decisions

**Out of scope:**
- Consumer (B2C) payment flows
- Card acquiring / POS
- Inbound payment reconciliation (addressed in Stage 7 at high level only)

---

## 2. System Overview

The system processes a single payment instruction through **seven sequential stages**, each acting as an independent decision node. A payment must pass all automated stages before reaching the human approval gate. Any failure at any stage halts the payment and triggers a defined exception path.

```
Payment Instruction
        │
        ▼
┌───────────────────┐
│  Stage 0          │  Initiation — Capture & Validate Input
└────────┬──────────┘
         │ PASS
         ▼
┌───────────────────┐
│  Stage 1          │  Payee DB Lookup — Is payee known?
└────────┬──────────┘
         │ FOUND
         ▼
┌───────────────────┐
│  Stage 2          │  PPM / Contract Check — Is this payment permitted?
└────────┬──────────┘
         │ PERMITTED
         ▼
┌───────────────────┐
│  Stage 3          │  Tax Withholding Engine — What is the WHT amount?
└────────┬──────────┘
         │ CALCULATED
         ▼
┌───────────────────┐
│  Stage 4          │  KYC / Sanctions Screen — Is payee clear?
└────────┬──────────┘
         │ CLEAR
         ▼
┌───────────────────┐
│  Stage 5          │  Approval Gate — Human or automated sign-off
└────────┬──────────┘
         │ APPROVED
         ▼
┌───────────────────┐
│  Stage 6          │  Payment Execution — Push to bank/rail
└────────┬──────────┘
         │ SETTLED
         ▼
┌───────────────────┐
│  Stage 7          │  Reconciliation & Audit Log
└───────────────────┘
```

Any stage that returns FAIL, BLOCKED, or EXCEPTION routes the payment to the **Exception Handling Queue** (EHQ) and halts further processing.

---

## 3. Actors & Roles

| Actor | Description |
|---|---|
| **Payment Initiator** | Internal user or system submitting a payment instruction |
| **Payee** | The business entity receiving the payment |
| **Compliance Officer** | Reviews flagged KYC / sanctions alerts |
| **Finance Approver** | Provides final sign-off at the approval gate |
| **System Orchestrator** | The pipeline engine coordinating all stages |
| **World Check / KYC API** | Third-party sanctions and PEP screening provider |
| **Tax Engine** | LLM-backed intelligent tool for WHT determination |
| **Bank / Payment Rail** | Destination system receiving cleared payment instructions |

---

## 4. Payment Lifecycle — Full Workflow

---

### Stage 0 — Payment Initiation

**Purpose:** Capture and validate the raw payment instruction before it enters the pipeline.

**Inputs:**
- Payer entity (name, jurisdiction, entity ID)
- Payee entity (name, jurisdiction, entity ID or reference)
- Gross payment amount and currency
- Payment purpose / service type (e.g., consulting, software license, royalty)
- Reference to the governing agreement (PPM ID)
- Initiator identity and timestamp

**Process:**
1. Accept payment instruction from API, form submission, or batch file upload
2. Validate all mandatory fields are present and properly formatted
3. Validate currency against supported currency list
4. Validate payment amount is a positive, non-zero value
5. Assign a unique **Payment Reference ID (PRID)** and timestamp
6. Write initial record to the Payment Ledger with status `INITIATED`

**Barrier — Input Validation:**
- Missing mandatory fields → reject with field-level error; do not enter pipeline
- Invalid or unsupported currency → reject with error code `ERR_CURRENCY_UNSUPPORTED`
- Amount exceeding system-defined single-transaction limit → hold for manual review

**Outcome:**
- `PASS` → Payment record created; pipeline proceeds to Stage 1
- `FAIL` → Payment rejected at source; initiator notified with specific error; no record persisted

---

### Stage 1 — Payee Verification (Database Lookup)

**Purpose:** Confirm the payee exists as a registered, active entity in the internal system before any further processing.

**Inputs:**
- Payee entity ID or reference from Stage 0

**Process:**
1. Execute API call to internal entity database
2. Look up payee by entity ID, registered name, or tax identifier
3. Retrieve payee record including: registration status, jurisdiction, entity type, account details, onboarding date
4. Check payee status flag: `ACTIVE` / `INACTIVE` / `SUSPENDED` / `PENDING_REVIEW`

**Barrier — Payee Status Check:**
- Payee not found in database → route to EHQ with flag `UNKNOWN_PAYEE`; initiate new payee onboarding workflow if required
- Payee status `INACTIVE` or `SUSPENDED` → block payment; alert compliance
- Payee status `PENDING_REVIEW` → hold payment; await review completion
- Payee account details incomplete (missing bank account, IBAN, SWIFT) → block; request data completion

**Safeguards:**
- All lookup calls are read-only; no write operations at this stage
- API call timeout: 5 seconds; retry up to 3 times before routing to EHQ with `ERR_DB_TIMEOUT`
- All lookup events logged with timestamp, operator ID, and result

**Outcome:**
- `FOUND & ACTIVE` → Payee record attached to PRID; proceed to Stage 2
- `NOT FOUND` → Payment blocked; onboarding workflow triggered
- `INACTIVE / SUSPENDED` → Payment blocked; compliance alerted
- `PENDING_REVIEW` → Payment held; status checked on a scheduled basis

---

### Stage 2 — Contractual Obligation Check (PPM Gate)

**Purpose:** Verify that the proposed payment is explicitly permitted under the governing agreement (Project/Payment Management agreement — PPM) between payer and payee.

**Inputs:**
- PPM document ID (from Stage 0)
- Payee entity record (from Stage 1)
- Payment purpose / service type
- Gross payment amount
- Payment date

**Process:**
1. Retrieve the governing PPM document from the contract store
2. Extract key contract parameters:
   - Approved payee list
   - Permitted service categories
   - Payment frequency limits (e.g., monthly cap)
   - Approved currency and amount thresholds
   - Contract effective dates and expiry
   - Payment schedule (milestone-based, recurring, ad hoc)
3. Validate the payment instruction against each extracted parameter
4. Cross-check cumulative payments made under this PPM to enforce aggregate caps

**Barrier — Contract Permissibility:**
- Payee not listed in this PPM → block; flag `CONTRACT_PAYEE_MISMATCH`
- Service type not approved under this PPM → block; flag `CONTRACT_SERVICE_NOT_APPROVED`
- Payment amount exceeds per-transaction limit in PPM → block; flag `CONTRACT_AMOUNT_EXCEEDED`
- PPM expired or not yet effective → block; flag `CONTRACT_INACTIVE`
- Payment would breach cumulative cap → block; flag `CONTRACT_CAP_BREACH`

**Safeguards:**
- Contract documents stored in immutable, versioned document store
- Every check produces a structured decision record referencing the specific PPM clause
- Human override available only for Finance Approver with mandatory justification and audit trail

**Outcome:**
- `PERMITTED` → Contract check passed; attach contract decision record to PRID; proceed to Stage 3
- `BLOCKED` → Payment halted; initiator and Finance Approver notified with specific clause reference
- `OVERRIDE REQUESTED` → Escalated to Finance Approver for manual contract review

---

### Stage 3 — Tax Withholding Calculation (Intelligent Engine)

**Purpose:** Determine the correct withholding tax (WHT) rate applicable to this payment and calculate the net disbursable amount. This is the most complex stage and requires an **LLM-backed intelligent tool**, not static rule logic.

**Inputs:**
- Payer jurisdiction (country, tax residency)
- Payee jurisdiction (country, tax treaty status)
- Payment purpose / service type (critical for WHT rate differentiation)
- Gross payment amount
- Currency
- Applicable tax year / period

**Why an Intelligent Tool is Required:**
Withholding tax obligations differ not only by country pair but also by the **nature of the service**. The same payee receiving two different types of payments (e.g., a software license fee vs. a consulting fee) may attract entirely different WHT rates under the same tax treaty. Static if/then rule tables cannot capture this nuance reliably across multiple jurisdictions. The engine must:
- Read and interpret tax treaty text and domestic tax law (unstructured, vector-searchable)
- Apply the correct rate from structured rate tables
- Produce a reasoned, auditable output

**Process:**

```
Payment Context
(payer, payee, amount, service type)
        │
        ▼
┌────────────────────────────────────┐
│  VECTOR RETRIEVAL LAYER            │
│  - Query vector store of tax       │
│    treaties, domestic WHT rules,   │
│    OECD model convention articles  │
│  - Identify applicable treaty,     │
│    relevant article, service       │
│    classification                  │
└────────────┬───────────────────────┘
             │ Rule Reference Set
             ▼
┌────────────────────────────────────┐
│  STRUCTURED CALCULATION LAYER      │
│  - Look up WHT rate from rate      │
│    table (treaty / domestic)       │
│  - Apply reduced rate if treaty    │
│    benefit confirmed               │
│  - Calculate WHT amount            │
│  - Calculate net payable amount    │
└────────────┬───────────────────────┘
             │ WHT Decision Record
             ▼
     Tax Output + Audit Trail
```

**Output per payment:**
- Applicable tax treaty (if any) and article reference
- Service classification (e.g., royalty, FTS, business income)
- WHT rate (%)
- WHT amount (in payment currency)
- Net payable amount (Gross − WHT)
- Justification narrative (plain text, auditable)
- Confidence level flag: `HIGH` / `MEDIUM` / `REVIEW_REQUIRED`

**Barrier — Tax Determination:**
- Service type ambiguous or unclassifiable → flag `WHT_CLASSIFICATION_AMBIGUOUS`; route to tax team for manual determination; hold payment
- No applicable tax treaty found and domestic rate applies → proceed with domestic WHT rate, flagged for Finance review
- Confidence level `REVIEW_REQUIRED` → automatically escalate to tax reviewer before proceeding; do not block but do hold
- Payee has not provided treaty benefit documentation (e.g., Form W-8BEN-E, Tax Residency Certificate) → apply higher domestic WHT rate; flag `TREATY_DOCS_MISSING`

**Safeguards:**
- Engine never makes a unilateral determination for ambiguous service types
- All outputs include full reasoning chain for audit purposes
- Rate tables maintained and version-controlled by the tax team; engine reads but cannot modify
- Engine result is advisory; Finance Approver can override with mandatory documentation

**Outcome:**
- `CALCULATED — HIGH CONFIDENCE` → WHT and net amount confirmed; proceed to Stage 4
- `CALCULATED — MEDIUM CONFIDENCE` → Proceed but flag for Finance Approver attention at Stage 5
- `REVIEW_REQUIRED` → Hold payment; route to tax team; resume pipeline after manual determination
- `BLOCKED` → Unresolvable classification conflict; payment held pending tax team guidance

---

### Stage 4 — KYC / Sanctions Screening

**Purpose:** Screen the payee against global sanctions lists, Politically Exposed Persons (PEP) databases, and adverse media to ensure the payment does not violate financial crime regulations.

**Inputs:**
- Payee legal name
- Payee jurisdiction
- Payee entity type (individual, corporate, government)
- Payee tax identifier / registration number
- Payment amount and purpose (for risk scoring context)

**Process:**
1. Submit payee details to World Check API (or equivalent: Refinitiv, Dow Jones, ComplyAdvantage)
2. Receive match results across:
   - OFAC (US sanctions)
   - UN Security Council Consolidated List
   - EU Consolidated Sanctions List
   - UK HM Treasury list
   - Domestic sanctions lists (jurisdiction-specific)
   - PEP databases (Tier 1, 2, 3)
   - Adverse media flags
3. Apply fuzzy matching logic to handle name variations; score each match by confidence
4. Classify results: `CLEAR`, `POTENTIAL_MATCH`, `CONFIRMED_MATCH`

**Barrier — Sanctions & KYC:**

| Result | Action |
|---|---|
| `CLEAR` | Proceed to Stage 5 |
| `POTENTIAL_MATCH` | Hold payment; route to Compliance Officer for manual review within 24 hours |
| `CONFIRMED_MATCH` | Block payment immediately; freeze any pending instructions to this payee; file Suspicious Activity Report (SAR) if required; alert Legal and Compliance |

**Safeguards:**
- World Check / screening API called fresh for every payment — no caching of prior results beyond 24 hours
- All screening calls logged with: query payload (pseudonymised), timestamp, provider response, match score, decision
- Compliance Officer decision (clear / escalate / block) logged with officer ID, timestamp, and rationale
- Blocked payments cannot be unblocked by any single user — requires dual authorisation from Compliance + Legal
- System applies a payment-level risk score based on: payment amount, payee jurisdiction risk tier (FATF ratings), service type, and KYC result

**Outcome:**
- `CLEAR` → Proceed to Stage 5 with KYC clear certificate attached to PRID
- `POTENTIAL_MATCH — UNDER REVIEW` → Payment held; 24-hour compliance review window; pipeline paused
- `CONFIRMED_MATCH — BLOCKED` → Payment permanently blocked; legal and compliance notified; regulatory reporting triggered

---

### Stage 5 — Approval Gate

**Purpose:** Provide a consolidated payment dossier to an authorised approver who reviews all stage outcomes before releasing the payment to the banking layer.

**Inputs (auto-compiled into Payment Dossier):**
- Payment instruction summary (Stage 0)
- Payee verification result (Stage 1)
- Contract permissibility decision + clause reference (Stage 2)
- Tax withholding calculation, WHT amount, net payable amount (Stage 3)
- KYC / sanctions screening result (Stage 4)
- Any flags or confidence level warnings from preceding stages

**Approval Tiers:**

| Net Payable Amount | Approver Required |
|---|---|
| < $10,000 | Automated approval if all stages are GREEN |
| $10,000 – $100,000 | Single Finance Approver |
| $100,000 – $500,000 | Finance Approver + CFO |
| > $500,000 | Finance Approver + CFO + Board Designee |

**Process:**
1. System generates Payment Dossier (PDF + structured data record)
2. Dossier routed to required approver(s) via notification (email / in-system alert)
3. Approver reviews dossier in approval UI; can:
   - **Approve** → proceed to Stage 6
   - **Reject** → payment blocked; initiator notified with reason
   - **Query** → return to relevant stage (e.g., request tax team clarification); pipeline paused
   - **Override** (for flags only, not for confirmed blocks) → mandatory written justification required; dual-approval for overrides above $50,000

**Approval SLA:**
- Approver must act within 48 business hours
- Reminder notification sent at 24 hours
- Escalation to next level if no action within 48 hours

**Barrier — Approval Gate:**
- Dossier incomplete (any stage missing outcome) → cannot be submitted for approval; system error raised
- Confirmed KYC block present in dossier → approval UI will not allow approval action; only rejection available
- Approval SLA breached → automatic escalation; payment placed on hold

**Outcome:**
- `APPROVED` → Payment released to Stage 6
- `REJECTED` → Payment terminated; full decision record preserved in audit log
- `QUERIED` → Pipeline paused; stage re-processed; dossier recompiled

---

### Stage 6 — Payment Execution

**Purpose:** Transmit the approved, cleared payment instruction to the banking layer or payment rail for final settlement.

**Inputs:**
- Approved Payment Dossier from Stage 5
- Net payable amount (post-WHT)
- Payee bank details (account number, IBAN, SWIFT/BIC, routing code)
- Payment rail selection (SWIFT, SEPA, ACH, NEFT, RTGS, etc.)

**Process:**
1. Construct payment message in the format required by the selected rail (e.g., MT103 for SWIFT, ISO 20022 for SEPA)
2. Submit to bank API / payment gateway
3. Receive acknowledgement reference (bank transaction ID)
4. Update Payment Ledger: status → `SUBMITTED_TO_BANK`
5. Poll for settlement confirmation (real-time or scheduled based on rail)
6. On settlement confirmation: update status → `SETTLED`
7. Separately transmit WHT amount to the relevant tax authority account (per jurisdiction requirement)

**Barrier — Execution:**
- Bank API rejection (invalid account, format error) → route to EHQ; notify Finance team
- Payment rail unavailable → retry with exponential backoff (max 3 attempts over 4 hours); then hold for manual submission
- Settlement timeout → flag `SETTLEMENT_DELAYED`; monitor and escalate at 24 / 48 / 72 hour intervals

**Safeguards:**
- Payment instruction is immutable once submitted to bank — no modifications permitted post-submission
- Any amendment requires a new payment instruction through the full pipeline from Stage 0
- WHT remittance to tax authority is a separate, auto-triggered instruction with its own PRID

**Outcome:**
- `SETTLED` → Payment complete; proceed to Stage 7
- `FAILED` → Payment returned by bank; route to EHQ; Finance and initiator notified
- `PENDING` → Settlement in progress; monitoring active

---

### Stage 7 — Post-Payment Reconciliation & Audit

**Purpose:** Close the payment loop, update all records, and ensure a complete, immutable audit trail exists for the payment.

**Process:**
1. Match bank settlement confirmation against Payment Ledger record
2. Update payee account statement
3. Update PPM cumulative payment tracker (for contract cap enforcement in Stage 2)
4. Generate payment completion notice to initiator and payee
5. Archive full Payment Dossier including all stage decision records to immutable audit store
6. Flag any reconciliation discrepancy (e.g., settlement amount differs from instructed amount) for Finance review

**Audit Record — Mandatory Fields per Payment:**

| Field | Description |
|---|---|
| PRID | Unique payment reference |
| Timestamp chain | Stage entry/exit timestamps for all 7 stages |
| Initiator ID | Identity of the person/system that submitted |
| All stage outcomes | Pass / fail / override for each stage |
| WHT rate and amount | From tax engine |
| KYC screening result | Provider response and match classification |
| Approver IDs | All approvers with timestamp and decision |
| Bank transaction ID | Settlement reference |
| Final status | SETTLED / FAILED / BLOCKED |

**Retention:** Audit records retained for a minimum of 7 years (aligned with FATF, OECD, and local statutory requirements).

---

## 5. Barriers & Safeguards Summary

| Stage | Barrier Type | Safeguard Mechanism | Block or Hold? |
|---|---|---|---|
| 0 — Initiation | Input validation | Field-level schema enforcement | Block (reject at source) |
| 1 — Payee Lookup | Entity status check | Read-only DB API, timeout/retry | Block or Hold |
| 2 — PPM Check | Contract permissibility | Immutable versioned contract store, clause-level decision record | Block |
| 3 — Tax Engine | WHT classification accuracy | Vector + tabular dual-layer; confidence flag; no autonomous classification of ambiguous types | Hold (if ambiguous) |
| 4 — KYC / Sanctions | Sanctions list match | Real-time World Check API; no cached results > 24h; dual-auth to unblock | Block (confirmed match) |
| 5 — Approval Gate | Human oversight | Tiered approval by amount; SLA enforcement; override requires justification | Block (if dossier incomplete or KYC confirmed) |
| 6 — Execution | Bank API / rail failure | Retry logic; immutable instruction post-submission | Hold (retry) or Block (repeated failure) |
| 7 — Reconciliation | Settlement discrepancy | Automated matching; discrepancy flagged to Finance | Hold (discrepancy) |

---

## 6. Outcome Matrix

| Scenario | Final Outcome | Stage Where Resolved |
|---|---|---|
| All checks pass, below auto-approval threshold | `SETTLED` | Stage 6 |
| All checks pass, requires human approval | `SETTLED` (post approval) | Stage 5 → 6 |
| Payee not in database | `BLOCKED — UNKNOWN PAYEE` | Stage 1 |
| Payment not permitted by contract | `BLOCKED — CONTRACT` | Stage 2 |
| WHT classification ambiguous | `HELD — TAX REVIEW` | Stage 3 |
| Treaty documents missing | `SETTLED — HIGHER WHT` | Stage 3 → 6 |
| Payee is potential sanctions match | `HELD — COMPLIANCE REVIEW` | Stage 4 |
| Payee is confirmed sanctioned entity | `BLOCKED — SANCTIONS` | Stage 4 |
| Approver rejects | `BLOCKED — REJECTED` | Stage 5 |
| Bank API rejects instruction | `FAILED — EXECUTION` | Stage 6 |
| Settlement amount mismatch | `HELD — RECONCILIATION` | Stage 7 |

---

## 7. Intelligent Tax Engine — Design Detail

### Architecture

The tax engine is a two-layer hybrid system. It must not be implemented as a static rule engine.

**Layer 1 — Vector Retrieval (Rule Discovery)**
- Source corpus: tax treaties (bilateral), OECD Model Tax Convention, domestic WHT statutes, CBDT circulars, IRS publications, EU directive text
- All documents chunked, embedded, and indexed in a vector store
- At runtime: payment context (payer jurisdiction, payee jurisdiction, service type) is used to construct a retrieval query
- Top-k relevant chunks returned; relevant articles identified

**Layer 2 — Structured Calculation (Rate Application)**
- Rate tables maintained in a structured database (not in the LLM)
- Fields: payer country, payee country, service category, treaty article, standard rate %, reduced treaty rate %, conditions for reduced rate
- Engine applies retrieved rule to rate table to fetch exact rate
- Calculation: `WHT Amount = Gross Amount × Applicable WHT Rate`; `Net Payable = Gross Amount − WHT Amount`

### Service Type Classification (Critical)

The WHT rate is highly sensitive to service classification. Examples:

| Service Type | Classification | WHT Treatment (illustrative) |
|---|---|---|
| Software license fee | Royalty | Higher WHT rate (often 10–25%) |
| Cloud SaaS subscription | Business income / FTS | Lower or nil WHT under many treaties |
| Management consulting | FTS (Fees for Technical Services) | Moderate WHT (5–15%) |
| Interest on loan | Interest | Treaty-specific (often 5–15%) |
| Dividend distribution | Dividend | Treaty-specific (often 5–15%) |
| Contract staffing | Employment / business income | Domestic rate often applies |

The engine must classify the service from the payment purpose field and flag ambiguous cases for human review.

### Confidence Levels

| Level | Criteria | Action |
|---|---|---|
| `HIGH` | Clear treaty, unambiguous service type, rate table match | Proceed automatically |
| `MEDIUM` | Treaty found, service type classification inferred with moderate certainty | Proceed but flag for Finance Approver attention |
| `REVIEW_REQUIRED` | Ambiguous service type, multiple possible classifications, no treaty found, or conflicting rules | Hold payment; escalate to tax team |

---

## 8. KYC / Sanctions Engine — Design Detail

### Screening Scope

| Check Type | Source | Frequency |
|---|---|---|
| Sanctions screening | OFAC, UN, EU, UK HM Treasury, local lists | Every payment |
| PEP screening | World Check / ComplyAdvantage PEP database | Every payment |
| Adverse media | Provider-aggregated news and media | Every payment |
| Entity type verification | Company registry lookup | On payee onboarding + annual refresh |

### Match Scoring

| Score | Label | Meaning |
|---|---|---|
| 0.0 – 0.49 | `NO_MATCH` | No meaningful similarity; mark CLEAR |
| 0.50 – 0.79 | `POTENTIAL_MATCH` | Name/entity similarity above threshold; human review required |
| 0.80 – 1.00 | `CONFIRMED_MATCH` | High-confidence match to sanctioned entity; block immediately |

### Escalation and Resolution

- `POTENTIAL_MATCH`: Compliance Officer must review within 24 hours. Decision logged with officer ID.
  - Resolved as false positive → CLEAR → resume pipeline
  - Resolved as true match → BLOCK → SAR filing initiated
- `CONFIRMED_MATCH`: Dual authorisation from Compliance + Legal to mark as false positive (rare edge case only); otherwise permanent block

---

## 9. Data Requirements

| Data Entity | Owner | Source | Storage | Sensitivity |
|---|---|---|---|---|
| Payment Instruction | Initiator | Input form / API | Payment Ledger DB | High |
| Payee Entity Record | Finance / Ops | Internal entity DB | Entity DB | High |
| PPM / Contract Document | Legal | Contract management system | Immutable document store | High |
| Tax Rate Tables | Tax team | Internal maintenance | Structured DB (read-only by engine) | Medium |
| Tax Treaty / Rule Corpus | Tax team | Public tax authority sources | Vector store | Low |
| KYC Screening Result | Compliance | World Check API | Audit log (encrypted) | Very High |
| Payment Dossier | System (compiled) | All stages | Document store + audit log | Very High |
| Bank Settlement Record | Bank | Bank API | Payment Ledger DB | High |

---

## 10. Integration Touchpoints

| Integration | Type | Purpose | Stage |
|---|---|---|---|
| Internal Entity Database | REST API (read-only) | Payee verification | Stage 1 |
| Contract Management System | REST API / document retrieval | PPM document fetch | Stage 2 |
| Vector Store (tax corpus) | Vector search API | Tax rule retrieval | Stage 3 |
| Tax Rate Table DB | SQL query | WHT rate lookup | Stage 3 |
| World Check / KYC API | REST API | Sanctions and PEP screening | Stage 4 |
| Approval Workflow System | REST API / webhook | Dossier routing and approval capture | Stage 5 |
| Bank / Payment Rail API | REST API / SWIFT / ISO 20022 | Payment execution | Stage 6 |
| Notification Service | Email / in-system | Alerts, SLA reminders, outcomes | All stages |
| Audit Log Store | Append-only DB / S3 | Immutable record of all decisions | All stages |

---

## 11. Non-Functional Requirements

| Category | Requirement |
|---|---|
| **Availability** | 99.9% uptime for payment pipeline services |
| **Latency** | Stages 1–4 must complete within 30 seconds combined (auto-path); approval excluded |
| **Security** | All data in transit: TLS 1.3 minimum; all data at rest: AES-256 encrypted |
| **Auditability** | Every state change is logged with timestamp, actor, and before/after values; append-only |
| **Scalability** | Pipeline must support concurrent processing of up to 500 payments without degradation |
| **Compliance** | Adherent to FATF recommendations, PCI-DSS (where card data involved), GDPR / data privacy laws of applicable jurisdictions |
| **Recoverability** | Any stage failure must preserve payment state; no double-spend risk; idempotent execution |
| **Observability** | Full tracing (distributed trace ID per PRID) across all microservices; alerting on stage failure rates |

---

## 12. Glossary

| Term | Definition |
|---|---|
| **PRID** | Payment Reference ID — unique identifier assigned at Stage 0 |
| **PPM** | Project/Payment Management agreement — the governing contract between payer and payee |
| **WHT** | Withholding Tax — tax deducted at source by the payer on behalf of the tax authority |
| **FTS** | Fees for Technical Services — a common service classification in bilateral tax treaties |
| **KYC** | Know Your Customer — the process of verifying the identity and risk profile of a counterparty |
| **PEP** | Politically Exposed Person — an individual in a prominent public function, subject to enhanced due diligence |
| **SAR** | Suspicious Activity Report — a mandatory regulatory filing when financial crime is suspected |
| **EHQ** | Exception Handling Queue — the holding state for payments that fail any pipeline stage |
| **Vector Store** | A database that stores document embeddings for semantic similarity search |
| **World Check** | Refinitiv's global risk intelligence database used for sanctions and PEP screening |
| **FATF** | Financial Action Task Force — the global money laundering and terrorist financing watchdog |
| **OFAC** | Office of Foreign Assets Control — US Treasury sanctions authority |

---

*ONE BCG | Confidential – Internal Use Only | v1.0 | 2026-05-19*
