#!/usr/bin/pwsh
<#
.SYNOPSIS
Script to crawl AniDB for character names
.PARAMETER startletter
Letter to start from (a-z). Useful for resuming.
Default is a.
.PARAMETER startpage
Letter page to start from. Asume default number of rows per page.
Default is 0.
.PARAMETER output
Output filename.
Default is names_anidb.txt.
#>
param(
    [char]$startletter = 'a',
    [int]$startpage = 0,
    [string]$output = 'names_anidb.txt'
)

# disable extra noise for invoke-webrequest
$ProgressPreference = "SilentlyContinue"

# get anonymous sesssion
Invoke-WebRequest "https://anidb.net" -SessionVariable 'Session' | Out-Null
Start-Sleep -Seconds 2

$result = @()
foreach ($letter in 'a'..'z')
{
    if ($letter -lt $startletter)
    {
        continue
    }

    $page = 0
    if ($letter -eq $startletter)
    {
        $page = $startpage
    }

    $hasNextPage = $false
    do
    {
        try
        {
            Write-Host "Requesting letter $letter, page $page..."
            $url = "https://anidb.net/character/?char=$letter&noalias=1&orderby.name=0.1&view=list&page=$page"
            $response = Invoke-WebRequest $url -WebSession $Session
            $html = $response.content
            $hasNextPage = $html.contains('>next</a></li>')
            $pos = $html.IndexOf('<table class="characterlist">')
            if ($pos -lt 1)
            {
                if ($html.Contains('Please unban me'))
                {
                    Write-Host 'Saving the results...'
                    '# https://anidb.net/character' | Out-File -LiteralPath $output
                    $result | Sort-Object | Get-Unique | Out-File -LiteralPath $output -Append
                    Read-Host -Prompt 'Rate limited, plz unban...'
                    continue
                }
                else
                {
                    Write-Host $html
                    Write-Error "This script needs updating"
                    exit -1
                }
            }

            do
            {
                $pos = $html.IndexOf('<td data-label="Title"', $pos)
                if ($pos -lt 0)
                {
                    break
                }
                
                $pos = $html.IndexOf('<a href=', $pos)
                $pos = $html.IndexOf('>', $pos)
                $endPos = $html.IndexOf('</a></td>', $pos)
                $name = $html.Substring($pos + 1, $endPos - $pos - 1).Trim()

                $pos = $html.IndexOf('<td data-label="Type"', $endPos)
                $pos = $html.IndexOf('>', $pos)
                $endPos = $html.IndexOf('</td>', $pos)
                $type = $html.Substring($pos + 1, $endPos - $pos - 1)

                if ($type -ieq 'Character') # consider adding Mecha
                {
                    $result += $name
                }
                else
                {
                    Write-Host "Skipped $name ($type)"
                }
            } until ($pos -lt 0)
            
            $page++
            Start-Sleep -Seconds 2 # increase if needed
        }
        catch
        {
            Write-Host "Failed to fetch the page" -ForegroundColor Yellow
        }

    } while ($hasNextPage)
}

Write-Host 'Saving the results...'
'# https://anidb.net/character' | Out-File -LiteralPath $output
$result | Sort-Object | Get-Unique | Out-File -LiteralPath $output -Append

Write-Host 'Done.'