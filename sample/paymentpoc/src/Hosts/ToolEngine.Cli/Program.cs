/*
 *  ONE BCG ToolEngine v2026 — Payment Pipeline CLI Demo
 *  ─────────────────────────────────────────────────────────
 *  Interactive console runner that exercises all 8 pipeline stages
 *  against a locally running ToolEngine.Api instance.
 *
 *  Usage:
 *    dotnet run --project src/Hosts/ToolEngine.Cli
 *
 *  Requirements:
 *    ToolEngine.Api running at https://localhost:51698  (or set TOOLENGINE_URL env var)
 */

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const string DEFAULT_BASE = "http://localhost:5000";

var baseUrl = Environment.GetEnvironmentVariable("TOOLENGINE_URL") ?? DEFAULT_BASE;
var http    = new HttpClient { BaseAddress = new Uri(baseUrl) };

var json = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented               = false,
    DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
};

// ── Branding ──────────────────────────────────────────────────────────────────
Clear();
Banner();

// ── Auth with connection retry ────────────────────────────────────────────────
// Retries indefinitely with a 3-second delay between attempts so the CLI can be
// started before the API is ready. Press Q during the wait to quit.
H2("Authentication");
Dim($"  Connecting to {baseUrl}");

string? jwt = null;
var attempt = 0;

while (jwt is null)
{
    attempt++;
    Console.Write($"  [{attempt}] Requesting token… ");
    jwt = await AcquireToken();

    if (jwt is not null)
    {
        Console.WriteLine();
        break;
    }

    Console.ForegroundColor = ConsoleColor.Red;
    Console.Write("not available.");
    Console.ResetColor();
    Console.Write("  Retrying in 3s — press ");
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("Q");
    Console.ResetColor();
    Console.Write(" to quit: ");

    // Wait 3 seconds in 100 ms slices so Q can interrupt
    var quit = false;
    for (var tick = 0; tick < 30; tick++)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Q) { quit = true; break; }
        }
        await Task.Delay(100);
    }

    Console.WriteLine();

    if (quit)
    {
        Dim("Exiting.");
        return 0;
    }
}

http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt!);
Ok($"JWT acquired — connected to {baseUrl}");

// ── Main menu ─────────────────────────────────────────────────────────────────
while (true)
{
    Console.WriteLine();
    H2("Main Menu");
    Menu(new[]
    {
        "1  Run happy path (Acme · PPM-001 · GBP 5,000 → approval → resume → complete)",
        "2  Run PPM expired scenario (Horizon Advisory · PPM-002 · blocked Stage 2)",
        "3  Run KYC block scenario (Risq Capital · PPM-003 · blocked Stage 4)",
        "4  Run over-limit scenario (Acme · PPM-001 · GBP 300,000 · blocked Stage 2)",
        "5  Custom payment (enter values manually)",
        "6  List registered tools",
        "7  List pending approvals",
        "Q  Quit",
    });

    var choice = Prompt("Select").Trim().ToUpperInvariant();
    Console.WriteLine();

    switch (choice)
    {
        case "1":
            await RunPaymentFlow(new PaymentRequest(
                PayerName:         "ONE BCG UK Ltd",
                PayerJurisdiction: "GB",
                PayerEntityId:     "PAYER-ONEBCG-001",
                PayeeRef:          "Acme Consulting",
                GrossAmount:       5000m,
                Currency:          "GBP",
                ServiceType:       0,   // ManagementConsulting
                PpmId:             "PPM-001",
                CallerType:        0)); // Human
            break;

        case "2":
            await RunPaymentFlow(new PaymentRequest(
                PayerName:         "ONE BCG UK Ltd",
                PayerJurisdiction: "GB",
                PayerEntityId:     "PAYER-ONEBCG-001",
                PayeeRef:          "Horizon Advisory",
                GrossAmount:       10_000m,
                Currency:          "USD",
                ServiceType:       0,
                PpmId:             "PPM-002",
                CallerType:        0));
            break;

        case "3":
            await RunPaymentFlow(new PaymentRequest(
                PayerName:         "ONE BCG UK Ltd",
                PayerJurisdiction: "GB",
                PayerEntityId:     "PAYER-ONEBCG-001",
                PayeeRef:          "Risq Capital",
                GrossAmount:       1_000m,
                Currency:          "USD",
                ServiceType:       0,
                PpmId:             "PPM-003",
                CallerType:        0));
            break;

        case "4":
            await RunPaymentFlow(new PaymentRequest(
                PayerName:         "ONE BCG UK Ltd",
                PayerJurisdiction: "GB",
                PayerEntityId:     "PAYER-ONEBCG-001",
                PayeeRef:          "Acme Consulting",
                GrossAmount:       300_000m,
                Currency:          "GBP",
                ServiceType:       0,
                PpmId:             "PPM-001",
                CallerType:        0));
            break;

        case "5":
            var custom = PromptPaymentRequest();
            await RunPaymentFlow(custom);
            break;

        case "6":
            await ListTools();
            break;

        case "7":
            await ListPendingApprovals();
            break;

        case "Q":
            Dim("Exiting.");
            return 0;

        default:
            Warn("Unknown option.");
            break;
    }
}

