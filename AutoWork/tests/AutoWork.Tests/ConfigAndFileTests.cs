using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Logging;
using AutoWork.Core.Security;
using AutoWork.Tools;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// The environment overlay is what makes a headless or containerised install usable without a
/// single click, so its precedence rules need pinning down.
/// </summary>
public sealed class ConfigurationTests : IDisposable
{
    private readonly List<string> _touched = [];

    private void SetEnvironment(string name, string? value)
    {
        _touched.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    [Fact]
    public void A_vendor_key_in_the_environment_seeds_a_usable_model()
    {
        SetEnvironment("DEEPSEEK_API_KEY", "sk-test-value");

        var config = new AutoWorkConfig();
        ConfigStore.ApplyEnvironmentOverlay(config);

        var seeded = config.Models.SingleOrDefault(m => m.Preset == "deepseek");

        Assert.NotNull(seeded);
        Assert.Equal("https://api.deepseek.com/v1", seeded.Endpoint);

        // The key itself is never copied into config — only a reference to the variable.
        Assert.Equal("env:DEEPSEEK_API_KEY", seeded.ApiKeyRef);
        Assert.DoesNotContain("sk-test-value", seeded.ApiKeyRef);

        Assert.Equal(seeded.Id, config.Agent.PlannerModelId);
    }

    [Fact]
    public void A_model_the_user_already_configured_is_not_overwritten_by_the_environment()
    {
        SetEnvironment("DEEPSEEK_API_KEY", "sk-test-value");

        var config = new AutoWorkConfig
        {
            Models = [new ModelProfile { Id = "mine", Preset = "deepseek", ModelId = "deepseek-reasoner", Endpoint = "https://proxy.internal/v1" }],
        };

        ConfigStore.ApplyEnvironmentOverlay(config);

        var deepseek = Assert.Single(config.Models, m => m.Preset == "deepseek");
        Assert.Equal("https://proxy.internal/v1", deepseek.Endpoint);
    }

    /// <summary>
    /// config.json is documented and meant to be hand-edited, so a file written the documented
    /// way has to load. It did not: the app wrote PascalCase, the docs showed camelCase, and a
    /// documented file parsed cleanly into all-defaults — silently discarding the user's folder
    /// grants, which is the single worst setting to lose without being told.
    /// </summary>
    [Fact]
    public void A_config_written_the_way_the_documentation_shows_is_actually_loaded()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"autowork-cfg-{Guid.NewGuid():n}.json");

