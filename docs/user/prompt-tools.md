# Prompts and chat

A tool with `input = "prompt:<placeholder>"` asks for text when it runs. A chat
bubble opens at the end of the selection, or at the caret, with the placeholder
in its input box:

```toml
[launch]
command = "my-assistant.exe"
input = "prompt:Ask a question or make a request"
stdin = "${input}"
output = "showPromptResponse"
```

Press Enter or click ↑ to send, and Shift+Enter to start a new line. Each
message runs the tool with the message as `${input}`; it reaches standard input
only when `stdin` refers to it. Canceling before sending anything does not run
the tool.

## Conversations

With `showPromptResponse`, each response is added to the chat and the next
message can be sent. While the tool runs, the input box stays editable for
drafting the next message. Canceling after a response ends the conversation.
To show a response as it is generated, add `stream = "output"` (or `"stdout"`,
matching the channel used); see [Streaming output](streaming-output.md).

Each tool keeps its own conversation. `${chatHistory}` gives the tool the
conversation so far as a JSON array of `user` and `assistant` messages,
including the latest message:

```toml
[launch]
command = "my-assistant.exe"
input = "prompt:Ask or refine"
preserveChatHistory = true
stdin = "${chatHistory}"
output = "showPromptResponse"
```

Closing the chat forgets the conversation, unless `preserveChatHistory = true`
keeps it for up to 30 minutes after it was last used.

Messages in the chat can be selected. Each has a Copy button, and an Insert
button that replaces the selection in the document, or inserts at the caret
when nothing is selected.

A tool without a prompt can use `showPromptResponse` too; the chat then opens
at the selection or caret to show the output.