// ── Payment flow orchestration ────────────────────────────────────────────────
async Task RunPaymentFlow(PaymentRequest req)
{
    H2("Submitting Payment");
    PrintRequest(req);

    Step("POST /api/v1/payments");
    var paymentResponse = await http.PostAsJsonAsync("/api/v1/payments", req, json);
    var responseBody    = await paymentResponse.Content.ReadAsStringAsync();
    var responseData    = JsonDocument.Parse(responseBody).RootElement;

    PrintHttpStatus(paymentResponse.StatusCode);

    if ((int)paymentResponse.StatusCode == 202)
    {
        // Suspended at approval gate
        var prid             = GetStr(responseData, "prid");
        var pendingApprovalId= GetStr(responseData, "pendingApprovalId");
        var stageReached     = GetInt(responseData, "stageReached");

        Warn($"Pipeline SUSPENDED at Stage {stageReached} — human approval required");
        Dim($"  PRID:               {prid}");
        Dim($"  Pending Approval ID: {pendingApprovalId}");

        if (string.IsNullOrEmpty(pendingApprovalId)) { Err("pendingApprovalId missing — cannot continue."); return; }

        // Fetch approval details
        Step($"GET /api/v1/approvals/{pendingApprovalId}");
        var approvalDetailResponse = await http.GetAsync($"/api/v1/approvals/{pendingApprovalId}");
        var approvalDetail         = JsonDocument.Parse(await approvalDetailResponse.Content.ReadAsStringAsync()).RootElement;
        Dim($"  Risk:    {GetStr(approvalDetail, "risk")}");
        Dim($"  Channel: {GetStr(approvalDetail, "channel")}");
        Dim($"  Expires: {GetStr(approvalDetail, "expiresAt")}");

        // Dev: fetch raw token
        Step($"GET /api/v1/approvals/{pendingApprovalId}/token  [DEV]");
        var approvalTokenResponse = await http.GetAsync($"/api/v1/approvals/{pendingApprovalId}/token");
        if (!approvalTokenResponse.IsSuccessStatusCode)
        {
            Err("Could not retrieve approval token (dev endpoint unavailable).");
            return;
        }
        var approvalTokenData = JsonDocument.Parse(await approvalTokenResponse.Content.ReadAsStringAsync()).RootElement;
        var token             = GetStr(approvalTokenData, "approvalToken");
        Warn($"  [DEV] Approval token: {token}");

        Console.WriteLine();
        var action = Prompt("(A)pprove / (D)eny / (S)kip [A]").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(action)) action = "A";

        if (action == "D")
        {
            Step($"POST /api/v1/approvals/{pendingApprovalId}/deny");
            var dr = await http.PostAsJsonAsync(
                $"/api/v1/approvals/{pendingApprovalId}/deny",
                new { reason = "Denied via CLI demo." }, json);
            PrintHttpStatus(dr.StatusCode);
            Warn("Approval denied. Payment will not proceed.");
            return;
        }

        if (action == "S")
        {
            Dim("Skipping approval — payment left in PENDING_APPROVAL state.");
            return;
        }

        // Approve
        Step($"POST /api/v1/approvals/{pendingApprovalId}/approve");
        var approvePayload = new { approvalToken = token };
        var approveResponse = await http.PostAsJsonAsync(
            $"/api/v1/approvals/{pendingApprovalId}/approve", approvePayload, json);
        var approveData = JsonDocument.Parse(await approveResponse.Content.ReadAsStringAsync()).RootElement;
        PrintHttpStatus(approveResponse.StatusCode);
        if (!approveResponse.IsSuccessStatusCode)
        {
            Err($"Approval failed: {GetStr(approveData, "error")}");
            return;
        }
        Ok("Approval granted.");

        // Resume
        Step($"POST /api/v1/payments/{prid}/resume");
        var resumeResponse = await http.PostAsJsonAsync($"/api/v1/payments/{prid}/resume", new { }, json);
        var resumeData     = JsonDocument.Parse(await resumeResponse.Content.ReadAsStringAsync()).RootElement;
        PrintHttpStatus(resumeResponse.StatusCode);

        if (resumeResponse.IsSuccessStatusCode)
        {
            Ok($"Pipeline complete — {GetStr(resumeData, "message")}");
        }
        else
        {
            Err($"Resume failed: {GetStr(resumeData, "errorCode")} — {GetStr(resumeData, "message")}");
        }

        prid ??= GetStr(resumeData, "prid");
        if (prid is not null) await PrintPipelineTrace(prid);
    }
    else if (paymentResponse.IsSuccessStatusCode)
    {
        var prid  = GetStr(responseData, "prid");
        var msg   = GetStr(responseData, "message");
        var stage = GetInt(responseData, "stageReached");
        Ok($"Payment completed — Stage {stage} — {msg}");
        Dim($"  PRID: {prid}");
        if (prid is not null) await PrintPipelineTrace(prid);
    }
    else
    {
        var prid     = GetStr(responseData, "prid");
        var stage    = GetInt(responseData, "stageReached");
        var errCode  = GetStr(responseData, "errorCode");
        var msg      = GetStr(responseData, "message");

        Err($"Blocked at Stage {stage}: {errCode}");
        Err($"  {msg}");
        Dim($"  PRID: {prid ?? "—"}");

        if (prid is not null) await PrintPipelineTrace(prid);
    }
}