        System.IO.File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "permissions": {
                "roots": [
                  { "path": "/home/fadhil/Projects", "access": "ReadWrite", "includeSubfolders": true }
                ],
                "allowShell": true,
                "allowNetwork": false
              },
              "agent": { "maxSteps": 12 }
            }
            """);

        try
        {
            var config = new ConfigStore(path).Current;

            var root = Assert.Single(config.Permissions.Roots);
            Assert.Equal("/home/fadhil/Projects", root.Path);
            Assert.Equal(FolderAccess.ReadWrite, root.Access);

            Assert.True(config.Permissions.AllowShell);
            Assert.False(config.Permissions.AllowNetwork);
            Assert.Equal(12, config.Agent.MaxSteps);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    /// <summary>Files written by earlier builds are PascalCase and must keep loading.</summary>
    [Fact]
    public void A_config_written_by_an_earlier_build_still_loads()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"autowork-cfg-{Guid.NewGuid():n}.json");

        System.IO.File.WriteAllText(path, """
            {
              "SchemaVersion": 1,
              "Permissions": {
                "Roots": [ { "Path": "/srv/work", "Access": "Read", "IncludeSubfolders": false } ],
                "AllowDelete": true
              }
            }
            """);

        try
        {
            var config = new ConfigStore(path).Current;

            var root = Assert.Single(config.Permissions.Roots);
            Assert.Equal("/srv/work", root.Path);
            Assert.Equal(FolderAccess.Read, root.Access);
            Assert.True(config.Permissions.AllowDelete);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    /// <summary>What is saved must be what the documentation tells people to expect.</summary>
    [Fact]
    public void Saving_writes_camel_case_property_names()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"autowork-cfg-{Guid.NewGuid():n}.json");

        try
        {
            var store = new ConfigStore(path);
            store.Update(config => config.Permissions.Roots =
                [new PermissionRoot { Path = "/tmp/x", Access = FolderAccess.ReadWrite }]);

            var written = System.IO.File.ReadAllText(path);

            Assert.Contains("\"permissions\"", written);
            Assert.Contains("\"schemaVersion\"", written);
            Assert.DoesNotContain("\"Permissions\"", written);

            // Enum values stay PascalCase, as the documented examples show.
            Assert.Contains("\"ReadWrite\"", written);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void An_azure_key_alone_seeds_nothing_because_its_endpoint_cannot_be_guessed()
    {
        // Seeding on the key alone would add a model pointing at a placeholder hostname, and
        // being first in the list it would become the default planner and fail every run.
        SetEnvironment("AZURE_OPENAI_API_KEY", "azure-test-value");

        var config = new AutoWorkConfig();
        ConfigStore.ApplyEnvironmentOverlay(config);

        Assert.DoesNotContain(config.Models, m => m.Preset == "azure-openai");
        Assert.Null(config.Agent.PlannerModelId);
    }

    [Fact]
    public void An_azure_key_with_its_endpoint_and_deployment_seeds_a_callable_model()
    {
        SetEnvironment("AZURE_OPENAI_API_KEY", "azure-test-value");
        SetEnvironment("AZURE_OPENAI_ENDPOINT", "https://freellm.openai.azure.com/");
        SetEnvironment("AZURE_OPENAI_DEPLOYMENT", "my-gpt5-deployment");

        var config = new AutoWorkConfig();
        ConfigStore.ApplyEnvironmentOverlay(config);

        var seeded = Assert.Single(config.Models, m => m.Preset == "azure-openai");

        Assert.Equal("https://freellm.openai.azure.com/", seeded.Endpoint);

        // The deployment name is chosen by whoever deployed it, so no default could be right.
        Assert.Equal("my-gpt5-deployment", seeded.ModelId);
        Assert.Equal("env:AZURE_OPENAI_API_KEY", seeded.ApiKeyRef);
        Assert.Equal(seeded.Id, config.Agent.PlannerModelId);
    }

    [Fact]
    public void AUTOWORK_MODEL_defines_and_selects_a_model_outright()
    {
        SetEnvironment("AUTOWORK_MODEL", "my-local-model");
        SetEnvironment("AUTOWORK_ENDPOINT", "http://127.0.0.1:8080/v1");
        SetEnvironment("AUTOWORK_PROVIDER", "custom");

        var config = new AutoWorkConfig();
        ConfigStore.ApplyEnvironmentOverlay(config);

        var chosen = config.ResolvePlannerModel();

        Assert.NotNull(chosen);
        Assert.Equal("my-local-model", chosen.ModelId);
        Assert.Equal("http://127.0.0.1:8080/v1", chosen.Endpoint);
    }

    [Fact]
    public void Folder_grants_and_capability_switches_come_through_the_environment()
    {
        var first = Path.Combine(Path.GetTempPath(), "aw-env-a");
        var second = Path.Combine(Path.GetTempPath(), "aw-env-b");

        SetEnvironment("AUTOWORK_FOLDERS", $"{first};{second}");
        SetEnvironment("AUTOWORK_ALLOW_SHELL", "true");
        SetEnvironment("AUTOWORK_MAX_STEPS", "12");

        var config = new AutoWorkConfig();
        ConfigStore.ApplyEnvironmentOverlay(config);

        Assert.Equal(2, config.Permissions.Roots.Count);
        Assert.All(config.Permissions.Roots, root => Assert.Equal(FolderAccess.ReadWrite, root.Access));
        Assert.True(config.Permissions.AllowShell);
        Assert.Equal(12, config.Agent.MaxSteps);
    }

    [Fact]
    public void Read_only_grants_stay_read_only()
    {
        SetEnvironment("AUTOWORK_FOLDERS_READONLY", Path.Combine(Path.GetTempPath(), "aw-env-ro"));

        var config = new AutoWorkConfig();
        ConfigStore.ApplyEnvironmentOverlay(config);

        Assert.Equal(FolderAccess.Read, Assert.Single(config.Permissions.Roots).Access);
    }

    [Fact]
    public void Model_roles_fall_back_to_a_capable_model_when_unset()
    {
        var config = new AutoWorkConfig
        {
            Models =
            [
                new ModelProfile { Id = "chat", Capabilities = ModelCapabilities.Tools },
                new ModelProfile { Id = "eyes", Capabilities = ModelCapabilities.Vision },
                new ModelProfile { Id = "embed", Capabilities = ModelCapabilities.Embeddings },
            ],
        };

        Assert.Equal("chat", config.ResolvePlannerModel()?.Id);
        Assert.Equal("chat", config.ResolveExecutorModel()?.Id);
        Assert.Equal("eyes", config.ResolveVisionModel()?.Id);
        Assert.Equal("embed", config.ResolveEmbeddingModel()?.Id);
    }

    [Fact]
    public void A_disabled_model_is_never_selected()
    {
        var config = new AutoWorkConfig
        {
            Models = [new ModelProfile { Id = "off", Capabilities = ModelCapabilities.Tools, Enabled = false }],
        };

        Assert.Null(config.ResolvePlannerModel());
    }

    public void Dispose()
    {
        foreach (var name in _touched) Environment.SetEnvironmentVariable(name, null);
    }
}

/// <summary>
/// Batch operations are the ones that can ruin a folder in one call, so the behaviour under
/// test is mostly about what the tool refuses to do.
/// </summary>
public sealed class FileToolTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "autowork-files", Guid.NewGuid().ToString("n")[..8]);
    private readonly FileTools _tools;
    private readonly ToolContext _context;

    public FileToolTests()
    {
        Directory.CreateDirectory(_workspace);

        _context = new ToolContext
        {
            Guard = new PathGuard(new PermissionPolicy
            {
                Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
                AllowDelete = true,
                SoftDelete = false,
            }),
            Approvals = new AutoApproveBroker(),
            Log = NullActionLog.Instance,
            Options = new AgentOptions(),
            RunId = "test",
            WorkingDirectory = _workspace,
        };

        _tools = new FileTools(_context);
    }

    private async Task<string> InvokeAsync(string tool, Dictionary<string, object?> arguments)
    {
        var function = _tools.GetTools(_context).Single(t => t.Name == tool).Function;
        var result = await function.InvokeAsync(new AIFunctionArguments(arguments));
        return result?.ToString() ?? "";
    }

    [Fact]
    public async Task A_dry_run_rename_previews_without_touching_anything()
    {
        File.WriteAllText(Path.Combine(_workspace, "IMG_001.jpg"), "a");
        File.WriteAllText(Path.Combine(_workspace, "IMG_002.jpg"), "b");

        var result = await InvokeAsync("files_batch_rename", new()
        {
            ["folder"] = _workspace,
            ["matchPattern"] = @"^IMG_(\d+)\.jpg$",
            ["replacement"] = "holiday-$1.jpg",
            ["dryRun"] = true,
        });

        Assert.Contains("Would rename 2 files", result);
        Assert.True(File.Exists(Path.Combine(_workspace, "IMG_001.jpg")));
        Assert.False(File.Exists(Path.Combine(_workspace, "holiday-001.jpg")));
    }

    [Fact]
    public async Task Applying_a_rename_moves_every_matching_file()
    {
        File.WriteAllText(Path.Combine(_workspace, "IMG_001.jpg"), "a");
        File.WriteAllText(Path.Combine(_workspace, "IMG_002.jpg"), "b");

        var result = await InvokeAsync("files_batch_rename", new()
        {
            ["folder"] = _workspace,
            ["matchPattern"] = @"^IMG_(\d+)\.jpg$",
            ["replacement"] = "holiday-$1.jpg",
            ["dryRun"] = false,
        });

        Assert.Contains("Renamed 2 files", result);
        Assert.True(File.Exists(Path.Combine(_workspace, "holiday-001.jpg")));
        Assert.False(File.Exists(Path.Combine(_workspace, "IMG_001.jpg")));
    }

    [Fact]
    public async Task A_rename_that_would_collide_is_refused_before_anything_moves()
    {
        // Both files would become "photo.jpg". Applying half of that is worse than refusing.
        File.WriteAllText(Path.Combine(_workspace, "a_001.jpg"), "a");
        File.WriteAllText(Path.Combine(_workspace, "b_002.jpg"), "b");

        var result = await InvokeAsync("files_batch_rename", new()
        {
            ["folder"] = _workspace,
            ["matchPattern"] = @"^._(\d+)\.jpg$",
            ["replacement"] = "photo.jpg",
            ["dryRun"] = false,
        });

        Assert.StartsWith("REFUSED:", result);
        Assert.True(File.Exists(Path.Combine(_workspace, "a_001.jpg")));
        Assert.True(File.Exists(Path.Combine(_workspace, "b_002.jpg")));
    }

    [Fact]
    public async Task An_invalid_regular_expression_reports_the_problem_instead_of_throwing()
    {
        var result = await InvokeAsync("files_batch_rename", new()
        {
            ["folder"] = _workspace,
            ["matchPattern"] = "([unclosed",
            ["replacement"] = "x",
            ["dryRun"] = true,
        });

        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public async Task Organising_by_type_groups_files_into_named_folders()
    {
        File.WriteAllText(Path.Combine(_workspace, "report.pdf"), "a");
        File.WriteAllText(Path.Combine(_workspace, "budget.xlsx"), "b");
        File.WriteAllText(Path.Combine(_workspace, "photo.jpg"), "c");

        var result = await InvokeAsync("files_organize", new()
        {
            ["folder"] = _workspace,
            ["strategy"] = "type",
            ["dryRun"] = false,
        });

        Assert.Contains("Organised 3 files", result);
        Assert.True(File.Exists(Path.Combine(_workspace, "Documents", "report.pdf")));
        Assert.True(File.Exists(Path.Combine(_workspace, "Spreadsheets", "budget.xlsx")));
        Assert.True(File.Exists(Path.Combine(_workspace, "Images", "photo.jpg")));
    }

    [Fact]
    public async Task Writing_over_an_existing_file_needs_an_explicit_overwrite()
    {
        var target = Path.Combine(_workspace, "notes.txt");
        File.WriteAllText(target, "original");

        var refused = await InvokeAsync("files_write", new()
        {
            ["path"] = target, ["content"] = "replacement",
        });

        Assert.StartsWith("REFUSED:", refused);
        Assert.Equal("original", File.ReadAllText(target));

        var allowed = await InvokeAsync("files_write", new()
        {
            ["path"] = target, ["content"] = "replacement", ["overwrite"] = true,
        });

        Assert.DoesNotContain("REFUSED", allowed);
        Assert.Equal("replacement", File.ReadAllText(target));
    }

    [Fact]
    public async Task Reading_outside_the_granted_folder_is_refused_through_the_tool_surface()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():n}.txt");
        File.WriteAllText(outside, "secret");

        try
        {
            var result = await InvokeAsync("files_read", new() { ["path"] = outside });

            Assert.StartsWith("REFUSED:", result);
            Assert.DoesNotContain("secret", result);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Shell_tools_are_not_offered_at_all_when_the_policy_forbids_them()
    {
        var shell = new ShellTools(_context);

        Assert.Empty(shell.GetTools(_context));
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
