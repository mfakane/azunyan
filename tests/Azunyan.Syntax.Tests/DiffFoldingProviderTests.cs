using Azunyan.Core;
using Xunit;

namespace Azunyan.Syntax.Tests;

public sealed class DiffFoldingProviderTests
{
    [Fact]
    public async Task Each_file_and_each_hunk_folds_up_to_the_next_header()
    {
        const string text =
            "diff --git a/one b/one\n"
            + "--- a/one\n"
            + "+++ b/one\n"
            + "@@ -1 +1 @@\n"
            + "-a\n"
            + "+b\n"
            + "@@ -9 +9 @@\n"
            + "-c\n"
            + "diff --git a/two b/two\n"
            + "@@ -1 +1 @@\n"
            + "+d\n";
        var snapshot = new TextSnapshot(text);

        var folds = await new DiffFoldingProvider().GetFoldsAsync(
            new EditorProviderContext(snapshot, 0, TextSelection.Caret(0)));

        Assert.Equal(
            [
                "\n-a\n+b\n",
                "\n-c\n",
                "\n--- a/one\n+++ b/one\n@@ -1 +1 @@\n-a\n+b\n@@ -9 +9 @@\n-c\n",
                "\n+d\n",
                "\n@@ -1 +1 @@\n+d\n"
            ],
            folds.Select(fold => snapshot.GetText(fold.Range)));
        Assert.Equal(folds.Count, folds.Select(fold => fold.Id).Distinct().Count());
    }
}