async Task PrintPipelineTrace(string prid)
{
    Console.WriteLine();
    H2("Audit Trail");
    Step($"GET /api/v1/payments/{prid}/audit");
    var r = await http.GetAsync($"/api/v1/payments/{prid}/audit");
    if (!r.IsSuccessStatusCode) { Err("Could not load audit trail."); return; }

    var entries = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
    if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
    {
        Dim("  No audit records.");
        return;
    }

    // Header
    PrintTableRow("Stage", "Details", "Outcome", "Time", isHeader: true);
    foreach (var e in entries.EnumerateArray())
    {
        var stage   = GetStr(e, "stageName") ?? GetStr(e, "stage") ?? "—";
        var details = GetStr(e, "details")   ?? "—";
        var outcome = GetStr(e, "outcome")   ?? "—";
        var time    = GetStr(e, "enteredAt") is string t
                      ? DateTime.Parse(t).ToLocalTime().ToString("HH:mm:ss")
                      : "—";

        var color = outcome switch
        {
            "PASS"            => ConsoleColor.Green,
            "FAIL"            => ConsoleColor.Red,
            var s when s.StartsWith("HELD") => ConsoleColor.Yellow,
            _                 => ConsoleColor.Gray,
        };

        PrintTableRow(stage, details, outcome, time, outcomeColor: color);
    }
}

