# Documents and files

Each Azunote window holds one document: a file, or untitled text that has not
been saved yet.

## Unsaved changes

While a document has unsaved changes, Azunote keeps a copy of its text a moment
after each edit, and removes the copy once the document is saved or its changes
are discarded. Closing the last window, File > Exit (which closes every
window), and a Windows shutdown or restart therefore close without asking about
unsaved changes. Closing any other window still asks.

On the next launch, each of those documents opens in its own window with its
changes still unsaved. A document with a file reopens that file with the
changes applied on top, so Undo returns to the file as it is on disk; if the
file no longer exists, the text opens as an untitled document. The same happens
after Azunote stops unexpectedly.

Besides edits to the text, two things leave a document unsaved:

- Choosing a line ending from View > Line Ending or the status bar. It applies
  from the next save.
- Another program deleting the file. The title shows `[DELETED]` until the
  document is saved or the file reappears unchanged.

A window that a command line waits on (`azu --wait`) still asks before closing,
because the command line reports what became of its document. Untitled text
that a command line reads back (`azu --output`) is never kept. See
[Command line](command-line.md).

## Encodings

File > Save with Encoding saves in the chosen encoding at once, and File >
Reopen with Encoding reads the file again in the chosen encoding instead of the
detected one. Both are also available from the encoding in the status bar.

The menus list the Unicode encodings, the system's code page, and those named
by `encodings` in [settings.toml](settings.md#settingstoml). Other... lists
every encoding in the command palette, where typing narrows them by name.

## Save As

Save As keeps the document's encoding. The line ending follows `end_of_line` in
the target folder's `.editorconfig`, or else stays the document's.
