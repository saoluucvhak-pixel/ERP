using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hak.Application.Exchange;
using Hak.Domain.Common;
using Hak.Domain.Vouchers;
using Json.Schema;
using Microsoft.EntityFrameworkCore;

namespace Hak.Integration.Tests;

public class JsonExchangeTests
{
    private static async Task<string> CodeOf(Func<Task> act) => (await Assert.ThrowsAsync<DomainException>(act)).Code;

    private static JsonObject Voucher(string sourceId, string amount = "2500000", string version = "1", string account = "EXP") => new()
    {
        ["source_id"] = sourceId, ["source_version"] = version, ["voucher_type"] = "GJ",
        ["voucher_date"] = "2026-03-31", ["posting_date"] = "2026-03-31", ["description"] = "Chi phí lương tháng 3",
        ["lines"] = new JsonArray(
            new JsonObject { ["account_code"] = account, ["side"] = "D", ["amount"] = amount },
            new JsonObject { ["account_code"] = "AP", ["side"] = "C", ["amount"] = amount, ["partner_code"] = "NCC01" }),
    };

    private static JsonObject Package(TestHost h, string packageId, params JsonObject[] vouchers)
    {
        var p = new JsonObject
        {
            ["schema_version"] = "1.0", ["package_id"] = packageId, ["event_id"] = "evt-" + packageId,
            ["source_system"] = TestHost.ExchangeSource, ["company_code"] = "TEST01", ["branch_code"] = null,
            ["created_at"] = "2026-04-01T09:00:00+07:00", ["document_type"] = "journal-voucher",
            ["source_id"] = "payroll-2026-03", ["source_version"] = "1",
            ["payload"] = new JsonObject { ["vouchers"] = new JsonArray(vouchers.Select(v => (JsonNode)v).ToArray()) },
        };
        PackageCrypto.Seal(p, TestHost.ExchangeKeyId, h.ExchangeSecret);
        return p;
    }

    [Fact]
    public void Canonicalizer_SortsKeys_StripsWhitespace_RejectsFloats()
    {
        Assert.Equal("""{"a":1,"b":[true,null,"x\n"],"c":{"d":"é"}}""",
            JsonCanonicalizer.Canonicalize("""{ "c": {"d":"é"}, "b": [true, null, "x\n"], "a": 1 }"""));
        // RFC 8785 orders by UTF-16 code units, not by culture.
        Assert.Equal("""{"B":1,"a":2,"é":3}""", JsonCanonicalizer.Canonicalize("""{"é":3,"a":2,"B":1}"""));
        Assert.Equal(ErrorCodes.PackageSchemaInvalid, Assert.Throws<DomainException>(() => JsonCanonicalizer.Canonicalize("""{"amount":1.5}""")).Code);
    }

    [Fact]
    public async Task DryRun_Then_Commit_CreatesDrafts_ThatStillNeedApproval()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var json = Package(h, "PKG-0001", Voucher("PR-03-A"), Voucher("PR-03-B", "1000000")).ToJsonString();

        var dry = await s.Import.ProcessAsync(h.Preparer, json, commit: false);
        Assert.Equal(("DRY_RUN", "OK", 2), (dry.Mode, dry.Status, dry.Counts.Valid));
        Assert.False(await s.Db.Vouchers.AnyAsync());

        var result = await s.Import.ProcessAsync(h.Preparer, json, commit: true);
        Assert.Equal(("COMMITTED", 2), (result.Status, result.Counts.Created));
        Assert.All(await s.Db.Vouchers.ToListAsync(), v => Assert.Equal(VoucherStatus.Draft, v.Status));
        Assert.False(await s.Db.JournalEntries.AnyAsync());

        // Import result conforms to its published schema.
        
        var schema = HakSchemas.Get("import-result.schema.json");
        using var doc = JsonDocument.Parse(result.ToJson());
        Assert.True(schema.Evaluate(doc.RootElement).IsValid, result.ToJson());