async Task ListTools()
{
    H2("Registered Tools");
    Step("GET /api/v1/tools");
    var toolsResponse = await http.GetAsync("/api/v1/tools");
    if (!toolsResponse.IsSuccessStatusCode) { Err("Could not fetch tools."); return; }
    var toolList = JsonDocument.Parse(await toolsResponse.Content.ReadAsStringAsync()).RootElement;
    if (toolList.ValueKind != JsonValueKind.Array) { Err("Unexpected response."); return; }

    PrintTableRow("Full Name", "Version", "Type", "Enabled", isHeader: true);
    foreach (var t in toolList.EnumerateArray())
        PrintTableRow(
            GetStr(t, "fullName")  ?? "—",
            GetStr(t, "version")   ?? "—",
            GetStr(t, "type")      ?? "—",
            GetStr(t, "isEnabled") ?? "—");
}

async Task ListPendingApprovals()
{
    H2("Pending Approvals");
    Step("GET /api/v1/approvals");
    var approvalsResponse = await http.GetAsync("/api/v1/approvals");
    if (!approvalsResponse.IsSuccessStatusCode) { Err("Could not fetch approvals."); return; }
    var approvalList = JsonDocument.Parse(await approvalsResponse.Content.ReadAsStringAsync()).RootElement;
    if (approvalList.ValueKind != JsonValueKind.Array) { Err("Unexpected response."); return; }
    if (approvalList.GetArrayLength() == 0) { Dim("  No pending approvals."); return; }

    PrintTableRow("ID", "Tool", "Risk", "Expires", isHeader: true);
    foreach (var a in approvalList.EnumerateArray())
        PrintTableRow(
            GetStr(a, "id")          ?? "—",
            GetStr(a, "toolFullName")?? "—",
            GetStr(a, "risk")        ?? "—",
            GetStr(a, "expiresAt")   ?? "—");
}

// ── Token ─────────────────────────────────────────────────────────────────────
async Task<string?> AcquireToken()
{
    try
    {
        // 5-second timeout per attempt so the CLI never hangs waiting for a slow/absent API
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var res = await http.PostAsJsonAsync("/dev/token", new
        {
            userId   = "cli-demo-user",
            userName = "CLI Demo User",
        }, json, cts.Token);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        return GetStr(doc, "token");
    }
    catch { return null; }
}

// ── Custom prompt ─────────────────────────────────────────────────────────────
PaymentRequest PromptPaymentRequest()
{
    H2("Custom Payment");
    var payerName         = Prompt("Payer Name          [ONE BCG UK Ltd]",  "ONE BCG UK Ltd");
    var payerJurisdiction = Prompt("Payer Jurisdiction  [GB]",              "GB").ToUpperInvariant();
    var payerEntityId     = Prompt("Payer Entity ID     [PAYER-ONEBCG-001]","PAYER-ONEBCG-001");
    var payeeRef          = Prompt("Payee Reference     [Acme Consulting]", "Acme Consulting");

    decimal grossAmount = 5000m;
    while (true)
    {
        var raw = Prompt("Gross Amount        [5000]", "5000");
        if (decimal.TryParse(raw, out grossAmount)) break;
        Warn("Enter a valid number.");
    }

    var currency         = Prompt("Currency  (GBP/USD/EUR) [GBP]", "GBP").ToUpperInvariant();
    var serviceTypeRaw   = Prompt("Service Type (0=ManagementConsulting 1=CloudSaas 2=SoftwareLicense 3=ContractStaffing) [0]", "0");
    var ppmId            = Prompt("PPM ID      [PPM-001]", "PPM-001");
    var callerTypeRaw    = Prompt("Caller Type (0=Human 1=Agent) [0]", "0");

    int.TryParse(serviceTypeRaw, out var serviceType);
    int.TryParse(callerTypeRaw,  out var callerType);

    return new PaymentRequest(payerName, payerJurisdiction, payerEntityId, payeeRef,
        grossAmount, currency, serviceType, ppmId, callerType);
}

