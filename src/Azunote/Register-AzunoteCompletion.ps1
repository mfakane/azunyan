#requires -Version 7.0
<#
.SYNOPSIS
Registers tab completion for the azu command in the current session.

.DESCRIPTION
Completion is answered by azu itself: the completer hands the command line
being edited to the same grammar the editor parses, so the suggestions never
drift from the accepted options. Dot-source this script from $PROFILE to keep
the registration:

    . 'C:\Program Files\Azunote\Register-AzunoteCompletion.ps1'

.PARAMETER CommandName
The command names to complete. Defaults to the azu client under the names a
shell resolves it by.
#>
[CmdletBinding()]
param(
    [string[]] $CommandName = @('azu', 'azu.exe')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Register-ArgumentCompleter -Native -CommandName $CommandName -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    # A trailing space is where the next argument begins, and the extent ends
    # before it, so put back whatever separates the cursor from the last token.
    $commandLine = $commandAst.Extent.Text
    if ($cursorPosition -gt $commandAst.Extent.EndOffset) {
        $commandLine += ' ' * ($cursorPosition - $commandAst.Extent.EndOffset)
    }

    # The directive takes the command line as one argument and a position
    # within it, so the cursor loses the offset of everything the input line
    # holds before this command.
    $position = [Math]::Clamp(
        $cursorPosition - $commandAst.Extent.StartOffset,
        0,
        $commandLine.Length)

    $element = $commandAst.CommandElements[0]
    $executable = if ($element -is [Management.Automation.Language.StringConstantExpressionAst]) {
        $element.Value
    }
    else {
        $element.Extent.Text
    }

    $suggestions = try {
        & $executable "[suggest:$position]" $commandLine 2>$null
    }
    catch {
        # An azu that cannot be run is not worth an error at the prompt.
        return
    }

    foreach ($suggestion in $suggestions) {
        $type = if ($suggestion.StartsWith('-')) {
            [Management.Automation.CompletionResultType]::ParameterName
        }
        else {
            [Management.Automation.CompletionResultType]::ParameterValue
        }
        [Management.Automation.CompletionResult]::new($suggestion, $suggestion, $type, $suggestion)
    }
}
