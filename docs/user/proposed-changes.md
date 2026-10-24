# Proposed changes

A tool whose output uses `proposeDocument` or `proposeSelection` does not change
the document directly. Azunote compares the output with the text the tool read,
the whole document or the selection, and shows the difference in a new
read-only window in the Diff mode. The bar above it names the tool and the
document: Apply makes the change and closes the window, while Discard, or
closing the window, leaves the document as it is. When the output is the same
as the text, a message says there is no change instead.

```toml
name = "Prettier (Preview)"

[launch]
command = "prettier"
args = ["--stdin-filepath", "${file}"]
input = "document"
stdin = "${input}"
stdout = ["proposeDocument", "ignore"]
stderr = ["ignore", "newDocument"]
```

## Editing while a change is proposed

The document stays editable while the tool runs and while the difference is
shown. The difference follows the edits, so it always shows what Apply would do
to the document as it is now. For `proposeSelection`, the text followed is the
selection, which grows and shrinks with the edits made inside it.

A proposed change to a line you have edited is left out: the bar says how many
are, and the edited lines are underlined as warnings until the change is
applied or discarded. Changes on neighboring lines both stay. The change keeps
the document's line endings, whatever the tool printed. Closing the document's
window closes the windows of its proposed changes.

## Selecting

The difference and the document select the same text. Selecting in one selects
the matching text in the other and scrolls to it: a kept or removed line of the
difference is that line of the document, and an added line or a header is where
its change goes. A selection in the document selects the part the difference
shows, and a caret on a line the difference leaves out selects nothing.