        // Imported drafts go through the same engine: submit → approve → post.
        var id = result.Items[0].VoucherId!.Value;
        await s.Engine.SubmitAsync(h.Preparer, id);
        await s.Engine.ApproveAsync(h.Approver, id);
        await s.Engine.PostAsync(h.Approver, id);
    }

    [Fact]
    public async Task Replay_OfSamePackage_IsRejected()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var json = Package(h, "PKG-0002", Voucher("PR-1")).ToJsonString();
        await s.Import.ProcessAsync(h.Preparer, json, true);
        Assert.Equal(ErrorCodes.PackageReplayed, await CodeOf(() => s.Import.ProcessAsync(h.Preparer, json, true)));
    }

    [Fact]
    public async Task SameSourceInNewPackage_IsDuplicateOrConflict_NeverOverwrite()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        await s.Import.ProcessAsync(h.Preparer, Package(h, "PKG-0003", Voucher("PR-9")).ToJsonString(), true);

        var same = await s.Import.ProcessAsync(h.Preparer, Package(h, "PKG-0004", Voucher("PR-9")).ToJsonString(), true);
        Assert.Equal(("COMMITTED", "DUPLICATE_IDENTICAL"), (same.Status, same.Items[0].Outcome));

        var changed = await s.Import.ProcessAsync(h.Preparer, Package(h, "PKG-0005", Voucher("PR-9", "9999", version: "2")).ToJsonString(), true);
        Assert.Equal(("REJECTED", "CONFLICT"), (changed.Status, changed.Items[0].Outcome));
        Assert.Equal(2_500_000m, (await s.Db.VoucherLines.FirstAsync()).Amount);
        Assert.Equal(1, await s.Db.Vouchers.CountAsync());
    }

    [Fact]
    public async Task InvalidItem_RejectsWholePackage_AndWritesNothing()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var json = Package(h, "PKG-0006", Voucher("OK-1"), Voucher("BAD-1", account: "NOPE")).ToJsonString();
        var r = await s.Import.ProcessAsync(h.Preparer, json, true);
        Assert.Equal("REJECTED", r.Status);
        Assert.Equal(ErrorCodes.AccountNotFound, r.Items[1].Errors[0].Code);
        Assert.False(await s.Db.Vouchers.AnyAsync());
        Assert.False(await s.Db.ImportBatches.AnyAsync());
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.Action == "import.reject"));
    }

    [Fact]
    public async Task TamperedPayload_FailsSignature()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var p = Package(h, "PKG-0007", Voucher("T-1"));
        p["payload"]!["vouchers"]![0]!["lines"]![0]!["amount"] = "1";
        Assert.Equal(ErrorCodes.PackageSignatureInvalid, await CodeOf(() => s.Import.ProcessAsync(h.Preparer, p.ToJsonString(), false)));
    }

    [Fact]
    public async Task ChecksumMismatch_IsDetected_EvenWhenSignatureIsValid()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var p = Package(h, "PKG-0008", Voucher("T-2"));
        p["checksum"]!["value"] = new string('0', 64);
        p["auth"]!["signature"] = PackageCrypto.Signature(p, h.ExchangeSecret);
        Assert.Equal(ErrorCodes.PackageChecksumMismatch, await CodeOf(() => s.Import.ProcessAsync(h.Preparer, p.ToJsonString(), false)));
    }

    [Fact]
    public async Task UnknownKey_And_WrongCompany_AreRejected()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var p = Package(h, "PKG-0009", Voucher("T-3"));
        p["auth"]!["key_id"] = "unknown";
        Assert.Equal(ErrorCodes.UnknownSource, await CodeOf(() => s.Import.ProcessAsync(h.Preparer, p.ToJsonString(), false)));

        var q = Package(h, "PKG-0010", Voucher("T-4"));
        q["company_code"] = "OTHER";
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Import.ProcessAsync(h.Preparer, q.ToJsonString(), false)));
    }

    [Theory]
    [InlineData("""{"amount": 2500000}""")]          // money as JSON number
    [InlineData("""{"amount": "2,500,000"}""")]      // locale formatting
    [InlineData("""{"amount": "-5"}""")]             // negative
    [InlineData("""{"side": "Debit"}""")]
    [InlineData("""{"unexpected": true}""")]
    public async Task SchemaViolations_AreRejectedBeforeAnyProcessing(string linePatch)
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var v = Voucher("S-1");
        foreach (var kv in JsonNode.Parse(linePatch)!.AsObject().ToList())
            v["lines"]![0]!.AsObject()[kv.Key] = kv.Value?.DeepClone();
        var p = Package(h, "PKG-0011", v);
        Assert.Equal(ErrorCodes.PackageSchemaInvalid, await CodeOf(() => s.Import.ProcessAsync(h.Preparer, p.ToJsonString(), false)));
    }

    [Fact]
    public async Task Import_RequiresImportPermission()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        Assert.Equal(ErrorCodes.Forbidden, await CodeOf(() => s.Import.ProcessAsync(h.Auditor, Package(h, "PKG-0012", Voucher("P-1")).ToJsonString(), false)));
    }

    [Fact]
    public async Task LockedPeriod_IsReportedInDryRun()
    {
        using var h = await TestHost.CreateAsync();
        using var s = h.NewScope();
        var march = await s.Db.AccountingPeriods.SingleAsync(p => p.PeriodNo == 3);
        await s.Setup.LockPeriodAsync(h.Approver, march.Id);
        var r = await s.Import.ProcessAsync(h.Preparer, Package(h, "PKG-0013", Voucher("L-1")).ToJsonString(), false);
        Assert.Equal("HAS_ERRORS", r.Status);
        Assert.Contains(r.Items[0].Errors, e => e.Code == ErrorCodes.PeriodLocked);
    }

    [Fact]
    public void ExampleFiles_MatchSchema()
    {
        var root = FindRepoRoot();
        var schema = HakSchemas.FromText(File.ReadAllText(Path.Combine(root, "schemas", "journal-voucher.schema.json")));
        using var valid = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas", "examples", "journal-voucher.valid.json")));
        using var invalid = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas", "examples", "journal-voucher.invalid.json")));
        Assert.True(schema.Evaluate(valid.RootElement).IsValid);
        Assert.False(schema.Evaluate(invalid.RootElement).IsValid);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HakAccounting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}
