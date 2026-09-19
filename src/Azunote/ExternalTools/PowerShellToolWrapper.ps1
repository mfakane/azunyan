# The script every `pwsh` external tool runs. Azunote hands it to PowerShell as
# an encoded command rather than as a file, so no execution policy can keep a
# tool from running; it lives here as PowerShell so that it can be read, linted
# and run on its own.
#
# The command line to run and whether standard input was configured arrive one
# of two ways. A process started for a request finds them in its environment. A
# process started before its request is known, one of the waiting processes the
# warm pool keeps, finds a pipe name there instead and reads them from that
# pipe; it is the same script from that point on, so a warm run and a cold run
# behave identically.
#
# The syntax is limited to what both PowerShell 7 and Windows PowerShell 5.1
# accept, and avoids double quotes so that it survives every way it is handed to
# the interpreter.

$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $OutputEncoding
try
{
    [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
}
catch
{
}

$azunoteScript = $null
$azunoteHasInput = $false
$azunoteStreaming = $false
if ($env:AZUNOTE_EXTERNAL_TOOL_PIPE)
{
    try
    {
        $ErrorActionPreference = 'Stop'
        $azunotePipeName = $env:AZUNOTE_EXTERNAL_TOOL_PIPE
        $azunoteToken = $env:AZUNOTE_EXTERNAL_TOOL_TOKEN
        [Environment]::SetEnvironmentVariable('AZUNOTE_EXTERNAL_TOOL_PIPE', $null)
        [Environment]::SetEnvironmentVariable('AZUNOTE_EXTERNAL_TOOL_TOKEN', $null)
        $azunotePipe = New-Object -TypeName System.IO.Pipes.NamedPipeClientStream -ArgumentList '.', $azunotePipeName, ([System.IO.Pipes.PipeDirection]::InOut)
        $azunotePipe.Connect(60000)
        $azunoteWriter = New-Object -TypeName System.IO.StreamWriter -ArgumentList $azunotePipe, ([System.Text.UTF8Encoding]::new($false))
        $azunoteWriter.AutoFlush = $true
        $azunoteWriter.WriteLine($azunoteToken)
        $azunoteReader = New-Object -TypeName System.IO.StreamReader -ArgumentList $azunotePipe, ([System.Text.UTF8Encoding]::new($false))
        $azunoteRequest = $azunoteReader.ReadLine() | ConvertFrom-Json
        $azunotePipe.Dispose()
        Set-Location -LiteralPath $azunoteRequest.cwd
        [Environment]::CurrentDirectory = $azunoteRequest.cwd
        foreach ($azunoteEntry in $azunoteRequest.env.PSObject.Properties)
        {
            [Environment]::SetEnvironmentVariable($azunoteEntry.Name, $azunoteEntry.Value)
        }

        $azunoteScript = [string]$azunoteRequest.script
        $azunoteHasInput = [bool]$azunoteRequest.stdin
        $azunoteStreaming = [bool]$azunoteRequest.stream
    }
    catch
    {
        exit 199
    }

    $ErrorActionPreference = 'Continue'
    Remove-Variable -Name azunotePipeName, azunoteToken, azunotePipe, azunoteWriter, azunoteReader, azunoteRequest, azunoteEntry -ErrorAction SilentlyContinue
}
else
{
    $azunoteScript = [string]$env:AZUNOTE_EXTERNAL_TOOL_COMMAND
    $azunoteHasInput = $env:AZUNOTE_EXTERNAL_TOOL_STDIN -eq '1'
    $azunoteStreaming = $env:AZUNOTE_EXTERNAL_TOOL_STREAM -eq '1'
    # Cleared so that the command runs with the environment a waiting
    # process leaves behind, which is the one the tool was configured with.
    [Environment]::SetEnvironmentVariable('AZUNOTE_EXTERNAL_TOOL_COMMAND', $null)
    [Environment]::SetEnvironmentVariable('AZUNOTE_EXTERNAL_TOOL_STDIN', $null)
    [Environment]::SetEnvironmentVariable('AZUNOTE_EXTERNAL_TOOL_STREAM', $null)
}

$azunoteNewLine = [Environment]::NewLine
$azunoteBlock = [scriptblock]::Create($azunoteScript)
$azunoteWantsInput = $azunoteHasInput -and $azunoteScript -match '\$input'
$azunoteBody = $null
$azunotePipeline = $null
$azunoteStatement = $null
$azunoteAst = $azunoteBlock.Ast
if ($azunoteHasInput -and $null -eq $azunoteAst.ParamBlock -and $null -eq $azunoteAst.BeginBlock -and $null -eq $azunoteAst.ProcessBlock -and $null -ne $azunoteAst.EndBlock -and $azunoteAst.EndBlock.Statements.Count -eq 1 -and $azunoteAst.EndBlock.Statements[0] -is [System.Management.Automation.Language.PipelineAst])
{
    $azunoteStatement = $azunoteAst.EndBlock.Statements[0]
}

if ($azunoteWantsInput)
{
    # The line is run with $input bound to the lines. When it is only a
    # call to a script block, that block is what runs, because a block
    # invoked with & would get an $input of its own instead.
    $azunoteBody = $azunoteScript
    if ($null -ne $azunoteStatement -and $azunoteStatement.PipelineElements.Count -eq 1 -and $azunoteStatement.PipelineElements[0] -is [System.Management.Automation.Language.CommandAst])
    {
        $azunoteCommand = $azunoteStatement.PipelineElements[0]
        if ($azunoteCommand.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Ampersand -and $azunoteCommand.CommandElements.Count -eq 1 -and $azunoteCommand.CommandElements[0] -is [System.Management.Automation.Language.ScriptBlockExpressionAst])
        {
            $azunoteInner = $azunoteCommand.CommandElements[0].ScriptBlock
            if ($null -eq $azunoteInner.ParamBlock -and $null -eq $azunoteInner.BeginBlock -and $null -eq $azunoteInner.ProcessBlock -and $null -ne $azunoteInner.EndBlock)
            {
                $azunoteBody = $azunoteInner.EndBlock.Extent.Text
            }
        }
    }
}
elseif ($azunoteHasInput -and $null -ne $azunoteStatement -and $azunoteStatement.PipelineElements[0] -is [System.Management.Automation.Language.CommandAst])
{
    try
    {
        $azunotePipeline = $azunoteBlock.GetSteppablePipeline()
    }
    catch
    {
        $azunotePipeline = $null
    }
}

$azunoteLines = New-Object -TypeName System.Collections.Generic.List[string]
if ($null -ne $azunotePipeline -or $null -ne $azunoteBody)
{
    $azunoteStandardInput = New-Object -TypeName System.IO.StreamReader -ArgumentList ([Console]::OpenStandardInput()), ([System.Text.UTF8Encoding]::new($false))
    $azunoteText = $azunoteStandardInput.ReadToEnd()
    $azunoteStandardInput.Dispose()
    foreach ($azunoteLine in [regex]::Split($azunoteText, '\r\n|\r|\n'))
    {
        $azunoteLines.Add($azunoteLine)
    }

    if ($azunoteLines.Count -gt 0 -and $azunoteLines[$azunoteLines.Count - 1] -eq '')
    {
        $azunoteLines.RemoveAt($azunoteLines.Count - 1)
    }

    $azunoteMatch = [regex]::Match($azunoteText, '\r\n|\r|\n')
    if ($azunoteMatch.Success)
    {
        $azunoteNewLine = $azunoteMatch.Value
    }
}

$azunoteInputLines = $azunoteLines.ToArray()
$azunoteResult = New-Object -TypeName System.Collections.Generic.List[object]
# A streamed run writes each value as it is produced, so that Azunote can
# apply it while the command is still running. The separator is written
# before the next value rather than after the last one, which is what keeps
# a streamed run free of the trailing newline a buffered run also omits.
# A value that is not a string is formatted on its own, because the whole
# sequence is never in hand at once.
$azunoteStream = @{ Wrote = $false }
try
{
    # The output is written here rather than by the host, which would end it
    # with a newline the command never produced. A command that writes to
    # [Console]::Out itself bypasses this and keeps every byte it wrote.
    & {
        if ($null -ne $azunotePipeline)
        {
            $azunotePipeline.Begin($true)
            try
            {
                foreach ($azunoteItem in $azunoteInputLines)
                {
                    $azunotePipeline.Process($azunoteItem)
                }
            }
            finally
            {
                $azunotePipeline.End()
            }
        }
        elseif ($null -ne $azunoteBody)
        {
            & ([scriptblock]::Create('$input = $azunoteInputLines' + [Environment]::NewLine + $azunoteBody))
        }
        else
        {
            & $azunoteBlock
        }
    } | ForEach-Object {
        if ($azunoteStreaming)
        {
            if ($azunoteStream.Wrote)
            {
                [Console]::Out.Write($azunoteNewLine)
            }

            if ($_ -is [string])
            {
                [Console]::Out.Write($_)
            }
            else
            {
                [Console]::Out.Write((($_ | Out-String -Width 4096) -replace '(\r\n|\r|\n)+$', ''))
            }

            [Console]::Out.Flush()
            $azunoteStream.Wrote = $true
        }
        else
        {
            $azunoteResult.Add($_)
        }
    }
}
finally
{
    if ($azunoteResult.Count -gt 0)
    {
        $azunoteStrings = $true
        foreach ($azunoteItem in $azunoteResult)
        {
            if ($azunoteItem -isnot [string])
            {
                $azunoteStrings = $false
                break
            }
        }

        if ($azunoteStrings)
        {
            [Console]::Out.Write(($azunoteResult -join $azunoteNewLine))
        }
        else
        {
            [Console]::Out.Write((($azunoteResult | Out-String -Width 4096) -replace '(\r\n|\r|\n)+$', ''))
        }
    }
}
