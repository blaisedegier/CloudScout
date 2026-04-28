using CloudScout.Core.Classification;
using CloudScout.Core.Classification.Extraction;
using CloudScout.Core.Crawling;
using CloudScout.Core.Persistence;
using CloudScout.Core.Persistence.Entities;
using CloudScout.Core.Services;
using CloudScout.Core.Taxonomy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudScout.Core.Tests.Services;

/// <summary>
/// End-to-end tests for the scan-delta optimisation. Stubs out the cloud provider and
/// classification tiers so the orchestrator exercises real persistence (in-memory EF Core)
/// and real delta logic. Each test runs scans through the public RunScanAsync entry point.
/// </summary>
public class ScanOrchestratorDeltaTests
{
    private static readonly DateTime BaseTime = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task First_scan_with_no_prior_session_marks_all_files_as_New()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var connection = await fixture.SeedConnectionAsync();

        fixture.Provider.QueueFiles(
            File("id-1", "/a.txt", BaseTime),
            File("id-2", "/b.txt", BaseTime));

        await fixture.Orchestrator.RunScanAsync(connection.Id, "test-taxonomy");

        var files = await fixture.Db.CrawledFiles.AsNoTracking().ToListAsync();
        files.Should().HaveCount(2);
        files.Should().OnlyContain(f => f.ChangeStatus == ChangeStatusValues.New);

        // Tier 0 stub produces one suggestion per file, and no prior session exists, so every
        // file goes through the classification path (no copying yet).
        var suggestions = await fixture.Db.FileSuggestions.AsNoTracking().ToListAsync();
        suggestions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Second_scan_with_identical_files_marks_all_as_Unchanged_and_clones_suggestions()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var connection = await fixture.SeedConnectionAsync();

        fixture.Provider.QueueFiles(
            File("id-1", "/a.txt", BaseTime),
            File("id-2", "/b.txt", BaseTime));
        await fixture.Orchestrator.RunScanAsync(connection.Id, "test-taxonomy");

        // Re-queue the same files for the second scan.
        fixture.Provider.QueueFiles(
            File("id-1", "/a.txt", BaseTime),
            File("id-2", "/b.txt", BaseTime));

        // No tier should run on the second scan — assert by counting how many times the stub
        // tier was invoked across both scans.
        var invocationsBeforeSecondScan = fixture.Tier.InvocationCount;
        await fixture.Orchestrator.RunScanAsync(connection.Id, "test-taxonomy");

        // Find the second session (the most recent completed for the connection).
        var sessions = await fixture.Db.ScanSessions
            .AsNoTracking()
            .OrderBy(s => s.StartedUtc)
            .ToListAsync();
        sessions.Should().HaveCount(2);

        var secondSession = sessions[1];
        var secondScanFiles = await fixture.Db.CrawledFiles
            .AsNoTracking()
            .Where(f => f.SessionId == secondSession.Id)
            .ToListAsync();

        secondScanFiles.Should().HaveCount(2);
        secondScanFiles.Should().OnlyContain(f => f.ChangeStatus == ChangeStatusValues.Unchanged);

        // Each unchanged file should have its prior session's suggestion copied with the new FileId.
        var secondScanSuggestions = await fixture.Db.FileSuggestions
            .AsNoTracking()
            .Where(s => secondScanFiles.Select(f => f.Id).Contains(s.FileId))
            .ToListAsync();
        secondScanSuggestions.Should().HaveCount(2);

