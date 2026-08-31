using Azunote;
using Azunyan.Core;
using Xunit;

namespace Azunote.Tests.Shell;

public sealed class DocumentStatusPresenterLanguageModeTests
{
    [Fact]
    public void Refresh_projects_the_current_language_mode()
    {
        var editor = new FakeEditorView();
        var session = new DocumentSession();
        var status = new FakeStatusBarView();
        var chrome = new FakeWindowChromeView();
        var presenter = new DocumentStatusPresenter(
            editor,
            session,
            status,
            chrome,
            () => "C#");

        presenter.Refresh();

        Assert.Equal("C#", status.State?.LanguageMode);
    }
}