// ── Print helpers ─────────────────────────────────────────────────────────────
void Clear()  => Console.Clear();
void NewLine()=> Console.WriteLine();

void Banner()
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("═══════════════════════════════════════════════════════════");
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Write("  ONE BCG");
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("  ToolEngine v2026 — Payment Pipeline CLI");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("  Multi-Stage pipeline ");
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("═══════════════════════════════════════════════════════════");
    Console.ResetColor();
    NewLine();
}

void H2(string title)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"── {title} " + new string('─', Math.Max(0, 52 - title.Length)));
    Console.ResetColor();
}

void Menu(string[] items)
{
    foreach (var item in items)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {item}");
    }
    Console.ResetColor();
}

void Step(string msg)
{
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine($"  → {msg}");
    Console.ResetColor();
}

void Ok(string msg)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  ✓ {msg}");
    Console.ResetColor();
}

void Err(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  ✕ {msg}");
    Console.ResetColor();
}

void Warn(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  ! {msg}");
    Console.ResetColor();
}

void Dim(string msg)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine(msg);
    Console.ResetColor();
}

void PrintHttpStatus(System.Net.HttpStatusCode code)
{
    var c = (int)code;
    Console.ForegroundColor = c < 300 ? ConsoleColor.Green
                            : c < 400 ? ConsoleColor.Yellow
                            : ConsoleColor.Red;
    Console.WriteLine($"    HTTP {c} {code}");
    Console.ResetColor();
}

void PrintRequest(PaymentRequest r)
{
    Dim($"  Payer:    {r.PayerName} ({r.PayerJurisdiction}) · {r.PayerEntityId}");
    Dim($"  Payee:    {r.PayeeRef}");
    Dim($"  Amount:   {r.Currency} {r.GrossAmount:N2}  · ServiceType={r.ServiceType}  · PPM={r.PpmId}");
}

void PrintTableRow(
    string c1, string c2, string c3, string c4,
    bool isHeader = false,
    ConsoleColor outcomeColor = ConsoleColor.Gray)
{
    if (isHeader)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {c1,-40} {c2,-20} {c3,-18} {c4}");
        Console.WriteLine($"  {new string('─', 90)}");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"  {c1,-40} {c2,-20} ");
        Console.ForegroundColor = outcomeColor;
        Console.Write($"{c3,-18} ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(c4);
        Console.ResetColor();
    }
}

string Prompt(string label, string? defaultValue = null)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write($"  {label}: ");
    Console.ForegroundColor = ConsoleColor.White;
    var input = Console.ReadLine()?.Trim() ?? string.Empty;
    Console.ResetColor();
    return string.IsNullOrEmpty(input) && defaultValue is not null ? defaultValue : input;
}

// ── JSON helpers ──────────────────────────────────────────────────────────────
static string? GetStr(JsonElement e, string key)
{
    if (e.TryGetProperty(key, out var v))
        return v.ValueKind == JsonValueKind.String ? v.GetString()
             : v.ValueKind == JsonValueKind.Null   ? null
             : v.ToString();
    return null;
}

static string? GetInt(JsonElement e, string key)
{
    if (e.TryGetProperty(key, out var v))
        return v.ValueKind is JsonValueKind.Number ? v.GetInt32().ToString() : v.ToString();
    return null;
}

// ── DTOs ──────────────────────────────────────────────────────────────────────
record PaymentRequest(
    string  PayerName,
    string  PayerJurisdiction,
    string  PayerEntityId,
    string  PayeeRef,
    decimal GrossAmount,
    string  Currency,
    int     ServiceType,
    string  PpmId,
    int     CallerType);
