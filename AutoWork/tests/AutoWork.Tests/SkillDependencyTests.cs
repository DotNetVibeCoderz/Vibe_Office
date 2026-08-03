using AutoWork.Agents;
using AutoWork.Core.Skills;

namespace AutoWork.Tests;

/// <summary>
/// Working out what a skill's scripts need. The important cases are the ones where the answer is
/// "do not install anything" — a wrong package name succeeds quietly and runs its own code, so
/// over-eagerness here is worse than a missing library.
/// </summary>
public sealed class SkillDependencyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "autowork-skilldeps", Guid.NewGuid().ToString("n")[..8]);
    private readonly FileSkillStore _store;

    public SkillDependencyTests() => _store = new FileSkillStore(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Skill Install(params (string Path, string Content)[] files) =>
        _store.Install(
            """
            ---
            name: sample
            description: d
            ---

            Body.
            """,
            "repo", "sample",
            [.. files.Select(f => (f.Path, System.Text.Encoding.UTF8.GetBytes(f.Content)))]);

    [Fact]
    public void A_declared_requirements_file_is_read_as_written()
    {
        var skill = Install(("requirements.txt", "anthropic>=0.39.0\nmcp>=1.1.0\n\n# a comment\n"));

        var plan = SkillDependencies.Plan(skill);

        Assert.Equal(["anthropic>=0.39.0", "mcp>=1.1.0"], plan.Packages);
        Assert.Contains("requirements.txt", plan.Source);
    }

    /// <summary>
    /// The published skills mostly declare nothing, so the imports have to be read. Both spellings
    /// of an import statement appear in real scripts.
    /// </summary>
    [Fact]
    public void Imports_are_resolved_through_the_table_to_their_real_package_names()
    {
        var skill = Install(("scripts/fill.py", """
            import sys
            from pypdf import PdfReader, PdfWriter
            import pdfplumber
            from PIL import Image
            import fitz
            """));

        var plan = SkillDependencies.Plan(skill);

        // fitz is PyMuPDF and PIL is Pillow — the whole reason a table exists rather than a guess.
        Assert.Equal(
            ["Pillow", "PyMuPDF", "pdfplumber", "pypdf"],
            plan.Packages.OrderBy(p => p, StringComparer.Ordinal).ToArray());

        Assert.Contains("imports", plan.Source);
    }

    /// <summary>Otherwise every script would appear to need `os` and `json` from PyPI.</summary>
    [Fact]
    public void Standard_library_imports_are_never_installed()
    {
        var skill = Install(("scripts/plain.py", """
            import os, sys, json
            import re
            from pathlib import Path
            from datetime import datetime
            import subprocess
            """));

        var plan = SkillDependencies.Plan(skill);

        Assert.True(plan.IsEmpty, $"expected nothing to install, got {string.Join(", ", plan.Packages)}");
    }

    /// <summary>
    /// `anthropics/skills/pdf` really does `from extract_form_field_info import get_field_info`,
    /// naming a file sitting next to it. Installing a PyPI package by that name would be absurd
    /// at best and hostile at worst.
    /// </summary>
    [Fact]
    public void A_sibling_module_in_the_skill_is_not_mistaken_for_a_package()
    {
        var skill = Install(
            ("scripts/extract_form_field_info.py", "def get_field_info(): pass"),
            ("scripts/fill.py", "from extract_form_field_info import get_field_info"));

        Assert.True(SkillDependencies.Plan(skill).IsEmpty);
    }

    /// <summary>
    /// The line the whole design turns on: an unknown import is reported, never guessed. Inventing
    /// a package name that looks right is exactly how typosquatting gets its foothold.
    /// </summary>
    [Fact]
    public void An_import_that_is_not_in_the_table_is_reported_rather_than_guessed_at()
    {
        var skill = Install(("scripts/odd.py", "import some_obscure_vendor_sdk\nimport pypdf"));

        var plan = SkillDependencies.Plan(skill);

        Assert.Equal(["pypdf"], plan.Packages);
        Assert.Contains(plan.Notes, n => n.Contains("some_obscure_vendor_sdk"));
        Assert.DoesNotContain(plan.Packages, p => p.Contains("obscure", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// pip can install `pdf2image`; it cannot install poppler. Saying so up front beats a
    /// traceback about a missing binary after the install appeared to succeed.
    /// </summary>
    [Fact]
    public void A_package_that_needs_a_system_tool_says_so()
    {
        var skill = Install(("scripts/convert.py", "from pdf2image import convert_from_path"));

        var plan = SkillDependencies.Plan(skill);

        Assert.Contains("pdf2image", plan.Packages);
        Assert.Contains(plan.Notes, n => n.Contains("poppler"));
    }

    [Fact]
    public void A_package_named_in_requirements_is_not_added_twice_by_the_import_scan()
    {
        var skill = Install(
            ("requirements.txt", "pypdf>=4.0"),
            ("scripts/read.py", "import pypdf"));

        Assert.Equal(["pypdf>=4.0"], SkillDependencies.Plan(skill).Packages);
    }

    [Fact]
    public void A_skill_with_no_python_at_all_needs_nothing()
    {
        var skill = Install(("templates/page.html", "<html></html>"), ("reference.md", "# Notes"));

        Assert.True(SkillDependencies.Plan(skill).IsEmpty);
    }

    /// <summary>An empty plan is provisioned by definition, so nothing is ever installed for it.</summary>
    [Fact]
    public void Nothing_to_install_counts_as_already_provisioned()
    {
        var skill = Install(("reference.md", "# Notes"));

        Assert.True(SkillDependencies.IsProvisioned(skill, SkillDependencies.Plan(skill)));
    }

    [Fact]
    public void A_skill_that_needs_packages_is_not_provisioned_until_it_has_an_environment()
    {
        var skill = Install(("scripts/read.py", "import pypdf"));

        Assert.False(SkillDependencies.IsProvisioned(skill, SkillDependencies.Plan(skill)));
        Assert.Null(SkillDependencies.InterpreterFor(skill));
    }
}