        // No new tier runs — copy path was taken.
        fixture.Tier.InvocationCount.Should().Be(invocationsBeforeSecondScan);
    }

    [Fact]
    public async Task Mix_scan_handles_New_Modified_and_Unchanged_correctly()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var connection = await fixture.SeedConnectionAsync();

        // Scan 1: two files
        fixture.Provider.QueueFiles(
            File("id-keep", "/keep.txt", BaseTime),
            File("id-change", "/change.txt", BaseTime));
        await fixture.Orchestrator.RunScanAsync(connection.Id, "test-taxonomy");

        var invocationsAfterFirst = fixture.Tier.InvocationCount;
        invocationsAfterFirst.Should().Be(2, "first scan classifies both files");

        // Scan 2: keep one, modify one (different ModifiedUtc), add a brand-new third.
        fixture.Provider.QueueFiles(
            File("id-keep", "/keep.txt", BaseTime),                           // Unchanged
            File("id-change", "/change.txt", BaseTime.AddHours(1)),           // Modified
            File("id-new", "/new.txt", BaseTime));                            // New
        await fixture.Orchestrator.RunScanAsync(connection.Id, "test-taxonomy");

        var sessions = await fixture.Db.ScanSessions.AsNoTracking().OrderBy(s => s.StartedUtc).ToListAsync();
        var secondSession = sessions[1];

        var secondScanFiles = await fixture.Db.CrawledFiles
            .AsNoTracking()
            .Where(f => f.SessionId == secondSession.Id)
            .ToDictionaryAsync(f => f.ExternalFileId, f => f.ChangeStatus);

        secondScanFiles["id-keep"].Should().Be(ChangeStatusValues.Unchanged);
        secondScanFiles["id-change"].Should().Be(ChangeStatusValues.Modified);
        secondScanFiles["id-new"].Should().Be(ChangeStatusValues.New);

        // Two new tier runs (modified + new); the unchanged file used the copy path.
        var newInvocations = fixture.Tier.InvocationCount - invocationsAfterFirst;
        newInvocations.Should().Be(2, "only the modified and new files should re-classify");
    }

    private static RemoteFileMetadata File(string externalId, string path, DateTime modifiedUtc) =>
        new(
            ExternalFileId: externalId,
            ExternalPath: path,
            FileName: path[(path.LastIndexOf('/') + 1)..],
            ParentFolderPath: "/",
            MimeType: "text/plain",
            SizeBytes: 100,
            CreatedUtc: modifiedUtc,
            ModifiedUtc: modifiedUtc);

    /// <summary>
    /// Bundles together the in-memory DbContext, a fake provider, and a stub tier that records
    /// invocation counts. Real ScanOrchestrator wiring around them — the orchestrator under test
    /// is the production code with no test-only seams.
    /// </summary>
    private sealed class TestFixture : IAsyncDisposable
    {
        public CloudScoutDbContext Db { get; }
        public ScanOrchestrator Orchestrator { get; }
        public FakeStorageProvider Provider { get; }
        public RecordingTier Tier { get; }

        private TestFixture(CloudScoutDbContext db, ScanOrchestrator orch, FakeStorageProvider provider, RecordingTier tier)
        {
            Db = db;
            Orchestrator = orch;
            Provider = provider;
            Tier = tier;
        }

        public static Task<TestFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<CloudScoutDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new CloudScoutDbContext(options);

            var provider = new FakeStorageProvider();
            var tier = new RecordingTier();
            var pipeline = new ClassificationPipeline(new IClassificationTier[] { tier }, NullLogger<ClassificationPipeline>.Instance);
            var textExtraction = new TextExtractionService(Array.Empty<ITextExtractor>(), NullLogger<TextExtractionService>.Instance);
            var taxonomyProvider = new FakeTaxonomyProvider();

            var orchestrator = new ScanOrchestrator(
                db,
                new[] { (ICloudStorageProvider)provider },
                pipeline,
                textExtraction,
                taxonomyProvider,
                NullLogger<ScanOrchestrator>.Instance);

            return Task.FromResult(new TestFixture(db, orchestrator, provider, tier));
        }

        public async Task<CloudConnection> SeedConnectionAsync()
        {
            var c = new CloudConnection
            {
                Provider = "FakeDrive",
                AccountIdentifier = "test@example.com",
                HomeAccountId = "home-account",
                Status = ConnectionStatus.Active,
            };
            Db.CloudConnections.Add(c);
            await Db.SaveChangesAsync();
            return c;
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class FakeStorageProvider : ICloudStorageProvider
    {
        private readonly Queue<RemoteFileMetadata> _next = new();

        public string ProviderName => "FakeDrive";

        public void QueueFiles(params RemoteFileMetadata[] files)
        {
            _next.Clear();
            foreach (var f in files) _next.Enqueue(f);
        }

        public Task<ConnectionResult> AuthenticateInteractiveAsync(
            Func<string, Task>? deviceCodePrompt = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Auth flow not exercised in delta tests.");

        public async IAsyncEnumerable<RemoteFileMetadata> EnumerateFilesAsync(
            string homeAccountId,
            string? resumeFromPath = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (_next.Count > 0)
            {
                yield return _next.Dequeue();
                await Task.Yield();
            }
        }

        public Task<Stream> DownloadAsync(string homeAccountId, string externalFileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());
    }

    /// <summary>Stub Tier 0 that records how many times it ran and emits one suggestion per file.</summary>
    private sealed class RecordingTier : IClassificationTier
    {
        public int InvocationCount { get; private set; }
        public int Tier => 0;

        public Task<IReadOnlyList<ClassificationResult>> ClassifyAsync(
            ClassificationContext context,
            TaxonomyDefinition taxonomy,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            IReadOnlyList<ClassificationResult> results = new[]
            {
                new ClassificationResult("test.category", 0.9, Tier, "test reason"),
            };
            return Task.FromResult(results);
        }
    }

    private sealed class FakeTaxonomyProvider : ITaxonomyProvider
    {
        public Task<TaxonomyDefinition> GetAsync(string nameOrPath, CancellationToken cancellationToken = default)
        {
            var taxonomy = new TaxonomyDefinition(
                name: "Test",
                version: "1.0",
                categories: new List<TaxonomyCategory>
                {
                    new() { Id = "test.category", DisplayName = "Test Category" },
                });
            return Task.FromResult(taxonomy);
        }
    }
}
